// Hang budget of a delegated CLI rotate (#161): a rotation must not sit in its child forever.
//
// The stop path (#153, #160) answers "the SCM wants us to stop"; this file answers the other
// question the ticket left open - "nobody stopped us, the CLI is simply wedged". Both end the
// same way (terminate, reap, close the job object), which is why `reconcile::run_cli_child`
// shares one convergence path between them, and the assertions here are about what makes the
// budget path *distinguishable*: a `TimedOut` outcome, a message that does not read as a
// service stop, and a mount file the failed rotation never touched.
//
// The budget is injected, never waited out: the stub is a 3000-second `ping`, and every
// assertion below has to hold within seconds of it starting. CI must not pay for the real
// 15 minutes.
//
// Isolation: temp mount files under `%TEMP%`, removed on drop; no registry, no real
// `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::reconcile::{rotate_mount_at, run_cli_child, ChildOutcome};
use crate::rotate_stop_tests::{pid_is_running, MountFixture};

/// The stub "CLI" that never exits on its own: `ping.exe` doing 3000 one-second round trips.
/// Same fixture family as the stop-path suite, opposite intent - there the stop ends it, here
/// the budget has to. Childless on purpose, so nothing about this file depends on a tree.
const STUB_EXE: &str = "ping.exe";
const STUB_HUNG_ARGS: [&str; 3] = ["-n", "3000", "127.0.0.1"];

/// The budget a test can afford: long enough that process startup cannot beat it, short enough
/// that the suite does not notice it.
const SHORT_HANG_BUDGET: Duration = Duration::from_millis(500);

/// Every wait in this file is bounded, so a regression fails the test instead of wedging the
/// runner until the job timeout.
const ALWAYS_TOO_LONG: Duration = Duration::from_secs(20);

/// The longest the budget path may take end to end: the injected budget plus process startup,
/// reaping and the scratch reclaim's retries. An order of magnitude below the 3000s stub, so
/// "the budget fired" cannot be satisfied by "the stub exited".
const BUDGET_PATH_BUDGET: Duration = Duration::from_secs(10);

/// Scenario: the child outlives its hang budget with no stop in sight. The call has to end it
/// and reap it, and has to say so with its own outcome - not with `Interrupted`, which is what
/// the service stop path reports.
#[tokio::test]
async fn a_hung_child_is_terminated_and_reaped_at_the_hang_budget() {
    let shutdown = CancellationToken::new();
    let started = tokio::time::Instant::now();

    let outcome = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        run_cli_child(STUB_EXE, &STUB_HUNG_ARGS, &shutdown, SHORT_HANG_BUDGET),
    )
    .await
    .expect("the hang budget must end the child, not the test's own timeout")
    .expect("spawn the hung stub");

    let ChildOutcome::TimedOut { pid } = outcome else {
        panic!(
            "a child that outlived its hang budget must be reported as timed out, not as \
             finished or interrupted"
        );
    };
    let pid = pid.expect("a spawned child reports its pid");
    // `Child::kill` is TerminateProcess followed by `wait`, so the pid is already gone when
    // the call returns: nothing that can write profiles.json outlives a budget expiry.
    assert!(
        !pid_is_running(pid),
        "the hung CLI child {pid} must be gone by the time the budget path returns; a live \
         pid here means the reap was skipped"
    );
    assert!(
        started.elapsed() < BUDGET_PATH_BUDGET,
        "the budget path took {:?}, which is past the {:?} a 500ms budget plus reaping may \
         cost - the wait was not bounded by the injected budget",
        started.elapsed(),
        BUDGET_PATH_BUDGET
    );
}

/// Scenario: what the caller of a rotation sees when the budget fires. The message has to be
/// distinguishable from "interrupted by service stop", because the two have different causes
/// and different operator responses, and the mount file must be left exactly as it was - a
/// rotation that never completed is replayed, never recorded.
#[tokio::test]
async fn a_hung_rotation_fails_with_its_own_message_and_leaves_the_mount_alone() {
    let fixture = MountFixture::new("hang-budget");
    let shutdown = CancellationToken::new();

    let err = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        rotate_mount_at(
            &fixture.path,
            STUB_EXE,
            &STUB_HUNG_ARGS,
            "mount-1",
            &shutdown,
            SHORT_HANG_BUDGET,
        ),
    )
    .await
    .expect("a hung rotate must converge on its budget, not wait the stub out")
    .expect_err("a rotation that outlived its hang budget must fail for its caller");

    assert!(
        err.contains("hang budget"),
        "the failure has to name the hang budget, got: {err}"
    );
    assert!(
        !err.contains("interrupted by service stop"),
        "a budget expiry is not a service stop and must not be reported as one, got: {err}"
    );

    assert_eq!(
        std::fs::read_to_string(&fixture.path).expect("read the mount file back"),
        fixture.original,
        "a rotation that never completed must not rewrite the mount file at all"
    );
    let mounts = fixture.mounts();
    assert_eq!(
        mounts[0]["lastRotatedAt"],
        serde_json::Value::Null,
        "a rotation ended by its hang budget must not be recorded as done"
    );
    assert_eq!(mounts[0]["lastFetchedAt"], serde_json::Value::Null);
}
