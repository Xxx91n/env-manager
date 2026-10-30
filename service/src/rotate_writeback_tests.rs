// Write-back semantics of a completed delegated rotation (#165).
//
// A delegated rotation writes each rotated envelope back into the mount that owns it, so the
// service must stamp its metadata onto what the child left behind instead of re-saving the
// snapshot it read before the call. These tests drive `reconcile::rotate_mount_at` against a
// real child process that rewrites the mount file, and against the three post-rotation file
// states that must not be confused with each other: written, vanished, deleted, unreadable.
//
// Separate module from `rotate_stop_tests` on purpose: that file pins the stop path (#153,
// #160), this one pins the completed path, and the two stubs measure opposite outcomes.
//
// Isolation: every fixture is a temp file under `%TEMP%`, removed on drop. No registry, no
// real `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::path::{Path, PathBuf};
use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::reconcile::rotate_mount_at;
use crate::rotate_stop_tests::MountFixture;

/// Every wait here is bounded, so a regression fails the test instead of wedging the runner
/// until the job timeout. The stub scripts finish in milliseconds; 20s is the same budget the
/// stop-path suite uses for real child processes.
const WRITEBACK_TIMEOUT: Duration = Duration::from_secs(20);

/// A temp PowerShell script that stands in for the delegated CLI's side effect on the mount
/// file, removed on drop. Driving the real rotation would need a working provider; the script
/// reproduces exactly the file states the service has to cope with after the child exits.
struct RewriteScript {
    path: PathBuf,
}

impl RewriteScript {
    fn writing(mount_path: &Path, mounts_json: &str) -> Self {
        // Single-quoted here-string: the JSON goes through verbatim (no interpolation or
        // backtick escaping). `WriteAllText` writes UTF-8 without a BOM, which is the form
        // the mount loader accepts.
        Self::from_script(&format!(
            "[System.IO.File]::WriteAllText('{}', @'\n{}\n'@)\n",
            mount_path.display(),
            mounts_json
        ))
    }

    /// The mount file gone entirely - the extreme form of "the mount vanished".
    fn deleting(mount_path: &Path) -> Self {
        Self::from_script(&format!(
            "[System.IO.File]::Delete('{}')\n",
            mount_path.display()
        ))
    }

    fn from_script(script: &str) -> Self {
        let path = std::env::temp_dir().join(format!(
            "env-manager-rotate-rewrite-{}.ps1",
            uuid::Uuid::new_v4()
        ));
        std::fs::write(&path, script).expect("write the stub script");
        Self { path }
    }

    fn args(&self) -> Vec<String> {
        vec![
            "-NoProfile".to_string(),
            "-ExecutionPolicy".to_string(),
            "Bypass".to_string(),
            "-File".to_string(),
            self.path.display().to_string(),
        ]
    }
}

