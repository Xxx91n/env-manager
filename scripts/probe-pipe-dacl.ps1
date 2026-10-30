# probe-pipe-dacl.ps1 - effect-level evidence for the service named-pipe DACL.
#
# Why this is a dispatch-only CI job and not a test: "who may connect to
# \\.\pipe\EnvManager.Service" is a property of a real LocalSystem service and a real
# second identity. No unit seam can produce either, and faking them would pin an
# assumption instead of the behaviour. The job that calls this script
# (.github/workflows/pipe-dacl-observation.yml) is manual-only, so it never sits on the
# PR path; its run id and its uploaded artifact are the evidence.
#
# What it asserts (the behavioural half of the acceptance criteria):
#   1. both pipes carry the declared SDDL, read through a handle with SE_KERNEL_OBJECT -
#      the by-name API answers 87 ERROR_INVALID_PARAMETER for a named pipe, so it cannot
#      be the route (that measurement is recorded too, as the reason);
#   2. the two shipped client shapes still work against the Service pipe: the CLI
#      gateway (`service ping --service-pipe`) and the GUI watchdog's raw
#      NamedPipeClientStream ping;
#   3. a non-administrator identity is refused at the first CreateFile with err=5, and a
#      second --mode=service process cannot take the first instance over.
#
# Exit code: 0 = every asserted property held, 1 = at least one did not. The JSONL
# evidence and the summary are written either way.
#
# The low-privilege raw open runs this same file in -ProbePipe mode under the secondary
# identity. Same code, different identity: a separate probe script would be a second
# implementation of the measurement it is meant to corroborate.

param(
    [string]$CliExe,
    [string]$ServiceExe,
    [string]$EvidenceDir = (Join-Path (Get-Location) 'test-results/pipe-dacl'),
    [string]$ServiceName = 'EnvManagerService',
    [string]$ScratchRoot = (Join-Path $env:ProgramData 'EnvManagerPipeProbe'),
    [string]$DeclaredSddl = 'D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;GA;;;OW)',
    [string]$ProbePipe,
    [string]$ProbeResultPath
)

$ErrorActionPreference = 'Stop'
$script:PipeService = 'EnvManager.Service'
$script:PipeBackground = 'EnvManager.Background'
$script:Checks = New-Object System.Collections.Generic.List[object]
# True only after this run registered the SCM service itself, so cleanup can never
# delete a registration it did not create.
$script:CreatedService = $false
# PID of the service process this run started, for the same reason.
$script:ServicePid = 0
# True only after this run created the local account, so cleanup can never delete one it
# did not create.
$script:CreatedLowUser = $false
# Binaries and the probe copy live in <ScratchRoot>\bin (Users: ReadAndExecute);
# everything the secondary identity writes - its wrapper and its output files - lives in
# <ScratchRoot>\work (Users: Modify). Two directories so the account that runs the probe
# never holds write access to anything it executes.
$script:WorkDir = Join-Path $ScratchRoot 'work'
$script:BinDir = Join-Path $ScratchRoot 'bin'
# Two access masks, because they answer different questions. 0x120000 is SYNCHRONIZE |
# READ_CONTROL - the bare access check, deliberately below what any client asks for.
# 0xC0000000 is GENERIC_READ | GENERIC_WRITE, the mask NamedPipeClientStream requests, so
# it is what the CLI gateway and the GUI watchdog really need. The second is written in
# decimal because PowerShell parses the hex literal as a negative Int32.
$script:BareAccess = 0x120000
$script:ClientAccess = 3221225472
# One row per (identity, pipe) pair; rendered as a table into the summary artifact.
$script:Matrix = New-Object System.Collections.Generic.List[object]

# --- native access ------------------------------------------------------------

$script:ProbeNativeSource = @'
using System;
using System.Runtime.InteropServices;

