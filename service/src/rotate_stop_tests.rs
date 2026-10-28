// Stop-path convergence of a delegated CLI rotate (issue #153).
//
// Evidence seam chosen for this ticket: the service-side rotation call
// (`reconcile::run_cli_child` and `reconcile::rotate_mount_at`) driven against a real
// child process and a temp mount file. A literal `Stop-Service` run would additionally
// need an installed service, a working provider and a rotation slow enough to still be in
// flight when the stop lands, none of which is deterministic on a CI runner. The layer
// above this seam is pinned by `startup_contract_tests` in `main.rs`: listener before
// SERVICE_RUNNING, pipe released before SERVICE_STOPPED, and - added for #153 - the body
// does not return while a connection handler is still in flight, which is the property the
// handler join in `ipc::accept_loop` contributes to the stop path.
//
// Isolation: every fixture is a uniquely named file under `%TEMP%`, removed on drop. No
// registry, no real `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::path::PathBuf;
use std::time::Duration;
use tokio_util::sync::CancellationToken;

use crate::reconcile::{rotate_mount_at, run_cli_child, ChildOutcome};
use crate::SHUTDOWN_DRAIN;

/// The stub "CLI": one `ping.exe` process doing 30 one-second round trips, so it is
/// still running long after the stop lands. A shell wrapper is deliberately avoided -
/// `cmd /c ping` would leave the real work in a grandchild that terminating the stub
/// would not touch.
const STUB_EXE: &str = "ping.exe";
const STUB_SLOW_ARGS: [&str; 3] = ["-n", "30", "127.0.0.1"];

/// Cancel the token after a short delay, so the stop lands while the child is in flight.
/// 300ms is nothing against a 30s stub, and `run_cli_child` has already spawned the child
/// before it races the token, so the ordering under test is the real one.
///
/// A test that needs to time the stop itself drives the call from a task and cancels from
/// the test body instead (`stop_terminates_and_reaps_the_rotate_child`).
fn cancel_after(shutdown: &std::sync::Arc<CancellationToken>, delay: Duration) {
    let timer = shutdown.clone();
    tokio::spawn(async move {
        tokio::time::sleep(delay).await;
        timer.cancel();
    });
}

/// Whether Windows still has a process with this pid. `tasklist` is used rather than
/// `OpenProcess` + `GetExitCodeProcess` so the assertion does not need a `windows-sys`
/// feature that no production code in this crate requires.
fn pid_is_running(pid: u32) -> bool {
    let output = std::process::Command::new("tasklist")
        .args(["/FI", &format!("PID eq {pid}"), "/NH", "/FO", "CSV"])
        .output()
        .expect("tasklist must be runnable for the liveness assertion to mean anything");
    // With `/NH` there is no header row, so a quoted pid only appears in a matching
    // process row; a miss prints an `INFO: No tasks...` line instead.
    String::from_utf8_lossy(&output.stdout).contains(&format!("\"{pid}\""))
}

/// Every wait in this file is bounded, so a convergence regression fails the test instead
/// of wedging the runner until the job timeout (the constraint `resilience_tests.rs`
/// learned the same way). 20s is far short of the 30s stub, and far beyond the stop budget.
const ALWAYS_TOO_LONG: Duration = Duration::from_secs(20);

/// A temp `secretMount.json` holding one un-rotated mount, deleted with the file.
struct MountFixture {
    path: PathBuf,
    original: String,
}

impl MountFixture {
    fn new(label: &str) -> Self {
        let path = std::env::temp_dir().join(format!(
            "env-manager-rotate-stop-{label}-{}.json",
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
                "schemaVersion": 1,
            }
        ]);
        let original = serde_json::to_string_pretty(&mounts).unwrap();
        std::fs::write(&path, &original).expect("write the temp mount fixture");
        Self { path, original }
    }

    /// Reading the fixture back through JSON is the point: #153 forbids a stop that
    /// leaves the mount file half-written, and a file that no longer parses is exactly
    /// that, whether or not the bytes look plausible.
    fn mounts(&self) -> Vec<serde_json::Value> {
        let raw = std::fs::read_to_string(&self.path).expect("read the mount file back");
        serde_json::from_str(&raw).expect("the mount file must stay whole JSON")
    }
}

