// Stop-path convergence of a delegated CLI rotate (issue #153) and of the provider helper
// tree it starts (#160).
//
// Evidence seam chosen for these tickets: the service-side rotation call
// (`reconcile::run_cli_child` and `reconcile::rotate_mount_at`) driven against a real
// child process and a temp mount file. A literal `Stop-Service` run would additionally
// need an installed service, a working provider and a rotation slow enough to still be in
// flight when the stop lands, none of which is deterministic on a CI runner. The layer
// above this seam is pinned by `startup_contract_tests` in `main.rs`: listener before
// SERVICE_RUNNING, pipe released before SERVICE_STOPPED, and - added for #153 - the body
// does not return while a connection handler is still in flight, which is the property the
// handler join in `ipc::accept_loop` contributes to the stop path.
//
// Two stubs, because the two tickets measure opposite things. `STUB_EXE` is a single
// `ping.exe`, deliberately without children, so a #153 assertion cannot be satisfied by a
// tree kill it never had. `TREE_STUB_EXE` is a `powershell.exe` that starts a grandchild
// and reports its pid, which is what the #160 job object has to converge.
//
// Isolation: every fixture is a uniquely named file under `%TEMP%`, removed on drop. No
// registry, no real `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::path::{Path, PathBuf};
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

/// The stub "CLI" for the #160 tree assertions: a `powershell.exe` that starts a `ping`
/// grandchild, writes that pid to a temp file and then waits for it. PowerShell rather
/// than `cmd /c` because only it can hand back the grandchild's pid, and the pid is what
/// makes "the helper is gone" measurable instead of inferred.
const TREE_STUB_EXE: &str = "powershell.exe";

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

/// How long the helper tree may take to disappear *after* the stop path returns (#160).
/// Closing a kill-on-close job handle asks the kernel to terminate its members; it does
/// not reap them synchronously, so the ticket's acceptance budget is spent polling.
const TREE_KILL_BUDGET: Duration = Duration::from_secs(5);

/// A temp `secretMount.json` holding one un-rotated mount, deleted with the file.
/// Shared with `rotate_writeback_tests`, which drives the completed-rotation file states
/// against the same fixture shape.
pub(crate) struct MountFixture {
    pub(crate) path: PathBuf,
    original: String,
}

