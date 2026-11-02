// Post-exit pipe drain of a delegated CLI rotate (#192).
//
// The exit branch of `reconcile::run_cli_child` used to `await` both pipe readers without
// any bound, on the untested assumption that a provider helper cannot inherit the CLI's
// stdout/stderr handles. This file drives a stub that starts a `ping` grandchild with
// `cmd /c start /b` before it exits: Windows creates that child with handle inheritance
// on, so the CLI is gone while the grandchild still holds both write ends. `start /b` is
// the mechanism, not an accident of the fixture. The grandchild is also in the
// delegation's kill-on-close job, so it dies when `spawn_cli_child` drops the job handle;
// no test waits the 30s helper out.
//
// Both budgets are injected through `run_cli_child` and `rotate_mount_at_with_pipe_drain`,
// the same convention `hang_budget` uses: a bounded wait CI cannot shorten is a bounded
// wait CI cannot test. The stop / hang / write-back fixtures pass the production default
// explicitly and keep every assertion they had, so those assertions are not weakened.
//
// Isolation: temp mount files under `%TEMP%`, removed on drop. `refresh_mount_at` takes
// the fixture path, so no real `%LOCALAPPDATA%` / `ProgramData` state is read or written.

use std::io::Write;
use std::sync::Mutex;
use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::reconcile::{
    refresh_mount_at, rotate_mount_at_with_pipe_drain, rotation_in_flight, run_cli_child,
    ChildOutcome, PIPE_DRAIN_ON_EXIT, ROTATE_HANG_BUDGET,
};
use crate::rotate_stop_tests::MountFixture;

/// The stub "CLI": `cmd.exe` starts a `ping` grandchild in the background and exits.
/// `/s` makes cmd strip the quotes Rust puts around the command string; `/d` keeps
/// AutoRun commands out of the fixture. `start /b` is what leaves the inherited pipe
/// handles open after cmd is gone: cmd creates the child with handle inheritance on.
const STUB_EXE: &str = "cmd.exe";
const PIPE_HOLDING_ARGS: [&str; 4] = [
    "/d",
    "/s",
    "/c",
    "start /b ping.exe -n 30 127.0.0.1",
];
/// The control stub: same shell, no children, so both pipes close the moment it exits.
const FAST_ARGS: [&str; 4] = ["/d", "/s", "/c", "echo rotated"];

/// Injected post-exit drain: long enough for `ping` to start and hold the handles,
/// short enough that CI never waits the 30s helper out.
const SHORT_PIPE_DRAIN: Duration = Duration::from_millis(400);
/// A longer injected drain for the recovery test, so the "stands down" half has a
/// measurable window without asking CI to wait a real duration either.
const RECOVERY_DRAIN: Duration = Duration::from_secs(1);

/// Every wait in this file is bounded, so a regression fails the test instead of
/// wedging the runner until the job timeout. 20s is far short of the 30s helper.
const ALWAYS_TOO_LONG: Duration = Duration::from_secs(20);
/// End-to-end ceiling for one delegation: injected drain + process startup + reap +
/// scratch reclaim. An order of magnitude below the helper's 30s.
const DRAIN_PATH_BUDGET: Duration = Duration::from_secs(5);
/// How long a second rotation / refresh may take to get through the lock.
const LOCK_FREE_BUDGET: Duration = Duration::from_secs(10);
/// `ROTATIONS_IN_FLIGHT` is process-wide, so another suite's rotation can briefly hold
/// it. Poll for our own contribution to be gone instead of asserting on one read.
const RECOVERY_BUDGET: Duration = Duration::from_secs(10);
const POLL_STEP: Duration = Duration::from_millis(100);

/// A thread-local WARN+ buffer. One plain `#[test]` owns a current-thread runtime and
/// wraps the whole delegation in `tracing::subscriber::with_default`, so every service
/// log line lands on the thread that owns this buffer; other tests keep their own
/// (empty) default and cannot write here.
static LOG_BYTES: Mutex<Vec<u8>> = Mutex::new(Vec::new());

struct LogWriter;

