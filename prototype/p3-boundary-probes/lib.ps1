# lib.ps1 - shared observation helpers for the p3 boundary probes.
# PROTOTYPE: throwaway measurement code for questions V1-V3. Never merged to main.
# Everything here reads system state, or creates things under a "proto"/"EnvManagerProto"
# name and removes them again. No registry write goes through env-manager itself.

# Deliberately no Set-StrictMode: a throwaway harness must record a failure per cell
# instead of aborting the leg on an undefined variable.
$ErrorActionPreference = 'Stop'

# The SDDL service/src/pipe_dacl.rs declares but never applies.
$script:ProtoSddlConst = 'D:(A;;GA;;;BA)(A;;GA;;;SY)(A;;GA;;;OW)'
# Machine-neutral scratch root: a standard-user process cannot traverse into another
# user's profile, so nothing a secondary identity writes may live under %TEMP%.
$script:ProtoRoot = 'C:\proto'

# --- evidence plumbing -------------------------------------------------------

function Initialize-ProtoEvidence {
    param([Parameter(Mandatory)][string]$Leg)
    $dir = if ($env:PROTO_EVIDENCE_DIR) { $env:PROTO_EVIDENCE_DIR } else { Join-Path (Get-Location) 'test-results/proto' }
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    New-Item -ItemType Directory -Force -Path $script:ProtoRoot | Out-Null
    $script:ProtoEvidenceDir = $dir
    $script:ProtoLeg = $Leg
    $script:ProtoEvidencePath = Join-Path $dir "evidence-$Leg.jsonl"
    # -Encoding utf8 rather than utf8NoBOM: the SYSTEM-side wrapper may run under
    # Windows PowerShell 5.1, which has no utf8NoBOM value.
    '' | Set-Content -Path $script:ProtoEvidencePath -Encoding utf8 -NoNewline
    Write-Host "=== PROTOTYPE leg=$Leg evidence=$script:ProtoEvidencePath ==="
}

function Write-Evidence {
    param([Parameter(Mandatory)][string]$Key, [AllowNull()][object]$Value, [string]$Note = '')
    $text = ($Value | Out-String).Trim() -replace "`r", ''
    $rec = [ordered]@{
        ts    = (Get-Date).ToString('o')
        leg   = $script:ProtoLeg
        key   = $Key
        value = $text
        note  = $Note
    }
    Add-Content -Path $script:ProtoEvidencePath -Value ($rec | ConvertTo-Json -Compress -Depth 6) -Encoding utf8
    $one = ($text -replace '\s+', ' ')
    if ($one.Length -gt 900) { $one = $one.Substring(0, 900) + '...' }
    Write-Host "[EVID] $Key = $one"
    if ($Note) { Write-Host "        note: $Note" }
}

function Write-ProtoVerdictBlock {
    param([Parameter(Mandatory)][string]$Text)
    Write-Host ''
    Write-Host '================ PROTOTYPE OBSERVATION SUMMARY ================'
    Write-Host $Text
    Write-Host '=============================================================='
    Set-Content -Path (Join-Path $script:ProtoEvidenceDir "summary-$script:ProtoLeg.md") -Value $Text -Encoding utf8
}

# --- native security-descriptor access ---------------------------------------

$script:ProtoNativeSource = @'
using System;
using System.Runtime.InteropServices;