public static class PipeProbe
{
    public const uint DACL_SECURITY_INFORMATION = 0x4;
    public const uint OWNER_SECURITY_INFORMATION = 0x1;
    public const uint GROUP_SECURITY_INFORMATION = 0x2;
    public const uint SDDL_REVISION_1 = 1;
    public const uint SE_KERNEL_OBJECT = 6;
    public const uint SE_FILE_OBJECT = 1;
    public const uint READ_CONTROL = 0x00020000;
    public const uint FILE_SHARE_BOTH = 3;
    public const uint OPEN_EXISTING = 3;
    public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern IntPtr CreateFileW(string name, uint access, uint share, IntPtr sa, uint creation, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr h);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern uint GetSecurityInfo(IntPtr handle, uint objectType, uint si,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern uint GetNamedSecurityInfoW(string name, uint objectType, uint si,
        out IntPtr owner, out IntPtr group, out IntPtr dacl, out IntPtr sacl, out IntPtr sd);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool ConvertSecurityDescriptorToStringSecurityDescriptorW(
        IntPtr sd, uint revision, uint si, out IntPtr sddl, out uint chars);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr LocalFree(IntPtr h);

    public static string Describe(int err)
    {
        switch (err)
        {
            case 2: return " ERROR_FILE_NOT_FOUND(no instance listening)";
            case 5: return " ERROR_ACCESS_DENIED(the DACL refused this token)";
            case 87: return " ERROR_INVALID_PARAMETER";
            case 109: return " ERROR_PIPE_NOT_CONNECTED";
            case 231: return " ERROR_PIPE_BUSY(no free instance)";
            default: return "";
        }
    }

    // The DACL of a live instance, read the only way that works for a named pipe.
    public static string DaclByHandle(string name)
    {
        IntPtr h = CreateFileW(name, READ_CONTROL, FILE_SHARE_BOTH, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == INVALID_HANDLE_VALUE)
        {
            int openErr = Marshal.GetLastWin32Error();
            return "OPEN-READ-CONTROL-ERR=" + openErr + Describe(openErr);
        }
        try
        {
            IntPtr owner, group, dacl, sacl, sd;
            uint rc = GetSecurityInfo(h, SE_KERNEL_OBJECT, DACL_SECURITY_INFORMATION,
                out owner, out group, out dacl, out sacl, out sd);
            if (rc != 0) { return "GET-SECURITY-INFO-ERR=" + rc + Describe((int)rc); }
            try
            {
                IntPtr text;
                uint chars;
                if (!ConvertSecurityDescriptorToStringSecurityDescriptorW(sd, SDDL_REVISION_1, DACL_SECURITY_INFORMATION, out text, out chars))
                {
                    int convErr = Marshal.GetLastWin32Error();
                    return "CONVERT-ERR=" + convErr + Describe(convErr);
                }
                try { return Marshal.PtrToStringUni(text); }
                finally { LocalFree(text); }
            }
            finally { LocalFree(sd); }
        }
        finally { CloseHandle(h); }
    }

    // The route the deleted helper used. Kept as a measurement, not as a fallback.
    public static string DaclByName(string name)
    {
        IntPtr owner, group, dacl, sacl, sd;
        uint rc = GetNamedSecurityInfoW(name, SE_FILE_OBJECT, DACL_SECURITY_INFORMATION,
            out owner, out group, out dacl, out sacl, out sd);
        if (rc != 0) { return "GET-NAMED-SEC-INFO-ERR=" + rc + Describe((int)rc); }
        LocalFree(sd);
        return "GET-NAMED-SEC-INFO-OK(by name worked, contrary to the recorded measurement)";
    }

    // A bare access check: no CLI, no retry, no envelope. The mask is the caller's; the
    // probe runs once with SYNCHRONIZE|READ_CONTROL (0x120000, below any client's request)
    // and once with GENERIC_READ|GENERIC_WRITE (0xC0000000, what a pipe client asks for).
    public static string TryOpen(string name, uint access)
    {
        IntPtr h = CreateFileW(name, access, FILE_SHARE_BOTH, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        if (h == INVALID_HANDLE_VALUE)
        {
            int err = Marshal.GetLastWin32Error();
            return "REFUSED err=" + err + Describe(err);
        }
        CloseHandle(h);
        return "OPENED ok";
    }
}
'@

function Initialize-ProbeType {
    if (-not ('PipeProbe' -as [type])) {
        Add-Type -TypeDefinition $script:ProbeNativeSource -Language CSharp -ErrorAction Stop | Out-Null
    }
}

# --- evidence ----------------------------------------------------------------

function Write-Evidence {
    param([string]$Key, [AllowNull()][object]$Value, [string]$Note = '')
    $text = ($Value | Out-String).Trim() -replace "`r", ''
    $record = [ordered]@{
        ts    = (Get-Date).ToString('o')
        key   = $Key
        value = $text
        note  = $Note
    }
    Add-Content -Path $script:EvidencePath -Value ($record | ConvertTo-Json -Compress -Depth 6) -Encoding utf8
    $flat = ($text -replace '\s+', ' ')
    if ($flat.Length -gt 600) { $flat = $flat.Substring(0, 600) + '...' }
    Write-Host ("[EVID] {0} = {1}" -f $Key, $flat)
}

function Add-Check {
    param([string]$Name, [bool]$Ok, [string]$Detail = '')
    $script:Checks.Add([pscustomobject]@{ Name = $Name; Ok = $Ok; Detail = $Detail })
    $verdict = if ($Ok) { 'PASS' } else { 'FAIL' }
    Write-Host ("[{0}] {1} {2}" -f $verdict, $Name, $Detail)
}

# The identity matrix the artifact is supposed to carry: who, which pipe, and what the
# access check answered for each mask and through each shipped client.
function Add-MatrixRow {
    param([string]$Identity, [string]$Pipe, [string]$Open, [string]$Cli)
    $script:Matrix.Add([pscustomobject]@{ Identity = $Identity; Pipe = $Pipe; Open = $Open; Cli = $Cli })
}

# --- process helpers ---------------------------------------------------------

function ConvertTo-ArgList {
    param([string[]]$Arguments)
    $parts = @()
    foreach ($a in $Arguments) {
        if ($a -match '[\s"]') { $parts += '"' + ($a -replace '"', '\"') + '"' } else { $parts += $a }
    }
    return ($parts -join ' ')
}

function ConvertTo-ArgLine {
    param([string]$Exe, [string[]]$Arguments)
    return ('"' + $Exe + '" ' + (ConvertTo-ArgList -Arguments $Arguments)).Trim()
}

function Invoke-Captured {
    param([string]$Exe, [string[]]$Arguments = @(), [int]$TimeoutSec = 60)
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $Exe
    $psi.Arguments = ConvertTo-ArgList -Arguments $Arguments
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $p = [System.Diagnostics.Process]::Start($psi)
    $outTask = $p.StandardOutput.ReadToEndAsync()
    $errTask = $p.StandardError.ReadToEndAsync()
    $exited = $p.WaitForExit($TimeoutSec * 1000)
    if (-not $exited) { Stop-ProcessTree -Process $p }
    $p.WaitForExit()
    $sw.Stop()
    $code = -1
    if ($exited) { $code = $p.ExitCode }
    return [pscustomobject]@{
        Code     = $code
        StdOut   = $outTask.Result.Trim()
        StdErr   = $errTask.Result.Trim()
        DurMs    = $sw.ElapsedMilliseconds
        TimedOut = -not $exited
    }
}

# Process.Kill([bool]) only exists on .NET Core; Windows PowerShell 5.1 has the
# parameterless overload only. This script runs under pwsh in CI and the secondary-identity
# child never reaches this code, but a manual 5.1 invocation would otherwise lose the
# timeout kill silently, so the overload is probed instead of assumed.
function Stop-ProcessTree {
    param([System.Diagnostics.Process]$Process)
    if (-not $Process) { return }
    try {
        $treeKill = $Process.GetType().GetMethod('Kill', [type[]]@([bool]))
        if ($treeKill) { $Process.Kill($true) } else { $Process.Kill() }
    }
    catch { Write-Host ("kill failed: " + $_.Exception.Message) }
}

# Terminates one process this run started. The image-name guard covers PID reuse between
# the start and the cleanup: a recycled PID must never cost somebody else their process.
function Stop-ProbeProcess {
    param([int]$ProcessId, [string]$ImageName)
    if (-not $ProcessId) { return }
    $proc = Get-Process -Id $ProcessId -ErrorAction SilentlyContinue
    if ($proc -and $proc.ProcessName -eq $ImageName) {
        Stop-Process -Id $ProcessId -Force -ErrorAction SilentlyContinue
        Write-Evidence -Key 'cleanup.killed' -Value ("{0} pid={1}" -f $ImageName, $ProcessId)
    }
}

function Get-ChildProcessId {
    param([int]$ParentProcessId, [string]$ImageName)
    $child = Get-CimInstance Win32_Process -Filter ("ParentProcessId = {0}" -f $ParentProcessId) -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq $ImageName } |
        Select-Object -First 1
    if ($child) { return [int]$child.ProcessId }
    return 0
}

# --- pipe observation --------------------------------------------------------

function Connect-PipePing {
    param([string]$Pipe, [string]$Request = '{"method":"ping"}', [int]$TimeoutMs = 5000)
    $client = $null
    try {
        $client = New-Object System.IO.Pipes.NamedPipeClientStream('.', $Pipe, [System.IO.Pipes.PipeDirection]::InOut)
        $client.Connect($TimeoutMs)
        # No BOM preamble: the service parses the first byte and a BOM turns the request
        # into "invalid request: expected value at line 1 column 1".
        $writer = New-Object System.IO.StreamWriter($client, (New-Object System.Text.UTF8Encoding($false)), 1024, $true)
        $writer.WriteLine($Request)
        $writer.Flush()
        $reader = New-Object System.IO.StreamReader($client, [System.Text.Encoding]::UTF8, $false, 1024, $true)
        return [pscustomobject]@{ Connected = $true; Response = $reader.ReadLine(); Error = '' }
    }
    catch {
        return [pscustomobject]@{ Connected = $false; Response = ''; Error = ('{0}: {1}' -f $_.Exception.GetType().Name, $_.Exception.Message) }
    }
    finally {
        if ($client) { try { $client.Dispose() } catch { Write-Host ("dispose failed: " + $_.Exception.Message) } }
    }
}

function Wait-PipePing {
    param([string]$Pipe, [int]$TimeoutSec = 30)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $probe = Connect-PipePing -Pipe $Pipe -TimeoutMs 1500
        if ($probe.Connected) { return $probe.Response }
        Start-Sleep -Milliseconds 400
    }
    return '(never answered)'
}

