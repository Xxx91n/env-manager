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

use crate::reconcile::{delegated_rotate_argv, rotate_mount_at, ROTATION_TIME_LIMIT};
use crate::rotate_stop_tests::MountFixture;

/// Every wait here is bounded, so a regression fails the test instead of wedging the runner
/// until the job timeout. The stub scripts finish in milliseconds; 20s is the same budget the
/// stop-path suite uses for real child processes.
const WRITEBACK_TIMEOUT: Duration = Duration::from_secs(20);

/// A provider-managed Periodic mount has no local envelope for the delegated CLI to rotate.
/// The Background-mode handler reports a visible skip without launching a child or
/// advancing rotation/fetch stamps; otherwise an exit-0 no-op would falsely claim that the provider value was rotated.
#[tokio::test]
async fn provider_managed_periodic_mount_without_envelope_is_skipped_without_stamping_or_delegating() {
    for (label, envelope) in [
        ("null-envelope", serde_json::Value::Null),
        ("empty-envelope", serde_json::json!("")),
    ] {
        let fixture = MountFixture::new(label);
        let mut mounts = fixture.mounts();
        mounts[0]["envelope"] = envelope;
        mounts[0]["lastRotatedAt"] = serde_json::json!("2026-01-01T00:00:00Z");
        mounts[0]["lastFetchedAt"] = serde_json::json!("2026-01-02T00:00:00Z");
        std::fs::write(&fixture.path, serde_json::to_string_pretty(&mounts).unwrap())
            .expect("write a provider-managed Periodic mount");
        let before = std::fs::read_to_string(&fixture.path)
            .expect("read the original mount file");
        let shutdown = CancellationToken::new();

        let info = tokio::time::timeout(
            WRITEBACK_TIMEOUT,
            rotate_mount_at(
                &fixture.path,
                "must-not-be-launched.exe",
                &[],
                "mount-1",
                &shutdown,
                ROTATION_TIME_LIMIT,
            ),
        )
        .await
        .expect("skipping a provider-managed mount must be immediate")
        .expect("a provider-managed mount is a skip, not an error");

        assert_eq!(info["rotated"], false);
        assert_eq!(info["skipped"], true);
        assert_eq!(info["skipReason"], "providerManaged");
        assert_eq!(info["lastRotatedAt"], "2026-01-01T00:00:00Z");
        assert_eq!(info["lastFetchedAt"], "2026-01-02T00:00:00Z");
        assert_eq!(
            std::fs::read_to_string(&fixture.path).expect("read the mount file after skip"),
            before,
            "a skipped mount must remain byte-for-byte unchanged"
        );
    }
}

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
        rotate_mount_at(
            &fixture.path,
            "powershell.exe",
            &arg_refs,
            "mount-1",
            &shutdown,
            ROTATION_TIME_LIMIT,
        ),
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
        rotate_mount_at(
            &fixture.path,
            "powershell.exe",
            &arg_refs,
            "mount-1",
            &shutdown,
            ROTATION_TIME_LIMIT,
        ),
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
        rotate_mount_at(
            &fixture.path,
            "powershell.exe",
            &arg_refs,
            "mount-1",
            &shutdown,
            ROTATION_TIME_LIMIT,
        ),
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
        rotate_mount_at(
            &fixture.path,
            "powershell.exe",
            &arg_refs,
            "mount-1",
            &shutdown,
            ROTATION_TIME_LIMIT,
        ),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect_err("an unreadable mount file must not be reported as a completed rotation");
    assert!(
        err.contains("did not parse"),
        "the client must be told the mount file could not be read, got: {err}"
    );
}

/// The delegation shape pinned by the #195 contract: `rotate <mountId>` re-encrypts the
/// whole store, so the command handed to the CLI must not carry a mount id. A mount filter
/// in the CLI (option 1) or a service-side mount -> profile mapping (option 2) were both
/// rejected in favour of the whole-store contract. The assertion targets the production
/// assembly `rotate_mount` hands down (not a copy of the constant), so a mount argument
/// appended at that call site cannot evade the guard.
#[test]
fn delegated_rotation_command_carries_no_mount_id() {
    assert_eq!(
        delegated_rotate_argv(),
        vec!["profile", "secret-provider", "rotate"],
        "the delegated rotation must stay unscoped: the CLI's RotateAll re-encrypts every \
         profile's locally-held envelopes, and the IPC mount id only names the stamp target"
    );
}

/// A two-mount mount file so the stamp-scope assertion can prove the #195 contract: the
/// child's rewrite lands on **both** envelopes (whole-store re-encryption), while the
/// service stamps `lastRotatedAt` only on the mount the IPC call named.
struct TwoMountFixture {
    path: PathBuf,
}