public static class ProtoWin {
    public const uint SE_FILE_OBJECT = 1;
    public const uint OWNER_SECURITY_INFORMATION = 0x1;
    public const uint GROUP_SECURITY_INFORMATION = 0x2;
    public const uint DACL_SECURITY_INFORMATION  = 0x4;
    public const uint SACL_SECURITY_INFORMATION  = 0x8;
    public const uint LABEL_SECURITY_INFORMATION = 0x20;
    public const uint SDDL_REVISION_1 = 1;
    public const uint SE_PRIVILEGE_ENABLED = 0x2;
    public const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    public const uint TOKEN_QUERY = 0x8;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetNamedSecurityInfoW(
        string name, uint objectType, uint si,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint SetNamedSecurityInfoW(
        string name, uint objectType, uint si,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        IntPtr sd, uint revision, uint si, out IntPtr sddl, out uint charCount);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(
        string sddl, uint revision, out IntPtr sd, out uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool OpenProcessToken(IntPtr processToken, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool LookupPrivilegeValueW(string lpSystemName, string lpName, out LUID lpLuid);

    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    // Native TOKEN_PRIVILEGES is DWORD + LUID(8) + DWORD = 16 bytes; the fields are
    // spelled as four 32-bit members so the managed layout matches exactly.
    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_PRIVILEGES {
        public uint PrivilegeCount;
        public uint LuidLow;
        public int LuidHigh;
        public uint Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll, ref TOKEN_PRIVILEGES newState, uint bufferLength, IntPtr previousState, IntPtr returnLength);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint GetSecurityInfo(
        IntPtr handle, uint objectType, uint si,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint SetSecurityInfo(
        IntPtr handle, uint objectType, uint si,
        IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSecurityDescriptorDacl(IntPtr sd, out bool daclPresent, out IntPtr dacl, out bool daclDefaulted);

    public static string EnablePrivilege(string name) {
        IntPtr token;
        if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out token)) {
            return "OpenProcessToken err=" + Marshal.GetLastWin32Error();
        }
        try {
            LUID luid;
            if (!LookupPrivilegeValueW(null, name, out luid)) {
                return name + " LookupPrivilegeValue err=" + Marshal.GetLastWin32Error();
            }
            TOKEN_PRIVILEGES tp;
            tp.PrivilegeCount = 1;
            tp.LuidLow = luid.LowPart;
            tp.LuidHigh = luid.HighPart;
            tp.Attributes = SE_PRIVILEGE_ENABLED;
            Marshal.SetLastPInvokeError(0);
            bool ok = AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero);
            int err = Marshal.GetLastWin32Error();
            if (!ok) { return name + " AdjustTokenPrivileges err=" + err; }
            // ERROR_NOT_ALL_ASSIGNED means the token does not hold the privilege at all,
            // while AdjustTokenPrivileges still reports success.
            if (err == 1300) { return name + " NOT ASSIGNED (token does not hold it)"; }
            return name + " enabled (err=" + err + ")";
        } finally { CloseHandle(token); }
    }

    public static string Describe(int err) {
        switch (err) {
            case 2: return " ERROR_FILE_NOT_FOUND(no pipe instance / name free)";
            case 3: return " ERROR_PATH_NOT_FOUND";
            case 5: return " ERROR_ACCESS_DENIED(SD refused this token)";
            case 231: return " ERROR_PIPE_BUSY(no free instance)";
            case 109: return " ERROR_PIPE_NOT_CONNECTED";
            case 120: return " ERROR_CALL_NOT_IMPLEMENTED";
            case 123: return " ERROR_INVALID_NAME";
            case 87: return " ERROR_INVALID_PARAMETER";
            default: return "";
        }
    }

    /// Renders the SD of a named object as SDDL for the requested information bits.
    public static string ReadSddl(string path, uint si) {
        IntPtr owner, group, dacl, sacl, sd;
        uint rc = GetNamedSecurityInfoW(path, SE_FILE_OBJECT, si, out owner, out group, out dacl, out sacl, out sd);
        if (rc != 0) { return "GET-NAMED-SEC-ERR=" + rc + Describe((int)rc); }
        if (sd == IntPtr.Zero) { return "SD-NULL"; }
        try {
            IntPtr text;
            uint len;
            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, SDDL_REVISION_1, si, out text, out len)) {
                return "CONVERT-ERR=" + Marshal.GetLastWin32Error();
            }
            try {
                if (text == IntPtr.Zero) { return "SDDL-EMPTY"; }
                return Marshal.PtrToStringUni(text);
            } finally { LocalFree(text); }
        } finally { LocalFree(sd); }
    }

    public static string DaclOf(string path) {
        // No LABEL_SECURITY_INFORMATION: an integrity-label query on a named pipe object is
        // itself refused, and it poisoned the whole request with 87 ERROR_INVALID_PARAMETER.
        return ReadSddl(path, DACL_SECURITY_INFORMATION | OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION);
    }

    public static string DaclOnlyOf(string path) {
        return ReadSddl(path, DACL_SECURITY_INFORMATION);
    }

    public static string SaclOf(string path) {
        return ReadSddl(path, SACL_SECURITY_INFORMATION);
    }