impl MountFixture {
    pub(crate) fn new(label: &str) -> Self {
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
    pub(crate) fn mounts(&self) -> Vec<serde_json::Value> {
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
    shutdown.cancel();
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

/// A temp file the tree stub writes its grandchild's pid into, removed on drop so a failing
/// run does not leave the report behind for the next one to read.
struct PidReport {
    path: PathBuf,
}

impl PidReport {
    fn new(label: &str) -> Self {
        let path = std::env::temp_dir().join(format!(
            "env-manager-tree-{label}-{}.txt",
            uuid::Uuid::new_v4()
        ));
        Self { path }
    }

    /// The grandchild's pid once the stub has written it. `None` while the file is still
    /// missing or half-written, which is the normal state for the first few hundred ms.
    fn read(&self) -> Option<u32> {
        std::fs::read_to_string(&self.path)
            .ok()?
            .trim()
            .parse()
            .ok()
    }
}

impl Drop for PidReport {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

/// argv for `TREE_STUB_EXE`: start a `ping` grandchild, report its pid, wait for it. The
/// stub therefore stands in for the CLI's provider layer - alive, with a live helper -
/// which is the state a stop used to walk away from.
fn tree_stub_args(report_path: &Path) -> Vec<String> {
    // A single-quoted PowerShell string takes the path verbatim except for `'`, which has to
    // be doubled - a profile directory can legitimately carry one (`C:\Users\O'Brien\...`),
    // and an unescaped quote would end the literal and leave the stub unable to report its
    // pid. `-LiteralPath` keeps `[`/`]` in the same path from being read as a wildcard.
    let report = report_path.display().to_string().replace('\'', "''");
    let script = format!(
        "$p = Start-Process -FilePath ping.exe -ArgumentList '-n','45','127.0.0.1' \
         -PassThru -WindowStyle Hidden; Set-Content -LiteralPath '{report}' -Value $p.Id \
         -Encoding ascii; $p.WaitForExit()"
    );
    vec![
        "-NoProfile".to_string(),
        "-NonInteractive".to_string(),
        "-Command".to_string(),
        script,
    ]
}

/// How often a poll re-runs its probe: short against every budget in this file, long enough
/// that `tasklist` is not spawned hundreds of times in one test.
const POLL_STEP: Duration = Duration::from_millis(100);

/// Re-run `probe` until it yields, bounded so a convergence regression fails the test
/// instead of wedging the runner. `None` means the budget ran out; the caller owns the
/// message, because "the stub never reported a pid" and "the helper outlived the stop" are
/// different failures with different causes.
async fn poll_until<T>(budget: Duration, mut probe: impl FnMut() -> Option<T>) -> Option<T> {
    let deadline = tokio::time::Instant::now() + budget;
    loop {
        if let Some(value) = probe() {
            return Some(value);
        }
        if tokio::time::Instant::now() >= deadline {
            return None;
        }
        tokio::time::sleep(POLL_STEP).await;
    }
}

/// Scenario for #160: the stop has to converge the helper tree, not only the direct child.
///
/// The stub is the CLI and the `ping` it starts is the provider helper (`sops`, `op`,
/// `pwsh`). A stop that terminates only the CLI leaves the helper running - that is what
/// the prototype measured against a real sops run, where the helper outlived
/// `SERVICE_STOPPED` by 8s and the decrypted file by 22s. The job object has to take the
/// helper down with the CLI, which is why the assertion is made against the *grandchild's*
/// pid and not against the stub's.
///
/// Ordering this fixture depends on: `run_cli_child` assigns the child to its job before
/// its first `await` - spawn, `child.id()` and the assignment are consecutive synchronous
/// statements - while the stub cannot reach `Start-Process` until Windows has created the
/// process and loaded its shell, hundreds of milliseconds later. Losing that race would
/// put `ping` outside the job and fail this test for the wrong reason, so the failure
/// message names it alongside the real regression; a barrier that removes it entirely
/// would need an "assignment done" signal, which is the pre-assignment window documented
/// on `run_cli_child` and cannot be closed from the test side.
#[tokio::test]
async fn stop_kills_the_helper_tree_not_just_the_direct_child() {
    let report = PidReport::new("helper");
    let shutdown = std::sync::Arc::new(CancellationToken::new());
    let token = shutdown.clone();
    let report_path = report.path.clone();

    let in_flight = tokio::spawn(async move {
        let args = tree_stub_args(&report_path);
        let argv: Vec<&str> = args.iter().map(|a| a.as_str()).collect();
        run_cli_child(TREE_STUB_EXE, &argv, &token).await
    });

    // The helper has to be up before the stop lands, otherwise "the pid disappeared" would
    // only mean it never started. PowerShell needs a moment to load and start ping.
    let helper_pid = poll_until(ALWAYS_TOO_LONG, || report.read())
        .await
        .expect(
            "the stub never reported its grandchild's pid; the tree-kill assertion would \
             have nothing to measure and must not pass silently",
        );
    assert!(
        pid_is_running(helper_pid),
        "the liveness probe must see the grandchild {helper_pid} alive before the stop, or \
         the assertion after it proves nothing"
    );

    shutdown.cancel();
    let outcome = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect("the stop must terminate and reap the stub inside SHUTDOWN_DRAIN")
        .expect("the rotate call task must not panic")
        .expect("spawn the stub rotate child");
    assert!(
        matches!(outcome, ChildOutcome::Interrupted { .. }),
        "a stop during the rotation must report the child as interrupted"
    );

    // The job handle is closed by the time `run_cli_child` returns, but termination is
    // asynchronous, so the ticket's 5s budget is spent polling rather than on one read.
    let gone = poll_until(TREE_KILL_BUDGET, || {
        (!pid_is_running(helper_pid)).then_some(())
    })
    .await;
    assert!(
        gone.is_some(),
        "the stub's grandchild {helper_pid} outlived the stop by more than \
         {TREE_KILL_BUDGET:?}: either the child never joined its job object (a `job object` \
         warning in the service log names the call that failed) or the tree kill regressed \
         to the direct child only"
    );
}
