// Scratch-root convergence for the delegated CLI's sops plaintext (#177).
//
// Evidence seam: the same one `rotate_stop_tests` uses - `reconcile::run_cli_child`
// driven against a real child process - because the property under test is what happens to
// a directory when that child is terminated, and the layer above it (a literal
// `Stop-Service`) would need an installed service and a rotation slow enough to still be
// in flight when the stop lands.
//
// The stub stands in for the C# provider: it reads `SCRATCH_ENV`, creates one
// `env-manager-sops-<guid>` directory in it with a `secret.json`, records the two paths it
// used, and then waits (interrupted case) or exits (completed case). Recording the file is
// what keeps both tests from passing vacuously: a delegation that never wrote plaintext
// would satisfy "no plaintext is left" without proving anything.
//
// Isolation: every fixture is a uniquely named file under `%TEMP%`, removed on drop. No
// registry, no real `%LOCALAPPDATA%\EnvManager` or `ProgramData\EnvManager` state.

use std::path::{Path, PathBuf};
use std::sync::{Arc, Mutex};
use std::time::Duration;

use tokio_util::sync::CancellationToken;

use crate::reconcile::{run_cli_child, ChildOutcome, PIPE_DRAIN_ON_EXIT, ROTATE_HANG_BUDGET};
use crate::sops_scratch::{SopsScratch, SCRATCH_ENV};
use crate::SHUTDOWN_DRAIN;

/// The stub "CLI": PowerShell, because the fixture needs to write two files and read an
/// environment variable, and `cmd /c` can do neither without a second script on disk.
const STUB_EXE: &str = "powershell.exe";

/// Bounds every wait in this file, so a regression fails the test instead of wedging the
/// runner until the job timeout.
const ALWAYS_TOO_LONG: Duration = Duration::from_secs(20);

/// How often a poll re-runs its probe. Short against the budget above and long enough that
/// the marker file is not read hundreds of times a second.
const POLL_STEP: Duration = Duration::from_millis(100);

/// What the stub left behind, in the order it wrote it: the scratch root the service handed
/// it, then the plaintext file inside that root.
struct StubReport {
    path: PathBuf,
}

impl StubReport {
    fn new(label: &str) -> Self {
        let path = std::env::temp_dir().join(format!(
            "env-manager-scratch-{label}-{}.txt",
            uuid::Uuid::new_v4()
        ));
        Self { path }
    }

    /// `None` while the stub has not written both lines yet, which is the normal state for
    /// the first second of a PowerShell start.
    fn read(&self) -> Option<(String, String)> {
        let raw = std::fs::read_to_string(&self.path).ok()?;
        let mut lines = raw.lines().map(str::trim).filter(|l| !l.is_empty());
        let root = lines.next()?.to_string();
        let file = lines.next()?.to_string();
        Some((root, file))
    }
}

impl Drop for StubReport {
    fn drop(&mut self) {
        let _ = std::fs::remove_file(&self.path);
    }
}