impl TwoMountFixture {
    fn new() -> Self {
        let path = std::env::temp_dir().join(format!(
            "env-manager-rotate-two-mount-{}.json",
            uuid::Uuid::new_v4()
        ));
        let mounts = serde_json::json!([
            {
                "id": "mount-1",
                "provider": "vault-kv2",
                "name": "TEST_TOKEN",
                "scope": "user",
                "refreshPolicy": "Periodic",
                "refreshIntervalSeconds": 300,
                "createdAt": "2026-01-01T00:00:00Z",
                "schemaVersion": 2,
                "envelope": "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token-1\"}"
            },
            {
                "id": "mount-2",
                "provider": "vault-kv2",
                "name": "TEST_TOKEN",
                "scope": "user",
                "refreshPolicy": "Periodic",
                "refreshIntervalSeconds": 300,
                "createdAt": "2026-01-01T00:00:00Z",
                "schemaVersion": 2,
                "envelope": "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token-2\"}"
            }
        ]);
        let original = serde_json::to_string_pretty(&mounts).unwrap();
        std::fs::write(&path, &original).expect("write the temp two-mount fixture");
        Self { path }
    }

    fn mounts(&self) -> Vec<serde_json::Value> {
        let raw = std::fs::read_to_string(&self.path).expect("read the mount file back");
        serde_json::from_str(&raw).expect("the mount file must stay whole JSON")
    }
}

impl Drop for TwoMountFixture {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

/// The stamp-scope half of the whole-store contract (#195): an IPC `rotate <mountId>`
/// re-encrypts every profile's locally-held envelopes - the child rewrites both envelopes
/// in place - but the service owns the `lastRotatedAt` stamp only on the
/// mount the call named. The child reproduces the real CLI write-back
/// (`SecretMountEnvelopeHolder` stamps `LastRotatedAt` on every mount it rotates and never
/// writes `LastFetchedAt`, which the service stopped writing too - #293), so the assertions
/// can pin both halves: the named mount's stamp
/// is overwritten with the service's own now, and the other mount's CLI stamp passes
/// through untouched.
const CHILD_STAMP: &str = "2026-01-02T03:04:05Z";

#[tokio::test]
async fn completed_rotation_stamps_only_the_named_mount() {
    let fixture = TwoMountFixture::new();
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
            "envelope": "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token-1-rotated\"}",
            "lastRotatedAt": CHILD_STAMP
        },
        {
            "id": "mount-2",
            "provider": "vault-kv2",
            "name": "TEST_TOKEN",
            "scope": "user",
            "refreshPolicy": "Periodic",
            "refreshIntervalSeconds": 300,
            "createdAt": "2026-01-01T00:00:00Z",
            "schemaVersion": 2,
            "envelope": "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token-2-rotated\"}",
            "lastRotatedAt": CHILD_STAMP
        }
    ]);
    let script = RewriteScript::writing(&fixture.path, &serde_json::to_string(&rewritten).unwrap());
    let args = script.args();
    let arg_refs: Vec<&str> = args.iter().map(|a| a.as_str()).collect();

    let info = tokio::time::timeout(
        WRITEBACK_TIMEOUT,
        rotate_mount_at(
            &fixture.path,
            "powershell.exe",
            &arg_refs,
            "mount-1",
            &shutdown,
            ROTATION_TIME_LIMIT,
        ),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect("a child that exits 0 completes the rotation");
    assert_eq!(info["rotated"], true);

    let mounts = fixture.mounts();
    assert_eq!(
        mounts.len(),
        2,
        "the child's whole-store rewrite must survive the metadata stamp"
    );
    assert_eq!(
        mounts[0]["envelope"],
        "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token-1-rotated\"}",
        "the named mount keeps the envelope the rotation child wrote"
    );
    assert_ne!(
        mounts[0]["lastRotatedAt"],
        serde_json::Value::Null,
        "the mount named by the IPC call is stamped"
    );
    assert_ne!(
        mounts[0]["lastRotatedAt"], CHILD_STAMP,
        "the service owns the named mount's stamp: it overwrites the value the CLI child \
         wrote with its own now"
    );
    assert_eq!(
        mounts[0]["lastFetchedAt"],
        serde_json::Value::Null,
        "the service stamps lastRotatedAt only - lastFetchedAt is never written (#293)"
    );
    assert_eq!(
        mounts[1]["envelope"],
        "{\"provider\":\"vault-kv2\",\"version\":1,\"targetName\":\"env-manager/token-2-rotated\"}",
        "the whole-store re-encryption also lands on the other mount's envelope"
    );
    assert_eq!(
        mounts[1]["lastRotatedAt"], CHILD_STAMP,
        "the CLI child stamps every mount it rotates, and the service must preserve that \
         value on a mount the call did not name - neither clearing nor overwriting it"
    );
    assert_eq!(
        mounts[1]["lastFetchedAt"],
        serde_json::Value::Null,
        "the service-owned stamp never lands on a mount the call did not name"
    );
}