    /// The exact two-step service/src/pipe_dacl.rs performs (convert SDDL, then
    /// SetNamedSecurityInfoW on SE_FILE_OBJECT after the pipe already exists).
    /// Returns the codes rather than logging, so the probe records them itself.
    public static string ApplyDaclFromSddl(string path, string sddl) {
        IntPtr sd;
        uint size;
        if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SDDL_REVISION_1, out sd, out size)) {
            return "CONVERT-ERR=" + Marshal.GetLastWin32Error() + Describe(Marshal.GetLastWin32Error());
        }
        try {
            uint rc = SetNamedSecurityInfoW(path, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, sd, IntPtr.Zero);
            return "rc=" + rc + Describe((int)rc) + " (0 == ERROR_SUCCESS)";
        } finally { LocalFree(sd); }
    }

    /// A client-side open of the pipe name: this is where the DACL is enforced.
    /// 0x120000 == GENERIC_READ | GENERIC_WRITE.
    public static string TryOpen(string path, uint access) {
        // flags 0 = synchronous; a named pipe with a waiting instance returns at once.
        IntPtr h = CreateFileW(path, access, 0, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) {
            int err = Marshal.GetLastWin32Error();
            return "REFUSED err=" + err + Describe(err);
        }
        CloseHandle(h);
        return "OPENED ok";
    }

    // The name-based GetNamedSecurityInfoW cannot open a live named pipe (87
    // ERROR_INVALID_PARAMETER for "\\.\pipe\x", 3 ERROR_PATH_NOT_FOUND for "\pipe\x",
    // 5 ERROR_ACCESS_DENIED for the "\\localhost\pipe" form), so the descriptor is read
    // and written through a handle instead - the route the Sysinternals pipe tools use.
    public static string ReadSddlByHandle(string path, uint si) {
        // READ_CONTROL = 0x00020000; share mode 3 = FILE_SHARE_READ|FILE_SHARE_WRITE,
        // which CreateFile requires for a named pipe.
        IntPtr h = CreateFileW(path, 0x00020000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) {
            int e = Marshal.GetLastWin32Error();
            return "OPEN-READ-CONTROL-ERR=" + e + Describe(e);
        }
        try {
            IntPtr owner, group, dacl, sacl, sd;
            uint rc = GetSecurityInfo(h, SE_FILE_OBJECT, si, out owner, out group, out dacl, out sacl, out sd);
            if (rc != 0) { return "GET-SEC-INFO-ERR=" + rc + Describe((int)rc); }
            if (sd == IntPtr.Zero) { return "SD-NULL"; }
            IntPtr text; uint len;
            if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, SDDL_REVISION_1, si, out text, out len)) {
                return "CONVERT-ERR=" + Marshal.GetLastWin32Error();
            }
            string result = Marshal.PtrToStringUni(text);
            LocalFree(text);
            LocalFree(sd);
            return result;
        } finally { CloseHandle(h); }
    }

    /// Applies a DACL through a handle opened for WRITE_DAC (0x00040000). This is the
    /// realistic shape of "change the DACL after creation"; the name-based API could not
    /// even open the pipe to change it.
    public static string ApplyDaclByHandle(string path, string sddl) {
        IntPtr h = CreateFileW(path, 0x00040000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (h == new IntPtr(-1)) {
            int e = Marshal.GetLastWin32Error();
            return "OPEN-WRITE-DAC-ERR=" + e + Describe(e);
        }
        try {
            IntPtr sd; uint size;
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SDDL_REVISION_1, out sd, out size)) {
                return "CONVERT-ERR=" + Marshal.GetLastWin32Error();
            }
            try {
                IntPtr dacl; bool present; bool defaulted;
                if (!GetSecurityDescriptorDacl(sd, out present, out dacl, out defaulted)) {
                    return "GET-SD-DACL-ERR=" + Marshal.GetLastWin32Error();
                }
                uint rc = SetSecurityInfo(h, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION, IntPtr.Zero, IntPtr.Zero, dacl, IntPtr.Zero);
                return "rc=" + rc + Describe((int)rc) + " (0 == ERROR_SUCCESS) daclPresent=" + present;
            } finally { LocalFree(sd); }
        } finally { CloseHandle(h); }
    }

    public static string DaclByHandle(string path) {
        return ReadSddlByHandle(path, DACL_SECURITY_INFORMATION | OWNER_SECURITY_INFORMATION | GROUP_SECURITY_INFORMATION);
    }

    public static string DaclOnlyByHandle(string path) {
        return ReadSddlByHandle(path, DACL_SECURITY_INFORMATION);
    }

    public static string ClientIdentity() {
        return System.Environment.UserDomainName + "\\" + System.Environment.UserName;
    }
}
'@

function Add-ProtoNativeType {
    if (-not ('ProtoWin' -as [type])) {
        Add-Type -TypeDefinition $script:ProtoNativeSource -Language CSharp -ErrorAction Stop | Out-Null
    }
}

function Set-ProtoPrivileges {
    Add-ProtoNativeType
    foreach ($name in @('SeSecurityPrivilege', 'SeDebugPrivilege', 'SeTakeOwnershipPrivilege')) {
        Write-Evidence -Key "privilege.$name" -Value ([ProtoWin]::EnablePrivilege($name)) -Note 'an SD read/write against a denying DACL needs the privilege ENABLED, not merely held'
    }
}

# --- process helpers ---------------------------------------------------------

# cmd.exe cannot execute a .ps1 (it would hand it to the file association), so a script
# target is wrapped in a shell invocation for the secondary-identity cells.
function Resolve-ProtoCmdLine {
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @())
    $tail = (($Arguments | ForEach-Object { ConvertTo-ProtoArg $_ }) -join ' ')
    if ($Exe -match '\.(ps1|psm1)$') {
        $shell = if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) { 'pwsh.exe' } else { 'powershell.exe' }
        return $shell + ' -NoProfile -ExecutionPolicy Bypass -File ' + (ConvertTo-ProtoArg $Exe) + ' ' + $tail
    }
    return (ConvertTo-ProtoArg $Exe) + ' ' + $tail
}

function ConvertTo-ProtoArg {
    param([string]$a)
    if ($a -match '[\s"]') { return '"' + ($a -replace '"', '\"') + '"' }
    return $a
}

# Long-running spawn with a per-process environment: how the Background-mode cells get
# their state and temp redirects without touching machine-wide settings.
function Start-ProtoDetached {
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @(), [hashtable]$Environment = $null)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    if ($Arguments.Count -gt 0) { $psi.Arguments = (($Arguments | ForEach-Object { ConvertTo-ProtoArg $_ }) -join ' ') }
    $psi.UseShellExecute = $false
    $psi.CreateNoWindow = $true
    $psi.WindowStyle = 'Hidden'
    if ($Environment) {
        # EnvironmentVariables only takes effect when UseShellExecute is false.
        foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    }
    $p = [System.Diagnostics.Process]::Start($psi)
    $envText = ''
    if ($Environment) { $envText = (($Environment.Keys | ForEach-Object { "$_=$($Environment[$_])" }) -join ';') }
    Write-Evidence -Key 'spawn.detached' -Value "pid=$($p.Id) exe=$Exe args=$($psi.Arguments) env=$envText"
    return $p
}

