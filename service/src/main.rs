// env-manager-service.exe
// Windows service host for the Env Manager IPC endpoint.
// Background mode uses the launching user's secretMount.json. Service mode has no mount file:
// the old ProgramData design was never implemented, and this service does not fetch secrets
// on a timer.

use std::env;
use std::path::{Path, PathBuf};
use std::sync::Arc;
use std::time::Duration;
use tokio_util::sync::CancellationToken;

/// Budget for draining the task that is still running after one of them stops.
/// Bounded together with `scm::RUNTIME_DRAIN` by `scm::STOP_WAIT_HINT_MS`, which is
/// asserted at compile time. `ipc::accept_loop` drains its in-flight connection
/// handlers inside this same window, so the assert covers the whole stop chain.
pub(crate) const SHUTDOWN_DRAIN: Duration = Duration::from_secs(2);

mod reconcile;
mod ipc;
mod audit_ledger;
mod redaction;
mod process_guard;
mod job_object;
mod sops_tempdir;
mod scm;
#[cfg(test)]
mod resilience_tests;
#[cfg(test)]
mod rotate_stop_tests;
#[cfg(test)]
mod sops_tempdir_tests;
#[cfg(test)]
mod rotate_writeback_tests;
#[cfg(test)]
mod rotate_lock_tests;
#[cfg(test)]
mod rotate_hang_tests;
#[cfg(test)]
mod rotate_pipe_drain_tests;
#[cfg(test)]
mod mount_health_tests;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ServiceMode {
    Service,
    Background,
    Cli,
}

pub const SERVICE_MOUNTS_UNAVAILABLE_MESSAGE: &str =
    "Secret mounts are unavailable in Service mode.";
pub const SERVICE_MOUNTS_UNAVAILABLE_REASON: &str = "serviceMode";

impl ServiceMode {
    /// Resolve from --mode=<x> argv or SCM service args.
    /// Pattern provenance: CONTEXT.md term "A7 (service binary entry surface, photo pattern)" (design-review session; see docs/history/design-review-research.md).
    pub fn resolve(args: &[String]) -> Self {
        for arg in args {
            if let Some(val) = arg.strip_prefix("--mode=") {
                return match val.to_lowercase().as_str() {
                    "service" => ServiceMode::Service,
                    "background" => ServiceMode::Background,
                    "cli" => ServiceMode::Cli,
                    _ => ServiceMode::Service,
                };
            }
        }
        // Default: Background mode (user-launched).
        ServiceMode::Background
    }

    pub fn pipe_endpoint(&self) -> &'static str {
        match self {
            ServiceMode::Service => r"\\.\pipe\EnvManager.Service",
            ServiceMode::Background => r"\\.\pipe\EnvManager.Background",
            ServiceMode::Cli => r"\\.\pipe\EnvManager.Background",
        }
    }
}

/// Process uptime in seconds for heartbeat enrichment.
/// Used by IPC ping/status to help the GUI watchdog distinguish "busy" vs "deadlocked".
pub fn get_process_uptime() -> u64 {
    static START: std::sync::OnceLock<std::time::Instant> = std::sync::OnceLock::new();
    let start = START.get_or_init(|| std::time::Instant::now());
    start.elapsed().as_secs()
}