/// argv for `STUB_EXE`.
///
/// `wait_on_ping` is the difference between the two scenarios: `true` keeps the stub alive
/// (so a stop can land on it), `false` lets it exit 0 (the uninterrupted rotation).
/// `echo_stderr` writes one line to stderr before the report file, so a test that reacts to
/// the report knows the line is already in the pipe - what it then asserts is "captured
/// before the budget", not lucky timing.
fn stub_args(report: &Path, wait_on_ping: bool, echo_stderr: bool) -> Vec<String> {
    // A single-quoted PowerShell literal takes the path verbatim except for `'`, which has
    // to be doubled; `-LiteralPath` keeps `[`/`]` in the same path from being read as a
    // wildcard.
    let report = report.display().to_string().replace('\'', "''");
    let tail = if wait_on_ping {
        "ping -n 30 127.0.0.1 | Out-Null"
    } else {
        "Start-Sleep -Milliseconds 1"
    };
    let stderr_line = if echo_stderr {
        "[Console]::Error.WriteLine('scratch-root fallback probe'); "
    } else {
        ""
    };
    let script = format!(
        "$root = $env:{env}; \
         if (-not $root) {{ exit 3 }}; \
         $dir = Join-Path $root ('env-manager-sops-' + [guid]::NewGuid().ToString('N')); \
         New-Item -ItemType Directory -Path $dir -Force | Out-Null; \
         $file = Join-Path $dir 'secret.json'; \
         Set-Content -LiteralPath $file -Value '{{\"value\":\"probe\"}}' -Encoding ascii; \
         if (-not (Test-Path -LiteralPath $file)) {{ exit 4 }}; \
         {stderr_line}\
         Set-Content -LiteralPath '{report}' -Value @($root, $file) -Encoding ascii; \
         {tail}",
        env = SCRATCH_ENV,
        report = report,
        stderr_line = stderr_line,
        tail = tail
    );
    vec![
        "-NoProfile".to_string(),
        "-NonInteractive".to_string(),
        "-Command".to_string(),
        script,
    ]
}

/// Re-run `probe` until it yields, bounded so a regression fails the test instead of
/// hanging until the job timeout.
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

/// The root the service hands over is its own, not the provider's fallback: the assertion
/// would otherwise pass on a delegation that ignored the variable entirely.
fn assert_service_owned_root(root: &str) {
    let root = Path::new(root);
    assert!(
        root.starts_with(std::env::temp_dir()),
        "the scratch root {root:?} must sit under the service identity's %TEMP%"
    );
    let name = root
        .file_name()
        .and_then(|n| n.to_str())
        .unwrap_or_default();
    assert!(
        name.starts_with("env-manager-rotate-scratch-"),
        "the scratch root {root:?} must carry the service's own prefix, not the provider's"
    );
}

/// Scenario: the rotation completes. Acceptance criterion "未被打断的 rotate 也不留残留" -
/// the normal path must not leak either, because a delete that only works on the abort path
/// would be a different bug wearing the same name.
#[tokio::test]
async fn completed_delegation_reclaims_the_scratch_root() {
    let report = StubReport::new("completed");
    let argv = stub_args(&report.path, false, false);
    let argv_refs: Vec<&str> = argv.iter().map(|a| a.as_str()).collect();
    let shutdown = CancellationToken::new();

    let outcome = tokio::time::timeout(
        ALWAYS_TOO_LONG,
        run_cli_child(
            STUB_EXE,
            &argv_refs,
            &shutdown,
            ROTATE_HANG_BUDGET,
            PIPE_DRAIN_ON_EXIT,
        ),
    )
    .await
    .expect("a stub that exits must not be held by the delegation")
    .expect("spawn the stub rotate child");
    assert!(
        matches!(outcome, ChildOutcome::Finished { success: true, .. }),
        "the stub is expected to exit 0; a non-zero exit means it never wrote its plaintext \
         (its own Test-Path guard) or never saw {SCRATCH_ENV}"
    );

    let (root, file) = report
        .read()
        .expect("the stub must record the root and the plaintext file it wrote");
    assert_service_owned_root(&root);
    assert!(
        !Path::new(&root).exists(),
        "the scratch root {root} outlived a completed delegation: the provider's own \
         `finally` deleted {file}, and the service root holding it has to go too"
    );
}