function Invoke-ProtoProcess {
    param([Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @(), [int]$TimeoutSec = 60, [string]$StdIn = $null, [hashtable]$Environment = $null)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    if ($Arguments.Count -gt 0) {
        $psi.Arguments = (($Arguments | ForEach-Object { ConvertTo-ProtoArg $_ }) -join ' ')
    }
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    if ($null -ne $StdIn) { $psi.RedirectStandardInput = $true }
    $psi.CreateNoWindow = $true
    if ($Environment) {
        foreach ($k in $Environment.Keys) { $psi.EnvironmentVariables[$k] = [string]$Environment[$k] }
    }
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    if ($null -ne $StdIn) { $p.StandardInput.Write($StdIn); $p.StandardInput.Close() }
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    if (-not $p.WaitForExit($TimeoutSec * 1000)) {
        try { $null = $p.Kill($true) } catch { }
        $sw.Stop()
        return [pscustomobject]@{ StdOut = ''; StdErr = "PROTOTYPE: killed after ${TimeoutSec}s"; Code = -1; DurMs = $sw.ElapsedMilliseconds; TimedOut = $true }
    }
    $p.WaitForExit()
    $sw.Stop()
    [pscustomobject]@{
        StdOut   = $outTask.Result
        StdErr   = $errTask.Result
        Code     = $p.ExitCode
        DurMs    = $sw.ElapsedMilliseconds
        TimedOut = $false
        Pid      = $p.Id
    }
}

function Get-ProtoHostFacts {
    Add-ProtoNativeType
    Write-Evidence -Key 'host.os' -Value ([System.Environment]::OSVersion.VersionString)
    Write-Evidence -Key 'host.computer' -Value $env:COMPUTERNAME
    Write-Evidence -Key 'host.arch' -Value $env:PROCESSOR_ARCHITECTURE
    Write-Evidence -Key 'host.cpu_count' -Value ([Environment]::ProcessorCount)
    Write-Evidence -Key 'host.user' -Value ([ProtoWin]::ClientIdentity())
    Write-Evidence -Key 'host.whoami_groups' -Value ((Invoke-ProtoProcess -Exe whoami.exe -Arguments @('/groups') -TimeoutSec 30).StdOut) -Note 'Administrators must be Enabled (not Use-for-deny-only) for a BA ACE to grant'
    Write-Evidence -Key 'host.whoami_priv' -Value ((Invoke-ProtoProcess -Exe whoami.exe -Arguments @('/priv') -TimeoutSec 30).StdOut)
    Write-Evidence -Key 'host.ps_version' -Value $PSVersionTable.PSVersion.ToString()
}

# --- pipe observation --------------------------------------------------------

function Get-ProtoPipeSddl {
    param([Parameter(Mandatory)][string]$Pipe, [string]$Label = '')
    Add-ProtoNativeType
    # Every combination that could plausibly answer is tried and recorded, because which one
    # works is itself the evidence for issue #154: a DACL that cannot be read by name or by
    # handle cannot be retuned after creation either. Each open consumes one pipe instance,
    # which the service's accept loop then replaces - that is the behaviour measured, not a
    # side effect.
    $attempts = @(
        @{ key = 'sddl.handle.daclOnly'; fn = { param($p) [ProtoWin]::DaclOnlyByHandle($p) }; path = "\\.\pipe\$Pipe" },
        @{ key = 'sddl.handle.daclOwnerGroup'; fn = { param($p) [ProtoWin]::DaclByHandle($p) }; path = "\\.\pipe\$Pipe" },
        @{ key = 'sddl.byName.daclOnly'; fn = { param($p) [ProtoWin]::DaclOnlyOf($p) }; path = "\\.\pipe\$Pipe" },
        @{ key = 'sddl.byName.daclOnly'; fn = { param($p) [ProtoWin]::DaclOnlyOf($p) }; path = "\pipe\$Pipe" },
        @{ key = 'sddl.byName.daclOnly'; fn = { param($p) [ProtoWin]::DaclOnlyOf($p) }; path = "\\localhost\pipe\$Pipe" },
        @{ key = 'sddl.byName.daclOnly'; fn = { param($p) [ProtoWin]::DaclOnlyOf($p) }; path = "\Device\NamedPipe\$Pipe" }
    )
    $answer = 'ALL-ROUTES-FAILED'
    foreach ($a in $attempts) {
        $r = & $a.fn $a.path
        Write-Evidence -Key "$($a.key)[$Label]" -Value "$($a.path) => $r"
        if ($r -notmatch 'ERR=' -and $r -ne 'SD-NULL') { $answer = $r }
    }
    return $answer
}

# Applies the declared DACL by both post-creation routes and records each result: the
# name-based one the shipped helper uses, and the handle-based one that can actually work.
function Set-ProtoPipeDacl {
    param([Parameter(Mandatory)][string]$Pipe, [string]$Sddl = $script:ProtoSddlConst, [string]$Label = '')
    Add-ProtoNativeType
    $byName = [ProtoWin]::ApplyDaclFromSddl("\\.\pipe\$Pipe", $Sddl)
    $byHandle = [ProtoWin]::ApplyDaclByHandle("\\.\pipe\$Pipe", $Sddl)
    Write-Evidence -Key "apply.byName[$Label]" -Value "\\.\pipe\$Pipe => $byName" -Note 'the exact two calls service/src/pipe_dacl.rs performs'
    Write-Evidence -Key "apply.byHandle[$Label]" -Value "\\.\pipe\$Pipe => $byHandle" -Note 'SetSecurityInfo on a WRITE_DAC handle'
    return [pscustomobject]@{ ByName = $byName; ByHandle = $byHandle }
}

function Test-ProtoPipeOpen {
    param([Parameter(Mandatory)][string]$Pipe, [string]$Label = '', [switch]$ReadOnly)
    Add-ProtoNativeType
    # 2147483648 == 0x80000000 GENERIC_READ. The hex literal cannot be used: PowerShell
    # reads 0x80000000 as Int32.MinValue and the UInt32 marshal refuses it.
    $access = if ($ReadOnly) { 2147483648 } else { 0x120000 }
    $r = [ProtoWin]::TryOpen("\\.\pipe\$Pipe", $access)
    Write-Evidence -Key "open[$Label]" -Value "\\.\pipe\$Pipe access=$('0x{0:X}' -f [uint32]$access) => $r"
    return $r
}

function Connect-ProtoPipeClient {
    param([Parameter(Mandatory)][string]$Pipe, [string]$Request = '{"method":"ping"}', [int]$ConnectTimeoutMs = 5000)
    # The shipped-client shape: NamedPipeClientStream, one line each way.
    $client = $null
    try {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Pipe, [System.IO.Pipes.PipeDirection]::InOut)
        $client.Connect($ConnectTimeoutMs)
        # No preamble: the service parses the first byte, and a UTF-8 BOM turns the request
        # into "invalid request: expected value at line 1 column 1".
        $w = New-Object System.IO.StreamWriter($client, (New-Object System.Text.UTF8Encoding($false)), 1024, $true)
        $w.WriteLine($Request)
        $w.Flush()
        $rd = New-Object System.IO.StreamReader($client, [System.Text.Encoding]::UTF8, $false, 1024, $true)
        $resp = $rd.ReadLine()
        return [pscustomobject]@{ Connected = $true; Response = $resp; Error = '' }
    } catch {
        return [pscustomobject]@{ Connected = $false; Response = ''; Error = ('{0}: {1}' -f $_.Exception.GetType().Name, $_.Exception.Message) }
    } finally {
        if ($client) { try { $client.Dispose() } catch { } }
    }
}