impl Write for LogWriter {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        LOG_BYTES
            .lock()
            .expect("the log buffer must not be poisoned")
            .extend_from_slice(buf);
        Ok(buf.len())
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

fn capture_warns<T>(f: impl FnOnce() -> T) -> (T, String) {
    LOG_BYTES
        .lock()
        .expect("the log buffer must not be poisoned")
        .clear();
    let subscriber = tracing_subscriber::fmt()
        .with_max_level(tracing::Level::WARN)
        .with_ansi(false)
        .with_writer(|| LogWriter)
        .finish();
    let value = tracing::subscriber::with_default(subscriber, f);
    let log = LOG_BYTES
        .lock()
        .expect("the log buffer must not be poisoned");
    let text = String::from_utf8_lossy(&*log).to_string();
    (value, text)
}

/// Scenario: the CLI child exits, but a grandchild that inherited its stdout/stderr
/// keeps both pipes open. The call has to come back inside the injected budget, report
/// `Finished` (the exit status decides the rotation), and leave its own warn in the
/// service log - distinguishable from the stop drain and from a hang-budget expiry.
#[test]
fn an_exited_child_with_open_pipes_returns_within_the_drain_budget() {
    let ((outcome, elapsed), log) = capture_warns(|| {
        let rt = tokio::runtime::Builder::new_current_thread()
            .enable_all()
            .build()
            .expect("build the test runtime");
        rt.block_on(async {
            let shutdown = CancellationToken::new();
            let started = tokio::time::Instant::now();
            let outcome = tokio::time::timeout(
                ALWAYS_TOO_LONG,
                run_cli_child(
                    STUB_EXE,
                    &PIPE_HOLDING_ARGS,
                    &shutdown,
                    ROTATE_HANG_BUDGET,
                    SHORT_PIPE_DRAIN,
                ),
            )
            .await;
            (outcome, started.elapsed())
        })
    });

    let outcome = outcome
        .expect("the post-exit drain must be bounded: the 20s outer timeout fired first")
        .expect("spawn the pipe-holding stub");
    assert!(
        matches!(
            outcome,
            ChildOutcome::Finished {
                success: true,
                output_complete: false,
                ..
            }
        ),
        "a stub whose grandchild holds the pipes must finish with incomplete output, not block or fail"
    );
    assert!(
        elapsed < DRAIN_PATH_BUDGET,
        "the call took {elapsed:?}, past the {DRAIN_PATH_BUDGET:?} a {SHORT_PIPE_DRAIN:?} drain plus spawn, reap and reclaim may cost"
    );
    assert!(
        log.contains("post-exit pipe drain"),
        "the service log needs its own warn for a pipe that stayed open after exit, got: {log}"
    );
    assert!(
        log.contains("did not close within"),
        "the warn must name the budget that expired, got: {log}"
    );
    assert!(
        !log.contains("after it was terminated") && !log.contains("hang budget"),
        "the post-exit warn must be distinguishable from the stop drain and the hang budget, got: {log}"
    );
}

/// The other side of the new bound: a normal child still gives complete output and no
/// truncation. Guards against a drain that always reports `output_complete: false`.
#[tokio::test]
async fn a_child_that_closes_its_pipes_keeps_complete_output() {
    let shutdown = CancellationToken::new();
    let outcome = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        run_cli_child(
            STUB_EXE,
            &FAST_ARGS,
            &shutdown,
            ROTATE_HANG_BUDGET,
            SHORT_PIPE_DRAIN,
        ),
    )
    .await
    .expect("a childless stub must not be held by the post-exit drain")
    .expect("spawn the fast stub");

    match outcome {
        ChildOutcome::Finished {
            stdout,
            output_complete,
            ..
        } => {
            assert!(
                output_complete,
                "a childless stub closes both pipes immediately; the drain must not time out"
            );
            assert!(
                stdout.contains("rotated"),
                "the complete stdout must still reach the caller, got: {stdout}"
            );
        }
        _ => panic!("a child that exited must be reported as Finished"),
    }
}

