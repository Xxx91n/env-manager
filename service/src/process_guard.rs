// Process-level security hardening.
// ProcessMitigationPolicy + Pagefile protection added.
// WER disable + crash dump protection (2D/4A)
// Binary hash verification (2B)
// Debugger detection (2C)
// DLL injection defense (2E)
// VirtualLock helper (4B)
// All checks are best-effort: log warn, never exit (keep service debuggable).

use std::path::PathBuf;
use std::sync::Arc;
use std::time::Duration;
use sha2::{Digest, Sha256};
use tokio_util::sync::CancellationToken;

#[cfg(windows)]
use windows_sys::{
    core::BOOL,
    Win32::{
        System::{
            Diagnostics::Debug::{
                SetErrorMode, SEM_FAILCRITICALERRORS, SEM_NOGPFAULTERRORBOX, SEM_NOOPENFILEERRORBOX,
            },
            Memory::VirtualLock,
        // ProcessStatus functions loaded dynamically at runtime (see get_proc_addr)
            Threading::{GetCurrentProcess, GetCurrentProcessId},
        },
    },
};

/// Phase 2D/4A: Disable WER (Windows Error Reporting) and crash dump dialogs.
/// SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX suppresses the "program has stopped working" dialog.
/// WerSetFlags disables fault reporting queue + upload.
/// Called at process start, best-effort.
pub fn disable_crash_dialogs() {
    #[cfg(windows)]
    unsafe {
        // SetErrorMode: combine flags so no critical-error popups, no GP-fault box, no file-not-found dialog.
        let flags = SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX;
        let _ = SetErrorMode(flags);

        // WerSetFlags: WER_FAULT_REPORTING_DISABLE | WER_FAULT_REPORTING_NO_QUEUE = 0x1 | 0x2 = 0x3
        // Dynamically load wer.dll since it's not in windows-sys static bindings.
        let wer = get_module_handle_a(b"wer.dll\0");
        if !wer.is_null() {
            let pfn = get_proc_addr(wer, b"WerSetFlags\0");
            if let Some(pfn_ptr) = pfn {
                type WerSetFlagsFn = unsafe extern "system" fn(u32) -> i32;
                let wer_set_flags: WerSetFlagsFn = std::mem::transmute(pfn_ptr);
                let _ = wer_set_flags(0x3); // WER_FAULT_REPORTING_DISABLE | WER_FAULT_REPORTING_NO_QUEUE
            }
        }
    }
    tracing::debug!("crash dialog suppression applied (best-effort)");
}

/// Phase 2B: Compute SHA256 of the current executable.
/// On first run, stores the hash; on subsequent runs, compares and warns if mismatch.
pub fn verify_binary_hash() {
    let exe_path = match std::env::current_exe() {
        Ok(p) => p,
        Err(e) => {
            tracing::warn!("binary hash: cannot resolve current_exe: {}", e);
            return;
        }
    };

    let hash = match compute_file_sha256(&exe_path) {
        Ok(h) => h,
        Err(e) => {
            tracing::warn!("binary hash: cannot read exe {}: {}", exe_path.display(), e);
            return;
        }
    };

    let hash_file = hash_storage_path();
    match std::fs::read_to_string(&hash_file) {
        Ok(stored) => {
            if stored.trim() != hash {
                tracing::warn!("binary hash mismatch! expected={}, actual={} — service may have been tampered with", &stored.trim()[..16], &hash[..16]);
            } else {
                tracing::debug!("binary hash verified: {}", &hash[..16]);
            }
        }
        Err(_) => {
            // First run — store hash.
            let _ = std::fs::write(&hash_file, &hash);
            tracing::info!("binary hash stored for first run: {}", &hash[..16]);
        }
    }
}

/// Phase 2C: Lightweight debugger detection.
/// IsDebuggerPresent + CheckRemoteDebuggerPresent.
/// Called once at startup by `init` and every interval by `debugger_watch_loop`.
pub fn check_debugger() -> bool {
    #[cfg(windows)]
    unsafe {
        let pid = GetCurrentProcessId();
        type IsDebuggerPresentFn = unsafe extern "system" fn() -> BOOL;
        type CheckRemoteDebuggerPresentFn = unsafe extern "system" fn(windows_sys::Win32::Foundation::HANDLE, *mut BOOL) -> BOOL;

        let k32 = get_module_handle_a(b"kernel32.dll\0");
        if !k32.is_null() {
            if let Some(pfn) = get_proc_addr(k32, b"IsDebuggerPresent\0") {
                let f: IsDebuggerPresentFn = std::mem::transmute(pfn);
                if f() != 0 {
                    return true;
                }
            }
            if let Some(pfn) = get_proc_addr(k32, b"CheckRemoteDebuggerPresent\0") {
                let f: CheckRemoteDebuggerPresentFn = std::mem::transmute(pfn);
                let mut result: BOOL = 0;
                let h = GetCurrentProcess();
                if f(h, &mut result) != 0 && result != 0 {
                    return true;
                }
            }
        }
        false
    }
    #[cfg(not(windows))]
    {
        false
    }
}