function Wait-ProtoPipeAnswered {
    param([Parameter(Mandatory)][string]$Pipe, [int]$TimeoutSec = 25)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $c = Connect-ProtoPipeClient -Pipe $Pipe -Request '{"method":"ping"}' -ConnectTimeoutMs 1500
        if ($c.Connected) { return $c.Response }
        Start-Sleep -Milliseconds 400
    }
    return '(never answered)'
}

# --- SCM service as LocalSystem ---------------------------------------------

# service/src/scm.rs registers the dispatch table under this exact name, so the
# SCM service has to carry it: any other name makes StartServiceCtrlDispatcherW
# fail with ERROR_SERVICE_DOES_NOT_EXIST.
$script:ProtoServiceName = 'EnvManagerService'

function Test-ProtoServicePresent {
    param([string]$Name = $script:ProtoServiceName)
    return [bool](Get-Service -Name $Name -ErrorAction SilentlyContinue)
}

function New-ProtoService {
    param([Parameter(Mandatory)][string]$BinPath, [string]$ServiceArgs = '--mode=service', [string]$Name = $script:ProtoServiceName)
    if (Test-ProtoServicePresent -Name $Name) {
        throw "PROTOTYPE refuses to start: service '$Name' already exists (this machine has a real install)."
    }
    # sc.exe needs 'binPath=' and its value as separate argv tokens; the inner quotes
    # survive CommandLineToArgvW so ImagePath becomes "<exe>" --mode=service, the form
    # the SCM parser expects for a path with spaces.
    $imagePath = '"' + $BinPath + '" ' + $ServiceArgs
    $r = Invoke-ProtoProcess -Exe sc.exe -Arguments @('create', $Name, 'binPath=', $imagePath, 'start=', 'demand', 'obj=', 'LocalSystem', 'type=', 'own') -TimeoutSec 30
    Write-Evidence -Key 'service.create' -Value ("exit=$($r.Code) out=$($r.StdOut.Trim()) err=$($r.StdErr.Trim())")
    if ($r.Code -ne 0) { throw "sc create failed: $($r.StdErr)" }
    $qc = Invoke-ProtoProcess -Exe sc.exe -Arguments @('qc', $Name) -TimeoutSec 20
    Write-Evidence -Key 'service.qc' -Value $qc.StdOut -Note 'SERVICE_START_NAME must be LocalSystem (or NT AUTHORITY\SYSTEM) for the V1 premise'
    $sid = Invoke-ProtoProcess -Exe sc.exe -Arguments @('showsid', $Name) -TimeoutSec 20
    Write-Evidence -Key 'service.showsid' -Value ($sid.StdOut.Trim() -replace '\s+', ' ') -Note 'the service logon SID; LocalSystem pipes carry no per-user owner SID'
    return $r
}