/// Scenario for the ticket's evidence criterion: a stop lands on a rotation that is already
/// holding plaintext on disk. The CLI is terminated before its `finally`, so only the
/// service can remove the file - and the removal has to happen before `run_cli_child`
/// returns, inside the window the stop path has.
///
/// What this measures is the common case: the stub writes its file and releases the handle
/// before the stop lands, so the delete meets nothing. A helper that still owns the handle
/// after the job handle has closed is the bound `service/src/sops_scratch.rs` records -
/// retried for about a second, then logged at error level - and is asserted separately by
/// `reclaim_logs_when_the_tree_cannot_be_removed`.
#[tokio::test]
async fn interrupted_delegation_reclaims_the_scratch_root() {
    let report = StubReport::new("interrupted");
    let argv = stub_args(&report.path, true, false);
    let shutdown = Arc::new(CancellationToken::new());
    let token = shutdown.clone();

    let in_flight = tokio::spawn(async move {
        let argv_refs: Vec<&str> = argv.iter().map(|a| a.as_str()).collect();
        run_cli_child(
            STUB_EXE,
            &argv_refs,
            &token,
            ROTATE_HANG_BUDGET,
            PIPE_DRAIN_ON_EXIT,
        )
        .await
    });

    // The plaintext has to be on disk *before* the stop, otherwise "it is gone afterwards"
    // only means it was never written.
    let reported = poll_until(ALWAYS_TOO_LONG, || report.read()).await.expect(
        "the stub never recorded its scratch root; the assertion below would prove nothing",
    );
    let (root, file) = reported;
    assert_service_owned_root(&root);
    assert!(
        Path::new(&file).exists(),
        "the stub must have plaintext on disk before the stop lands, or the reclaim this \
         test measures is not the one #177 is about: {file}"
    );

    shutdown.cancel();
    let outcome = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect(
            "the stop, including the scratch reclaim, must finish inside SHUTDOWN_DRAIN: \
             waiting the stub out would take 30s and stall the handler join in \
             `ipc::accept_loop`",
        )
        .expect("the rotate call task must not panic")
        .expect("spawn the stub rotate child");
    assert!(
        matches!(outcome, ChildOutcome::Interrupted { .. }),
        "a stop during the rotation must report the child as interrupted"
    );

    assert!(
        !Path::new(&root).exists(),
        "the scratch root {root} survived the stop: {file} is the plaintext that \
         `SopsProvider` would have deleted in its `finally`, which a terminated CLI never runs"
    );
}

/// The stop drain keeps what already arrived: a helper that writes its line and then holds
/// the pipe open past the budget must not cost that line. The stub writes to stderr *before*
/// it publishes its report, so the cancel below can only land after the line is in the pipe;
/// without the incremental reader the aborted task would take it down with it, which is the
/// loss `docs/architecture.md` now refuses to promise away.
#[tokio::test(flavor = "current_thread")]
async fn interrupted_delegation_logs_stderr_that_arrived_before_the_budget() {
    let report = StubReport::new("stderr-drain");
    let argv = stub_args(&report.path, true, true);
    let shutdown = Arc::new(CancellationToken::new());
    let token = shutdown.clone();

    let sink = LogSink(Arc::new(Mutex::new(Vec::new())));
    let guard = tracing::subscriber::set_default(
        tracing_subscriber::fmt()
            .with_writer(sink.clone())
            .with_ansi(false)
            .finish(),
    );

    let in_flight = tokio::spawn(async move {
        let argv_refs: Vec<&str> = argv.iter().map(|a| a.as_str()).collect();
        run_cli_child(
            STUB_EXE,
            &argv_refs,
            &token,
            ROTATE_HANG_BUDGET,
            PIPE_DRAIN_ON_EXIT,
        )
        .await
    });

    poll_until(ALWAYS_TOO_LONG, || report.read())
        .await
        .expect("the stub never reported, so its stderr line was never written either");

    shutdown.cancel();
    let outcome = tokio::time::timeout(SHUTDOWN_DRAIN, in_flight)
        .await
        .expect("the stop must finish inside SHUTDOWN_DRAIN")
        .expect("the rotate call task must not panic")
        .expect("spawn the stub rotate child");
    assert!(matches!(outcome, ChildOutcome::Interrupted { .. }));

    let captured = String::from_utf8_lossy(&sink.0.lock().expect("the log sink lock")).to_string();
    assert!(
        captured.contains("scratch-root fallback probe"),
        "text that arrived before the drain budget must reach the service log even when the \
         pipe never closes; got: {captured}"
    );
    assert!(
        captured.contains("did not close within"),
        "giving up on the drain budget is itself a fact the log has to carry; got: {captured}"
    );

    drop(guard);
}

