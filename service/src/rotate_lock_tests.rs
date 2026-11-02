// The reconcile lock is not held across a delegated rotation's child wait (#161).
//
// Why this is measured through the public seam instead of the static: `RECONCILE_LOCK` is a
// private `tokio::sync::Mutex` with no test-only accessor, and the property that matters is
// observable without one - if the lock were held for the whole rotation, a second rotation
// could not get through while the first one's child is still running. So the fixture is one
// slow rotation in flight plus one fast rotation that has to complete in the meantime.
//
// Separate module from `rotate_stop_tests` on purpose: that file pins how a rotation ends
// when a stop lands, this one pins only what the lock and the in-flight gate cover while it
// runs - that a second rotation is not blocked behind the first one's child, and that the
// other two writers of the same file (`refresh_mount` and `run_reconcile_tick_at`) stand down
// instead of saving a pre-rotation snapshot over it, then resume stamping once no rotation is
// in flight (#193).
//
// Isolation: temp mount files under `%TEMP%`, removed on drop; no registry, no real
// `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::reconcile::{
    refresh_mount, rotate_mount_at, run_reconcile_tick_at, ROTATE_HANG_BUDGET,
};
use crate::rotate_stop_tests::{MountFixture, ROTATION_SUITE_GATE};
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
    let _suite = ROTATION_SUITE_GATE.read().await;
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

