// Windows Service Control Manager (SCM) control protocol for env-manager-service.exe.
//
// Why this exists (issue #149): the MSI registers EnvManagerService with
// Start="auto", but a process that never calls StartServiceCtrlDispatcherW leaves
// the SCM waiting for ServicesPipeTimeout and then failing the start with 1053 --
// one logged failure per boot, and the service never actually ran.
//
// Thread model (hard constraint): StartServiceCtrlDispatcherW is called exactly once,
// on the process's initial thread, and blocks until the service stops. The SCM invokes
// service_main on a different thread, and the tokio runtime is built there -- never on
// the dispatcher thread. Only windows-sys is used; no service crate is added.
//
// Startup order (hard constraint): SERVICE_RUNNING is reported as soon as the
// named-pipe listener exists, before the reconcile backlog. The ordering itself lives
// in `crate::run_server`, which calls the report callback between "listener created"
// and "reconcile spawned"; moving slow work ahead of that point reintroduces 1053.

use std::ffi::c_void;
use std::sync::{Arc, Mutex, MutexGuard, OnceLock};
use std::time::Duration;
use tokio_util::sync::CancellationToken;
use windows_sys::core::PWSTR;
use windows_sys::Win32::Foundation::{ERROR_SERVICE_SPECIFIC_ERROR, NO_ERROR};
use windows_sys::Win32::System::Services::{
    RegisterServiceCtrlHandlerExW, SetServiceStatus, StartServiceCtrlDispatcherW,
    SERVICE_ACCEPT_SHUTDOWN, SERVICE_ACCEPT_STOP, SERVICE_CONTROL_INTERROGATE,
    SERVICE_CONTROL_SHUTDOWN, SERVICE_CONTROL_STOP, SERVICE_RUNNING, SERVICE_START_PENDING,
    SERVICE_STATUS, SERVICE_STOPPED, SERVICE_STOP_PENDING, SERVICE_TABLE_ENTRYW,
    SERVICE_WIN32_OWN_PROCESS,
};

/// Must match `ServiceInstall/@Name` in frontend/scripts/installer.wxs.
const SERVICE_NAME: &str = "EnvManagerService";

/// SCM wait hints. Only advisory: the SCM's own ceiling is ServicesPipeTimeout.
const START_WAIT_HINT_MS: u32 = 3_000;
const STOP_WAIT_HINT_MS: u32 = 5_000;

/// Budget for draining the tokio runtime (and its pipe handles) after the server
/// body returns. Kept well under the stop wait hint, together with
/// `crate::SHUTDOWN_DRAIN`.
const RUNTIME_DRAIN: Duration = Duration::from_secs(2);

/// The stop path must fit inside the wait hint the SCM was given, otherwise the SCM
/// declares the service unresponsive instead of waiting it out. Two drains are the
/// whole budget: the server body drains its tasks, then the runtime drains.
/// Both sides are milliseconds: comparing `as_secs()` against a millisecond constant
/// would make the guard vacuous (it would only fire past ~83 minutes).
const _: () = assert!(
    crate::SHUTDOWN_DRAIN.as_millis() + RUNTIME_DRAIN.as_millis() < STOP_WAIT_HINT_MS as u128,
    "the stop path must finish inside STOP_WAIT_HINT_MS"
);

/// Service-specific exit code reported when the body ends without a stop request.
const EXIT_CODE_BODY_FAILED: u32 = 1;

/// Set by `service_main` once `RegisterServiceCtrlHandlerExW` succeeds. Stored as
/// `usize` because the raw handle pointer is neither `Send` nor `Sync`, which a
/// `static` requires; it is written once, on the SCM's service thread.
static STATUS_HANDLE: OnceLock<usize> = OnceLock::new();

/// Serializes status transitions against the control handler. The SCM reads states in
/// order, so a RUNNING report must not land after the STOP_PENDING the handler already
/// sent; the check and the report have to be one critical section. Never held across an
/// await. The only nested lock is LAST_STATUS, always taken after this one.
static STATUS_LOCK: Mutex<()> = Mutex::new(());

/// Last status sent to the SCM, replayed verbatim for `SERVICE_CONTROL_INTERROGATE` so
/// the checkpoint, wait hint and exit code the SCM already knows are not reset.
static LAST_STATUS: Mutex<Option<SERVICE_STATUS>> = Mutex::new(None);