fn main() {
    // Resolve mode FIRST so we know where to put the log file.
    let args: Vec<String> = env::args().collect();
    let mode = ServiceMode::resolve(&args);

    // Write service logs to a file so Session 0 service output is
    // not lost (stderr goes nowhere in Session 0). In Background mode, write
    // to %LOCALAPPDATA% alongside env-manager.log; in Service mode, write to
    // %ProgramData% alongside the audit ledger. No mount file is stored there.
    let log_dir = match mode {
        ServiceMode::Service => {
            let pd = std::env::var("ProgramData").unwrap_or_else(|_| "C:\\ProgramData".to_string());
            std::path::PathBuf::from(pd).join("EnvManager")
        }
        _ => {
            let la = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".to_string());
            std::path::PathBuf::from(la).join("EnvManager")
        }
    };
    let _ = std::fs::create_dir_all(&log_dir);
    let log_file = log_dir.join("env-manager-service.log");
    // ponytail: best-effort file open; if it fails, fall back to stderr only.
// tracing + tracing-appender unified logging backend.
// Daily rotation + per-module EnvFilter via RUST_LOG.
let file_appender = tracing_appender::rolling::daily(&log_dir, "env-manager-service.log");
let (non_blocking_file, guard) = tracing_appender::non_blocking(file_appender);
let filter = tracing_subscriber::EnvFilter::try_from_default_env()
    .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info"));
tracing_subscriber::fmt()
    .with_env_filter(filter)
    .with_writer(non_blocking_file)
    .with_ansi(false)
    .with_target(true)
    .with_file(false)
    .with_line_number(false)
    .init();
std::mem::forget(guard);
    tracing::info!("env-manager-service starting in {:?} mode", mode);
    if mode == ServiceMode::Service {
        tracing::warn!("{}", SERVICE_MOUNTS_UNAVAILABLE_MESSAGE);
    }

    // Entry guard (A7): refuse direct double-click launch with no --mode.
    // If interactive session and no --mode flag, print guidance and exit.
    if matches!(mode, ServiceMode::Background) && !args.iter().any(|a| a.starts_with("--mode=")) {
        // Check if we're in an interactive session (console attached).
        // In service mode, there's no console. In background mode launched by GUI,
        // the GUI passes --mode=background explicitly.
        // If someone double-clicks with no args at all, we land here as Background default.
        // Allow it but log a warning — background mode is user-launchable.
        tracing::info!("Background mode started by user (no --mode flag). This is acceptable for foreground GUI testing.");
    }

    // Service mode joins the SCM control protocol on this (the process's initial)
    // thread: StartServiceCtrlDispatcherW blocks there for the service's whole life and
    // the SCM calls service_main -- which builds the tokio runtime -- on another thread
    // (#149). The runtime below is only built for a launch the SCM did not initiate,
    // i.e. a console `--mode=service` run used for debugging.
    if matches!(mode, ServiceMode::Service) && scm::dispatch() {
        return;
    }

    // Process hardening (WER, hash, debugger, modules, lock). Service mode runs this
    // inside service_main, after SERVICE_START_PENDING, and only reaches this line when
    // the SCM did not launch the process.
    process_guard::init();

    let rt = match tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
    {
        Ok(rt) => rt,
        Err(e) => {
            tracing::error!("failed to create tokio runtime: {}", e);
            std::process::exit(1);
        }
    };

    rt.block_on(async {
        match mode {
            ServiceMode::Service | ServiceMode::Background => {
                let shutdown = Arc::new(CancellationToken::new());
                let exit = match mode {
                    ServiceMode::Service => {
                        run_server(mode.pipe_endpoint(), shutdown, || true).await
                    }
                    _ => run_background_server(mode.pipe_endpoint(), shutdown).await,
                };
                match exit {
                    ServerExit::Requested => {
                        tracing::info!("service shutting down (all tasks exited)")
                    }
                    other => tracing::error!("server exited without a stop request: {:?}", other),
                }
            }
            ServiceMode::Cli => {
                let pipe = mode.pipe_endpoint();
                if let Err(e) = ipc::cli_gateway(pipe).await {
                    eprintln!("Error: service IPC failed: {}", e);
                    std::process::exit(1);
                }
            }
        }
    });
}

/// Why the server body returned. Service mode maps this to a clean `SERVICE_STOPPED`
/// or to a service error, so an unexpected exit is not reported as a healthy stop.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum ServerExit {
    /// The shutdown token was cancelled: an SCM STOP/SHUTDOWN, an IPC `shutdown`, or a test.
    Requested,
    /// The named-pipe listener could not be created, so nothing was served.
    ListenFailed,
    /// The IPC accept loop returned without a stop request.
    IpcExited,
    /// The periodic process guard returned without a stop request (#294).
    ProcessGuardExited,
}