function Get-PipeDacl {
    param([string]$Pipe, [string]$Label)
    Initialize-ProbeType
    $path = '\\.\pipe\' + $Pipe
    $byHandle = [PipeProbe]::DaclByHandle($path)
    $byName = [PipeProbe]::DaclByName($path)
    Write-Evidence -Key ("sddl.byHandle[{0}]" -f $Label) -Value ("{0} => {1}" -f $path, $byHandle) -Note 'handle + SE_KERNEL_OBJECT: the route the pipe tools use'
    Write-Evidence -Key ("sddl.byName[{0}]" -f $Label) -Value ("{0} => {1}" -f $path, $byName) -Note 'the route the deleted helper used; 87 is why it could never take effect'
    return $byHandle
}

# The declared DACL is asserted as a set of ACEs, not as one string: Windows may order
# equal-privilege ACEs itself, and a reordering is not a widening.
function Get-AceSet {
    param([string]$Sddl)
    return @([regex]::Matches($Sddl, '\((?:A|D);[^)]*\)') | ForEach-Object { $_.Value } | Sort-Object)
}

# Assigning a descriptor maps generic rights onto the object type's specific rights, so the
# declared `GA` on the owner ACE is stored as `FILE_ALL_ACCESS` and reads back as `FA`.
# Normalizing the expectation the same way keeps the declaration the single source instead
# of duplicating a second literal that can drift from it.
function ConvertTo-StoredSddl {
    param([string]$Sddl)
    return ($Sddl -replace ';;GA;;', ';;FA;;')
}