/// Cancellation shared with the server body. The control handler trips it, which is
/// what makes a STOP/SHUTDOWN request reach the IPC listener and the reconcile loop.
static SHUTDOWN: OnceLock<Arc<CancellationToken>> = OnceLock::new();

fn to_wide(s: &str) -> Vec<u16> {
    let mut wide: Vec<u16> = s.encode_utf16().collect();
    wide.push(0);
    wide
}

/// Take the status lock, ignoring poisoning: a panic elsewhere must not stop the stop
/// path from reporting.
fn status_lock() -> MutexGuard<'static, ()> {
    STATUS_LOCK
        .lock()
        .unwrap_or_else(|poisoned| poisoned.into_inner())
}

/// Report one state transition to the SCM. A no-op before the status handle exists,
/// so the same call sites stay valid on the non-SCM fallback path.
fn report(state: u32, checkpoint: u32, wait_hint_ms: u32) {
    let _guard = status_lock();
    // Fire-and-forget: a rejected START_PENDING/STOPPED leaves nothing to decide.
    let _ = report_locked(state, checkpoint, wait_hint_ms, 0, 0);
}

/// Report a terminal STOPPED whose cause was a failure, so the SCM records a service
/// error (and applies the configured recovery actions) instead of a clean stop.
fn report_failure(service_exit_code: u32) {
    let _guard = status_lock();
    let _ = report_locked(
        SERVICE_STOPPED,
        0,
        0,
        ERROR_SERVICE_SPECIFIC_ERROR,
        service_exit_code,
    );
}

/// Report SERVICE_RUNNING unless a stop request already landed.
///
/// The check and the report share `STATUS_LOCK` with the control handler, so the SCM can
/// never end up with RUNNING after STOP_PENDING. Returns whether the SCM actually holds
/// RUNNING; the caller stops the body when it does not. A rejected report must not start
/// the body either - the SCM would stay in START_PENDING and time the start out (1053)
/// while the service is alive behind a status it never accepted.
pub fn report_running_unless_stopping() -> bool {
    let _guard = status_lock();
    if SHUTDOWN.get().is_some_and(|token| token.is_cancelled()) {
        tracing::info!("stop requested before the listener came up, RUNNING not reported");
        return false;
    }
    if !report_locked(SERVICE_RUNNING, 0, 0, 0, 0) {
        tracing::error!(
            "SetServiceStatus(SERVICE_RUNNING) failed (os error {}); not starting the service body",
            std::io::Error::last_os_error()
        );
        return false;
    }
    tracing::info!("reported SERVICE_RUNNING");
    true
}

/// Caller must hold `STATUS_LOCK`. Returns whether the SCM accepted the status; only an
/// accepted status is remembered for `SERVICE_CONTROL_INTERROGATE` to replay.
fn report_locked(
    state: u32,
    checkpoint: u32,
    wait_hint_ms: u32,
    exit_code: u32,
    service_exit_code: u32,
) -> bool {
    let Some(handle) = STATUS_HANDLE.get() else {
        return false;
    };
    let status = SERVICE_STATUS {
        dwServiceType: SERVICE_WIN32_OWN_PROCESS,
        dwCurrentState: state,
        dwControlsAccepted: SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN,
        dwWin32ExitCode: exit_code,
        dwServiceSpecificExitCode: service_exit_code,
        dwCheckPoint: checkpoint,
        dwWaitHint: wait_hint_ms,
    };
    let accepted = unsafe { SetServiceStatus(*handle as *mut c_void, &status) != 0 };
    if accepted {
        *LAST_STATUS
            .lock()
            .unwrap_or_else(|poisoned| poisoned.into_inner()) = Some(status);
    }
    accepted
}

/// SCM control handler. Runs on an SCM-owned thread, so it only reports state and
/// trips the shared cancellation token; all cleanup happens on the service thread.
unsafe extern "system" fn control_handler(
    control: u32,
    _event_type: u32,
    _event_data: *mut c_void,
    _context: *mut c_void,
) -> u32 {
    match control {
        SERVICE_CONTROL_STOP | SERVICE_CONTROL_SHUTDOWN => {
            tracing::info!("SCM control {} received, stopping the service", control);
            // One critical section: STOP_PENDING cannot be followed by a RUNNING report.
            let _guard = status_lock();
            let _ = report_locked(SERVICE_STOP_PENDING, 1, STOP_WAIT_HINT_MS, 0, 0);
            if let Some(shutdown) = SHUTDOWN.get() {
                shutdown.cancel();
            }
            NO_ERROR
        }
        SERVICE_CONTROL_INTERROGATE => {
            let _guard = status_lock();
            let last = *LAST_STATUS
                .lock()
                .unwrap_or_else(|poisoned| poisoned.into_inner());
            if let (Some(status), Some(handle)) = (last, STATUS_HANDLE.get()) {
                unsafe {
                    SetServiceStatus(*handle as *mut c_void, &status);
                }
            }
            NO_ERROR
        }
        _ => NO_ERROR,
    }
}