function Start-ProtoService {
    param([string]$Name = $script:ProtoServiceName, [int]$TimeoutSec = 45)
    $t0 = Get-Date
    $r = Invoke-ProtoProcess -Exe sc.exe -Arguments @('start', $Name) -TimeoutSec 60
    Write-Evidence -Key 'service.start' -Value ("exit=$($r.Code) out=$($r.StdOut.Trim()) err=$($r.StdErr.Trim())")
    $state = 'unknown'
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $s = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($s) { $state = [string]$s.Status }
        if ($state -eq 'Running') { break }
        Start-Sleep -Milliseconds 400
    }
    Write-Evidence -Key 'service.state' -Value "$state after $([math]::Round(((Get-Date) - $t0).TotalSeconds, 2))s"
    return $state
}

function Stop-ProtoService {
    param([string]$Name = $script:ProtoServiceName, [int]$TimeoutSec = 40)
    $t0 = Get-Date
    $r = Invoke-ProtoProcess -Exe sc.exe -Arguments @('stop', $Name) -TimeoutSec 60
    $scStopMs = ((Get-Date) - $t0).TotalMilliseconds
    $state = 'unknown'
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $s = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if (-not $s) { $state = 'Gone'; break }
        $state = [string]$s.Status
        if ($state -eq 'Stopped') { break }
        Start-Sleep -Milliseconds 200
    }
    [pscustomobject]@{
        StopExit     = $r.Code
        ScStopMs     = [math]::Round($scStopMs, 1)
        State        = $state
        ToStoppedSec = [math]::Round(((Get-Date) - $t0).TotalSeconds, 3)
        ScStopOut    = $r.StdOut.Trim()
    }
}

function Remove-ProtoService {
    param([string]$Name = $script:ProtoServiceName)
    if (-not (Test-ProtoServicePresent -Name $Name)) { return }
    $null = Invoke-ProtoProcess -Exe sc.exe -Arguments @('stop', $Name) -TimeoutSec 20
    Start-Sleep -Milliseconds 800
    $r = Invoke-ProtoProcess -Exe sc.exe -Arguments @('delete', $Name) -TimeoutSec 30
    Write-Evidence -Key 'service.delete' -Value ("exit=$($r.Code) $($r.StdOut.Trim())")
}