function Assert-DeclaredDacl {
    param([string]$Label, [string]$Sddl)
    $stored = ConvertTo-StoredSddl -Sddl $DeclaredSddl
    $expected = Get-AceSet -Sddl $stored
    $actual = Get-AceSet -Sddl $Sddl
    Write-Evidence -Key ("sddl.declared_vs_stored[{0}]" -f $Label) -Value ("declared={0} expected_readback={1}" -f $DeclaredSddl, $stored) -Note 'the owner ACE differs by the generic-to-specific mapping, nothing else'
    # An unreadable DACL yields no ACEs at all; without the count guard two empty sets
    # would compare equal and the check would pass on a failed read.
    $same = ($expected.Count -gt 0) -and ($actual.Count -eq $expected.Count) -and (-not (Compare-Object -ReferenceObject $expected -DifferenceObject $actual))
    Add-Check -Name ("sddl[{0}]" -f $Label) -Ok $same -Detail ("expected={0} actual={1}" -f ($expected -join ''), ($actual -join ''))
}

# --- SCM service -------------------------------------------------------------

function Test-ServicePresent {
    param([string]$Name)
    return [bool](Get-Service -Name $Name -ErrorAction SilentlyContinue)
}

function New-ProbeService {
    param([string]$BinPath, [string]$Name, [string]$ServiceArgs = '--mode=service')
    if (Test-ServicePresent -Name $Name) {
        throw "refusing to run: service '$Name' already exists on this machine (a real install would be disturbed)."
    }
    # sc.exe wants 'binPath=' and its value as separate argv tokens; the inner quotes
    # survive CommandLineToArgvW so ImagePath becomes "<exe>" --mode=service.
    $imagePath = '"' + $BinPath + '" ' + $ServiceArgs
    $created = Invoke-Captured -Exe 'sc.exe' -Arguments @('create', $Name, 'binPath=', $imagePath, 'start=', 'demand', 'obj=', 'LocalSystem', 'type=', 'own') -TimeoutSec 30
    Write-Evidence -Key 'service.create' -Value ("exit={0} out={1} err={2}" -f $created.Code, $created.StdOut, $created.StdErr)
    if ($created.Code -ne 0) { throw ("sc create failed: " + $created.StdErr) }
    $script:CreatedService = $true
    $qc = Invoke-Captured -Exe 'sc.exe' -Arguments @('qc', $Name) -TimeoutSec 20
    Write-Evidence -Key 'service.qc' -Value $qc.StdOut -Note 'SERVICE_START_NAME must be LocalSystem for this probe to mean anything'
}

function Start-ProbeService {
    param([string]$Name, [int]$TimeoutSec = 60)
    $started = Invoke-Captured -Exe 'sc.exe' -Arguments @('start', $Name) -TimeoutSec 60
    Write-Evidence -Key 'service.start' -Value ("exit={0} out={1} err={2}" -f $started.Code, $started.StdOut, $started.StdErr)
    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $service = Get-Service -Name $Name -ErrorAction SilentlyContinue
        if ($service -and $service.Status -eq 'Running') {
            # Recorded so cleanup can terminate this exact process if a stop ever hangs,
            # instead of killing every process that happens to share the image name.
            $process = Get-CimInstance Win32_Service -Filter ("Name='{0}'" -f $Name) -ErrorAction SilentlyContinue
            if ($process -and $process.ProcessId) { $script:ServicePid = [int]$process.ProcessId }
            return 'Running'
        }
        Start-Sleep -Milliseconds 400
    }
    return 'not-Running'
}

function Remove-ProbeService {
    param([string]$Name)
    # Only ever removes a service this run created: a pre-existing registration belongs to
    # a real install, and New-ProbeService refuses to start in that case.
    if (-not $script:CreatedService) { return }
    $script:CreatedService = $false
    if (-not (Test-ServicePresent -Name $Name)) { return }
    $null = Invoke-Captured -Exe 'sc.exe' -Arguments @('stop', $Name) -TimeoutSec 60
    Start-Sleep -Milliseconds 800
    $deleted = Invoke-Captured -Exe 'sc.exe' -Arguments @('delete', $Name) -TimeoutSec 30
    Write-Evidence -Key 'service.delete' -Value ("exit={0} {1}" -f $deleted.Code, $deleted.StdOut)
}

# --- secondary identity ------------------------------------------------------