/// The reclaim on its own, without a child: it deletes what `create` made, including a
/// nested directory tree, and treats an already-removed root as success rather than as an
/// error to retry.
#[tokio::test]
async fn reclaim_removes_the_tree_it_created() {
    let scratch = SopsScratch::create().expect("a scratch root must be creatable in %TEMP%");
    let root = scratch.path().to_path_buf();
    let nested = root.join("env-manager-sops-probe");
    std::fs::create_dir_all(&nested).expect("create the provider-shaped directory");
    std::fs::write(nested.join("secret.json"), "{\"value\":\"probe\"}")
        .expect("write the plaintext fixture");

    scratch.reclaim().await;
    assert!(
        !root.exists(),
        "reclaim must remove the whole tree, not just the top directory: {}",
        root.display()
    );

    // A second reclaim of a path that is already gone is success, not a retry loop: the
    // service can race its own delete with a concurrent delegation's.
    let again = SopsScratch::create().expect("a second scratch root must be creatable");
    let again_root = again.path().to_path_buf();
    std::fs::remove_dir_all(&again_root).expect("remove the second root out from under the guard");
    again.reclaim().await;
    assert!(!again_root.exists());
}

/// A `tracing` writer that keeps whatever was logged, so "删除失败必须留痕" is asserted
/// instead of read out of a comment. `set_default` is thread-local, which is why the tests
/// that use it stay on the single-threaded runtime `#[tokio::test]` gives them.
#[derive(Clone)]
struct LogSink(Arc<Mutex<Vec<u8>>>);

impl std::io::Write for LogSink {
    fn write(&mut self, buf: &[u8]) -> std::io::Result<usize> {
        if let Ok(mut out) = self.0.lock() {
            out.extend_from_slice(buf);
        }
        Ok(buf.len())
    }

    fn flush(&mut self) -> std::io::Result<()> {
        Ok(())
    }
}

impl<'a> tracing_subscriber::fmt::MakeWriter<'a> for LogSink {
    type Writer = LogSink;

    fn make_writer(&'a self) -> Self::Writer {
        LogSink(Arc::clone(&self.0))
    }
}

/// Scenario for the last acceptance criterion: a delete that cannot happen must leave a
/// trace. The file is held open with `share_mode(0)`, so no other handle may open, rename
/// or delete it and the failure is deterministic rather than a race - which is also the
/// shape of the real case, a killed helper still holding the plaintext.
#[tokio::test(flavor = "current_thread")]
async fn reclaim_logs_when_the_tree_cannot_be_removed() {
    use std::os::windows::fs::OpenOptionsExt;

    let scratch = SopsScratch::create().expect("a scratch root must be creatable in %TEMP%");
    let root = scratch.path().to_path_buf();
    let file = root.join("secret.json");
    std::fs::write(&file, "{\"value\":\"probe\"}").expect("write the plaintext fixture");

    let held = std::fs::OpenOptions::new()
        .read(true)
        .share_mode(0)
        .open(&file)
        .expect("hold the plaintext file open without sharing");

    let sink = LogSink(Arc::new(Mutex::new(Vec::new())));
    let guard = tracing::subscriber::set_default(
        tracing_subscriber::fmt()
            .with_writer(sink.clone())
            .with_ansi(false)
            .finish(),
    );

    scratch.reclaim().await;

    let captured = String::from_utf8_lossy(&sink.0.lock().expect("the log sink lock")).to_string();
    assert!(
        captured.contains("could not be removed"),
        "a delete that fails must leave a trace in the service log, never a silent leftover; \
         got: {captured}"
    );
    assert!(
        root.exists(),
        "the assertion above is about the leftover this test creates on purpose; removing it \
         anyway would measure nothing"
    );

    drop(held);
    drop(guard);
    let _ = std::fs::remove_dir_all(&root);
}

