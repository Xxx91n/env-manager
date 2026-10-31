// The reconcile lock is not held across a delegated rotation's child wait (#161).
//
// Why this is measured through the public seam instead of the static: `RECONCILE_LOCK` is a
// private `tokio::sync::Mutex` with no test-only accessor, and the property that matters is
// observable without one - if the lock were held for the whole rotation, a second rotation
// could not get through while the first one's child is still running. So the fixture is one
// slow rotation in flight plus one fast rotation that has to complete in the meantime.
//
// Separate module from `rotate_stop_tests` on purpose: that file pins how a rotation ends
// when a stop lands, this one pins only what the lock covers while it runs - that a second
// rotation is not blocked behind the first one's child, and that the other writer of the same
// file (`refresh_mount`) refuses instead of saving a pre-rotation snapshot over it.
//
// Isolation: temp mount files under `%TEMP%`, removed on drop; no registry, no real
// `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::reconcile::{refresh_mount, rotate_mount_at, ROTATE_HANG_BUDGET};
use crate::rotate_stop_tests::MountFixture;
use crate::SHUTDOWN_DRAIN;

/// The rotation that is already in flight: one `ping.exe` doing 30 one-second round trips,
/// the same stub family the stop-path suite uses. Long enough that a second rotation has to
/// overlap it, and childless, so nothing about this test depends on a process tree.
const STUB_EXE: &str = "ping.exe";
const STUB_SLOW_ARGS: [&str; 3] = ["-n", "30", "127.0.0.1"];

/// How long the second rotation may take to get through the lock. Far above the milliseconds
/// `cmd /c echo` needs and far below the 30s stub, so "blocked" and "not blocked" cannot be
/// confused with a slow runner.
const LOCK_FREE_BUDGET: Duration = Duration::from_secs(10);

/// Scenario for #161: a slow rotation must not starve the reconcile tick behind it.
///
/// Both calls queue on the same `RECONCILE_LOCK` that `run_reconcile_tick` takes. While the
/// lock was held across the child wait, the second rotation below could not start until the
/// first child exited - and with a wedged child, never. The assertion is therefore "the
/// second rotation completed while the first one is still running", which is exactly the
/// property the tick depends on.
#[tokio::test]
async fn a_slow_rotation_does_not_hold_the_reconcile_lock() {
    let slow = MountFixture::new("lock-slow");
    let fast = MountFixture::new("lock-fast");
    let slow_shutdown = CancellationToken::new();
    let token = slow_shutdown.clone();
    let slow_path = slow.path.clone();

    let in_flight = tokio::spawn(async move {
        rotate_mount_at(
            &slow_path,
            STUB_EXE,
            &STUB_SLOW_ARGS,
            "mount-1",
            &token,
            ROTATE_HANG_BUDGET,
        )
        .await
    });

    // Let the first rotation reach its child wait. 300ms is nothing against the 30s stub, and
    // the call has spawned by then, so the ordering under test is the real one rather than a
    // lock that was never contended.
    tokio::time::sleep(Duration::from_millis(300)).await;

    let fast_shutdown = CancellationToken::new();
    let fast_result = tokio::time::timeout(
        LOCK_FREE_BUDGET,
        rotate_mount_at(
            &fast.path,
            "cmd.exe",
            &["/c", "echo", "rotated"],
            "mount-1",
            &fast_shutdown,
            ROTATE_HANG_BUDGET,
        ),
    )
    .await
    .expect(
        "a rotation in flight must not hold RECONCILE_LOCK: the second rotation never got \
         through, which is the starvation this ticket removes",
    )
    .expect("the fast stub exits 0, so the second rotation completes");
    assert_eq!(
        fast_result["rotated"], true,
        "the second rotation must have completed, not merely returned"
    );

    assert!(
        !in_flight.is_finished(),
        "the first rotation has to still be in flight when the second one completes, \
         otherwise this test only proves the lock was never contended"
    );

    // End the slow rotation instead of waiting the 30s stub out.
    slow_shutdown.cancel();
    let err = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect("the stop must terminate and reap the slow child inside SHUTDOWN_DRAIN")
        .expect("the rotate task must not panic")
        .expect_err("an interrupted rotation must fail for its caller");
    assert!(
        err.contains("interrupted by service stop"),
        "the slow rotation must end as interrupted, not as a completed rotation, got: {err}"
    );
}

/// Scenario for #161: the *other* writer of the mount file stands down instead of saving a
/// snapshot it took before the child rewrote the envelopes.
///
/// Driven through a real rotation rather than a test-only marker, so what is asserted is the
/// production path. `refresh_mount` answers before it touches `%LOCALAPPDATA%`, which is why
/// this half needs no fixture: the assertion is the refusal itself. It also pins the "fails
/// fast" half - the old lock made this call block for the whole delegation, which for a wedged
/// child meant until it died.
#[tokio::test]
async fn refresh_stands_down_while_a_rotation_is_in_flight() {
    let slow = MountFixture::new("refresh-stand-down");
    let shutdown = CancellationToken::new();
    let token = shutdown.clone();
    let slow_path = slow.path.clone();

    let in_flight = tokio::spawn(async move {
        rotate_mount_at(
            &slow_path,
            STUB_EXE,
            &STUB_SLOW_ARGS,
            "mount-1",
            &token,
            ROTATE_HANG_BUDGET,
        )
        .await
    });

    // The same ordering the lock test relies on: the rotation has reached its child wait.
    tokio::time::sleep(Duration::from_millis(300)).await;

    let err = tokio::time::timeout(LOCK_FREE_BUDGET, refresh_mount("mount-1"))
        .await
        .expect("a refresh must answer, not queue behind a rotation that may never finish")
        .expect_err("a refresh must not write the mount file while a rotation owns it");
    assert!(
        err.contains("in flight"),
        "the refusal has to say why, so a caller can retry instead of reporting a broken \
         mount, got: {err}"
    );

    // End the slow rotation instead of waiting the 30s stub out.
    shutdown.cancel();
    let _ = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect("the stop must terminate and reap the slow child inside SHUTDOWN_DRAIN");
}
