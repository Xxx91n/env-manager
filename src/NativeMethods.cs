using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// Native interop helpers (architecture-recovery issue 05): WER crash-dialog suppression,
/// ACL-restricted file writes, and the WM_SETTINGCHANGE broadcast P/Invoke, moved verbatim
/// from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
    // v0.9.13 Phase 2D/4A: WER disable + crash dump protection P/Invoke
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetErrorMode(uint uMode);

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint SetProcessErrorMode(uint uMode);

    [System.Runtime.InteropServices.DllImport("wer.dll", SetLastError = true)]
    private static extern int WerSetFlags(int flags);

    /// <summary>
    /// Disable WER crash dialogs: SEM_FAILCRITICALERRORS | SEM_NOGPFAULTERRORBOX | SEM_NOOPENFILEERRORBOX
    /// + WerSetFlags(WER_FAULT_REPORTING_DISABLE | WER_FAULT_REPORTING_NO_QUEUE)
    /// Best-effort: swallowed exceptions never block the CLI.
    /// </summary>
    private static void DisableCrashDialogs()
    {
        try
        {
            // SEM_FAILCRITICALERRORS=0x0001 | SEM_NOGPFAULTERRORBOX=0x0002 | SEM_NOOPENFILEERRORBOX=0x8000
            const uint SEM_FLAGS = 0x0001 | 0x0002 | 0x8000;
            var old = SetErrorMode(SEM_FLAGS);
            SetErrorMode(SEM_FLAGS | old); // Preserve prior flags
        }
        catch { } // best-effort

        try
        {
            // WER_FAULT_REPORTING_DISABLE=0x1 | WER_FAULT_REPORTING_NO_QUEUE=0x2
            WerSetFlags(0x3);
        }
        catch { } // wer.dll may not be present
    }

    // v0.9.13 Phase 3A: NTFS ACL restriction on audit files
    // Restricts access to current user + SYSTEM only. Removes inheritance.
    private static void SetFileAclRestricted(string path)
    {
        if (!File.Exists(path)) return;
        try
        {
            var fi = new FileInfo(path);
            var security = fi.GetAccessControl();
            security.SetAccessRuleProtection(true, false); // Disable inheritance, no copy
            // Remove existing rules
            var rules = security.GetAccessRules(true, true, typeof(System.Security.Principal.NTAccount));
            foreach (System.Security.AccessControl.FileSystemAccessRule rule in rules)
            {
                security.RemoveAccessRule(rule);
            }
            // Add current user FullControl
            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().User;
            if (currentUser != null)
            {
                security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                    currentUser,
                    System.Security.AccessControl.FileSystemRights.FullControl,
                    System.Security.AccessControl.AccessControlType.Allow));
            }
            // Add SYSTEM FullControl
            var system = new System.Security.Principal.NTAccount("NT AUTHORITY\\SYSTEM");
            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                system,
                System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.AccessControlType.Allow));
            fi.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            DebugLog($"ACL restriction failed for {path}: {ex.Message}");
        }
    }
}

/// <summary>
/// LocalFree interop used by DpapiHelper buffer cleanup (architecture-recovery issue 06):
/// moved verbatim from EnvFeatures.cs. Behavior unchanged.
/// </summary>
internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LocalFree(IntPtr hMem);
}