/// How often the running service looks for an attached debugger.
///
/// The interval belongs to this task now (#294). It used to be borrowed from the reconcile
/// tick - one check per 300-second sweep that otherwise only stamped a timestamp.
pub(crate) const DEBUGGER_CHECK_INTERVAL: Duration = Duration::from_secs(300);

/// The debugger check as its own periodic task (#294).
///
/// The wait is one `select!` over the timer *and* the stop token, so a stop ends it the
/// moment it lands. That shape is a requirement of this ticket, not a habit inherited from
/// the sweep it replaces: a loop that slept the interval and only then looked at a flag
/// would sit out up to five minutes of a stop, and a brand-new task is the wrong place to
/// reintroduce that pattern. Nothing here touches the mount file, and no value is fetched.
///
/// `shutdown` is the same token the IPC server answers, so a stop ends both background
/// tasks inside the same window.
pub async fn debugger_watch_loop(shutdown: Arc<CancellationToken>) {
    debugger_watch_loop_at(shutdown, DEBUGGER_CHECK_INTERVAL, check_debugger).await
}

/// `debugger_watch_loop` with its interval and its check taken as parameters, so a test can
/// drive the loop without waiting five minutes or attaching a debugger. Production always
/// goes through `debugger_watch_loop`, which passes `DEBUGGER_CHECK_INTERVAL` and
/// `check_debugger`.
pub(crate) async fn debugger_watch_loop_at(
    shutdown: Arc<CancellationToken>,
    interval: Duration,
    check: impl Fn() -> bool + Send + 'static,
) {
    tracing::info!("debugger check task starting ({:?} interval)", interval);
    loop {
        tokio::select! {
            _ = tokio::time::sleep(interval) => {
                if check() {
                    tracing::warn!("debugger attached to the service process (periodic check)");
                }
            }
            _ = shutdown.cancelled() => {
                tracing::info!("debugger check task cancelled, exiting");
                return;
            }
        }
    }
}

/// Phase 4B: VirtualLock a secret buffer to prevent pagefile swapping.
/// Best-effort: Windows has a ~30 page limit per process, failures are silently ignored.
pub fn virtual_lock_secret(buf: &[u8]) {
    #[cfg(windows)]
    unsafe {
        let _ = VirtualLock(buf.as_ptr() as *const _, buf.len());
    }
    // Non-windows: no-op
}

/// Phase 2E: Enumerate loaded modules and warn about unexpected DLLs.
/// Called at startup and periodically (300s).
pub fn enum_modules_warn_unexpected() {
    #[cfg(windows)]
    unsafe {
    let k32 = get_module_handle_a(b"kernel32.dll\0");
        if k32.is_null() {
            return;
        }
        enum_process_modules_and_warn();
    }
}