function Get-ProtoServiceLogTail {
    param([Parameter(Mandatory)][string]$Dir, [int]$Lines = 500, [string]$Label = '')
    if (-not (Test-Path $Dir)) { Write-Evidence -Key "logfile[$Label]" -Value "missing dir $Dir"; return '' }
    $files = @(Get-ChildItem -Path $Dir -Filter 'env-manager-service.log*' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
    if ($files.Count -eq 0) { Write-Evidence -Key "logfile[$Label]" -Value "no log file under $Dir"; return '' }
    $f = $files[0]
    Write-Evidence -Key "logfile[$Label]" -Value "$($f.FullName) bytes=$($f.Length)"
    $tail = (Get-Content -Path $f.FullName -Tail $Lines -ErrorAction SilentlyContinue) -join "`n"
    Set-Content -Path (Join-Path $script:ProtoEvidenceDir ("service-log-$Label-$([System.IO.Path]::GetFileName($f.FullName))")) -Value $tail -Encoding utf8
    return $tail
}

# --- secondary identities ----------------------------------------------------

function Get-ProtoScratchDir {
    param([Parameter(Mandatory)][string]$Sub)
    $d = Join-Path $script:ProtoRoot $Sub
    New-Item -ItemType Directory -Force -Path $d | Out-Null
    return $d
}

function New-ProtoLocalUser {
    param([string]$Name = 'emp3low', [string]$Password = ('P3p' + [guid]::NewGuid().ToString('N').Substring(0, 12) + 'x!'))
    Remove-ProtoLocalUser -Name $Name
    $sec = ConvertTo-SecureString $Password -AsPlainText -Force
    $null = New-LocalUser -Name $Name -Password $sec -PasswordNeverExpires -AccountNeverExpires -ErrorAction Stop
    $null = Add-LocalGroupMember -Group 'Users' -Member $Name -ErrorAction SilentlyContinue
    try { $null = Remove-LocalGroupMember -Group 'Administrators' -Member $Name -ErrorAction SilentlyContinue } catch { }
    $cred = New-Object System.Management.Automation.PSCredential($Name, $sec)
    Write-Evidence -Key 'lowuser.created' -Value "$Name : member of Users only"
    return [pscustomobject]@{ Name = $Name; Credential = $cred; Plain = $Password }
}

function Remove-ProtoLocalUser {
    param([string]$Name = 'emp3low')
    if (Get-LocalUser -Name $Name -ErrorAction SilentlyContinue) {
        $null = Remove-LocalUser -Name $Name -ErrorAction SilentlyContinue
        Write-Evidence -Key 'lowuser.removed' -Value $Name
    }
}

function Grant-ProtoDirAccess {
    param([Parameter(Mandatory)][string]$Path, [string]$User = 'emp3low')
    # Translate through the local-account SID rather than a "machine-name" style string: the
    # just-created account does not always resolve through LookupAccountName yet, and
    # SetAccessRule then throws "Some or all identity references could not be translated".
    $local = Get-LocalUser -Name $User -ErrorAction SilentlyContinue
    if (-not $local) { Write-Evidence -Key "acl.grant[$User]" -Value "SKIPPED: no such local user for $Path"; return }
    $sidText = if ($local.SID -is [string]) { $local.SID } else { $local.SID.Value }
    $sid = New-Object System.Security.Principal.SecurityIdentifier($sidText)
    $acl = Get-Acl -Path $Path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule($sid, 'Modify', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -Path $Path -AclObject $acl
    Write-Evidence -Key "acl.grant[$User]" -Value "$Path ($sidText)"
}

# Two logon shapes on purpose: a named pipe default DACL commonly keys on the LOGON SID
# type (IU interactive / SU service / network), so "a standard user" is not one answer.
function Invoke-ProtoAsUserInteractive {
    param([Parameter(Mandatory)][pscustomobject]$As, [Parameter(Mandatory)][string]$Exe, [string[]]$Arguments = @(), [int]$TimeoutSec = 90)
    $dir = Get-ProtoScratchDir 'out'
    Grant-ProtoDirAccess -Path $dir -User $As.Name
    $out = Join-Path $dir ('interactive-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
    $cmdLine = Resolve-ProtoCmdLine -Exe $Exe -Arguments $Arguments
    $body = "chdir /d $dir`r`nwhoami /user /groups > `"$out`" 2>&1`r`n$cmdLine >> `"$out`" 2>&1`r`necho CLI_EXITCODE=%ERRORLEVEL% >> `"$out`""
    $bat = Join-Path $dir 'run-low.bat'
    [System.IO.File]::WriteAllText($bat, $body, [System.Text.Encoding]::ASCII)
    # -NoNewWindow is rejected together with -Credential, so the window exists and is
    # hidden instead. The exit code is read from the wrapper's own marker line.
    $p = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $bat) -Credential $As.Credential -WindowStyle Hidden -PassThru
    if (-not $p.WaitForExit($TimeoutSec * 1000)) { try { $null = $p.Kill($true) } catch { } }
    Start-Sleep -Milliseconds 400
    $text = if (Test-Path $out) { [System.IO.File]::ReadAllText($out) } else { '(no output file: the process never produced one)' }
    return [pscustomobject]@{ Out = $text; File = $out }
}

function Invoke-ProtoAsUserViaTask {
    param(
        [Parameter(Mandatory)][pscustomobject]$As,
        [Parameter(Mandatory)][string]$Exe,
        [string[]]$Arguments = @(),
        [ValidateSet('S4U', 'Password')][string]$LogonType = 'S4U',
        [int]$TimeoutSec = 120,
        [string]$TaskName = ('EnvManagerProtoTask' + (Get-Random -Maximum 99999))
    )
    $dir = Get-ProtoScratchDir 'out'
    Grant-ProtoDirAccess -Path $dir -User $As.Name
    $out = Join-Path $dir ("task-$TaskName.txt")
    if (Test-Path $out) { Remove-Item $out -Force }
    $cmdLine = Resolve-ProtoCmdLine -Exe $Exe -Arguments $Arguments
    $inner = "chdir /d $dir & whoami /user /groups > `"$out`" 2>&1 & $cmdLine >> `"$out`" 2>&1 & echo TASK_EXITCODE=%ERRORLEVEL% >> `"$out`" & echo TASKDONE >> `"$out`""
    $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c $inner"
    if ($LogonType -eq 'Password') {
        Register-ScheduledTask -TaskName $TaskName -Action $action -User $As.Name -Password $As.Plain -Force -ErrorAction Stop | Out-Null
    } else {
        $principal = New-ScheduledTaskPrincipal -UserId $As.Name -LogonType S4U
        Register-ScheduledTask -TaskName $TaskName -Action $action -Principal $principal -Force -ErrorAction Stop | Out-Null
    }
    $null = Start-ScheduledTask -TaskName $TaskName
    # LastTaskResult is 267011 (SCHED_S_TASK_NOT_SCHEDULED) before the first run finishes,
    # so polling it alone returned immediately and the output file had not been written yet.
    # The wrapper's own TASKDONE marker is the completion signal.
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $info = $null
    while ((Get-Date) -lt $deadline) {
        if ((Test-Path $out) -and ((Get-Content -Raw -Path $out) -match 'TASKDONE')) { break }
        Start-Sleep -Milliseconds 400
    }
    $info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    $text = if (Test-Path $out) { [System.IO.File]::ReadAllText($out) } else { '(no output file)' }
    return [pscustomobject]@{ Out = $text; LastResult = $info.LastTaskResult; ResultText = $info.ResultExpression }
}

function Invoke-ProtoAsSystemViaTask {
    param([Parameter(Mandatory)][string]$ScriptPath, [string[]]$Arguments = @(), [int]$TimeoutSec = 240, [string]$TaskName = ('EnvManagerProtoSystem' + (Get-Random -Maximum 99999)))
    $dir = Get-ProtoScratchDir 'out'
    $out = Join-Path $dir ("system-$TaskName.txt")
    if (Test-Path $out) { Remove-Item $out -Force }
    $argLine = (($Arguments | ForEach-Object { ConvertTo-ProtoArg $_ }) -join ' ')
    # pwsh keeps the same PowerShell semantics the orchestrating legs use; fall back to
    # Windows PowerShell only when the runner has no pwsh on PATH.
    $host_exe = if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) { 'pwsh.exe' } else { 'powershell.exe' }
    $inner = $host_exe + " -NoProfile -ExecutionPolicy Bypass -File `"$ScriptPath`" $argLine > `"$out`" 2>&1`r`necho SYSTEMTASK_EXIT=%ERRORLEVEL% >> `"$out`""
    $bat = Join-Path $dir ("sys-$TaskName.bat")
    [System.IO.File]::WriteAllText($bat, $inner, [System.Text.Encoding]::ASCII)
    $action = New-ScheduledTaskAction -Execute 'cmd.exe' -Argument "/c `"$bat`""
    $principal = New-ScheduledTaskPrincipal -UserId 'SYSTEM' -LogonType ServiceAccount -RunLevel Highest
    Register-ScheduledTask -TaskName $TaskName -Action $action -Principal $principal -Force -ErrorAction Stop | Out-Null
    $null = Start-ScheduledTask -TaskName $TaskName
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    $info = $null
    while ((Get-Date) -lt $deadline) {
        # Same reason as Invoke-ProtoAsUserViaTask: wait for the wrapper's own marker rather
        # than LastTaskResult, which reads "not scheduled" until the first run completes.
        if ((Test-Path $out) -and ((Get-Content -Raw -Path $out) -match 'SYSTEMTASK_EXIT')) { break }
        Start-Sleep -Milliseconds 500
    }
    $info = Get-ScheduledTaskInfo -TaskName $TaskName -ErrorAction SilentlyContinue
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    $text = if (Test-Path $out) { [System.IO.File]::ReadAllText($out) } else { '(no output file)' }
    return [pscustomobject]@{ Out = $text; File = $out; LastResult = $info.LastTaskResult }
}

function Get-ProtoProcessSnapshot {
    param([Parameter(Mandatory)][string[]]$Names)
    $rows = @(Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $Names -contains $_.Name } | ForEach-Object {
        '{0} pid={1} ppid={2} created={3} cmd={4}' -f $_.Name, $_.ProcessId, $_.ParentProcessId, $_.CreationDate, $_.CommandLine
    })
    if ($rows.Count -eq 0) { return '(none running)' }
    return ($rows -join ' || ')
}

function Get-ProtoTempResidue {
    param([Parameter(Mandatory)][string]$Root, [string]$Pattern = 'env-manager-sops-*')
    if (-not (Test-Path $Root)) { return [pscustomobject]@{ Root = $Root; Found = $false; Detail = '(root missing)' } }
    $dirs = @(Get-ChildItem -Path $Root -Filter $Pattern -Directory -ErrorAction SilentlyContinue)
    if ($dirs.Count -eq 0) {
        return [pscustomobject]@{ Root = $Root; Found = $false; Detail = '(no matching directory)' }
    }
    $detail = @()
    foreach ($d in $dirs) {
        foreach ($f in @(Get-ChildItem -Path $d.FullName -File -Recurse -ErrorAction SilentlyContinue)) {
            $head = ''
            try { $head = (Get-Content -Path $f.FullName -TotalCount 1 -ErrorAction Stop) } catch { $head = '(unreadable)' }
            $detail += ('{0} bytes={1} head={2}' -f $f.FullName, $f.Length, $head)
        }
    }
    return [pscustomobject]@{ Root = $Root; Found = $true; Detail = ($detail -join ' || ') }
}

# Records a terminating error as evidence, so a leg that dies mid-run still leaves the
# reason in the artifact instead of only in the step log.
function Write-ProtoFatal {
    param([Parameter(Mandatory)]$ErrorRecord)
    $where = ''
    try { $where = $ErrorRecord.InvocationInfo.PositionMessage } catch { }
    $trace = ''
    try { $trace = $ErrorRecord.ScriptStackTrace } catch { }
    Write-Evidence -Key 'FATAL' -Value ($ErrorRecord.Exception.Message + ' @ ' + $where)
    Write-Evidence -Key 'FATAL.trace' -Value ((($trace -split "`n") -join " <- ") -replace "^ +", "")
    Write-Host ("PROTOTYPE FATAL: " + $ErrorRecord.Exception.Message)
}