/// Run the server body used by Service mode: listener, `SERVICE_RUNNING`, then the
/// background tasks (IPC serving and the process guard).
///
/// Ordering is a hard constraint (#149): the named-pipe listener is created first,
/// `on_listening` fires the moment it exists, and only then does any background task
/// start. Service mode reports `SERVICE_RUNNING` from `on_listening`, so any slow work
/// moved ahead of that point brings back the boot-time 1053 failure. `on_listening`
/// returns whether the listener was announced; a `false` means a stop is already
/// pending and the body must not start working.
pub async fn run_server(
    pipe_name: &'static str,
    shutdown: Arc<CancellationToken>,
    on_listening: impl FnOnce() -> bool + Send,
) -> ServerExit {
    tracing::info!("IPC pipe: {}", pipe_name);

    let first = match ipc::listen(pipe_name, true, &shutdown).await {
        Ok(server) => server,
        Err(e) => {
            if shutdown.is_cancelled() {
                return ServerExit::Requested;
            }
            tracing::error!("cannot create the IPC listener on {}: {}", pipe_name, e);
            return ServerExit::ListenFailed;
        }
    };

    // Listen -> report -> background tasks, in that order. Reporting RUNNING is skipped
    // when a stop landed while the listener was being created: the SCM already has
    // STOP_PENDING, and moving its state forward again would be wrong.
    if !on_listening() {
        return ServerExit::Requested;
    }

    // Service-mode startup sweep of leftover sops plaintext roots (#219). Keep it after
    // the RUNNING report so a slow sweep cannot push that report past ServicesPipeTimeout,
    // and before task spawns so no handler can create a root concurrently. Background mode
    // runs the same sweep before starting its IPC task in `run_background_server`.
    // The 24h threshold is a second guard, not the mechanism that protects this instance's
    // roots; placement before handlers is.
    sops_tempdir::sweep_stale_roots(&env::temp_dir(), sops_tempdir::SWEEP_MIN_AGE);

    // Three things the body owns once RUNNING is reported: the pipe listener it just
    // created (handed straight to the IPC task), the IPC task that serves requests on it,
    // and the process guard that checks for an attached debugger on its own interval.
    // The periodic mount sweep that used to be the second task is gone (#294); the guard
    // took its slot, so a stop still converges everything the body started.
    let ipc = tokio::spawn(ipc::serve(pipe_name, first, shutdown.clone()));
    let guard = tokio::spawn(process_guard::debugger_watch_loop(shutdown.clone()));
    run_until_exit(ipc, guard, &shutdown).await
}

/// Run the server body used by Background mode. Reclaim stale scratch roots before
/// starting IPC handlers, so a process killed during rotation leaves no permanent
/// plaintext behind. Unlike Service mode, Background mode has no SCM RUNNING report to
/// protect; its process guard and IPC task start immediately after the sweep.
pub async fn run_background_server(
    pipe_name: &'static str,
    shutdown: Arc<CancellationToken>,
) -> ServerExit {
    tracing::info!("IPC pipe: {}", pipe_name);
    sops_tempdir::sweep_stale_roots(&env::temp_dir(), sops_tempdir::SWEEP_MIN_AGE);
    let ipc = tokio::spawn(ipc::start_ipc_server(pipe_name, shutdown.clone(), false));
    let guard = tokio::spawn(process_guard::debugger_watch_loop(shutdown.clone()));
    run_until_exit(ipc, guard, &shutdown).await
}

/// Wait for either task to finish, then drain the other one under a bounded timeout,
/// so the caller can report a stopped process knowing the pipe handle is closed.
///
/// The IPC task's budget covers its own connection handlers: `ipc::accept_loop` joins
/// them before returning. In Background mode, a rotate handler can own a delegated CLI
/// child, so this keeps that child from outliving process shutdown (#153); Service mode
/// refuses rotate and reports `SERVICE_STOPPED` only after the pipe is closed. The process
/// guard answers the same stop token, so shutdown ends its wait at once (#294).
async fn run_until_exit(
    mut ipc: tokio::task::JoinHandle<()>,
    mut guard: tokio::task::JoinHandle<()>,
    shutdown: &CancellationToken,
) -> ServerExit {
    let finished = tokio::select! {
        _ = &mut ipc => ServerExit::IpcExited,
        _ = &mut guard => ServerExit::ProcessGuardExited,
    };

    // Both loops only return on cancellation in normal operation, so a cancelled
    // token means a stop request (SCM STOP/SHUTDOWN, IPC `shutdown`) rather than a
    // failure of whichever task happened to be observed first.
    let exit = if shutdown.is_cancelled() {
        ServerExit::Requested
    } else {
        finished
    };
    match exit {
        ServerExit::Requested => tracing::info!("shutdown requested, draining tasks"),
        other => tracing::warn!("a server task exited on its own: {:?}", other),
    }

    // Drain the task that is still running. Which one finished first is a race, and
    // re-polling a completed JoinHandle panics, so this keys off `finished`, not `exit`.
    shutdown.cancel();
    let pending = match finished {
        ServerExit::IpcExited => &mut guard,
        _ => &mut ipc,
    };
    if tokio::time::timeout(SHUTDOWN_DRAIN, pending).await.is_err() {
        tracing::warn!("shutdown drain timed out after {:?}", SHUTDOWN_DRAIN);
    }
    exit
}