/// The other half of the retry story: a delete that only failed because a helper was still
/// winding down must succeed within the budget, not be reported as a leftover. The holder
/// lets go shortly after the first attempts, which is the shape of a killed helper that
/// terminates a fraction of a second after the job handle closes.
#[tokio::test(flavor = "current_thread")]
async fn reclaim_succeeds_once_the_holder_lets_go() {
    use std::os::windows::fs::OpenOptionsExt;

    let scratch = SopsScratch::create().expect("a scratch root must be creatable in %TEMP%");
    let root = scratch.path().to_path_buf();
    let file = root.join("secret.json");
    std::fs::write(&file, "{\"value\":\"probe\"}").expect("write the plaintext fixture");

    let held = std::fs::OpenOptions::new()
        .read(true)
        .share_mode(0)
        .open(&file)
        .expect("hold the plaintext file open without sharing");

    // 100ms lands after the first delete attempt and long before the budget runs out; if a
    // loaded runner delays it past the first attempt anyway, the assertion below still
    // measures the property it is about.
    std::thread::spawn(move || {
        std::thread::sleep(Duration::from_millis(100));
        drop(held);
    });

    scratch.reclaim().await;
    assert!(
        !root.exists(),
        "a holder that releases within the retry budget must not leave the root behind: {}",
        root.display()
    );
}

/// Dropping without reclaiming is the abandoned-future path - a connection handler killed
/// while `reclaim` waits out a helper. `Drop` has to make its own attempt and leave a trace
/// when that fails too, or the tree is lost in silence again.
#[test]
fn drop_logs_when_the_last_attempt_also_fails() {
    use std::os::windows::fs::OpenOptionsExt;

    let scratch = SopsScratch::create().expect("a scratch root must be creatable in %TEMP%");
    let root = scratch.path().to_path_buf();
    let file = root.join("secret.json");
    std::fs::write(&file, "{\"value\":\"probe\"}").expect("write the plaintext fixture");

    let held = std::fs::OpenOptions::new()
        .read(true)
        .share_mode(0)
        .open(&file)
        .expect("hold the plaintext file open without sharing");

    let sink = LogSink(Arc::new(Mutex::new(Vec::new())));
    let guard = tracing::subscriber::set_default(
        tracing_subscriber::fmt()
            .with_writer(sink.clone())
            .with_ansi(false)
            .finish(),
    );

    // No `reclaim` call at all: the shape of a future dropped before one could finish.
    drop(scratch);

    let captured = String::from_utf8_lossy(&sink.0.lock().expect("the log sink lock")).to_string();
    assert!(
        captured.contains("was left behind"),
        "a scratch root that cannot be deleted on drop must leave a trace, never a silent \
         leftover; got: {captured}"
    );

    drop(held);
    drop(guard);
    let _ = std::fs::remove_dir_all(&root);
}

// Startup sweep of leftovers (#219). Evidence seam: `sops_scratch::sweep_stale_roots`
// driven against real directories under `%TEMP%`, plus one leg through `run_server` itself,
// because the property under test is what a service start does to directories a previous
// instance left behind. The race half of the ticket - "this instance's own roots are never
// swept" - is not a timestamp property here but an ordering property, pinned by the call's
// placement between the RUNNING report and the task spawns; the run_server leg below
// exercises that exact sequence on a stale and a fresh fixture.

/// The two prefixes the sweep owns, spelled out so a rename in `sops_scratch` fails these
/// tests instead of silently orphaning one family.
const ROOT_PREFIX: &str = "env-manager-rotate-scratch-";
const LEGACY_PREFIX: &str = "env-manager-sops-";