function New-LowUser {
    param([string]$Name = 'em-pipe-low')
    # Refuse rather than reuse: cleanup deletes this account, and an account that already
    # existed on the machine is not this run's to remove.
    if (Get-LocalUser -Name $Name -ErrorAction SilentlyContinue) {
        throw "refusing to run: local account '$Name' already exists (cleanup would delete it)."
    }
    $password = 'Pp' + [guid]::NewGuid().ToString('N').Substring(0, 16) + 'x!'
    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    $null = New-LocalUser -Name $Name -Password $secure -PasswordNeverExpires -AccountNeverExpires -ErrorAction Stop
    $script:CreatedLowUser = $true
    $null = Add-LocalGroupMember -Group 'Users' -Member $Name -ErrorAction SilentlyContinue
    try { $null = Remove-LocalGroupMember -Group 'Administrators' -Member $Name -ErrorAction SilentlyContinue }
    catch { Write-Host ("not an administrator to begin with: " + $_.Exception.Message) }
    Write-Evidence -Key 'lowuser.created' -Value ($Name + ' : member of Users only')
    return [pscustomobject]@{ Name = $Name; Credential = (New-Object System.Management.Automation.PSCredential($Name, $secure)) }
}

function Remove-LowUser {
    param([string]$Name = 'em-pipe-low')
    # Same rule as the service: only what this run created is removed.
    if (-not $script:CreatedLowUser) { return }
    $script:CreatedLowUser = $false
    if (Get-LocalUser -Name $Name -ErrorAction SilentlyContinue) {
        $null = Remove-LocalUser -Name $Name -ErrorAction SilentlyContinue
        Write-Evidence -Key 'lowuser.removed' -Value $Name
    }
}

function Grant-ScratchAccess {
    param([string]$Path, [string]$Access = 'Modify')
    if (-not (Test-Path -LiteralPath $Path)) { return }
    # The secondary identity has to read the staged binaries and write its own output.
    # The two directories carry different rights on purpose: <ScratchRoot> (and bin/ under
    # it) is readable, <ScratchRoot>\work is writable, so the account that runs the probe
    # never holds write access to anything it executes.
    $acl = Get-Acl -LiteralPath $Path
    $rule = New-Object System.Security.AccessControl.FileSystemAccessRule('Users', $Access, 'ContainerInherit,ObjectInherit', 'None', 'Allow')
    $acl.SetAccessRule($rule)
    Set-Acl -LiteralPath $Path -AclObject $acl
    Write-Evidence -Key ("acl.users-{0}[{1}]" -f $Access, $Path) -Value 'applied'
}

function New-LowUserCommandFile {
    param([string]$Line, [string]$OutFile)
    # cmd.exe cannot execute a .ps1, and Start-Process -Credential cannot redirect output
    # (redirection needs UseShellExecute = false, which -Credential rejects), so the child
    # runs through a generated batch file and writes into the work directory.
    $bat = Join-Path $script:WorkDir ('run-low-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.bat')
    $body = "chdir /d `"$script:WorkDir`"`r`n$Line > `"$OutFile`" 2>&1`r`necho LOW_EXIT=%ERRORLEVEL% >> `"$OutFile`""
    [System.IO.File]::WriteAllText($bat, $body, [System.Text.Encoding]::ASCII)
    return $bat
}

function Invoke-AsLowUser {
    param([pscustomobject]$As, [string]$Exe, [string[]]$Arguments = @(), [int]$TimeoutSec = 120)
    $out = Join-Path $script:WorkDir ('low-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
    $bat = New-LowUserCommandFile -Line (ConvertTo-ArgLine -Exe $Exe -Arguments $Arguments) -OutFile $out
    # -NoNewWindow cannot be combined with -Credential, so the window exists and is hidden.
    $proc = Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $bat) -Credential $As.Credential -WindowStyle Hidden -PassThru
    if (-not $proc.WaitForExit($TimeoutSec * 1000)) { Stop-ProcessTree -Process $proc }
    Start-Sleep -Milliseconds 400
    $text = if (Test-Path $out) { [System.IO.File]::ReadAllText($out) } else { '(no output file: the process produced none)' }
    return $text
}