/// `ServiceMain`, called by the SCM on its own thread. This is where the tokio
/// runtime is created; the initial thread stays blocked in the dispatcher.
unsafe extern "system" fn service_main(_argc: u32, _argv: *mut PWSTR) {
    let name = to_wide(SERVICE_NAME);
    let handle =
        RegisterServiceCtrlHandlerExW(name.as_ptr(), Some(control_handler), std::ptr::null());
    if handle.is_null() {
        // Without a status handle every later report is a no-op, so the SCM would
        // run to ServicesPipeTimeout. Nothing else can be done from here.
        tracing::error!(
            "RegisterServiceCtrlHandlerExW failed (os error {}); the SCM will time this start out",
            std::io::Error::last_os_error()
        );
        return;
    }
    let _ = STATUS_HANDLE.set(handle as usize);

    // Install the cancellation token BEFORE the first report: that report advertises
    // SERVICE_ACCEPT_STOP, so a STOP arriving in the window between the two must find
    // a token to trip instead of being dropped while the SCM waits for STOPPED.
    let shutdown = Arc::new(CancellationToken::new());
    let _ = SHUTDOWN.set(shutdown.clone());
    report(SERVICE_START_PENDING, 1, START_WAIT_HINT_MS);

    // Expensive hardening (binary hash, module enumeration, Authenticode) runs only
    // after START_PENDING: the SCM now holds a wait hint, so a slow check cannot burn
    // the whole ServicesPipeTimeout window before the first status arrives.
    crate::process_guard::init();

    let runtime = match tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
    {
        Ok(runtime) => runtime,
        Err(e) => {
            tracing::error!("failed to create tokio runtime: {}", e);
            report_failure(EXIT_CODE_BODY_FAILED);
            return;
        }
    };

    let pipe = crate::RuntimeMode::Service.pipe_endpoint();
    // Report as soon as the listener exists: this is the point that keeps the boot-time
    // start inside ServicesPipeTimeout (#149).
    let exit = runtime.block_on(crate::run_server(
        pipe,
        shutdown,
        report_running_unless_stopping,
    ));

    // Drain before reporting STOPPED so that "Stopped" implies the pipe is free for
    // the next start.
    runtime.shutdown_timeout(RUNTIME_DRAIN);

    match exit {
        crate::ServerExit::Requested => {
            tracing::info!("service stopped on request");
            report(SERVICE_STOPPED, 0, 0);
        }
        other => {
            tracing::error!("service body exited without a stop request: {:?}", other);
            report_failure(EXIT_CODE_BODY_FAILED);
        }
    }
}

/// Join the SCM control protocol. Blocks the calling (initial) thread until the
/// service stops, so this must be the first thing after logging and process
/// hardening in Service mode.
///
/// Returns `false` when the process was not started by the SCM -- a console
/// `--mode=service` run for debugging, or a misconfigured service entry. The caller
/// then falls back to running the same body without SCM status reporting, which is
/// how a developer can reproduce service behaviour interactively.
pub fn dispatch() -> bool {
    let name = to_wide(SERVICE_NAME);
    let table = [
        SERVICE_TABLE_ENTRYW {
            lpServiceName: name.as_ptr() as PWSTR,
            lpServiceProc: Some(service_main),
        },
        SERVICE_TABLE_ENTRYW {
            lpServiceName: std::ptr::null_mut(),
            lpServiceProc: None,
        },
    ];
    let started = unsafe { StartServiceCtrlDispatcherW(table.as_ptr()) };
    if started == 0 {
        tracing::warn!(
            "StartServiceCtrlDispatcherW failed (os error {}): not launched by the SCM",
            std::io::Error::last_os_error()
        );
        return false;
    }
    true
}