/// Create a fixture root with plaintext inside, the shape both families take on disk.
fn make_root(prefix: &str, label: &str) -> PathBuf {
    let path = std::env::temp_dir().join(format!("{prefix}{}-{label}", uuid::Uuid::new_v4()));
    std::fs::create_dir_all(&path).expect("create the fixture root");
    std::fs::write(path.join("secret.json"), "{\"value\":\"probe\"}")
        .expect("write the fixture plaintext");
    path
}

/// Backdate a fixture's creation time past the sweep threshold. PowerShell's
/// `SetCreationTimeUtc` is the lever that needs no new dependency; the callers assert on
/// the effect (a deletion or a survival), so a broken lever fails the test rather than
/// passing it vacuously.
fn backdate_creation(path: &Path, hours_ago: i64) {
    let script = format!(
        "[System.IO.Directory]::SetCreationTimeUtc('{}', (Get-Date).ToUniversalTime().AddHours(-{}))",
        path.display().to_string().replace('\'', "''"),
        hours_ago
    );
    let status = std::process::Command::new("powershell.exe")
        .args(["-NoProfile", "-NonInteractive", "-Command", &script])
        .status()
        .expect("run the backdating PowerShell");
    assert!(status.success(), "backdating the fixture creation time");
}

/// One production-shaped sweep call over the real `%TEMP%`, so each test reads as the
/// behaviour it pins rather than as argument plumbing.
fn sweep_now() {
    crate::sops_scratch::sweep_stale_roots(
        &std::env::temp_dir(),
        crate::sops_scratch::SWEEP_MIN_AGE,
    );
}

/// Scenario for the ticket's evidence criterion, stale half: an expired service root and an
/// expired pre-#177 provider directory are both gone after one sweep. The legacy family is
/// the one no code path ever reclaimed, which is why it gets its own fixture instead of
/// sharing the service root's.
#[test]
fn sweep_removes_stale_roots_of_both_families() {
    let stale_root = make_root(ROOT_PREFIX, "sweep-stale");
    let stale_legacy = make_root(LEGACY_PREFIX, "sweep-stale");
    backdate_creation(&stale_root, 25);
    backdate_creation(&stale_legacy, 25);

    sweep_now();

    assert!(
        !stale_root.exists(),
        "an expired service scratch root must be removed by the startup sweep: {}",
        stale_root.display()
    );
    assert!(
        !stale_legacy.exists(),
        "an expired pre-#177 provider directory must be removed by the same sweep: {}",
        stale_legacy.display()
    );
}

/// The threshold half: roots inside the 24h window survive, because a root younger than a
/// live delegation's maximum lifetime cannot be assumed orphaned.
#[test]
fn sweep_spares_roots_inside_the_threshold() {
    let fresh_root = make_root(ROOT_PREFIX, "sweep-fresh");
    let fresh_legacy = make_root(LEGACY_PREFIX, "sweep-fresh");

    sweep_now();

    assert!(
        fresh_root.exists(),
        "a root inside the age threshold must survive the sweep: {}",
        fresh_root.display()
    );
    assert!(
        fresh_legacy.exists(),
        "the threshold applies to the legacy family too: {}",
        fresh_legacy.display()
    );

    let _ = std::fs::remove_dir_all(&fresh_root);
    let _ = std::fs::remove_dir_all(&fresh_legacy);
}