/// Resolve the user-level secretMount.json path. Service mode has no mount file.
fn secret_mount_path_for(mode: ServiceMode, local_app_data: &Path) -> Option<PathBuf> {
    if mode == ServiceMode::Service {
        return None;
    }

    Some(local_app_data.join("EnvManager").join("secretMount.json"))
}

pub fn secret_mount_path() -> Option<PathBuf> {
    let args: Vec<String> = env::args().collect();
    let mode = ServiceMode::resolve(&args);
    if mode == ServiceMode::Service {
        return None;
    }
    let local_app_data = env::var_os("LOCALAPPDATA")
        .map(PathBuf::from)
        .unwrap_or_else(|| PathBuf::from("."));
    let path = secret_mount_path_for(mode, &local_app_data)?;
    if let Some(dir) = path.parent() {
        std::fs::create_dir_all(dir).ok();
    }
    Some(path)
}

#[cfg(test)]
mod mount_path_tests {
    use super::*;

    #[test]
    fn service_mode_has_no_mount_path() {
        let local_app_data = PathBuf::from("service-local-app-data");
        assert!(secret_mount_path_for(ServiceMode::Service, &local_app_data).is_none());
    }

    #[test]
    fn background_and_cli_modes_use_local_app_data() {
        let local_app_data = PathBuf::from("user-local-app-data");
        let expected = Some(
            local_app_data
                .join("EnvManager")
                .join("secretMount.json"),
        );

        for mode in [ServiceMode::Background, ServiceMode::Cli] {
            assert_eq!(secret_mount_path_for(mode, &local_app_data), expected);
        }
    }
}

#[cfg(test)]
mod startup_contract_tests {
    //! Startup ordering contract for the SCM control protocol (issue #149).
    //!
    //! The SCM RUNNING report hangs off `run_server`'s `on_listening` callback, so
    //! two properties have to hold at that exact point: the named-pipe listener is
    //! already connectable (otherwise the service reports Running while the GUI and
    //! CLI still cannot reach it), and no background task has started yet (otherwise
    //! slow work pushes the report past `ServicesPipeTimeout` and start fails with 1053
    //! again). Only the first property is observable from outside; the second follows
    //! from the report firing before the spawns.
    //!
    //! The stop side of the same contract lives here too (#153): when the body returns,
    //! the caller can report `SERVICE_STOPPED`, so the body must have released the pipe and
    //! joined its connection handlers. A Background-mode rotate handler may own a delegated
    //! CLI child; Service mode refuses rotate.
    use super::*;
    use std::sync::atomic::{AtomicBool, AtomicU8, Ordering};
    use std::time::Duration;

    /// Test-only pipe names so a run never collides with a real service instance.
    const ORDER_PIPE: &str = r"\\.\pipe\EnvManager.TestScmOrdering";
    const RELEASE_PIPE: &str = r"\\.\pipe\EnvManager.TestScmRelease";
    const IN_FLIGHT_PIPE: &str = r"\\.\pipe\EnvManager.TestScmInFlight";
    const ERROR_FILE_NOT_FOUND: i32 = 2;

    fn open(pipe: &str) -> std::io::Result<tokio::net::windows::named_pipe::NamedPipeClient> {
        tokio::net::windows::named_pipe::ClientOptions::new().open(pipe)
    }

    /// Run the server body on `pipe` and block until it reaches the listener stage, which
    /// is the point where the SCM's RUNNING report would fire. Returns the body handle and
    /// its stop token; the listener stage is asserted here because every caller depends on
    /// it and a body that never got there would make each of them fail for the wrong reason.
    async fn spawn_body_until_listening(
        pipe: &'static str,
    ) -> (tokio::task::JoinHandle<ServerExit>, Arc<CancellationToken>) {
        let listening = Arc::new(AtomicBool::new(false));
        let writer = listening.clone();
        let shutdown = Arc::new(CancellationToken::new());
        let server = tokio::spawn(run_server(pipe, shutdown.clone(), move || {
            writer.store(true, Ordering::SeqCst);
            true
        }));
        for _ in 0..100 {
            if listening.load(Ordering::SeqCst) {
                break;
            }
            tokio::time::sleep(Duration::from_millis(50)).await;
        }
        assert!(
            listening.load(Ordering::SeqCst),
            "the server body never reached the listener stage"
        );
        (server, shutdown)
    }

