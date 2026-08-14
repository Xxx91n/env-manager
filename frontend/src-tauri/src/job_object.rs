// Job Object: GUI process self-joins a Job Object with KILL_ON_JOB_CLOSE
// so that WebView2 child processes (renderer, GPU) are cleaned up when
// the GUI exits — even on task manager forced kill.
// Reference: clash-verge-rev PR #6853 (Job Object for sidecar lifecycle)
// Different scope: we protect the GUI's own children, NOT the service
// (service is intentionally detached via std::mem::forget, v0.9.3 design).

#[cfg(windows)]
pub fn init_job_object() {
    use windows_sys::Win32::{
        Foundation::{CloseHandle, GetLastError},
        System::JobObjects::{
            AssignProcessToJobObject, CreateJobObjectW, JobObjectExtendedLimitInformation,
            SetInformationJobObject, JOBOBJECT_EXTENDED_LIMIT_INFORMATION,
            JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE,
        },
    };
    use windows_sys::Win32::System::Threading::GetCurrentProcess;

    unsafe {
        // Create a Job Object
        let job = CreateJobObjectW(std::ptr::null(), std::ptr::null());
        if job.is_null() {
            tracing::warn!("[job_object] CreateJobObjectW failed: {}", GetLastError());
            return;
        }

        // Set KILL_ON_JOB_CLOSE so all processes in the job are killed
        // when the last handle to the job is closed (i.e., when GUI exits).
        let mut info: JOBOBJECT_EXTENDED_LIMIT_INFORMATION = std::mem::zeroed();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        let ok = SetInformationJobObject(
            job,
            JobObjectExtendedLimitInformation,
            &info as *const _ as *const _,
            std::mem::size_of::<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>() as u32,
        );
        if ok == 0 {
            tracing::warn!("[job_object] SetInformationJobObject failed: {}", GetLastError());
            let _ = CloseHandle(job);
            return;
        }

        // Assign the current process (GUI) to the Job Object.
        // WebView2 child processes inherit the Job assignment automatically.
        let ok = AssignProcessToJobObject(job, GetCurrentProcess());
        if ok == 0 {
            tracing::warn!("[job_object] AssignProcessToJobObject failed: {}", GetLastError());
            let _ = CloseHandle(job);
            return;
        }

        // Intentionally leak the job handle — it must stay open for the
        // lifetime of the process so KILL_ON_JOB_CLOSE fires on exit.
        // When the process exits, the OS closes the handle and kills
        // all job members (WebView2 renderer/GPU subprocesses).
        let _ = job; // Handle is an OS-owned raw pointer; keep it alive for process lifetime
        tracing::info!("[job_object] GUI process assigned to Job Object with KILL_ON_JOB_CLOSE");
    }
}

#[cfg(not(windows))]
pub fn init_job_object() {
    // No-op on non-Windows platforms
}

#[cfg(test)]
#[cfg(windows)]
mod tests {
    use super::*;

    #[test]
    fn test_init_job_object_no_panic() {
        init_job_object();
    }
}