/// "跳过占用中": a stale root whose plaintext file is held open without sharing is skipped,
/// and the skip is logged at error level with the path - the sweep runs at startup where no
/// window closes behind it, so it does not retry what the next start can sweep again.
#[test]
fn sweep_skips_an_occupied_stale_root_and_logs_it() {
    use std::os::windows::fs::OpenOptionsExt;

    let root = make_root(ROOT_PREFIX, "sweep-occupied");
    let file = root.join("secret.json");
    let held = std::fs::OpenOptions::new()
        .read(true)
        .share_mode(0)
        .open(&file)
        .expect("hold the plaintext file open without sharing");
    // Backdate only after the handle is held: from that moment every sweep - this one and
    // any running concurrently in the test binary - sees a stale root it cannot delete, so
    // no parallel runner can remove the fixture out from under this test. Backdating first
    // would leave a window where the fixture is stale and unheld, and another test's
    // `run_server` sweep could delete it whole.
    backdate_creation(&root, 25);

    let sink = LogSink(Arc::new(Mutex::new(Vec::new())));
    let guard = tracing::subscriber::set_default(
        tracing_subscriber::fmt()
            .with_writer(sink.clone())
            .with_ansi(false)
            .finish(),
    );

    sweep_now();

    let captured = String::from_utf8_lossy(&sink.0.lock().expect("the log sink lock")).to_string();
    assert!(
        captured.contains("ERROR"),
        "the spec asks for an error-level trace; the line must not downgrade to warn/info; \
         got: {captured}"
    );
    assert!(
        captured.contains("could not be removed at startup"),
        "an occupied leftover must leave a trace naming the path, never a silent skip; \
         got: {captured}"
    );
    assert!(
        root.exists(),
        "the occupied root must survive the sweep: the assertion above is about the \
         leftover this test creates on purpose, and removing it anyway would measure nothing"
    );

    drop(held);
    drop(guard);
    let _ = std::fs::remove_dir_all(&root);
}

/// The sweep is a prefix scanner over a shared directory, so its blast radius is pinned:
/// a directory outside the two families (the shape `StubReport` uses for its marker files)
/// and a non-directory carrying a swept prefix both survive untouched.
#[test]
fn sweep_leaves_unrelated_names_alone() {
    let unrelated = make_root("env-manager-scratch-sweep-guard-", "unrelated");
    let prefixed_file = std::env::temp_dir().join(format!("{LEGACY_PREFIX}{}.txt", uuid::Uuid::new_v4()));
    std::fs::write(&prefixed_file, "not a plaintext root")
        .expect("write the same-prefixed file fixture");

    sweep_now();

    assert!(
        unrelated.exists(),
        "a directory outside the two swept families must never be touched: {}",
        unrelated.display()
    );
    assert!(
        prefixed_file.exists(),
        "a file with a swept prefix is not a plaintext root and must not be deleted: {}",
        prefixed_file.display()
    );

    let _ = std::fs::remove_dir_all(&unrelated);
    let _ = std::fs::remove_file(&prefixed_file);
}

/// The run_server leg: a service start removes a stale root and spares a fresh one, through
/// the same body the SCM drives. This is also the only test that observes the placement the
/// ordering guarantee rests on - the sweep runs inside the body, between the report point
/// and the task spawns, so the deletion happens without any test-side prodding.
#[tokio::test]
async fn service_start_sweeps_stale_roots_and_spares_fresh_ones() {
    let stale = make_root(ROOT_PREFIX, "sweep-startup");
    let fresh = make_root(ROOT_PREFIX, "sweep-startup-fresh");
    backdate_creation(&stale, 25);

    let shutdown = Arc::new(CancellationToken::new());
    let server = tokio::spawn(crate::run_server(
        r"\\.\pipe\EnvManager.TestSweepStartup",
        shutdown.clone(),
        || true,
    ));

    poll_until(ALWAYS_TOO_LONG, || (!stale.exists()).then_some(()))
        .await
        .expect("the service start never removed the stale root; the sweep did not run");

    assert!(
        fresh.exists(),
        "a root inside the age threshold must survive a service start: {}",
        fresh.display()
    );

    shutdown.cancel();
    assert_eq!(
        tokio::time::timeout(ALWAYS_TOO_LONG, server)
            .await
            .expect("the server body must return after shutdown")
            .expect("server task"),
        crate::ServerExit::Requested,
    );

    let _ = std::fs::remove_dir_all(&fresh);
}
