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
use std::sync::atomic::{AtomicU32, Ordering};
use std::sync::{Arc, OnceLock};
use std::time::Duration;
use tokio_util::sync::CancellationToken;
use windows_sys::core::PWSTR;
use windows_sys::Win32::Foundation::{ERROR_SERVICE_SPECIFIC_ERROR, NO_ERROR};
use windows_sys::Win32::System::Services::{
    RegisterServiceCtrlHandlerExW, SetServiceStatus, StartServiceCtrlDispatcherW,
    SERVICE_ACCEPT_SHUTDOWN, SERVICE_ACCEPT_STOP, SERVICE_CONTROL_INTERROGATE,
    SERVICE_CONTROL_SHUTDOWN, SERVICE_CONTROL_STOP, SERVICE_RUNNING, SERVICE_START_PENDING,
    SERVICE_STATUS, SERVICE_STOP_PENDING, SERVICE_STOPPED, SERVICE_TABLE_ENTRYW,
    SERVICE_WIN32_OWN_PROCESS,
};

/// Must match `ServiceInstall/@Name` in frontend/scripts/installer.wxs.
const SERVICE_NAME: &str = "EnvManagerService";

/// SCM wait hints. Only advisory: the SCM's own ceiling is ServicesPipeTimeout.
const START_WAIT_HINT_MS: u32 = 3_000;
const STOP_WAIT_HINT_MS: u32 = 5_000;

/// Budget for draining the tokio runtime (and its pipe handles) after the server
/// body returns. Kept well under the stop wait hint.
const RUNTIME_DRAIN: Duration = Duration::from_secs(5);

/// Service-specific exit code reported when the body ends without a stop request.
const EXIT_CODE_BODY_FAILED: u32 = 1;

/// Set by `service_main` once `RegisterServiceCtrlHandlerExW` succeeds. Stored as
/// `usize` because the raw handle pointer is neither `Send` nor `Sync`, which a
/// `static` requires; it is written once, on the SCM's service thread.
static STATUS_HANDLE: OnceLock<usize> = OnceLock::new();

/// Last reported state, so `SERVICE_CONTROL_INTERROGATE` can answer with it.
static CURRENT_STATE: AtomicU32 = AtomicU32::new(SERVICE_START_PENDING);

/// Cancellation shared with the server body. The control handler trips it, which is
/// what makes a STOP/SHUTDOWN request reach the IPC listener and the reconcile loop.
static SHUTDOWN: OnceLock<Arc<CancellationToken>> = OnceLock::new();

fn to_wide(s: &str) -> Vec<u16> {
    let mut wide: Vec<u16> = s.encode_utf16().collect();
    wide.push(0);
    wide
}

/// Report one state transition to the SCM. A no-op before the status handle exists,
/// so the same call sites stay valid on the non-SCM fallback path.
fn report(state: u32, checkpoint: u32, wait_hint_ms: u32) {
    report_with(state, checkpoint, wait_hint_ms, 0, 0);
}

fn report_with(state: u32, checkpoint: u32, wait_hint_ms: u32, exit_code: u32, service_exit_code: u32) {
    let Some(handle) = STATUS_HANDLE.get() else {
        return;
    };
    CURRENT_STATE.store(state, Ordering::SeqCst);
    let status = SERVICE_STATUS {
        dwServiceType: SERVICE_WIN32_OWN_PROCESS,
        dwCurrentState: state,
        dwControlsAccepted: SERVICE_ACCEPT_STOP | SERVICE_ACCEPT_SHUTDOWN,
        dwWin32ExitCode: exit_code,
        dwServiceSpecificExitCode: service_exit_code,
        dwCheckPoint: checkpoint,
        dwWaitHint: wait_hint_ms,
    };
    unsafe {
        SetServiceStatus(*handle as *mut c_void, &status);
    }
}

/// Report a terminal STOPPED whose cause was a failure, so the SCM records a service
/// error (and applies the configured recovery actions) instead of a clean stop.
fn report_failure(service_exit_code: u32) {
    report_with(
        SERVICE_STOPPED,
        0,
        0,
        ERROR_SERVICE_SPECIFIC_ERROR,
        service_exit_code,
    );
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
            report(SERVICE_STOP_PENDING, 1, STOP_WAIT_HINT_MS);
            if let Some(shutdown) = SHUTDOWN.get() {
                shutdown.cancel();
            }
            NO_ERROR
        }
        SERVICE_CONTROL_INTERROGATE => {
            report(CURRENT_STATE.load(Ordering::SeqCst), 0, 0);
            NO_ERROR
        }
        _ => NO_ERROR,
    }
}

/// `ServiceMain`, called by the SCM on its own thread. This is where the tokio
/// runtime is created; the initial thread stays blocked in the dispatcher.
unsafe extern "system" fn service_main(_argc: u32, _argv: *mut PWSTR) {
    let name = to_wide(SERVICE_NAME);
    let handle = RegisterServiceCtrlHandlerExW(
        name.as_ptr(),
        Some(control_handler),
        std::ptr::null(),
    );
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
    report(SERVICE_START_PENDING, 1, START_WAIT_HINT_MS);

    let shutdown = Arc::new(CancellationToken::new());
    let _ = SHUTDOWN.set(shutdown.clone());

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
    let exit = runtime.block_on(crate::run_server(pipe, true, shutdown, || {
        // Report as soon as the listener exists: this is the point that keeps the
        // boot-time start inside ServicesPipeTimeout (#149).
        report(SERVICE_RUNNING, 0, 0);
        tracing::info!("reported SERVICE_RUNNING");
    }));

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