    /// Scenario: the RUNNING report point is reached only once the listener exists.
    #[tokio::test]
    async fn listener_is_connectable_when_the_running_report_fires() {
        // 0 = callback not reached yet, 1 = reached but the pipe refused a client,
        // 2 = reached and a client connected.
        let observed = Arc::new(AtomicU8::new(0));
        let writer = observed.clone();
        let shutdown = Arc::new(CancellationToken::new());
        let server = tokio::spawn(run_server(ORDER_PIPE, shutdown.clone(), move || {
            let reachable = open(ORDER_PIPE).is_ok();
            writer.store(if reachable { 2 } else { 1 }, Ordering::SeqCst);
            true
        }));
        // Same polling budget as the resilience suite's wait_for_pipe.
        for _ in 0..100 {
            if observed.load(Ordering::SeqCst) != 0 {
                break;
            }
            tokio::time::sleep(Duration::from_millis(50)).await;
        }
        assert_eq!(
            observed.load(Ordering::SeqCst),
            2,
            "the pipe listener must be connectable before the RUNNING report fires"
        );

        shutdown.cancel();
        assert_eq!(
            server.await.expect("server task"),
            ServerExit::Requested,
            "a shutdown request must be reported as Requested, not as a failure"
        );
    }

    /// Scenario: `Stop-Service` semantics — after the server body returns, the pipe
    /// instance is gone, so a restarted service (or an anti-squatting probe) can own
    /// the name immediately instead of waiting for the kernel to reap the handle.
    #[tokio::test]
    async fn stopping_releases_the_pipe() {
        let (server, shutdown) = spawn_body_until_listening(RELEASE_PIPE).await;

        shutdown.cancel();
        assert_eq!(server.await.expect("server task"), ServerExit::Requested);

        let err = open(RELEASE_PIPE)
            .err()
            .expect("the pipe must be gone after stop");
        assert_eq!(
            err.raw_os_error(),
            Some(ERROR_FILE_NOT_FOUND),
            "expected the pipe name to be free after stop, got: {err}"
        );
    }

    /// Scenario: shutdown with a connection already accepted and a request still being
    /// served. The body must not return until that handler does, so the caller can report
    /// `SERVICE_STOPPED` only after the pipe is closed. In Background mode a rotate handler
    /// may own the CLI child that writes profiles.json; Service mode refuses rotation (#153).
    ///
    /// Regression pin for the shape before this ticket: the accept loop used to return on
    /// cancellation with the handler still detached, so the body resolved while the
    /// handler was parked and its work outlived shutdown.
    #[tokio::test]
    async fn stop_waits_for_an_in_flight_connection_handler() {
        let (mut server, shutdown) = spawn_body_until_listening(IN_FLIGHT_PIPE).await;

        // A client that connects and stays silent parks `handle_connection` in its read
        // loop. That is the cheapest honest in-flight handler: no real provider rotation
        // is available to a test, and the join under test is the same either way. The
        // sleep lets the accept loop observe the connect and register the handler before
        // the stop lands.
        let client = crate::resilience_tests::open_with_retry(IN_FLIGHT_PIPE).await;
        tokio::time::sleep(Duration::from_millis(200)).await;

        shutdown.cancel();
        // The drain window is `SHUTDOWN_DRAIN`; an eighth of it (5 x 50ms) is spent proving
        // the body is still inside the handler. Polling rather than one long sleep keeps a
        // slow runner from passing the assertion by finishing early, and staying well inside
        // the budget keeps drift from letting the outer drain expire the pin for the wrong
        // reason.
        for _ in 0..5 {
            assert!(
                !server.is_finished(),
                "the stop path returned while a connection handler was still in flight"
            );
            tokio::time::sleep(Duration::from_millis(50)).await;
        }

        // Releasing the handler must release the body, not merely unblock the test. The
        // window is a quarter of `SHUTDOWN_DRAIN` on purpose: at the full budget the body
        // would also resolve when the drain simply expires, so the assertion could never
        // fail for the reason it states.
        drop(client);
        let exit = tokio::time::timeout(SHUTDOWN_DRAIN / 4, &mut server)
            .await
            .expect("the body must return once its handler is done")
            .expect("server task");
        assert_eq!(exit, ServerExit::Requested);
    }
}