/// The semantics chosen for #192: a zero exit status is the rotation's answer, even
/// when the diagnostic output is incomplete. The mount is stamped, so the replay rule
/// ("unstamped = not rotated") stays true; a failure here would leave a rotated mount
/// looking untouched and tell the IPC client that completed work failed.
#[tokio::test]
async fn an_incomplete_diagnostic_does_not_fail_a_completed_rotation() {
    let fixture = MountFixture::new("pipe-incomplete");
    let shutdown = CancellationToken::new();

    let info = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        rotate_mount_at_with_pipe_drain(
            &fixture.path,
            STUB_EXE,
            &PIPE_HOLDING_ARGS,
            "mount-1",
            &shutdown,
            ROTATE_HANG_BUDGET,
            SHORT_PIPE_DRAIN,
        ),
    )
    .await
    .expect("the post-exit drain must not hold the rotation")
    .expect("an exited-0 child completes the rotation even when a helper held its pipes");

    assert_eq!(info["rotated"], true);
    assert_ne!(
        info["lastRotatedAt"],
        serde_json::Value::Null,
        "a completed rotation must be recorded, not replayed as a failure"
    );
    let mounts = fixture.mounts();
    assert_ne!(
        mounts[0]["lastRotatedAt"],
        serde_json::Value::Null,
        "the mount file must carry the rotation stamp"
    );
    assert_ne!(mounts[0]["lastFetchedAt"], serde_json::Value::Null);
}

/// The liveness half of #192: the RAII marker is dropped when the call returns, so the
/// shared gate that both `run_reconcile_tick` and `refresh_mount` consult stops standing
/// down. `refresh` is driven against the temp fixture; the tick reads the same
/// `rotation_in_flight()` under the same lock, so pinning the gate pins what it checks.
/// An end-to-end tick assertion needs the mount-path seam tracked separately (#193) -
/// this ticket deliberately does not widen its scope to add it.
#[tokio::test]
async fn the_in_flight_gate_recovers_after_the_drain_budget() {
    let fixture = MountFixture::new("pipe-inflight");
    let shutdown = CancellationToken::new();
    let token = shutdown.clone();
    let path = fixture.path.clone();
    let in_flight = tokio::spawn(async move {
        rotate_mount_at_with_pipe_drain(
            &path,
            STUB_EXE,
            &PIPE_HOLDING_ARGS,
            "mount-1",
            &token,
            ROTATE_HANG_BUDGET,
            RECOVERY_DRAIN,
        )
        .await
    });

    // The child has exited and the post-exit drain is running: the delegation still
    // owns the gate. 300ms is nothing against the injected 1s drain.
    tokio::time::sleep(Duration::from_millis(300)).await;
    let err = tokio::time::timeout(LOCK_FREE_BUDGET, refresh_mount_at(&fixture.path, "mount-1"))
        .await
        .expect("a refresh must answer, not queue behind a delegation")
        .expect_err("a refresh must stand down while the delegation owns the mount file");
    assert!(
        err.contains("in flight"),
        "the refusal has to say why, got: {err}"
    );

    let info = tokio::time::timeout(ALWAYS_TOO_LONG, in_flight)
        .await
        .expect("the post-exit drain must not keep ROTATIONS_IN_FLIGHT at 1 forever")
        .expect("the rotate task must not panic")
        .expect("an exited-0 child completes the rotation");
    assert_eq!(info["rotated"], true);

    let deadline = tokio::time::Instant::now() + RECOVERY_BUDGET;
    let mut recovered = false;
    while tokio::time::Instant::now() < deadline {
        if !rotation_in_flight() {
            match refresh_mount_at(&fixture.path, "mount-1").await {
                Ok(_) => {
                    recovered = true;
                    break;
                }
                // Another suite's rotation started between the snapshot and the call.
                Err(e) if e.contains("in flight") => {}
                Err(e) => panic!("refresh must succeed once the gate is free, got: {e}"),
            }
        }
        tokio::time::sleep(POLL_STEP).await;
    }
    assert!(
        recovered,
        "ROTATIONS_IN_FLIGHT never returned to zero: the tick would skip every interval \
         and refresh would answer 'in flight' forever"
    );
}

/// The production default is the bound, not a second unbounded wait. Keeps a future
/// edit from quietly raising the exit drain to a duration only the 300s tick notices.
#[test]
fn the_production_pipe_drain_is_bounded() {
    assert!(
        PIPE_DRAIN_ON_EXIT <= Duration::from_secs(1),
        "the exit drain must stay a short liveness bound, got {PIPE_DRAIN_ON_EXIT:?}"
    );
}