# Same identity, but the child keeps running (Background mode serves until killed), so
# there is nothing to wait for. Cleanup kills it by image name.
function Start-AsLowUser {
    param([pscustomobject]$As, [string]$Exe, [string[]]$Arguments = @())
    $out = Join-Path $script:WorkDir ('low-bg-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
    $bat = New-LowUserCommandFile -Line (ConvertTo-ArgLine -Exe $Exe -Arguments $Arguments) -OutFile $out
    Write-Evidence -Key 'lowuser.background_launch' -Value ("{0} {1} => {2}" -f $Exe, (ConvertTo-ArgList -Arguments $Arguments), $bat)
    return Start-Process -FilePath 'cmd.exe' -ArgumentList @('/c', $bat) -Credential $As.Credential -WindowStyle Hidden -PassThru
}

# --- client mode -------------------------------------------------------------

# Runs under the secondary identity. One measurement, no orchestration: the raw access
# check that the CLI cannot report (it maps every UnauthorizedAccessException to
# "Access denied (requires elevation)", which says nothing about the DACL). Both masks are
# recorded because a DACL can admit the bare one while refusing a real client's request.
function Invoke-ClientProbeMode {
    param([string]$Pipe, [string]$ResultPath)
    Initialize-ProbeType
    $path = if ($Pipe -like '\\.\pipe\*') { $Pipe } else { '\\.\pipe\' + $Pipe }
    $bare = [PipeProbe]::TryOpen($path, $script:BareAccess)
    $client = [PipeProbe]::TryOpen($path, $script:ClientAccess)
    $line = ('identity={0} pipe={1} bare(0x120000)={2} client(0xC0000000)={3}' -f [System.Security.Principal.WindowsIdentity]::GetCurrent().Name, $path, $bare, $client)
    if ($ResultPath) { Set-Content -LiteralPath $ResultPath -Value $line -Encoding ASCII }
    else { Write-Host $line }
}

# One call site for "what does this identity get from this pipe", so the Service and
# Background legs cannot drift apart in how they measure or in what they record.
function Invoke-LowUserBareOpen {
    param([pscustomobject]$As, [string]$Pipe, [string]$ProbeCopy, [string]$Label, [string]$Note = '')
    $out = Join-Path $script:WorkDir ('bare-' + [guid]::NewGuid().ToString('N').Substring(0, 8) + '.txt')
    $console = Invoke-AsLowUser -As $As -Exe 'powershell.exe' -Arguments @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $ProbeCopy,
        '-ProbePipe', $Pipe, '-ProbeResultPath', $out
    )
    $text = if (Test-Path $out) { (Get-Content -LiteralPath $out -Raw).Trim() } else { '(no result file) ' + $console }
    Write-Evidence -Key ("client.low_open[{0}]" -f $Label) -Value $text -Note $Note
    return $text
}

if ($ProbePipe) {
    Invoke-ClientProbeMode -Pipe $ProbePipe -ResultPath $ProbeResultPath
    exit 0
}

# --- main --------------------------------------------------------------------

if (-not $CliExe -or -not $ServiceExe) {
    throw 'usage: probe-pipe-dacl.ps1 -CliExe <env-manager-cli.exe> -ServiceExe <env-manager-service.exe>'
}
if (-not (Test-Path -LiteralPath $CliExe)) { throw "missing CLI: $CliExe" }
if (-not (Test-Path -LiteralPath $ServiceExe)) { throw "missing service binary: $ServiceExe" }

New-Item -ItemType Directory -Force -Path $EvidenceDir | Out-Null
New-Item -ItemType Directory -Force -Path $ScratchRoot | Out-Null
New-Item -ItemType Directory -Force -Path $script:BinDir | Out-Null
New-Item -ItemType Directory -Force -Path $script:WorkDir | Out-Null
$script:EvidencePath = Join-Path $EvidenceDir 'evidence-pipe-dacl.jsonl'
Set-Content -LiteralPath $script:EvidencePath -Value '' -Encoding utf8

$lowUser = $null
$backgroundProc = $null
$probeCopy = Join-Path $script:BinDir 'probe-pipe-dacl.ps1'
Copy-Item -LiteralPath $PSCommandPath -Destination $probeCopy -Force

try {
    Initialize-ProbeType
    Write-Evidence -Key 'target.cliexe' -Value $CliExe
    Write-Evidence -Key 'target.serviceexe' -Value $ServiceExe
    Write-Evidence -Key 'target.declared_sddl' -Value $DeclaredSddl -Note 'the constant service/src/ipc.rs hands to CreateNamedPipeW'
    Write-Evidence -Key 'host.os' -Value ([System.Environment]::OSVersion.VersionString)
    Write-Evidence -Key 'host.user' -Value ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name)
    $groups = Invoke-Captured -Exe 'whoami.exe' -Arguments @('/groups') -TimeoutSec 30
    Write-Evidence -Key 'host.whoami_groups' -Value $groups.StdOut -Note 'Administrators must be Enabled (not deny-only) for the BA ACE to grant'

    # The secondary identity is prepared first: a failure here must not cost the SDDL
    # evidence, so it is recorded and the matrix marks the cells as skipped.
    try {
        $lowUser = New-LowUser
        Grant-ScratchAccess -Path $ScratchRoot -Access 'ReadAndExecute'
        Grant-ScratchAccess -Path $script:WorkDir -Access 'Modify'
        foreach ($dir in @((Split-Path -Parent $CliExe), (Split-Path -Parent $ServiceExe))) {
            Grant-ScratchAccess -Path $dir -Access 'ReadAndExecute'
        }
    }
    catch {
        Write-Evidence -Key 'lowuser.failed' -Value $_.Exception.Message
        $lowUser = $null
    }

    # (1) Service mode: the pipe that was never verified before.
    New-ProbeService -BinPath $ServiceExe -Name $ServiceName
    $state = Start-ProbeService -Name $ServiceName
    Write-Evidence -Key 'service.state' -Value $state
    $firstAnswer = Wait-PipePing -Pipe $script:PipeService -TimeoutSec 30
    Write-Evidence -Key 'service.first_ping' -Value $firstAnswer

    $serviceSddl = Get-PipeDacl -Pipe $script:PipeService -Label 'service'
    Assert-DeclaredDacl -Label 'service' -Sddl $serviceSddl

    # (2) The two shipped client shapes, unchanged, against the tightened pipe, plus the
    # raw access checks from this administrative token - the control row of the matrix.
    $adminIdentity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
    $servicePath = '\\.\pipe\' + $script:PipeService
    $cli = Invoke-Captured -Exe $CliExe -Arguments @('service', 'ping', '--service-pipe') -TimeoutSec 60
    Write-Evidence -Key 'client.cli_gateway' -Value ("exit={0} dur={1}ms out={2} err={3}" -f $cli.Code, $cli.DurMs, $cli.StdOut, $cli.StdErr)
    Add-Check -Name 'client.cli_gateway' -Ok (($cli.Code -eq 0) -and ($cli.StdOut -match 'pong')) -Detail ("exit={0} out={1}" -f $cli.Code, $cli.StdOut)

    $watchdog = Connect-PipePing -Pipe $script:PipeService
    Write-Evidence -Key 'client.watchdog' -Value ("connected={0} resp={1} err={2}" -f $watchdog.Connected, $watchdog.Response, $watchdog.Error)
    Add-Check -Name 'client.watchdog' -Ok (($watchdog.Connected) -and ($watchdog.Response -match '"ok":\s*true')) -Detail ("resp={0}" -f $watchdog.Response)

    $adminBare = [PipeProbe]::TryOpen($servicePath, $script:BareAccess)
    $adminClient = [PipeProbe]::TryOpen($servicePath, $script:ClientAccess)
    Add-MatrixRow -Identity $adminIdentity -Pipe $script:PipeService -Open ("bare={0} client={1}" -f $adminBare, $adminClient) -Cli ("exit={0} pong={1}" -f $cli.Code, [bool]($cli.StdOut -match 'pong'))

    # (3) Non-privileged identity on the Service pipe: both masks must be refused with
    # err=5, and the CLI (what a user would actually see) is recorded alongside.
    if ($lowUser) {
        $lowText = Invoke-LowUserBareOpen -As $lowUser -Pipe $script:PipeService -ProbeCopy $probeCopy -Label 'service' -Note 'err=5 is the access check refusing the token, before any request is parsed'
        $refusedBoth = ($lowText -match 'bare\(0x120000\)=REFUSED err=5') -and ($lowText -match 'client\(0xC0000000\)=REFUSED err=5')
        Add-Check -Name 'client.low_open[service]' -Ok $refusedBoth -Detail $lowText

        $lowCli = Invoke-AsLowUser -As $lowUser -Exe $CliExe -Arguments @('service', 'ping', '--service-pipe')
        Write-Evidence -Key 'client.low_cli_gateway' -Value $lowCli -Note 'recorded, not asserted: the CLI maps a refused connect to its own elevation hint'
        Add-MatrixRow -Identity $lowUser.Name -Pipe $script:PipeService -Open $lowText -Cli ($lowCli -replace '\s+', ' ')
    }
    else {
        Write-Evidence -Key 'client.low_open[service]' -Value 'SKIPPED: no secondary identity' -Note 'the err=5 assertion did not run'
        Add-Check -Name 'client.low_open[service]' -Ok $false -Detail 'skipped: no secondary identity, so the refusal is unproven'
    }

    # (4) Anti-squatting with an SD in play: a console launch of --mode=service is not
    # SCM-initiated, so it falls through to the same run_server body and must lose the
    # name to the running service. Its exit code is not the assertion - a failed listener
    # is logged and the process returns 0 by design - so the assertions are "it did not
    # stay serving" (no timeout) and "the first instance still answers". The cause is
    # pinned one layer down by the unit test, which asserts the OS error code.
    $squatter = Invoke-Captured -Exe $ServiceExe -Arguments @('--mode=service') -TimeoutSec 45
    Write-Evidence -Key 'antisquat.second_process' -Value ("exit={0} timedOut={1} dur={2}ms out={3} err={4}" -f $squatter.Code, $squatter.TimedOut, $squatter.DurMs, $squatter.StdOut, $squatter.StdErr)
    Add-Check -Name 'antisquat.second_process_exits' -Ok (-not $squatter.TimedOut) -Detail ("exit={0} dur={1}ms" -f $squatter.Code, $squatter.DurMs)

    $serviceAlive = Connect-PipePing -Pipe $script:PipeService
    Add-Check -Name 'antisquat.first_instance_survives' -Ok ($serviceAlive.Connected -and ($serviceAlive.Response -match '"ok":\s*true')) -Detail ("resp={0}" -f $serviceAlive.Response)

    Remove-ProbeService -Name $ServiceName
    $afterStop = [PipeProbe]::TryOpen($servicePath, $script:BareAccess)
    Write-Evidence -Key 'service.after_stop_open' -Value $afterStop -Note 'the pipe must be gone once the service stops'

    # (5) Background mode. Started by the secondary identity when there is one, because
    # the OW ACE exists for exactly that case: this pipe's owner is whoever launched it,
    # and the GUI's watchdog and rotate traffic use this pipe, not the Service one. The
    # administrator-launched fallback still proves the descriptor, but not the owner ACE.
    $backgroundOwner = $adminIdentity
    if ($lowUser) {
        $backgroundProc = Start-AsLowUser -As $lowUser -Exe $ServiceExe -Arguments @('--mode=background')
        $backgroundOwner = $lowUser.Name
    }
    else {
        $backgroundProc = Start-Process -FilePath $ServiceExe -ArgumentList '--mode=background' -PassThru -WindowStyle Hidden
    }
    $bgAnswer = Wait-PipePing -Pipe $script:PipeBackground -TimeoutSec 30
    Write-Evidence -Key 'background.launching_identity' -Value $backgroundOwner -Note 'the OW ACE resolves to this identity'
    Write-Evidence -Key 'background.first_ping' -Value $bgAnswer
    $backgroundSddl = Get-PipeDacl -Pipe $script:PipeBackground -Label 'background'
    Assert-DeclaredDacl -Label 'background' -Sddl $backgroundSddl
    $bgWatchdog = Connect-PipePing -Pipe $script:PipeBackground
    Add-Check -Name 'background.watchdog' -Ok ($bgWatchdog.Connected -and ($bgWatchdog.Response -match '"ok":\s*true')) -Detail ("resp={0}" -f $bgWatchdog.Response)

    $bgPath = '\\.\pipe\' + $script:PipeBackground
    Add-MatrixRow -Identity $backgroundOwner -Pipe $script:PipeBackground -Open ("bare={0} client={1}" -f [PipeProbe]::TryOpen($bgPath, $script:BareAccess), [PipeProbe]::TryOpen($bgPath, $script:ClientAccess)) -Cli ("pong={0}" -f [bool]($bgWatchdog.Response -match '"ok":\s*true'))

    # The owner ACE is only worth its line if the non-elevated owner really gets through,
    # and the GUI reaches this pipe as that user.
    if ($lowUser) {
        $bgText = Invoke-LowUserBareOpen -As $lowUser -Pipe $script:PipeBackground -ProbeCopy $probeCopy -Label 'background' -Note 'the owner ACE is what must make both of these OPENED ok'
        $openedBoth = ($bgText -match 'bare\(0x120000\)=OPENED ok') -and ($bgText -match 'client\(0xC0000000\)=OPENED ok')
        Add-Check -Name 'client.low_open[background]' -Ok $openedBoth -Detail $bgText

        $bgLowCli = Invoke-AsLowUser -As $lowUser -Exe $CliExe -Arguments @('service', 'ping')
        Write-Evidence -Key 'client.low_cli_gateway[background]' -Value $bgLowCli
        Add-Check -Name 'client.low_cli_gateway[background]' -Ok ($bgLowCli -match 'pong') -Detail (($bgLowCli -replace '\s+', ' '))
        Add-MatrixRow -Identity $lowUser.Name -Pipe ($script:PipeBackground + ' (as owner)') -Open $bgText -Cli ($bgLowCli -replace '\s+', ' ')
    }
}
catch {
    Write-Evidence -Key 'FATAL' -Value ($_.Exception.Message + ' @ ' + $_.InvocationInfo.PositionMessage)
    Add-Check -Name 'probe.completed' -Ok $false -Detail $_.Exception.Message
}
finally {
    if ($backgroundProc -and -not $backgroundProc.HasExited) { Stop-ProcessTree -Process $backgroundProc }
    # Only processes this run started are terminated. A blanket kill by image name would
    # reach a real install's service on the same machine, which is the one thing this
    # probe promises not to disturb (New-ProbeService refuses to run when one exists).
    Stop-ProbeProcess -ProcessId $script:ServicePid -ImageName 'env-manager-service'
    if ($backgroundProc) {
        $bgChild = Get-ChildProcessId -ParentProcessId $backgroundProc.Id -ImageName 'env-manager-service.exe'
        Stop-ProbeProcess -ProcessId $bgChild -ImageName 'env-manager-service'
    }
    Remove-ProbeService -Name $ServiceName
    Remove-LowUser

    $failed = @($script:Checks | Where-Object { -not $_.Ok })
    $summary = @(
        '# Pipe DACL observation',
        '',
        ('- declared SDDL: {0}' -f $DeclaredSddl),
        ('- expected read-back: {0}' -f (ConvertTo-StoredSddl -Sddl $DeclaredSddl)),
        ('- checks: {0} passed, {1} failed' -f ($script:Checks.Count - $failed.Count), $failed.Count),
        '',
        '## Identity matrix',
        '',
        '| identity | pipe | raw open | client |',
        '|---|---|---|---|'
    )
    foreach ($row in $script:Matrix) {
        $summary += ('| {0} | {1} | {2} | {3} |' -f $row.Identity, $row.Pipe, ($row.Open -replace '\s+', ' '), ($row.Cli -replace '\s+', ' '))
    }
    $summary += @('', '## Checks', '')
    foreach ($check in $script:Checks) {
        $verdict = 'FAIL'
        if ($check.Ok) { $verdict = 'PASS' }
        $summary += ('- [{0}] {1} - {2}' -f $verdict, $check.Name, $check.Detail)
    }
    $summary | Set-Content -LiteralPath (Join-Path $EvidenceDir 'summary-pipe-dacl.md') -Encoding utf8

    if ($failed.Count -gt 0) {
        Write-Host ("FAIL: {0} of {1} pipe DACL checks did not hold" -f $failed.Count, $script:Checks.Count)
        exit 1
    }
    Write-Host ("PASS: all {0} pipe DACL checks held" -f $script:Checks.Count)
    exit 0
}
