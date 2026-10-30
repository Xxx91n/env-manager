// Kill-on-close job object around one delegated CLI child (#160).
//
// Why this exists: `Child::kill()` is TerminateProcess on the process *this* crate
// spawned. The C# provider layer starts helpers of its own - `op`, `sops`, `pwsh` in
// `src/Secrets/Providers/` - and those outlive a stop that only terminates the CLI. They
// no longer write `profiles.json` / `secretMount.json`, so the write window #153 closed
// stays closed, but sops writes plaintext into `%TEMP%\env-manager-sops-<guid>\` and the
// deletion of that directory is the CLI's own `finally` (`SopsProvider.Decrypt`): a
// terminated CLI never runs it, so one stop used to leave both an orphan helper and a
// plaintext file behind.
//
// Chosen over `taskkill /T` (shells out, races the reap this crate already does) and over
// one process-wide job (one stop would then take down every unrelated delegation). The
// handle belongs to a single `run_cli_child` call, not to the service process, so the
// blast radius of a stop stays the rotation it interrupted.

use std::ffi::c_void;

use windows_sys::Win32::Foundation::{CloseHandle, GetLastError, HANDLE};
use windows_sys::Win32::System::JobObjects::{
    AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
    SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
};
use windows_sys::Win32::System::Threading::{OpenProcess, PROCESS_SET_QUOTA, PROCESS_TERMINATE};

/// A job object that terminates every process still inside it when its last handle closes.
///
/// Membership is inherited: a process assigned while it is running puts its *future*
/// children in the job, and those children's children too. Dropping this value is the
/// tree kill - the same instant at which `reconcile::run_cli_child` has already
/// terminated and reaped the direct child.
pub(crate) struct KillOnCloseJob {
    /// Held as `isize` rather than `HANDLE`, and cast back at each call: `HANDLE` is a raw
    /// pointer in the `windows-sys` this crate pins, and a raw pointer is neither `Send`
    /// nor `Sync`, while `run_cli_child` is awaited inside `tokio::spawn`ed connection
    /// handlers. A wrapper that is not `Send` would not compile there.
    handle: isize,
}

impl KillOnCloseJob {
    /// Create an anonymous job with `JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE`.
    ///
    /// No security attributes are passed, so the handle is not inheritable: an inheritable
    /// handle copied into the CLI child or a helper would keep the job alive past this
    /// call and the kill would never fire.
    pub(crate) fn new() -> Result<Self, String> {
        unsafe {
            let handle = CreateJobObjectW(std::ptr::null(), std::ptr::null()) as isize;
            if handle == 0 {
                return Err(format!("CreateJobObjectW failed: {}", GetLastError()));
            }

            let mut limits: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
            limits.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            let ok = SetInformationJobObject(
                handle as HANDLE,
                JobObjectExtendedLimitInformation,
                &limits as *const _ as *const c_void,
                std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
            );
            if ok == 0 {
                let err = GetLastError();
                let _ = CloseHandle(handle as HANDLE);
                return Err(format!("SetInformationJobObject failed: {}", err));
            }

            Ok(Self { handle })
        }
    }

    /// Put an already-running process into this job.
    ///
    /// `AssignProcessToJobObject` requires `PROCESS_SET_QUOTA | PROCESS_TERMINATE` on the
    /// target, and the pid is the only handle this crate owns: tokio holds the child's own
    /// handle, and borrowing it here would make two owners of one handle. The temporary
    /// handle opened for the assignment is closed immediately.
    pub(crate) fn assign(&self, pid: u32) -> Result<(), String> {
        unsafe {
            let process = OpenProcess(PROCESS_SET_QUOTA | PROCESS_TERMINATE, 0, pid);
            if process.is_null() {
                return Err(format!("OpenProcess({}): {}", pid, GetLastError()));
            }
            let ok = AssignProcessToJobObject(self.handle as HANDLE, process);
            let err = if ok == 0 { Some(GetLastError()) } else { None };
            let _ = CloseHandle(process);
            match err {
                Some(code) => Err(format!("AssignProcessToJobObject({}): {}", pid, code)),
                None => Ok(()),
            }
        }
    }
}

impl Drop for KillOnCloseJob {
    fn drop(&mut self) {
        // Last handle closed + KILL_ON_JOB_CLOSE: the kernel terminates every remaining
        // member. Termination is asynchronous, so a caller that needs proof polls rather
        // than assuming the processes are gone on the next instruction.
        unsafe { CloseHandle(self.handle as HANDLE) };
    }
}