/// Scenario for #161: `refresh_mount`, one of the two other writers of the mount file, stands
/// down instead of saving a snapshot it took before the child rewrote the envelopes.
///
/// Driven through a real rotation rather than a test-only marker, so what is asserted is the
/// production path: the rotation gets a temp mount file, and `refresh_mount` answers before it
/// reads any state, so the assertion is the refusal itself. The gate is per service, not per
/// mount (`ROTATIONS_IN_FLIGHT` is a process-wide count, and a refresh saves the whole mount
/// file), which is why the id here only names the call. It also pins the "fails fast" half -
/// the old lock made this call block for the whole delegation, which for a wedged child meant
/// until it died.
#[tokio::test]
async fn refresh_stands_down_while_a_rotation_is_in_flight() {
    let _suite = ROTATION_SUITE_GATE.read().await;
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

/// Scenario for #193: the reconcile tick is the third writer of `secretMount.json`, and like
/// `refresh_mount` it saves a whole snapshot. While a delegated rotation is in flight, the tick
/// must skip the interval without touching the file at all - byte-identical on disk and no
/// `lastFetchedAt` stamp - and without blocking behind the child wait; once the rotation ends and
/// releases `RotationInFlight`, the next tick stamps `lastFetchedAt` as usual.
#[tokio::test]
async fn tick_stands_down_while_a_rotation_is_in_flight() {
    let _quiet = ROTATION_SUITE_GATE.write().await;
    let fixture = MountFixture::new("tick-stand-down");
    let shutdown = CancellationToken::new();
    let token = shutdown.clone();
    let mount_path = fixture.path.clone();

    let in_flight = tokio::spawn(async move {
        rotate_mount_at(
            &mount_path,
            STUB_EXE,
            &STUB_SLOW_ARGS,
            "mount-1",
            &token,
            ROTATE_HANG_BUDGET,
        )
        .await
    });

    // Same ordering as the two sibling tests above: the rotation has reached its child wait and
    // holds `RotationInFlight`, while `RECONCILE_LOCK` has been released.
    tokio::time::sleep(Duration::from_millis(300)).await;

    tokio::time::timeout(LOCK_FREE_BUDGET, run_reconcile_tick_at(&fixture.path))
        .await
        .expect("a tick must skip promptly, not queue behind a rotation's child wait")
        .expect("standing down for an in-flight rotation is a clean skip, not a tick error");

    assert!(
        !in_flight.is_finished(),
        "the rotation must still be in flight when the tick returns, otherwise this test never \
         exercised the stand-down branch"
    );
    assert_eq!(
        std::fs::read_to_string(&fixture.path).expect("read the mount file back"),
        fixture.original,
        "a tick that stands down for an in-flight rotation must leave the mount file \
         byte-for-byte untouched"
    );
    let mounts = fixture.mounts();
    assert_eq!(
        mounts[0]["lastFetchedAt"],
        serde_json::Value::Null,
        "a skipped tick must not stamp lastFetchedAt"
    );

    // End the slow rotation instead of waiting the 30s stub out, then prove the gate released:
    // the next tick on the same untouched file stamps `lastFetchedAt` as usual.
    shutdown.cancel();
    let err = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect("the stop must terminate and reap the slow child inside SHUTDOWN_DRAIN")
        .expect("the rotate task must not panic")
        .expect_err("an interrupted rotation must fail for its caller");
    assert!(
        err.contains("interrupted by service stop"),
        "the slow rotation must end as interrupted, got: {err}"
    );
    assert_eq!(
        std::fs::read_to_string(&fixture.path).expect("read the mount file after stop"),
        fixture.original,
        "ending the interrupted rotation must still leave the mount file untouched"
    );

    run_reconcile_tick_at(&fixture.path)
        .await
        .expect("the tick after the rotation ends must succeed");
    let after = fixture.mounts();
    let stamped = after[0]["lastFetchedAt"]
        .as_str()
        .expect("once the rotation ends, the next tick must stamp lastFetchedAt");
    chrono::DateTime::parse_from_rfc3339(stamped)
        .expect("stamped lastFetchedAt must be valid RFC3339");
}

/// Scenario for #193: when no delegated rotation is in flight, `run_reconcile_tick_at` stamps
/// due `Periodic` mounts (`lastFetchedAt` absent or older than `refreshIntervalSeconds`) and
/// leaves fresh or `CreatedOnly` mounts untouched.
///
/// Starts from the exact `MountFixture` shape used in `tick_stands_down_while_a_rotation_is_in_flight`
/// so the two tests form a controlled pair: the only difference between "file untouched" above
/// and "stamp written" here is `ROTATIONS_IN_FLIGHT`. Holds the exclusive `ROTATION_SUITE_GATE`
/// write lock so no parallel `cargo test` worker can raise the process-wide counter mid-call.
#[tokio::test]
async fn tick_stamps_due_periodic_mount_when_no_rotation_is_in_flight() {
    let _quiet = ROTATION_SUITE_GATE.write().await;
    let floor = chrono::Utc::now() - Duration::from_secs(1);
    let fixture = MountFixture::new("tick-due");

    // 1) Initial Periodic mount with no `lastFetchedAt` (exact fixture the stand-down test used):
    //    one quiescent tick must stamp it.
    tokio::time::timeout(LOCK_FREE_BUDGET, run_reconcile_tick_at(&fixture.path))
        .await
        .expect("a quiescent tick must complete promptly")
        .expect("tick on a valid temp mount file must succeed");

    let after_first_raw =
        std::fs::read_to_string(&fixture.path).expect("read the mount file after first tick");
    assert_ne!(
        after_first_raw, fixture.original,
        "a due Periodic mount must be written when no rotation is in flight"
    );
    let first_mounts = fixture.mounts();
    let first_stamp = first_mounts[0]["lastFetchedAt"]
        .as_str()
        .expect("tick must write a string lastFetchedAt timestamp");
    let first_parsed = chrono::DateTime::parse_from_rfc3339(first_stamp)
        .expect("lastFetchedAt written by tick must be valid RFC3339")
        .with_timezone(&chrono::Utc);
    assert!(
        first_parsed >= floor,
        "first tick stamp {first_stamp} predates this test run"
    );

    // 2) A second tick before `refreshIntervalSeconds` (300s) elapses must leave the freshly
    //    stamped file byte-for-byte untouched.
    run_reconcile_tick_at(&fixture.path)
        .await
        .expect("tick on a fresh Periodic mount must succeed");
    assert_eq!(
        std::fs::read_to_string(&fixture.path).expect("read the mount file after fresh tick"),
        after_first_raw,
        "a Periodic mount whose interval has not elapsed must be skipped without rewriting"
    );

    // 3) Backdating `lastFetchedAt` past `refreshIntervalSeconds` makes the Periodic mount due
    //    again, while a `CreatedOnly` sibling in the same file must stay untouched.
    let expired_and_created_only = serde_json::json!([
        {
            "id": "mount-1",
            "provider": "vault-kv2",
            "name": "TEST_TOKEN",
            "scope": "user",
            "refreshPolicy": "Periodic",
            "refreshIntervalSeconds": 300,
            "lastFetchedAt": "2020-01-01T00:00:00Z",
            "createdAt": "2020-01-01T00:00:00Z",
            "schemaVersion": 1
        },
        {
            "id": "mount-2",
            "provider": "dpapi-current-user",
            "name": "LOCAL_SECRET",
            "scope": "user",
            "refreshPolicy": "CreatedOnly",
            "createdAt": "2020-01-01T00:00:00Z",
            "schemaVersion": 1
        }
    ]);
    std::fs::write(
        &fixture.path,
        serde_json::to_string_pretty(&expired_and_created_only).unwrap(),
    )
    .expect("write the expired + CreatedOnly fixture");

    run_reconcile_tick_at(&fixture.path)
        .await
        .expect("tick on an expired Periodic mount must succeed");

    let updated = fixture.mounts();
    assert_eq!(
        updated.len(),
        2,
        "saving refreshed mounts must preserve every sibling entry in secretMount.json"
    );
    let renewed = updated[0]["lastFetchedAt"]
        .as_str()
        .expect("expired Periodic mount must keep a string lastFetchedAt");
    let renewed_parsed = chrono::DateTime::parse_from_rfc3339(renewed)
        .expect("renewed lastFetchedAt must be valid RFC3339")
        .with_timezone(&chrono::Utc);
    assert!(
        renewed_parsed >= floor,
        "an expired Periodic mount must have its lastFetchedAt advanced to the current run, got {renewed}"
    );
    assert_eq!(updated[1]["id"], "mount-2");
    assert_eq!(
        updated[1]["lastFetchedAt"],
        serde_json::Value::Null,
        "a CreatedOnly mount must be ignored by the reconcile tick"
    );
}
