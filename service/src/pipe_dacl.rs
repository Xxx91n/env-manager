// Named pipe DACL (discretionary access control list).
// Restricts pipe connections to current user + Administrators + SYSTEM only.
// Called after pipe creation. Best-effort: logs a warn on failure, does not block.
// ponytail: one free fn, SDDL string approach (no manual ACL building).

/// PROTOTYPE(p3-boundary-probes) - DO NOT MERGE. The declared SDDL, named so the V1 probe
/// can apply the identical descriptor through both routes (create-then-set and
/// pass-at-creation) instead of paraphrasing it.
pub const PROTO_DECLARED_SDDL: &str = "D:(A;;GA;;;BA)(A;;GA;;;SY)(A;;GA;;;OW)";

/// PROTOTYPE(p3-boundary-probes) - DO NOT MERGE. Same two calls as
/// `set_pipe_dacl_current_user_only`, but the Win32 results come back as a string for the
/// probe to log: issue #154 needs to know whether SetNamedSecurityInfoW can retune a
/// named pipe at all, and a warn line that swallows the code cannot answer that.
#[cfg(windows)]
pub fn measure_set_pipe_dacl(pipe_name: &str) -> String {
    use std::ffi::c_void;
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Foundation::{GetLastError, LocalFree};
    use windows_sys::Win32::Security::Authorization::{
        ConvertStringSecurityDescriptorToSecurityDescriptorW, SetNamedSecurityInfoW,
        SE_FILE_OBJECT, SDDL_REVISION_1,
    };
    use windows_sys::Win32::Security::DACL_SECURITY_INFORMATION;

    let sddl: Vec<u16> = PROTO_DECLARED_SDDL
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();
    let pipe_wide: Vec<u16> = OsStr::new(pipe_name)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();

    let mut psd: *mut c_void = std::ptr::null_mut();
    if unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.as_ptr(),
            SDDL_REVISION_1,
            &mut psd,
            std::ptr::null_mut(),
        )
    } == 0
    {
        return format!(
            "CONVERT-ERR={}",
            unsafe { GetLastError() }
        );
    }
    let rc = unsafe {
        SetNamedSecurityInfoW(
            pipe_wide.as_ptr() as *const u16,
            SE_FILE_OBJECT,
            DACL_SECURITY_INFORMATION,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            psd as *const _,
            std::ptr::null_mut(),
        )
    };
    unsafe { LocalFree(psd as *mut c_void); }
    format!("rc={rc} (0 == ERROR_SUCCESS) last={}", unsafe { GetLastError() })
}

#[cfg(not(windows))]
pub fn measure_set_pipe_dacl(_pipe_name: &str) -> String {
    "non-windows".to_string()
}

#[cfg(windows)]
pub fn set_pipe_dacl_current_user_only(pipe_name: &str) {
    use std::ffi::OsStr;
    use std::os::windows::ffi::OsStrExt;
    use windows_sys::Win32::Security::Authorization::{
        ConvertStringSecurityDescriptorToSecurityDescriptorW,
        SDDL_REVISION_1,
    };
    use windows_sys::Win32::Security::DACL_SECURITY_INFORMATION;
    use windows_sys::Win32::Foundation::LocalFree;

    // SDDL: D:(A;;GA;;;BA)(A;;GA;;;SY)(A;;GA;;;OW)
    // GA = GENERIC_ALL, BA = Built-in Administrators, SY = SYSTEM, OW = Owner
    let sddl: Vec<u16> = PROTO_DECLARED_SDDL
        .encode_utf16()
        .chain(std::iter::once(0))
        .collect();

    let pipe_wide: Vec<u16> = OsStr::new(pipe_name)
        .encode_wide()
        .chain(std::iter::once(0))
        .collect();

    let mut psd: *mut std::ffi::c_void = std::ptr::null_mut();
    let ok = unsafe {
        ConvertStringSecurityDescriptorToSecurityDescriptorW(
            sddl.as_ptr(),
            SDDL_REVISION_1,
            &mut psd,
            std::ptr::null_mut(),
        )
    };

    if ok == 0 {
        tracing::warn!("set_pipe_dacl: SDDL conversion failed (error={})", unsafe { windows_sys::Win32::Foundation::GetLastError() });
        return;
    }

    // Apply the security descriptor to the named pipe via SetNamedSecurityInfoW.
    use windows_sys::Win32::Security::Authorization::SetNamedSecurityInfoW;
    use windows_sys::Win32::Security::Authorization::SE_FILE_OBJECT;

    let result = unsafe {
        SetNamedSecurityInfoW(
            pipe_wide.as_ptr() as *const u16,
            SE_FILE_OBJECT,
            DACL_SECURITY_INFORMATION,
            std::ptr::null_mut(),
            std::ptr::null_mut(),
            psd as *const _,
            std::ptr::null_mut(),
        )
    };

    unsafe { LocalFree(psd as *mut std::ffi::c_void); }

    if result != 0 {
        tracing::warn!("set_pipe_dacl: SetNamedSecurityInfoW failed (error={})", result);
    } else {
        tracing::info!("set_pipe_dacl: DACL applied to {}", pipe_name);
    }
}

#[cfg(not(windows))]
pub fn set_pipe_dacl_current_user_only(_pipe_name: &str) {}

#[cfg(test)]
mod tests {
    #[test]
    fn test_dacl_module_compiles() {
        super::set_pipe_dacl_current_user_only("test_pipe");
    }
}
