# test-msi-upgrade-matrix.ps1 - silent MSI install/upgrade/uninstall proof.
#
# Automates the four-step silent msiexec runbook in docs/build-and-release.md:
#   Step 0  hang baseline    -> a silent install that exceeds InstallTimeoutSeconds fails (default 60s, the runbook's hang threshold)
#   Step 1  silent install   -> install the previous release (upgrade mode) or the current MSI (clean mode)
#   Step 2  silent upgrade   -> install the current MSI over the previous release; a seeded legacy log must be cleaned up
#                               and the service entry must point at the current install directory
#           (clean mode)     -> force a repair (/fomus); the same seeded legacy log must survive
#   Step 2b repair after upgrade -> /fomus on the upgraded product must leave a freshly seeded log alone
#   Step 3  silent uninstall -> msiexec /x, then assert the service entry and binaries are gone
#
# A failure prints the msiexec action that reported "Return value 3" (or the last
# action that started when the process was killed), so a red job names the
# failing custom action instead of only an exit code.
#
# The upgrade legs assert the SCM facts an upgrade can be judged on - entry present,
# executable path inside the current install directory, that file on disk - not
# "the running old service was stopped by the upgrade". No step starts the old
# service on purpose: the previous release's binary predates the SCM control protocol
# (service/src/scm.rs), so it can never reach Running and a Start-Service attempt would
# only reintroduce a check that cannot pass. The current build's start/stop is asserted
# by the runbook's Step 1b, where it is reachable.
#
# One upgrade cell installs the previous release somewhere other than the cell's own
# INSTALLDIR: the product default first, a custom directory second. The two
# same-directory cells cannot show an install-directory move at all, because there the
# previous directory is the current one by construction; in this cell "the entry follows
# the new directory" and "the upgrade wrote the old directory back" are finally different
# facts, and one fact a service entry cannot show joins them - the directory the product
# migrated off holds no executable from the previous release, so anything still naming it
# resolves to nothing. Its two log probes take opposite expectations for that reason: the
# probe in the new directory must be deleted by the upgrade-time cleanup, because deleting
# a log there is what proves the cleanup follows INSTALLDIR instead of the default path -
# which in this cell is exactly where the previous release lives - while the probe in the
# migrated-off directory must survive, because the cleanup never runs against that
# directory and the previous product's cleanup component is skipped as shared. The
# executables in that directory leave for a different reason (the new install moves their
# components), so the pair distinguishes "the cleanup stayed put" from "the directory was
# emptied anyway" - a distinction the same-directory cells cannot draw.
#
# Reading the executable out of ImagePath has one shape it refuses rather than
# judges: an unquoted executable that contains spaces. Windows resolves an unquoted
# command line by trying progressively longer prefixes of it as the module name (for a
# path under Program Files: C:\Program.exe, then C:\Program Files\Env.exe, and so on),
# so the binary that starts need not be the one read here. A command line that merely
# carries a space elsewhere - the custom-path leg's "path --mode=service" - is
# unambiguous and stays accepted.
#
# -SelfTest feeds synthetic msiexec logs through the diagnosis helper and synthetic
# ImagePath shapes through the command-line parser, including the ambiguous shape no
# install produces; it needs no MSI and no elevation, so it can run anywhere
# PowerShell 5.1+ is installed.
[CmdletBinding()]
param(
    [switch]$SelfTest,
    [string]$Scenario = '',
    [string]$CurrentMsi = '',
    [string]$PreviousMsi = '',
    [string]$CurrentVersion = '',
    [string]$PreviousVersion = '',
    [string]$InstallDir = '',
    [string]$PreviousInstallDir = '',
    [string]$LogDir = 'test-results/msi-upgrade',
    [int]$InstallTimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

function Read-MsiLog {
    param([string]$LogPath)
    if (-not (Test-Path -LiteralPath $LogPath)) { return @() }
    return @(Get-Content -LiteralPath $LogPath -ErrorAction SilentlyContinue)
}

function Test-VersionBanner {
    param([string]$Banner, [string]$ExpectedVersion)
    # Match the complete version token: a \b would also accept v0.12.1.1 for
    # expected 0.12.1.
    $match = [regex]::Match($Banner, 'Env Manager v([0-9]+(?:\.[0-9]+)+)')
    return ($match.Success -and $match.Groups[1].Value -eq $ExpectedVersion)
}

function Get-MsiDiagnosis {
    param([string[]]$Lines = @(), [string]$LogPath = '')
    $logLines = @($Lines)
    if ($logLines.Count -eq 0 -and -not [string]::IsNullOrWhiteSpace($LogPath)) {
        $logLines = @(Read-MsiLog -LogPath $LogPath)
    }
    $failures = @($logLines | Where-Object { $_ -match 'Return value 3\.' })
    if ($failures.Count -gt 0) {
        # The last Return value 3 is commonly the INSTALL wrapper; the first
        # failure names the action that actually failed.
        $match = [regex]::Match([string]$failures[0], 'Action ended [0-9:.]+: ([^.]+)\. Return value 3')
        if ($match.Success) { return $match.Groups[1].Value }
        return ([string]$failures[-1]).Trim()
    }
    $starts = @($logLines | Where-Object { $_ -match 'Action start [0-9:.]+: ' })
    if ($starts.Count -gt 0) {
        return 'last action started before the failure: ' + ([string]$starts[-1]).Trim()
    }
    return 'no action information in the msiexec log'
}

function Get-ComparableDirectory {
    # Normalized form for "are these the same directory" comparisons: GetFullPath resolves
    # relative segments and drops the trailing separator that would otherwise make one
    # directory compare unequal to itself. TrimEndingDirectorySeparator is .NET Core only
    # and this script also runs on Windows PowerShell 5.1, where the separator survives -
    # and a false mismatch turns a same-directory cell into a migration, whose assertions
    # then contradict the cell they run in.
    param([string]$Path)
    return ([System.IO.Path]::GetFullPath($Path)).TrimEnd('\', '/')
}

function ConvertFrom-ServiceImagePath {
    # ImagePath holds the configured command line: the executable plus the
    # installer's arguments, with SCM quoting optional. Return the executable only.
    # Declared here, not beside Assert-ServiceImagePath, because the -SelfTest block
    # below calls it and PowerShell only sees a function after its definition.
    param([string]$CommandLine)
    $trimmed = ([string]$CommandLine).Trim()
    if ($trimmed.Length -eq 0) { return '' }
    if ($trimmed.StartsWith('"')) {
        $end = $trimmed.IndexOf('"', 1)
        if ($end -lt 1) { return '' }
        return $trimmed.Substring(1, $end - 1)
    }
    # Unquoted: cut at the first argument-shaped token, not at the first space, because
    # the arguments in this repo always start with '-'. Cutting at the first space would
    # truncate a spaced path to its first word (C:\Program Files\... -> C:\Program) and
    # hand the refusal below a prefix with no spaces in it, so it would compare that
    # fragment instead of reporting the ambiguity.
    $match = [regex]::Match($trimmed, '^(?<exe>.*?)\s+--?\S')
    if ($match.Success) { $exe = $match.Groups['exe'].Value } else { $exe = $trimmed }
    # An unquoted command line resolves by progressive prefixes (see the header), so an
    # executable still holding spaces here is not a fact this assertion can judge:
    # refuse instead of returning a path that only looks right. Throwing, rather than
    # returning '', keeps the ambiguity out of the caller's "cannot read an executable
    # path" message, which would be false here.
    if ($exe -match '\s') {
        throw "The executable in ImagePath is unquoted and contains spaces, so Windows resolves the module name by trying progressively longer prefixes of this command line (for a path under Program Files it may start C:\Program.exe, or another prefix, instead of the intended path); which binary starts is therefore ambiguous, and it need not be the one this assertion read. Quote the executable path. ImagePath: '$trimmed'"
    }
    return $exe
}

if ($SelfTest) {
    $failedLog = @(
        'Action start 9:00:00: InstallValidate.',
        'Action ended 9:00:01: InstallValidate. Return value 1.',
        'Action start 9:00:02: StartServices.',
        'Action ended 9:00:05: StartServices. Return value 3.',
        'Product: Env Manager -- Installation failed.'
    )
    $cleanLog = @(
        'Action ended 9:00:01: InstallValidate. Return value 1.',
        'Action ended 9:00:02: InstallFinalize. Return value 1.'
    )
    $killedLog = @(
        'Action start 9:00:00: InstallValidate.',
        'Action ended 9:00:01: InstallValidate. Return value 1.',
        'Action start 9:00:02: StartServices.'
    )
    $multiFailureLog = @(
        'Action start 9:00:02: StartServices.',
        'Action ended 9:00:05: StartServices. Return value 3.',
        'Action start 9:00:05: INSTALL.',
        'Action ended 9:00:06: INSTALL. Return value 3.'
    )
    $emptyLog = @()
    # The two shapes the installer actually produces, plus the ambiguous ones it does
    # not: the refusal cannot be reached by the matrix, so this is the only place it
    # runs. $unquotedCustom's command line does contain a space (before --mode=service)
    # and is still accepted, because the refusal checks the extracted executable rather
    # than the command line. The spaced path is tried with and without arguments because
    # the parser reaches the refusal by two routes - a split that leaves spaces in the
    # executable, and no splittable argument at all.
    $quotedDefault = '"C:\Program Files\Env Manager\env-manager-service.exe" --mode=service'
    $unquotedCustom = 'C:\env-manager-upgrade-test\env-manager-service.exe --mode=service'
    $spacedWithArgs = 'C:\Program Files\Env Manager\env-manager-service.exe --mode=service'
    $spacedBare = 'C:\Program Files\Env Manager\env-manager-service.exe'
    $spacedWithArgsRefusal = ''
    try { $null = ConvertFrom-ServiceImagePath -CommandLine $spacedWithArgs }
    catch { $spacedWithArgsRefusal = $_.Exception.Message }
    $spacedBareRefusal = ''
    try { $null = ConvertFrom-ServiceImagePath -CommandLine $spacedBare }
    catch { $spacedBareRefusal = $_.Exception.Message }
    $checks = @(
        @{ Name = 'Return value 3 names the action'; Actual = (Get-MsiDiagnosis -Lines $failedLog); Expected = 'StartServices' },
        @{ Name = 'first failure wins over the INSTALL wrapper'; Actual = (Get-MsiDiagnosis -Lines $multiFailureLog); Expected = 'StartServices' },
        @{ Name = 'exact banner version matches'; Actual = (Test-VersionBanner -Banner 'Env Manager v0.12.1' -ExpectedVersion '0.12.1'); Expected = $true },
        @{ Name = 'longer banner version does not match'; Actual = (Test-VersionBanner -Banner 'Env Manager v0.12.1.1' -ExpectedVersion '0.12.1'); Expected = $false },
        @{ Name = 'clean log reports no failure'; Actual = (Get-MsiDiagnosis -Lines $cleanLog); Expected = 'no action information in the msiexec log' },
        @{ Name = 'killed log names the last action that started'; Actual = (Get-MsiDiagnosis -Lines $killedLog); Expected = 'last action started before the failure: Action start 9:00:02: StartServices.' },
        @{ Name = 'empty log reports no failure'; Actual = (Get-MsiDiagnosis -Lines $emptyLog); Expected = 'no action information in the msiexec log' },
        @{ Name = 'quoted ImagePath yields the executable'; Actual = (ConvertFrom-ServiceImagePath -CommandLine $quotedDefault); Expected = 'C:\Program Files\Env Manager\env-manager-service.exe' },
        @{ Name = 'unquoted ImagePath without spaces yields the executable'; Actual = (ConvertFrom-ServiceImagePath -CommandLine $unquotedCustom); Expected = 'C:\env-manager-upgrade-test\env-manager-service.exe' },
        @{ Name = 'unquoted executable with spaces is refused as ambiguous'; Actual = ($spacedWithArgsRefusal.StartsWith('The executable in ImagePath is unquoted')); Expected = $true },
        @{ Name = 'the refusal carries the raw ImagePath'; Actual = ($spacedWithArgsRefusal.Contains($spacedWithArgs)); Expected = $true },
        @{ Name = 'unquoted spaced executable without arguments is refused too'; Actual = ($spacedBareRefusal.StartsWith('The executable in ImagePath is unquoted')); Expected = $true }
    )
    $bad = 0
    foreach ($check in $checks) {
        if ($check.Actual -eq $check.Expected) {
            Write-Host "SELFTEST OK: $($check.Name)"
        }
        else {
            Write-Host "SELFTEST FAIL: $($check.Name) (expected '$($check.Expected)', got '$($check.Actual)')"
            $bad++
        }
    }
    if ($bad -gt 0) { exit 1 }
    Write-Host 'SELFTEST PASSED'
    exit 0
}

if ([string]::IsNullOrWhiteSpace($CurrentMsi)) { throw '-CurrentMsi is required (or pass -SelfTest)' }
if (-not (Test-Path -LiteralPath $CurrentMsi)) { throw "Current MSI not found: $CurrentMsi" }
$upgrade = -not [string]::IsNullOrWhiteSpace($PreviousMsi)
if ($upgrade -and -not (Test-Path -LiteralPath $PreviousMsi)) { throw "Previous MSI not found: $PreviousMsi" }

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($CurrentVersion)) {
    $csproj = Join-Path $repoRoot 'env-manager.csproj'
    if (Test-Path -LiteralPath $csproj) {
        $CurrentVersion = ([xml](Get-Content -LiteralPath $csproj)).Project.PropertyGroup.Version
    }
}
if ([string]::IsNullOrWhiteSpace($CurrentVersion)) { throw '-CurrentVersion is required' }

$targetDir = $InstallDir
if ([string]::IsNullOrWhiteSpace($targetDir)) {
    $targetDir = Join-Path $env:ProgramFiles 'Env Manager'
}

# Where Step 1 puts the previous release. Empty means the cell's own directory, which is
# what both same-directory upgrade cells ask for; naming a different directory asks for a
# directory migration, and Step 1 is then the only step that writes there.
$previousDir = $PreviousInstallDir
if ([string]::IsNullOrWhiteSpace($previousDir)) { $previousDir = $targetDir }
# The INSTALLDIR override Step 1 passes. A cell that names no previous directory still has
# to pass its own: an empty request drops the override, which would install the previous
# release into the product default and hand every same-directory cell the wrong directory.
$previousRequestedDir = $PreviousInstallDir
if ([string]::IsNullOrWhiteSpace($previousRequestedDir)) { $previousRequestedDir = $InstallDir }
$migrating = $upgrade -and -not [string]::Equals(
    (Get-ComparableDirectory $previousDir),
    (Get-ComparableDirectory $targetDir),
    [System.StringComparison]::OrdinalIgnoreCase)
if (-not $upgrade -and -not [string]::IsNullOrWhiteSpace($PreviousInstallDir)) {
    throw '-PreviousInstallDir applies to an upgrade cell only (it needs -PreviousMsi)'
}
if ($upgrade -and -not [string]::IsNullOrWhiteSpace($PreviousInstallDir) -and -not $migrating) {
    # A cell that names the directory it already installs into asks for the
    # same-directory upgrade the other two cells cover, and would otherwise run their
    # assertions under a name that claims a move happened.
    throw "-PreviousInstallDir resolves to the cell's own INSTALLDIR ($targetDir); a same-directory upgrade is the upgrade-default or upgrade-custom-path cell"
}

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if ([string]::IsNullOrWhiteSpace($Scenario)) {
    if (-not $upgrade) {
        $Scenario = 'clean-current'
    }
    elseif ($migrating) {
        $Scenario = 'upgrade-dir-migration'
    }
    elseif ([string]::IsNullOrWhiteSpace($InstallDir)) {
        $Scenario = 'upgrade-default'
    }
    else {
        $Scenario = 'upgrade-custom-path'
    }
}
Write-Host "=== MSI upgrade matrix: $Scenario ==="
Write-Host "current MSI: $CurrentMsi (v$CurrentVersion)"
if ($upgrade) { Write-Host "previous MSI: $PreviousMsi (v$PreviousVersion)" }
Write-Host "install dir: $targetDir"
if ($migrating) { Write-Host "previous install dir: $previousDir (this cell moves the product)" }

function Get-EnvManagerService {
    return (Get-Service -Name 'EnvManagerService' -ErrorAction SilentlyContinue)
}

function Stop-EnvManagerResidue {
    $service = Get-EnvManagerService
    if ($service -and $service.Status -ne 'Stopped') {
        Stop-Service -Name 'EnvManagerService' -Force -ErrorAction SilentlyContinue
    }
    Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
}

function Invoke-MsiProcess {
    param([string]$Stage, [string[]]$MsiArgs, [string]$LogPath)
    Write-Host "=== $Stage ==="
    Write-Host ('msiexec ' + ($MsiArgs -join ' '))
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    $process = Start-Process -FilePath 'msiexec.exe' -ArgumentList $MsiArgs -PassThru
    if (-not $process.WaitForExit($InstallTimeoutSeconds * 1000)) {
        try { $process.Kill() }
        catch { Write-Host "kill after timeout failed: $($_.Exception.Message)" }
        $watch.Stop()
        $action = Get-MsiDiagnosis -LogPath $LogPath
        throw "$Stage exceeded ${InstallTimeoutSeconds}s (silent install hang regression; $action; log: $LogPath)"
    }
    $watch.Stop()
    $elapsed = [math]::Round($watch.Elapsed.TotalSeconds, 1)
    Write-Host "$Stage finished in ${elapsed}s with exit code $($process.ExitCode)"
    if ($watch.Elapsed.TotalSeconds -gt 10) {
        # The runbook's green install is under 10s on a build machine. A hosted
        # runner can be slower, so this is a signal, not a gate; the hard gate
        # is the hang bound above.
        Write-Warning "$Stage took ${elapsed}s; the runbook's green install is under 10s (hard hang gate: ${InstallTimeoutSeconds}s)"
    }
    if ($process.ExitCode -ne 0) {
        $action = Get-MsiDiagnosis -LogPath $LogPath
        throw "$Stage failed (msiexec exit $($process.ExitCode); $action; log: $LogPath)"
    }
}

function Add-InstallDirOverride {
    param([string[]]$MsiArgs, [string]$RequestedDir, [string]$ResolvedDir)
    # Install and repair both act on the directory the cell asked for, so they share
    # this one append. Uninstall deliberately does not use it: /x removes what is
    # installed and must not be pointed at a directory.
    if ([string]::IsNullOrWhiteSpace($RequestedDir)) { return $MsiArgs }
    # An override with no resolved directory reaches msiexec as INSTALLDIR="", which puts
    # the product somewhere no assertion in this script looks at. The pair is built in one
    # place so install and repair cannot drift apart on it.
    if ([string]::IsNullOrWhiteSpace($ResolvedDir)) {
        throw "an INSTALLDIR override was requested for '$RequestedDir' but no resolved directory was passed; msiexec would receive an empty INSTALLDIR"
    }
    return ($MsiArgs + "INSTALLDIR=`"$ResolvedDir`"")
}

function Invoke-SilentInstall {
    # Both directories are passed in rather than read from script scope: an upgrade cell
    # installs two releases, and a pair read from the wrong place puts the previous release
    # on top of the current directory with no assertion noticing.
    param([string]$Stage, [string]$MsiPath, [string]$LogPath, [string]$RequestedDir, [string]$ResolvedDir)
    $msiArgs = @('/i', "`"$MsiPath`"", '/quiet', '/norestart', '/L*v', "`"$LogPath`"")
    $msiArgs = Add-InstallDirOverride -MsiArgs $msiArgs -RequestedDir $RequestedDir -ResolvedDir $ResolvedDir
    Invoke-MsiProcess -Stage $Stage -MsiArgs $msiArgs -LogPath $LogPath
}

function Invoke-SilentUninstall {
    param([string]$MsiPath, [string]$LogPath)
    Stop-EnvManagerResidue
    $msiArgs = @('/x', "`"$MsiPath`"", '/quiet', '/norestart', '/L*v', "`"$LogPath`"")
    Invoke-MsiProcess -Stage 'Step 3: silent uninstall' -MsiArgs $msiArgs -LogPath $LogPath
}

function Invoke-SilentRepair {
    # The repair only ever acts on the product this cell just installed, but it takes the
    # same explicit pair as the install it repairs: one of the two reading a directory from
    # script scope is how a cell ends up repairing a directory it never installed into.
    param([string]$MsiPath, [string]$LogPath, [string]$RequestedDir, [string]$ResolvedDir)
    # /fomus reinstalls files (o), rewrites every machine and user registry entry
    # (m, u) and reinstalls shortcuts (s). The registry rewrite is what gives this
    # assertion teeth: it puts the LogsDir components into the install state, so a
    # component that still carried an install-time RemoveFile would delete the
    # seeded log here. Without it the repair would skip those components and the
    # assertion would pass on an installer that still deletes the file.
    $msiArgs = @('/fomus', "`"$MsiPath`"", '/quiet', '/norestart', '/L*v', "`"$LogPath`"")
    $msiArgs = Add-InstallDirOverride -MsiArgs $msiArgs -RequestedDir $RequestedDir -ResolvedDir $ResolvedDir
    Invoke-MsiProcess -Stage 'Step 2: forced repair (/fomus)' -MsiArgs $msiArgs -LogPath $LogPath
}

function Assert-ServicePresent {
    param([string]$Stage)
    $service = Get-EnvManagerService
    if (-not $service) { throw "${Stage}: EnvManagerService SCM entry is missing" }
    Write-Host "${Stage}: EnvManagerService present (status: $($service.Status))"
}

function Get-ServiceImageCommandLine {
    # The service's configured command line (executable plus arguments), read from
    # the store ServiceInstall writes to: GET-SERVICE only answers "does the entry
    # exist", and Get-CimInstance would add a WMI round trip for the same string.
    # Read-only on purpose - this asserts state, it never writes the registry, so
    # test-with-restore.ps1 does not apply (that harness rolls back variable writes).
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Services\EnvManagerService'
    $entry = Get-ItemProperty -LiteralPath $key -Name 'ImagePath' -ErrorAction SilentlyContinue
    if (-not $entry) { return '' }
    return ([string]$entry.ImagePath).Trim()
}

function Assert-ServiceImagePath {
    param([string]$Stage, [string]$TargetDir)
    $expected = [System.IO.Path]::GetFullPath((Join-Path $TargetDir 'env-manager-service.exe'))
    $commandLine = Get-ServiceImageCommandLine
    if ([string]::IsNullOrWhiteSpace($commandLine)) {
        throw "${Stage}: EnvManagerService has no ImagePath; expected the service binary at $expected"
    }
    # The parser refuses an ambiguous command line instead of returning a path, so
    # carry its reason out with the stage that hit it.
    try { $executable = ConvertFrom-ServiceImagePath -CommandLine $commandLine }
    catch { throw "${Stage}: $($_.Exception.Message)" }
    if ([string]::IsNullOrWhiteSpace($executable)) {
        throw "${Stage}: cannot read an executable path from ImagePath '$commandLine'"
    }
    # GetFullPath normalizes the spellings the filesystem allows (8.3 short names,
    # relative segments); the case-insensitive compare covers case, which
    # GetFullPath preserves. Raw string equality would fail a correctly installed
    # service on either of those.
    try { $executableFull = [System.IO.Path]::GetFullPath($executable) }
    catch { throw "${Stage}: cannot normalize the ImagePath executable '$executable' ($($_.Exception.Message))" }
    if (-not [string]::Equals($executableFull, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "${Stage}: EnvManagerService points at '$executable', expected the service binary in the current install directory ($expected); ImagePath: '$commandLine'"
    }
    if (-not (Test-Path -LiteralPath $executableFull)) {
        throw "${Stage}: EnvManagerService points at $executableFull, but that file does not exist"
    }
    Write-Host "${Stage}: EnvManagerService points at $executableFull (file present)"
}

function Assert-ServiceAbsent {
    param([string]$Stage)
    $service = Get-EnvManagerService
    if ($service) { throw "${Stage}: EnvManagerService SCM entry still exists after uninstall" }
    Write-Host "${Stage}: EnvManagerService SCM entry removed"
}

function Assert-InstalledPayload {
    param([string]$Stage, [string]$ExpectedVersion, [string]$TargetDir)
    foreach ($name in @('env-manager.exe', 'env-manager-cli.exe')) {
        $path = Join-Path $TargetDir $name
        if (-not (Test-Path -LiteralPath $path)) { throw "${Stage}: installed file missing at $path" }
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { return }
    $cli = Join-Path $TargetDir 'env-manager-cli.exe'
    $banner = & $cli 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "${Stage}: installed CLI exited $LASTEXITCODE" }
    if (-not (Test-VersionBanner -Banner $banner -ExpectedVersion $ExpectedVersion)) {
        throw "${Stage}: installed CLI version does not match v$ExpectedVersion. Banner: $($banner.Trim())"
    }
    Write-Host "${Stage}: installed payload reports v$ExpectedVersion"
}

function Assert-PreviousPayloadGone {
    param([string]$Stage, [string]$PreviousDir)
    # What a directory migration has to leave behind is nothing that can still be run: a
    # service entry, a shortcut or a user's PATH entry naming the old directory would point
    # at a binary this upgrade no longer put there. Only the product's own executables are
    # asserted, because a leftover directory says nothing about where the product now lives
    # and this assertion is about what can still be resolved and started.
    $leftovers = @()
    foreach ($name in @('env-manager.exe', 'env-manager-cli.exe', 'env-manager-service.exe')) {
        $path = Join-Path $PreviousDir $name
        if (Test-Path -LiteralPath $path) { $leftovers += $path }
    }
    if ($leftovers.Count -gt 0) {
        throw "${Stage}: the upgrade left $($leftovers.Count) executable(s) from the previous release in $PreviousDir ($($leftovers -join ', ')), a directory this upgrade no longer installs into"
    }
    Write-Host "${Stage}: the previous install directory holds no executable from the previous release"
}

function Assert-UninstalledBinaries {
    foreach ($name in @('env-manager.exe', 'env-manager-cli.exe')) {
        $path = Join-Path $targetDir $name
        if (Test-Path -LiteralPath $path) { throw "Step 3: installed file still present after uninstall: $path" }
    }
    if (Test-Path -LiteralPath $targetDir) { Write-Warning "Step 3: install directory remains after uninstall: $targetDir" }
    Assert-ServiceAbsent -Stage 'Step 3'
}

try {
    if ($upgrade) {
        # Step 1 writes the previous release where the cell says it was installed, which
        # only the migration cell makes different from the current directory. The pair is
        # passed rather than read from script scope so no cell installs into the wrong one.
        Invoke-SilentInstall -Stage 'Step 1: silent install (previous release)' -MsiPath $PreviousMsi `
            -LogPath (Join-Path $LogDir 'step1-previous-install.log') `
            -RequestedDir $previousRequestedDir -ResolvedDir $previousDir
        Assert-ServicePresent -Stage 'Step 1'
        Assert-InstalledPayload -Stage 'Step 1' -ExpectedVersion $PreviousVersion -TargetDir $previousDir
        # Seed the legacy log path the old installer wrote to. The cleanup
        # component is shared across versions, so the old product's uninstall
        # action is skipped; the current product's upgrade-only cleanup action
        # must delete it during the upgrade.
        $legacyProbe = Join-Path (Join-Path $targetDir 'logs') 'upgrade-probe.log'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $legacyProbe) | Out-Null
        Set-Content -LiteralPath $legacyProbe -Value 'legacy log probe' -Encoding utf8
        Write-Host "Step 2: seeded $legacyProbe to prove the upgrade cleanup runs"
        $migratedOffProbe = ''
        if ($migrating) {
            # The other half of the migration pair. Two mechanisms spare this file while the
            # executables beside it go: the upgrade-time cleanup is a custom action over the
            # new INSTALLDIR's logs directory, and the previous product's LegacyLogsCleanup
            # component keeps one component id across builds, so the major upgrade treats the
            # new product as another client and skips its uninstall-time RemoveFile. The
            # executables leave because the new install moves their own components to the new
            # directory. That difference is the point: this probe is what separates "the
            # cleanup stayed in the new directory" from "the directory was emptied anyway".
            $migratedOffProbe = Join-Path (Join-Path $previousDir 'logs') 'upgrade-probe.log'
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $migratedOffProbe) | Out-Null
            Set-Content -LiteralPath $migratedOffProbe -Value 'migrated-off log probe' -Encoding utf8
            Write-Host "Step 2: seeded $migratedOffProbe to prove the upgrade cleanup stays in the new directory"
        }
        Invoke-SilentInstall -Stage 'Step 2: silent upgrade (current over previous)' -MsiPath $CurrentMsi `
            -LogPath (Join-Path $LogDir 'step2-current-upgrade.log') `
            -RequestedDir $InstallDir -ResolvedDir $targetDir
        Assert-ServicePresent -Stage 'Step 2'
        Assert-InstalledPayload -Stage 'Step 2' -ExpectedVersion $CurrentVersion -TargetDir $targetDir
        # Presence alone cannot tell a correct service entry from one naming some
        # other directory, so the upgrade legs check where it points and that the
        # file is there.
        Assert-ServiceImagePath -Stage 'Step 2' -TargetDir $targetDir
        if ($migrating) {
            Assert-PreviousPayloadGone -Stage 'Step 2' -PreviousDir $previousDir
            if (-not (Test-Path -LiteralPath $migratedOffProbe)) {
                throw "Step 2: the upgrade cleanup deleted $migratedOffProbe, a log in the directory the product migrated off; the cleanup acts on the new install directory ($targetDir) only"
            }
            Write-Host "Step 2: the upgrade cleanup left $previousDir alone"
        }
        if (Test-Path -LiteralPath $legacyProbe) {
            throw "Step 2: the upgrade cleanup left $legacyProbe after the major upgrade"
        }
        Write-Host 'Step 2: the upgrade cleanup removed the seeded legacy log'
        # The cleanup gate has to hold after the upgrade installed the cleanup path:
        # a repair on an upgraded machine must leave an unrelated log alone. A
        # component-conditioned RemoveFile fails here, because the repair replays it.
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $legacyProbe) | Out-Null
        Set-Content -LiteralPath $legacyProbe -Value 'post-upgrade repair probe' -Encoding utf8
        Write-Host "Step 2b: seeded $legacyProbe to prove a repair after the upgrade leaves it alone"
        Invoke-SilentRepair -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step2b-current-repair.log') `
            -RequestedDir $InstallDir -ResolvedDir $targetDir
        if (-not (Test-Path -LiteralPath $legacyProbe)) {
            throw "Step 2b: a repair after the upgrade deleted $legacyProbe; the cleanup gate must not depend on the component being installed"
        }
        Write-Host 'Step 2b: the repair after the upgrade left the seeded log alone'
        Write-Host 'Step 3: the same seeded log must now be removed by the product uninstall'
        Invoke-SilentUninstall -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step3-uninstall.log')
        if (Test-Path -LiteralPath $legacyProbe) {
            throw "Step 3: LegacyLogsCleanup left $legacyProbe after uninstall"
        }
        Write-Host 'Step 3: LegacyLogsCleanup removed the re-seeded legacy log'
        Assert-UninstalledBinaries
    }
    else {
        Invoke-SilentInstall -Stage 'Step 1: silent install (current)' -MsiPath $CurrentMsi `
            -LogPath (Join-Path $LogDir 'step1-current-install.log') `
            -RequestedDir $InstallDir -ResolvedDir $targetDir
        Assert-ServicePresent -Stage 'Step 1'
        Assert-InstalledPayload -Stage 'Step 1' -ExpectedVersion $CurrentVersion -TargetDir $targetDir
        # The repair is the widest maintenance pass on a product that was never
        # upgraded, so it is where an unconditional install-time cleanup would still
        # show up. A clean install on an empty machine has no unrelated file to lose.
        # The probe is the same file name the upgrade leg seeds, so the two legs
        # disagree only in the expectation.
        $legacyProbe = Join-Path (Join-Path $targetDir 'logs') 'upgrade-probe.log'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $legacyProbe) | Out-Null
        Set-Content -LiteralPath $legacyProbe -Value 'repair probe' -Encoding utf8
        Write-Host "Step 2: seeded $legacyProbe to prove a repair does not run the install-time cleanup"
        Invoke-SilentRepair -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step2-current-repair.log') `
            -RequestedDir $InstallDir -ResolvedDir $targetDir
        if (-not (Test-Path -LiteralPath $legacyProbe)) {
            throw "Step 2: the forced repair deleted $legacyProbe; the install-time cleanup must only run on an upgrade"
        }
        Write-Host 'Step 2: the forced repair left the seeded log alone'
        Write-Host 'Step 3: the same seeded log must now be removed by the product uninstall'
        Invoke-SilentUninstall -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step3-uninstall.log')
        if (Test-Path -LiteralPath $legacyProbe) {
            throw "Step 3: LegacyLogsCleanup left $legacyProbe after uninstall"
        }
        Write-Host 'Step 3: LegacyLogsCleanup removed the seeded legacy log'
        Assert-UninstalledBinaries
    }
}
catch {
    Write-Host "=== MSI upgrade matrix FAILED ($Scenario) ==="
    Write-Host $_.Exception.Message
    Write-Host "msiexec logs: $LogDir"
    throw
}
Write-Host "=== MSI upgrade matrix PASSED ($Scenario) ==="
exit 0