impl Drop for RewriteScript {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

/// The other half of the completed-rotation contract: the child's own writes survive. A
/// delegated rotation stores each rotated envelope back into the mount that owns it, so the
/// service has to stamp its metadata onto what the child left behind - re-saving the
/// snapshot it read before the call would restore the pre-rotation envelopes and undo the
/// rotation it is recording, while still reporting success to the client.
#[tokio::test]
async fn completed_rotation_keeps_the_envelopes_the_child_wrote() {
    let fixture = MountFixture::new("child-wrote");
    let shutdown = std::sync::Arc::new(CancellationToken::new());
    let rewritten = serde_json::json!([
        {
            "id": "mount-1",
            "provider": "vault-kv2",
            "name": "TEST_TOKEN",
            "scope": "user",
            "refreshPolicy": "Periodic",
            "refreshIntervalSeconds": 300,
            "createdAt": "2026-01-01T00:00:00Z",
            "schemaVersion": 2,
            "envelope": "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token\"}"
        }
    ]);
    let script = RewriteScript::writing(&fixture.path, &serde_json::to_string(&rewritten).unwrap());
    let args = script.args();
    let arg_refs: Vec<&str> = args.iter().map(|a| a.as_str()).collect();

    let info = tokio::time::timeout(
        WRITEBACK_TIMEOUT,
        rotate_mount_at(&fixture.path, "powershell.exe", &arg_refs, "mount-1", &shutdown),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect("a child that exits 0 completes the rotation");
    assert_eq!(info["rotated"], true);

    let mounts = fixture.mounts();
    assert_eq!(
        mounts[0]["envelope"],
        "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token\"}",
        "the envelope the rotation child wrote must survive the metadata stamp; saving the \
         pre-call snapshot instead silently un-rotates the secret"
    );
    assert_ne!(
        mounts[0]["lastRotatedAt"],
        serde_json::Value::Null,
        "the stamp and the child's envelope have to land in the same file"
    );
}

/// The vanished-mount half of the write-back rule: when the file no longer names the mount
/// after the child ran, the rotation is still reported as done and the file is left exactly
/// as the child wrote it. Failing the caller would punish a rotation that already happened,
/// and rewriting the list would resurrect a mount file that lost the mount.
#[tokio::test]
async fn completed_rotation_skips_a_mount_that_vanished() {
    let fixture = MountFixture::new("vanished");
    let shutdown = std::sync::Arc::new(CancellationToken::new());
    let script = RewriteScript::writing(&fixture.path, "[]");
    let args = script.args();
    let arg_refs: Vec<&str> = args.iter().map(|a| a.as_str()).collect();

    let info = tokio::time::timeout(
        WRITEBACK_TIMEOUT,
        rotate_mount_at(&fixture.path, "powershell.exe", &arg_refs, "mount-1", &shutdown),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect("a mount that disappeared after the rotation must not fail its caller");
    assert_eq!(info["rotated"], true);
    assert_eq!(
        info["lastRotatedAt"],
        serde_json::Value::Null,
        "a mount that is no longer in the file cannot be stamped as rotated"
    );
    assert_eq!(
        std::fs::read_to_string(&fixture.path)
            .expect("read the mount file back")
            .trim(),
        "[]",
        "the service must not rewrite a mount file that no longer names the mount"
    );
}

/// The last form of "the mount vanished": the child removed the file outright. A missing
/// file means the same thing as an empty one - every mount is gone, the rotation already
/// happened - so it is a skip, not a failure, and the service must not recreate the file.
#[tokio::test]
async fn completed_rotation_tolerates_a_deleted_mount_file() {
    let fixture = MountFixture::new("deleted");
    let shutdown = std::sync::Arc::new(CancellationToken::new());
    let script = RewriteScript::deleting(&fixture.path);
    let args = script.args();
    let arg_refs: Vec<&str> = args.iter().map(|a| a.as_str()).collect();

    let info = tokio::time::timeout(
        WRITEBACK_TIMEOUT,
        rotate_mount_at(&fixture.path, "powershell.exe", &arg_refs, "mount-1", &shutdown),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect("a deleted mount file must not fail a rotation that already happened");
    assert_eq!(info["rotated"], true);
    assert_eq!(
        info["lastRotatedAt"],
        serde_json::Value::Null,
        "there is no mount record left to stamp"
    );
    assert!(
        !fixture.path.exists(),
        "the service must not recreate a mount file it did not write"
    );
}

/// Neither skip path may swallow a file the service cannot read: a child that leaves the
/// mount file unparseable has to surface as a failed rotation, not as "the mount vanished".
/// The plain loader folds a parse failure into an empty list, which is why the stamp path
/// reads strictly.
#[tokio::test]
async fn completed_rotation_reports_an_unreadable_mount_file() {
    let fixture = MountFixture::new("unreadable");
    let shutdown = std::sync::Arc::new(CancellationToken::new());
    let script = RewriteScript::writing(&fixture.path, "{ this is not a mount list");
    let args = script.args();
    let arg_refs: Vec<&str> = args.iter().map(|a| a.as_str()).collect();

    let err = tokio::time::timeout(
        WRITEBACK_TIMEOUT,
        rotate_mount_at(&fixture.path, "powershell.exe", &arg_refs, "mount-1", &shutdown),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect_err("an unreadable mount file must not be reported as a completed rotation");
    assert!(
        err.contains("did not parse"),
        "the client must be told the mount file could not be read, got: {err}"
    );
}
