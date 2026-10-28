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
# The upgrade legs assert the SCM facts an upgrade can actually be judged on - the
# entry exists, its executable path is inside the current install directory, and
# that file is on disk - rather than "the running old service was stopped by the
# upgrade". The latter is unreachable: EnvManagerService never joins the SCM
# control protocol, so it cannot report Running and Start-Service only returns
# 1053 after ServicesPipeTimeout. Nothing here starts the old service on purpose;
# re-adding that attempt would only reintroduce a step that can never pass.
#
# -SelfTest feeds synthetic msiexec logs through the diagnosis helper; it needs
# no MSI and no elevation, so it can run anywhere PowerShell 5.1+ is installed.
[CmdletBinding()]
param(
    [switch]$SelfTest,
    [string]$Scenario = '',
    [string]$CurrentMsi = '',
    [string]$PreviousMsi = '',
    [string]$CurrentVersion = '',
    [string]$PreviousVersion = '',
    [string]$InstallDir = '',
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
    $checks = @(
        @{ Name = 'Return value 3 names the action'; Actual = (Get-MsiDiagnosis -Lines $failedLog); Expected = 'StartServices' },
        @{ Name = 'first failure wins over the INSTALL wrapper'; Actual = (Get-MsiDiagnosis -Lines $multiFailureLog); Expected = 'StartServices' },
        @{ Name = 'exact banner version matches'; Actual = (Test-VersionBanner -Banner 'Env Manager v0.12.1' -ExpectedVersion '0.12.1'); Expected = $true },
        @{ Name = 'longer banner version does not match'; Actual = (Test-VersionBanner -Banner 'Env Manager v0.12.1.1' -ExpectedVersion '0.12.1'); Expected = $false },
        @{ Name = 'clean log reports no failure'; Actual = (Get-MsiDiagnosis -Lines $cleanLog); Expected = 'no action information in the msiexec log' },
        @{ Name = 'killed log names the last action that started'; Actual = (Get-MsiDiagnosis -Lines $killedLog); Expected = 'last action started before the failure: Action start 9:00:02: StartServices.' },
        @{ Name = 'empty log reports no failure'; Actual = (Get-MsiDiagnosis -Lines $emptyLog); Expected = 'no action information in the msiexec log' }
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

New-Item -ItemType Directory -Force -Path $LogDir | Out-Null

if ([string]::IsNullOrWhiteSpace($Scenario)) {
    if (-not $upgrade) {
        $Scenario = 'clean-current'
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
    return ($MsiArgs + "INSTALLDIR=`"$ResolvedDir`"")
}

function Invoke-SilentInstall {
    param([string]$Stage, [string]$MsiPath, [string]$LogPath)
    $msiArgs = @('/i', "`"$MsiPath`"", '/quiet', '/norestart', '/L*v', "`"$LogPath`"")
    $msiArgs = Add-InstallDirOverride -MsiArgs $msiArgs -RequestedDir $InstallDir -ResolvedDir $targetDir
    Invoke-MsiProcess -Stage $Stage -MsiArgs $msiArgs -LogPath $LogPath
}

function Invoke-SilentUninstall {
    param([string]$MsiPath, [string]$LogPath)
    Stop-EnvManagerResidue
    $msiArgs = @('/x', "`"$MsiPath`"", '/quiet', '/norestart', '/L*v', "`"$LogPath`"")
    Invoke-MsiProcess -Stage 'Step 3: silent uninstall' -MsiArgs $msiArgs -LogPath $LogPath
}

function Invoke-SilentRepair {
    param([string]$MsiPath, [string]$LogPath)
    # /fomus reinstalls files (o), rewrites every machine and user registry entry
    # (m, u) and reinstalls shortcuts (s). The registry rewrite is what gives this
    # assertion teeth: it puts the LogsDir components into the install state, so a
    # component that still carried an install-time RemoveFile would delete the
    # seeded log here. Without it the repair would skip those components and the
    # assertion would pass on an installer that still deletes the file.
    $msiArgs = @('/fomus', "`"$MsiPath`"", '/quiet', '/norestart', '/L*v', "`"$LogPath`"")
    $msiArgs = Add-InstallDirOverride -MsiArgs $msiArgs -RequestedDir $InstallDir -ResolvedDir $targetDir
    Invoke-MsiProcess -Stage 'Step 2: forced repair (/fomus)' -MsiArgs $msiArgs -LogPath $LogPath
}

function Assert-ServicePresent {
    param([string]$Stage)
    $service = Get-EnvManagerService
    if (-not $service) { throw "${Stage}: EnvManagerService SCM entry is missing" }
    Write-Host "${Stage}: EnvManagerService present (status: $($service.Status))"
}

function Get-ServiceImagePath {
    # The service's configured command line, read from the store ServiceInstall
    # writes to: GET-SERVICE only answers "does the entry exist", and Get-CimInstance
    # would add a WMI round trip for the same string. Read-only on purpose - this
    # asserts state, it never writes the registry, so test-with-restore.ps1 does not
    # apply (that harness exists to roll back variable writes).
    $key = 'HKLM:\SYSTEM\CurrentControlSet\Services\EnvManagerService'
    $entry = Get-ItemProperty -LiteralPath $key -Name 'ImagePath' -ErrorAction SilentlyContinue
    if (-not $entry) { return '' }
    return ([string]$entry.ImagePath).Trim()
}

function ConvertFrom-ServiceImagePath {
    # ImagePath is the configured command line: the executable plus the installer's
    # arguments, with SCM quoting optional. Return the executable only.
    param([string]$ImagePath)
    $trimmed = ([string]$ImagePath).Trim()
    if ($trimmed.Length -eq 0) { return '' }
    if ($trimmed.StartsWith('"')) {
        $end = $trimmed.IndexOf('"', 1)
        if ($end -lt 1) { return '' }
        return $trimmed.Substring(1, $end - 1)
    }
    # Unquoted: cut at the first argument-shaped token, not at the first space,
    # because an unquoted path under Program Files contains spaces itself and the
    # arguments in this repo always start with '-'.
    $match = [regex]::Match($trimmed, '^(?<exe>.*?)\s+--?\S')
    if ($match.Success) { return $match.Groups['exe'].Value }
    return $trimmed
}

function Assert-ServiceImagePath {
    param([string]$Stage, [string]$TargetDir)
    $expected = [System.IO.Path]::GetFullPath((Join-Path $TargetDir 'env-manager-service.exe'))
    $imagePath = Get-ServiceImagePath
    if ([string]::IsNullOrWhiteSpace($imagePath)) {
        throw "${Stage}: EnvManagerService has no ImagePath; expected the service binary at $expected"
    }
    $actual = ConvertFrom-ServiceImagePath -ImagePath $imagePath
    if ([string]::IsNullOrWhiteSpace($actual)) {
        throw "${Stage}: cannot read an executable path from ImagePath '$imagePath'"
    }
    # Normalize both sides before comparing: the same directory can be spelled with
    # a different case, a trailing separator or an 8.3 name, and a string compare on
    # the raw values would make the gate fail for a correctly installed service.
    try { $actualFull = [System.IO.Path]::GetFullPath($actual) }
    catch { throw "${Stage}: cannot normalize the ImagePath executable '$actual' ($($_.Exception.Message))" }
    if (-not [string]::Equals($actualFull, $expected, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "${Stage}: EnvManagerService points at '$actual', expected the service binary in the current install directory ($expected); ImagePath: '$imagePath'"
    }
    if (-not (Test-Path -LiteralPath $actualFull)) {
        throw "${Stage}: EnvManagerService points at $actualFull, but that file does not exist"
    }
    Write-Host "${Stage}: EnvManagerService points at $actualFull (file present)"
}

function Assert-ServiceAbsent {
    param([string]$Stage)
    $service = Get-EnvManagerService
    if ($service) { throw "${Stage}: EnvManagerService SCM entry still exists after uninstall" }
    Write-Host "${Stage}: EnvManagerService SCM entry removed"
}

function Assert-InstalledPayload {
    param([string]$Stage, [string]$ExpectedVersion)
    foreach ($name in @('env-manager.exe', 'env-manager-cli.exe')) {
        $path = Join-Path $targetDir $name
        if (-not (Test-Path -LiteralPath $path)) { throw "${Stage}: installed file missing at $path" }
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { return }
    $cli = Join-Path $targetDir 'env-manager-cli.exe'
    $banner = & $cli 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0) { throw "${Stage}: installed CLI exited $LASTEXITCODE" }
    if (-not (Test-VersionBanner -Banner $banner -ExpectedVersion $ExpectedVersion)) {
        throw "${Stage}: installed CLI version does not match v$ExpectedVersion. Banner: $($banner.Trim())"
    }
    Write-Host "${Stage}: installed payload reports v$ExpectedVersion"
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
        Invoke-SilentInstall -Stage 'Step 1: silent install (previous release)' -MsiPath $PreviousMsi -LogPath (Join-Path $LogDir 'step1-previous-install.log')
        Assert-ServicePresent -Stage 'Step 1'
        Assert-InstalledPayload -Stage 'Step 1' -ExpectedVersion $PreviousVersion
        # Seed the legacy log path the old installer wrote to. The cleanup
        # component is shared across versions, so the old product's uninstall
        # action is skipped; the current product's upgrade-only cleanup action
        # must delete it during the upgrade.
        $legacyProbe = Join-Path (Join-Path $targetDir 'logs') 'upgrade-probe.log'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $legacyProbe) | Out-Null
        Set-Content -LiteralPath $legacyProbe -Value 'legacy log probe' -Encoding utf8
        Write-Host "Step 2: seeded $legacyProbe to prove the upgrade cleanup runs"
        Invoke-SilentInstall -Stage 'Step 2: silent upgrade (current over previous)' -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step2-current-upgrade.log')
        Assert-ServicePresent -Stage 'Step 2'
        Assert-InstalledPayload -Stage 'Step 2' -ExpectedVersion $CurrentVersion
        # Presence alone cannot tell a correct service entry from one naming some
        # other directory, so the upgrade legs check where it points and that the
        # file is there.
        Assert-ServiceImagePath -Stage 'Step 2' -TargetDir $targetDir
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
        Invoke-SilentRepair -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step2b-current-repair.log')
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
        Invoke-SilentInstall -Stage 'Step 1: silent install (current)' -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step1-current-install.log')
        Assert-ServicePresent -Stage 'Step 1'
        Assert-InstalledPayload -Stage 'Step 1' -ExpectedVersion $CurrentVersion
        # The repair is the widest maintenance pass on a product that was never
        # upgraded, so it is where an unconditional install-time cleanup would still
        # show up. A clean install on an empty machine has no unrelated file to lose.
        # The probe is the same file name the upgrade leg seeds, so the two legs
        # disagree only in the expectation.
        $legacyProbe = Join-Path (Join-Path $targetDir 'logs') 'upgrade-probe.log'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $legacyProbe) | Out-Null
        Set-Content -LiteralPath $legacyProbe -Value 'repair probe' -Encoding utf8
        Write-Host "Step 2: seeded $legacyProbe to prove a repair does not run the install-time cleanup"
        Invoke-SilentRepair -MsiPath $CurrentMsi -LogPath (Join-Path $LogDir 'step2-current-repair.log')
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