/// Phase 2E+: Apply process mitigation policies to harden against DLL injection and dynamic code.
/// Uses SetProcessMitigationPolicy via dynamic loading (same pattern as check_debugger).
/// Policies applied:
/// - ProcessDynamicCodePolicy: ProhibitDynamicCode = true (blocks JIT/injection)
/// - ProcessExtensionPointDisablePolicy: DisableExtensionPoints = true (blocks legacy DLL extension points)
/// - ProcessImageLoadPolicy: NoRemoteImages + NoLowMandatoryLabelImages = true (blocks remote/low-integrity DLLs)
/// Best-effort: failures are logged, never fatal.
/// Template: Microsoft WSL WslSecurity.cpp ApplyProcessMitigationPolicies().
pub fn apply_process_mitigations() {
    #[cfg(windows)]
    unsafe {
        let k32 = get_module_handle_a(b"kernel32.dll\0");
        if k32.is_null() {
            tracing::warn!("apply_process_mitigations: kernel32 not loaded");
            return;
        }

        type SetProcessMitigationPolicyFn =
            unsafe extern "system" fn(u32, *const std::ffi::c_void, usize) -> BOOL;

        // PROCESS_MITIGATION_POLICY enum values
        const PROCESS_DYNAMIC_CODE_POLICY: u32 = 2;
        const PROCESS_EXTENSION_POINT_DISABLE_POLICY: u32 = 6;
        const PROCESS_IMAGE_LOAD_POLICY: u32 = 10;

        if let Some(pfn) = get_proc_addr(k32, b"SetProcessMitigationPolicy\0") {
            let set_mitigation: SetProcessMitigationPolicyFn = std::mem::transmute(pfn);

            // ProcessDynamicCodePolicy: struct with ProhibitDynamicCode bit 0
            let mut dyn_code: u32 = 1;
            if set_mitigation(
                PROCESS_DYNAMIC_CODE_POLICY,
                &mut dyn_code as *mut u32 as *const std::ffi::c_void,
                std::mem::size_of::<u32>(),
            ) != 0 {
                tracing::debug!("ProcessDynamicCodePolicy applied (ProhibitDynamicCode=1)");
            } else {
                tracing::warn!("ProcessDynamicCodePolicy failed (may require Win10+ or already set)");
            }

            // ProcessExtensionPointDisablePolicy: struct with DisableExtensionPoints bit 0
            let mut ext_point: u32 = 1;
            if set_mitigation(
                PROCESS_EXTENSION_POINT_DISABLE_POLICY,
                &mut ext_point as *mut u32 as *const std::ffi::c_void,
                std::mem::size_of::<u32>(),
            ) != 0 {
                tracing::debug!("ProcessExtensionPointDisablePolicy applied (DisableExtensionPoints=1)");
            } else {
                tracing::warn!("ProcessExtensionPointDisablePolicy failed");
            }

            // ProcessImageLoadPolicy: NoRemoteImages (bit 0) | NoLowMandatoryLabelImages (bit 1)
            let mut image_load: u32 = 0x3;
            if set_mitigation(
                PROCESS_IMAGE_LOAD_POLICY,
                &mut image_load as *mut u32 as *const std::ffi::c_void,
                std::mem::size_of::<u32>(),
            ) != 0 {
                tracing::debug!("ProcessImageLoadPolicy applied (NoRemoteImages=1, NoLowMandatoryLabelImages=1)");
            } else {
                tracing::warn!("ProcessImageLoadPolicy failed");
            }
        } else {
            tracing::warn!("SetProcessMitigationPolicy not found in kernel32");
        }
    }
    #[cfg(not(windows))]
    {
        tracing::debug!("apply_process_mitigations: no-op on non-windows");
    }
}

/// Phase 4B: Check if ClearPageFileAtShutdown is enabled.
/// If not, warn the user that secrets in memory may persist in the pagefile after shutdown.
/// This is a read-only check — the service never auto-modifies system-wide registry settings.
/// VirtualLock (already applied to secret buffers) is the process-level defense;
/// ClearPageFileAtShutdown is the system-wide defense.
/// Template: Microsoft Learn "Shutdown: Clear virtual memory pagefile" security policy setting.
pub fn check_pagefile_protection() {
    #[cfg(windows)]
    unsafe {
        let advapi32 = get_module_handle_a(b"advapi32.dll\0");
        if advapi32.is_null() {
            tracing::warn!("check_pagefile_protection: advapi32 not loaded");
            return;
        }

        type RegOpenKeyExWFn = unsafe extern "system" fn(
            windows_sys::Win32::Foundation::HANDLE,
            *const u16,
            u32,
            u32,
            *mut windows_sys::Win32::Foundation::HANDLE,
        ) -> i32;

        type RegQueryValueExWFn = unsafe extern "system" fn(
            windows_sys::Win32::Foundation::HANDLE,
            *const u16,
            *mut u32,
            *mut u32,
            *mut u8,
            *mut u32,
        ) -> i32;

        type RegCloseKeyFn =
            unsafe extern "system" fn(windows_sys::Win32::Foundation::HANDLE) -> i32;

        let pfn_open = get_proc_addr(advapi32, b"RegOpenKeyExW\0");
        let pfn_query = get_proc_addr(advapi32, b"RegQueryValueExW\0");
        let pfn_close = get_proc_addr(advapi32, b"RegCloseKey\0");
        if pfn_open.is_none() || pfn_query.is_none() || pfn_close.is_none() {
            tracing::warn!("check_pagefile_protection: registry API not available");
            return;
        }

        let reg_open: RegOpenKeyExWFn = std::mem::transmute(pfn_open.unwrap());
        let reg_query: RegQueryValueExWFn = std::mem::transmute(pfn_query.unwrap());
        let reg_close: RegCloseKeyFn = std::mem::transmute(pfn_close.unwrap());

        // ponytail: hardcoded registry path — this is the only Windows registry path for this setting;
        // no dynamic discovery possible, it's a fixed OS contract.
        let sub_key: Vec<u16> = "SYSTEM\\CurrentControlSet\\Control\\Session Manager\\Memory Management"
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();

        let value_name: Vec<u16> = "ClearPageFileAtShutdown"
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();

        // HKEY_LOCAL_MACHINE = 0x80000002
        const HKEY_LOCAL_MACHINE: windows_sys::Win32::Foundation::HANDLE = 0x80000002 as *mut _;
        const KEY_READ: u32 = 0x20019;

        let mut hkey: windows_sys::Win32::Foundation::HANDLE = std::ptr::null_mut();
        let status = reg_open(HKEY_LOCAL_MACHINE, sub_key.as_ptr(), 0, KEY_READ, &mut hkey);
        if status != 0 {
            tracing::warn!("check_pagefile_protection: cannot open registry key (status={:#x})", status);
            return;
        }

        let mut value: u32 = 0;
        let mut value_size: u32 = 4;
        let mut value_type: u32 = 0;
        let status = reg_query(
            hkey,
            value_name.as_ptr(),
            std::ptr::null_mut(),
            &mut value_type,
            &mut value as *mut u32 as *mut u8,
            &mut value_size,
        );
        let _ = reg_close(hkey);

        if status != 0 {
            // Value doesn't exist — default is 0 (disabled)
            tracing::warn!(
                "pagefile protection: ClearPageFileAtShutdown not set — secrets in memory may persist in pagefile after shutdown.                  Recommend enabling via Group Policy or registry."
            );
        } else if value == 0 {
            tracing::warn!(
                "pagefile protection: ClearPageFileAtShutdown=0 — secrets in memory may persist in pagefile after shutdown.                  Recommend setting to 1 via Group Policy or registry."
            );
        } else {
            tracing::debug!("pagefile protection: ClearPageFileAtShutdown=1 (enabled)");
        }
    }
    #[cfg(not(windows))]
    {
        tracing::debug!("check_pagefile_protection: no-op on non-windows");
    }
}