impl Drop for MountFixture {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

/// Scenario: `Stop-Service` (or an uninstall) lands while a rotate child is running. The
/// stop path must terminate that child and wait for its exit before returning, so the
/// `SERVICE_STOPPED` that follows names a process that can no longer write profiles.json.
#[tokio::test]
async fn stop_terminates_and_reaps_the_rotate_child() {
    assert!(
        pid_is_running(std::process::id()),
        "the liveness probe must report a running pid, or the assertion below proves nothing"
    );

    let shutdown = std::sync::Arc::new(CancellationToken::new());
    let token = shutdown.clone();
    let in_flight =
        tokio::spawn(async move { run_cli_child(STUB_EXE, &STUB_SLOW_ARGS, &token).await });

    // Let the child get going, then time only the stop: everything between the cancel and
    // the return is terminate + reap, so a slow runner cannot be blamed for the result -
    // and process startup cannot hide a convergence that took far longer than the budget.
    tokio::time::sleep(Duration::from_millis(300)).await;
    let outcome = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect(
            "the stop must terminate and reap the child inside SHUTDOWN_DRAIN; waiting the \
             stub out would take 30s and stall the join in `ipc::accept_loop`",
        )
        .expect("the rotate call task must not panic")
        .expect("spawn the stub rotate child");

    let ChildOutcome::Interrupted { pid } = outcome else {
        panic!("a stop during the rotation must report the child as interrupted");
    };
    let pid = pid.expect("a spawned child reports its pid");

    // `Child::kill` is TerminateProcess followed by `wait`, so the pid is already gone
    // when the call returns: nothing that can write profiles.json outlives the stop.
    assert!(
        !pid_is_running(pid),
        "the CLI child {pid} must be gone by the time the stop path returns"
    );
}

/// Scenario: recovery semantics of the interrupted rotation. The child is killed, the
/// error reaches the IPC client, and the mount file is untouched: no half-written JSON and
/// no rotation recorded, which is what makes the next `rotate` or reconcile tick replay it.
#[tokio::test]
async fn interrupted_rotation_leaves_the_mount_unrotated() {
    let fixture = MountFixture::new("unrotated");
    let shutdown = std::sync::Arc::new(CancellationToken::new());
    cancel_after(&shutdown, Duration::from_millis(300));

    let err = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        rotate_mount_at(&fixture.path, STUB_EXE, &STUB_SLOW_ARGS, "mount-1", &shutdown),
    )
    .await
    .expect("an interrupted rotate must converge, not wait the stub out")
    .expect_err("an interrupted rotate must fail for its client");
    // Pinned on the message because the message is what the client sees: `process_request`
    // prefixes it into the `rotate failed: ...` response the GUI shows. The typed
    // discriminant is pinned by `stop_terminates_and_reaps_the_rotate_child`, so rewording
    // this string is a deliberate change to user-visible text, not a refactor.
    assert!(
        err.contains("interrupted by service stop"),
        "the client must be told the rotation was interrupted, got: {err}"
    );

    assert_eq!(
        std::fs::read_to_string(&fixture.path).expect("read the mount file back"),
        fixture.original,
        "an interrupted rotation must not rewrite the mount file at all"
    );
    let mounts = fixture.mounts();
    assert_eq!(
        mounts[0]["lastRotatedAt"],
        serde_json::Value::Null,
        "a rotation that never completed must not be recorded as done"
    );
    assert_eq!(mounts[0]["lastFetchedAt"], serde_json::Value::Null);
}

/// The other side of the replay rule: a rotation whose child exited 0 *is* recorded, so
/// the next stop-driven replay does not rotate twice for no reason. `RotateAll` decrypts
/// and re-encrypts per secret, so a duplicate rotation is harmless but must not be normal.
#[tokio::test]
async fn completed_rotation_records_the_mount() {
    let fixture = MountFixture::new("recorded");
    let shutdown = std::sync::Arc::new(CancellationToken::new());

    let info = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        rotate_mount_at(&fixture.path, "cmd.exe", &["/c", "echo", "rotated"], "mount-1", &shutdown),
    )
    .await
    .expect("a child that finishes must not be held by the stop path")
    .expect("a child that exits 0 completes the rotation");
    assert_eq!(info["rotated"], true);
    assert!(
        info["rotateResult"]
            .as_str()
            .unwrap_or_default()
            .contains("rotated"),
        "the CLI's own report must reach the IPC client, got: {info}"
    );

    let mounts = fixture.mounts();
    assert_ne!(
        mounts[0]["lastRotatedAt"],
        serde_json::Value::Null,
        "a completed rotate must be recorded, or every later tick replays it"
    );
    assert_ne!(mounts[0]["lastFetchedAt"], serde_json::Value::Null);
}