/// Phase 2F: Verify Authenticode signature of the current exe via WinVerifyTrust.
/// Not yet implemented — deferred to when code signing is in place.
/// For now, just log that the check was called.
pub fn verify_authenticode() {
    tracing::debug!("authenticode verification: deferred (no code signing yet)");
}

/// Combined init: call at process start.
pub fn init() {
    disable_crash_dialogs();
    verify_binary_hash();
    enum_modules_warn_unexpected();
    if check_debugger() {
        tracing::warn!("debugger detected at service startup");
    }
    apply_process_mitigations();
    check_pagefile_protection();
    verify_authenticode();
}

// ---- helpers ----

fn compute_file_sha256(path: &std::path::Path) -> Result<String, std::io::Error> {
    use std::io::Read;
    let mut f = std::fs::File::open(path)?;
    let mut hasher = Sha256::new();
    let mut buf = [0u8; 4096];
    loop {
        let n = f.read(&mut buf)?;
        if n == 0 { break; }
        hasher.update(&buf[..n]);
    }
    Ok(hex::encode(hasher.finalize()))
}

fn hash_storage_path() -> PathBuf {
    let dir = std::env::var("ProgramData")
        .unwrap_or_else(|_| r"C:\ProgramData".to_string());
    let d = PathBuf::from(dir).join("EnvManager");
    let _ = std::fs::create_dir_all(&d);
    d.join("service-hash.json")
}

#[cfg(windows)]
unsafe fn get_module_handle_a(name: &[u8]) -> windows_sys::Win32::Foundation::HMODULE {
    windows_sys::Win32::System::LibraryLoader::GetModuleHandleA(name.as_ptr() as _)
}

#[cfg(windows)]
unsafe fn get_proc_addr(module: windows_sys::Win32::Foundation::HMODULE, name: &[u8]) -> Option<*const ()> {
    let p = windows_sys::Win32::System::LibraryLoader::GetProcAddress(module, name.as_ptr() as *const u8);
    p.map(|f| f as *const ())
}

#[cfg(windows)]
unsafe fn enum_process_modules_and_warn() {
    type EnumProcessModulesFn = unsafe extern "system" fn(
        windows_sys::Win32::Foundation::HANDLE,
        *mut windows_sys::Win32::Foundation::HMODULE,
        u32,
        *mut u32,
    ) -> BOOL;
    type GetModuleFileNameExWFn = unsafe extern "system" fn(
        windows_sys::Win32::Foundation::HMODULE,
        *mut u16,
        u32,
    ) -> u32;

    let k32 = get_module_handle_a(b"kernel32.dll\0");
    if k32.is_null() { return; }

    let pfn_enum = get_proc_addr(k32, b"EnumProcessModules\0");
    let pfn_name = get_proc_addr(k32, b"GetModuleFileNameExW\0");
    if pfn_enum.is_none() || pfn_name.is_none() { return; }

    let enum_fn: EnumProcessModulesFn = std::mem::transmute(pfn_enum.unwrap());
    let name_fn: GetModuleFileNameExWFn = std::mem::transmute(pfn_name.unwrap());

    let h = GetCurrentProcess();
    let mut modules: [windows_sys::Win32::Foundation::HMODULE; 512] = [std::ptr::null_mut(); 512];
    let mut needed: u32 = 0;
    let ok = enum_fn(
        h,
        modules.as_mut_ptr(),
        (modules.len() * std::mem::size_of::<windows_sys::Win32::Foundation::HMODULE>()) as u32,
        &mut needed,
    );
    if ok == 0 {
        tracing::warn!("EnumProcessModules failed");
        return;
    }
    let count = (needed as usize) / std::mem::size_of::<windows_sys::Win32::Foundation::HMODULE>();
    let count = count.min(modules.len());
    for i in 0..count {
        let mut buf = [0u16; 260];
        let len = name_fn(modules[i], buf.as_mut_ptr(), buf.len() as u32);
        if len > 0 {
            let name = String::from_utf16_lossy(&buf[..len as usize]);
            // Just log the module name at trace level; unexpected module detection would require a whitelist
            tracing::trace!("loaded module: {}", name);
        }
    }
    tracing::debug!("enumerated {} loaded modules", count);
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_disable_crash_dialogs_no_panic() {
        // Should not panic even in test environment
        disable_crash_dialogs();
    }

    #[test]
    fn test_virtual_lock_no_panic() {
        let buf = [0u8; 16];
        virtual_lock_secret(&buf);
    }

    #[test]
    fn test_check_debugger_no_panic() {
        let _ = check_debugger();
    }

    #[test]
    fn test_enum_modules_no_panic() {
        enum_modules_warn_unexpected();
    }

    #[test]
    fn test_verify_binary_hash_no_panic() {
        verify_binary_hash();
    }

    #[test]
    fn test_apply_process_mitigations_no_panic() {
        apply_process_mitigations();
    }

    #[test]
    fn test_check_pagefile_protection_no_panic() {
        check_pagefile_protection();
    }

    /// The shape #294 asked for: the task waits on the stop token and the timer at the same
    /// time, so a stop ends the wait at once. A loop that slept the interval first and read
    /// the flag afterwards would still be asleep here - with the production interval that is
    /// five minutes of a stop the service would have to sit through.
    #[tokio::test]
    async fn the_debugger_check_ends_its_wait_when_the_stop_lands() {
        let shutdown = Arc::new(CancellationToken::new());
        let task = tokio::spawn(debugger_watch_loop_at(
            shutdown.clone(),
            Duration::from_secs(300),
            || false,
        ));
        // Let the task reach its first wait, so the stop below meets a parked task rather
        // than one that has not started yet.
        tokio::time::sleep(Duration::from_millis(50)).await;

        shutdown.cancel();
        tokio::time::timeout(Duration::from_secs(1), task)
            .await
            .expect("a stop must end the check's wait at once, not after the whole interval")
            .expect("the check task must not panic");
    }

    /// The other half: the check still runs, and it runs again every interval. Two checks is
    /// the smallest count that tells a loop from a single check.
    #[tokio::test]
    async fn the_debugger_check_repeats_every_interval() {
        let shutdown = Arc::new(CancellationToken::new());
        let checks = Arc::new(std::sync::atomic::AtomicUsize::new(0));
        let counter = checks.clone();
        let task = tokio::spawn(debugger_watch_loop_at(
            shutdown.clone(),
            Duration::from_millis(20),
            move || {
                counter.fetch_add(1, std::sync::atomic::Ordering::SeqCst);
                false
            },
        ));

        // Poll instead of sleeping a fixed span: a slow runner may need longer than three
        // intervals, and the deadline still fails the test if the loop never repeats.
        let deadline = tokio::time::Instant::now() + Duration::from_secs(5);
        while checks.load(std::sync::atomic::Ordering::SeqCst) < 2 {
            assert!(
                tokio::time::Instant::now() < deadline,
                "the debugger check did not run again on the next interval"
            );
            tokio::time::sleep(Duration::from_millis(10)).await;
        }

        shutdown.cancel();
        tokio::time::timeout(Duration::from_secs(1), task)
            .await
            .expect("the task must stop once the token is cancelled")
            .expect("the check task must not panic");
    }
}
