# test-msi-upgrade-matrix.ps1 - silent MSI install/upgrade/uninstall proof.
#
# Automates the four-step silent msiexec runbook in docs/build-and-release.md:
#   Step 0  hang baseline    -> a silent install that exceeds InstallTimeoutSeconds fails (default 60s, the runbook's hang threshold)
#   Step 1  silent install   -> install the previous release (upgrade mode) or the current MSI (clean mode)
#   Step 2  silent upgrade   -> install the current MSI over the previous release; a seeded legacy log must be cleaned up,
#                               the service entry must point at the current install directory, and a service that was
#                               running before the upgrade must be stopped with the binary it held replaced
#           (clean mode)     -> force a repair (/fomus); the same seeded legacy log must survive
#   Step 2b repair after upgrade -> /fomus on the upgraded product must leave a freshly seeded log alone
#   Step 3  silent uninstall -> msiexec /x, then assert the service entry and binaries are gone
#
# A failure prints the msiexec action that reported "Return value 3" (or the last
# action that started when the process was killed), so a red job names the
# failing custom action instead of only an exit code.
#
# The upgrade legs assert the SCM facts an upgrade can be judged on - entry present,
# executable path inside the current install directory, that file on disk - and the one
# fact an entry cannot show: a service that was Running before the upgrade is Stopped
# after it, its process is gone, and the binary it held open was replaced rather than
# left for a reboot - or, in the migration cell, gone from the directory it ran from
# while the new install directory holds one. They start EnvManagerService after the
# previous install and carry it into the upgrade.
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
# Whether that start can succeed is read from the installed binary, not assumed from the
# release's age: a binary that does not carry StartServiceCtrlDispatcherW (service/src/scm.rs)
# cannot answer the SCM, and its start fails with 1053 at ServicesPipeTimeout (or 1067 when
# the process exits first). That case is recorded as an explicit skip - naming the SCM error
# and the missing import - in the uploaded log directory and in the run's job summary. A
# skip requires a determinate cause: a failure this script cannot name, or the same symptom
# from a binary that does carry the import, is a red. The skip does not extend to the new
# build - it was built from this tree and must carry the protocol - with the one exception
# the ticket names: a runner that denies the start applies to any binary, so it is recorded
# as a skip there too. That post-upgrade start is also the half of this coverage that is
# reachable now: a replacement that cannot run is not a replacement.
#
# Before attempting the start the leg clears the previous release's restart-on-failure
# actions. A start that never reports RUNNING counts as a failure, and the registered retry
# chain (three restarts, 60s apart) would fire inside Step 2, Step 2b and Step 3 - each of
# which replaces or deletes the binary the retry would relaunch.
#
# Reading the executable out of ImagePath has one shape it refuses rather than
# judges: an unquoted executable that contains spaces. Windows resolves an unquoted
# command line by trying progressively longer prefixes of it as the module name (for a
# path under Program Files: C:\Program.exe, then C:\Program Files\Env.exe, and so on),
# so the binary that starts need not be the one read here. A command line that merely
# carries a space elsewhere - the custom-path leg's "path --mode=service" - is
# unambiguous and stays accepted.
#
# -SelfTest feeds synthetic msiexec logs through the diagnosis helper, synthetic
# ImagePath shapes through the command-line parser (including the ambiguous shape no
# install produces) and synthetic start failures through the skip/fail verdict; it
# needs no MSI and no elevation, so it can run anywhere PowerShell 5.1+ is installed.
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

function Test-BinaryCarriesServiceProtocol {
    # Whether an installed service binary joins the SCM control protocol, read from the
    # binary instead of argued from its version. A PE import name is stored as ASCII in the
    # import name table, so StartServiceCtrlDispatcherW appears verbatim in a binary that
    # imports it - and only in such a binary, for anything this repository builds.
    # The polarity is what makes a byte scan good enough: a match can only REFUSE a skip
    # (it turns 1053 into a red), and a miss leaves the skip as it was. A binary that merely
    # mentioned the symbol in a message would therefore read as "has the protocol", which
    # fails loudly rather than passing quietly.
    param([string]$Path)
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "cannot tell whether the service binary implements the SCM control protocol: $Path does not exist"
    }
    # Latin-1 maps one byte to one char, so the search cannot straddle a multi-byte sequence
    # the way a UTF-16 decode would.
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $text = [System.Text.Encoding]::GetEncoding(28591).GetString($bytes)
    return $text.Contains('StartServiceCtrlDispatcherW')
}

function Get-ServiceStartVerdict {
    # What a failed start of EnvManagerService means for this run. Three verdicts:
    #   active - the service ran, so the upgrade assertions have something to assert;
    #   skip   - the binary or the runner cannot run it here, and the cause is
    #            determinate enough to name in the uploaded log;
    #   fail   - there is no determinate cause, so the coverage must not be dropped.
    # -ExpectsProtocol marks a binary the harness requires to run: a binary built from this
    # tree. For it every determinate failure is a red, because the harness has to be able to
    # start what it just built; for an installed release that may predate the SCM control
    # protocol it is the expected answer and therefore a skip. Access denied is the one code
    # that is a property of the runner rather than the binary, so it skips either way.
    # The caller reads -ExpectsProtocol from the binary (see
    # Test-BinaryCarriesServiceProtocol), so a skip states a checked fact rather than an
    # assumption about which release is current.
    # Declared here, not beside its callers, because the -SelfTest block below calls it
    # and PowerShell only sees a function after its definition.
    param([bool]$ReachedRunning, [int]$Win32Error, [switch]$ExpectsProtocol)
    if ($ReachedRunning) { return [pscustomobject]@{ Verdict = 'active'; Reason = '' } }
    if ($Win32Error -eq 5) {
        return [pscustomobject]@{
            Verdict = 'skip'
            Reason = 'the SCM refused the start with 5 (access denied), so this runner cannot start services'
        }
    }
    # 1053 and 1067 are what a binary without StartServiceCtrlDispatcherW produces; the
    # registration codes are properties of the installed release. Both groups are the
    # installed release's limit, so both skip - unless the binary is the one just built.
    $protocolBlind = @{
        # 0 means the SCM accepted the start and the service then never reported RUNNING,
        # which is the same event as 1067 seen from the other side.
        0 = 'the SCM accepted the start but the service never reported SERVICE_RUNNING'
        1053 = 'the SCM stopped waiting for a SERVICE_RUNNING report at ServicesPipeTimeout and failed the start with 1053'
        1067 = 'the process exited before reporting SERVICE_RUNNING, and the SCM answered 1067'
    }
    $registrationLimits = @{
        1058 = 'the SCM refused the start with 1058: the service is disabled'
        1069 = 'the SCM refused the start with 1069: the service failed to log on'
        1075 = 'the SCM refused the start with 1075: a service dependency is missing'
    }
    if (-not ($protocolBlind.ContainsKey($Win32Error) -or $registrationLimits.ContainsKey($Win32Error))) {
        return [pscustomobject]@{ Verdict = 'fail'; Reason = '' }
    }
    $symptom = if ($protocolBlind.ContainsKey($Win32Error)) { $protocolBlind[$Win32Error] } else { $registrationLimits[$Win32Error] }
    if ($ExpectsProtocol) {
        return [pscustomobject]@{
            Verdict = 'fail'
            Reason = ('{0}; the harness requires the binary it just built to start, so this is not skippable' -f $symptom)
        }
    }
    $evidence = if ($protocolBlind.ContainsKey($Win32Error)) {
        'the installed binary does not carry StartServiceCtrlDispatcherW, so it cannot answer the SCM'
    }
    else {
        'the installed release cannot be started as it was installed'
    }
    return [pscustomobject]@{
        Verdict = 'skip'
        Reason = ('{0}; {1}' -f $symptom, $evidence)
    }
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
    # The protocol probe decides which verdict a 1053 lands in, so a probe that always
    # answered "no" would keep every matrix cell green - the failure mode this whole ticket
    # is about. Both directions are planted in temp files, read here, and removed in the
    # finally even when a read throws: a leftover .bin in %TEMP% is not a failure anyone
    # would connect back to this run.
    $protocolProbe = Join-Path ([System.IO.Path]::GetTempPath()) ('env-manager-protocol-probe-' + [guid]::NewGuid().ToString('N') + '.bin')
    $noProtocolProbe = Join-Path ([System.IO.Path]::GetTempPath()) ('env-manager-noprotocol-probe-' + [guid]::NewGuid().ToString('N') + '.bin')
    $protocolProbeReads = $null
    $noProtocolProbeReads = $null
    try {
        [System.IO.File]::WriteAllBytes($protocolProbe, [System.Text.Encoding]::ASCII.GetBytes('junk StartServiceCtrlDispatcherW junk'))
        [System.IO.File]::WriteAllBytes($noProtocolProbe, [System.Text.Encoding]::ASCII.GetBytes('junk SetServiceStatus RegisterServiceCtrlHandlerExW junk'))
        $protocolProbeReads = Test-BinaryCarriesServiceProtocol -Path $protocolProbe
        $noProtocolProbeReads = Test-BinaryCarriesServiceProtocol -Path $noProtocolProbe
    }
    finally {
        Remove-Item -LiteralPath $protocolProbe, $noProtocolProbe -Force -ErrorAction SilentlyContinue
    }
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
        @{ Name = 'unquoted spaced executable without arguments is refused too'; Actual = ($spacedBareRefusal.StartsWith('The executable in ImagePath is unquoted')); Expected = $true },
        # The skip/fail split is the whole policy behind the running-old-service coverage:
        # a binary that cannot run is a recorded skip, the same failure from a binary that
        # carries the protocol is a regression, and a failure with no nameable cause is
        # never a skip. All three verdicts and both readings of the protocol probe are
        # planted here, because a policy that only ever produced "skip" would pass while
        # turning every unexplained failure green.
        @{ Name = 'a running service arms the upgrade assertions'; Actual = (Get-ServiceStartVerdict -ReachedRunning $true -Win32Error 0).Verdict; Expected = 'active' },
        @{ Name = '1053 without the protocol is a recorded skip'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1053).Verdict; Expected = 'skip' },
        @{ Name = 'the skip names the SCM error it saw'; Actual = ((Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1053).Reason -like '*1053*'); Expected = $true },
        @{ Name = '1053 with the protocol is a failure'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1053 -ExpectsProtocol).Verdict; Expected = 'fail' },
        @{ Name = '1067 without the protocol is a recorded skip'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1067).Verdict; Expected = 'skip' },
        @{ Name = '1067 with the protocol is a failure'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1067 -ExpectsProtocol).Verdict; Expected = 'fail' },
        @{ Name = 'a disabled service is a recorded skip'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1058).Verdict; Expected = 'skip' },
        @{ Name = 'a disabled service the harness must run is a failure'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1058 -ExpectsProtocol).Verdict; Expected = 'fail' },
        @{ Name = 'a logon failure is a recorded skip'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1069).Verdict; Expected = 'skip' },
        @{ Name = 'a logon failure the harness must run is a failure'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1069 -ExpectsProtocol).Verdict; Expected = 'fail' },
        @{ Name = 'a missing dependency is a recorded skip'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1075).Verdict; Expected = 'skip' },
        @{ Name = 'a missing dependency the harness must run is a failure'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 1075 -ExpectsProtocol).Verdict; Expected = 'fail' },
        @{ Name = 'an accepted start that never reported is a recorded skip'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 0).Verdict; Expected = 'skip' },
        @{ Name = 'an accepted start that never reported is a failure for this build'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 0 -ExpectsProtocol).Verdict; Expected = 'fail' },
        @{ Name = 'access denied is a runner limit, skipped'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 5).Verdict; Expected = 'skip' },
        @{ Name = 'access denied skips even for the binary just built'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 5 -ExpectsProtocol).Verdict; Expected = 'skip' },
        @{ Name = 'an unexplained start failure is never skipped'; Actual = (Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 87).Verdict; Expected = 'fail' },
        @{ Name = 'the failure verdict carries no skip reason'; Actual = ((Get-ServiceStartVerdict -ReachedRunning $false -Win32Error 87).Reason -eq ''); Expected = $true },
        @{ Name = 'a binary importing the dispatcher is read as protocol-capable'; Actual = $protocolProbeReads; Expected = $true },
        @{ Name = 'a binary without the import is read as protocol-blind'; Actual = $noProtocolProbeReads; Expected = $false }
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

function Invoke-ServiceStart {
    # Start EnvManagerService and report what the SCM said.
    # StartServiceW is called directly instead of through Start-Service because the fact
    # this script has to classify is a Win32 error code - 1053 above all - and
    # Start-Service hands back a localized sentence with the code buried or absent. The
    # call blocks until the service reports RUNNING or the SCM gives up at
    # ServicesPipeTimeout (30s when the machine has not changed it), which is what bounds
    # a leg whose previous release cannot start.
    param([string]$Stage, [string]$BinaryPath)
    if (-not ('EnvManagerServiceControl' -as [type])) {
        Add-Type -Language CSharp -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class EnvManagerServiceControl
{
    [StructLayout(LayoutKind.Sequential)]
    public struct ServiceStatusProcess
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    const uint SC_MANAGER_CONNECT = 0x0001;
    const uint SERVICE_QUERY_STATUS = 0x0004;
    const uint SERVICE_START = 0x0010;
    const int SC_STATUS_PROCESS_INFO = 0;

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenSCManager(string machineName, string databaseName, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern IntPtr OpenService(IntPtr scm, string serviceName, uint access);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern int StartService(IntPtr service, uint argumentCount, IntPtr arguments);
    // QueryServiceStatusEx, not QueryServiceStatus: the plain struct has no process id,
    // and the process id is what lets the upgrade assertions name the process they expect
    // to be gone.
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern int QueryServiceStatusEx(IntPtr service, int infoLevel, out ServiceStatusProcess status, int bufferSize, out int needed);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern int CloseServiceHandle(IntPtr handle);

    /// 0 when the SCM accepted the start, else the Win32 error that stopped it.
    public static int Start(string serviceName, out uint processId, out uint state, out string failure)
    {
        processId = 0;
        state = 0;
        failure = string.Empty;
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) { failure = "OpenSCManager"; return Marshal.GetLastWin32Error(); }
        try
        {
            IntPtr service = OpenService(scm, serviceName, SERVICE_QUERY_STATUS | SERVICE_START);
            if (service == IntPtr.Zero) { failure = "OpenService"; return Marshal.GetLastWin32Error(); }
            try
            {
                if (StartService(service, 0, IntPtr.Zero) == 0)
                {
                    int error = Marshal.GetLastWin32Error();
                    // Already running is not a failure of this call: something else (the
                    // SCM's failure actions, a leftover from an earlier attempt) started it,
                    // and the status query below still answers the question that matters.
                    if (error != 1056) { failure = "StartService"; return error; }
                }
                ServiceStatusProcess status;
                int needed;
                if (QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, out status, Marshal.SizeOf(typeof(ServiceStatusProcess)), out needed) == 0)
                {
                    failure = "QueryServiceStatusEx";
                    return Marshal.GetLastWin32Error();
                }
                processId = status.dwProcessId;
                state = status.dwCurrentState;
                // 0 means "the SCM accepted the start"; the caller decides whether the
                // reported state is the one it needs, and polls Status() while it is still
                // a pending state.
                return 0;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }

    /// 0 when the query succeeded, else the Win32 error. Read-only.
    public static int Status(string serviceName, out uint processId, out uint state)
    {
        processId = 0;
        state = 0;
        IntPtr scm = OpenSCManager(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) { return Marshal.GetLastWin32Error(); }
        try
        {
            IntPtr service = OpenService(scm, serviceName, SERVICE_QUERY_STATUS);
            if (service == IntPtr.Zero) { return Marshal.GetLastWin32Error(); }
            try
            {
                ServiceStatusProcess status;
                int needed;
                if (QueryServiceStatusEx(service, SC_STATUS_PROCESS_INFO, out status, Marshal.SizeOf(typeof(ServiceStatusProcess)), out needed) == 0)
                {
                    return Marshal.GetLastWin32Error();
                }
                processId = status.dwProcessId;
                state = status.dwCurrentState;
                return 0;
            }
            finally { CloseServiceHandle(service); }
        }
        finally { CloseServiceHandle(scm); }
    }
}
'@
    }
    $processId = [uint32]0
    $state = [uint32]0
    $failure = ''
    $win32Error = [EnvManagerServiceControl]::Start('EnvManagerService', [ref]$processId, [ref]$state, [ref]$failure)
    # StartServiceW is documented to wait for the service to report RUNNING, but it can also
    # return once the SCM has accepted the request, with the service still START_PENDING
    # (2) or CONTINUE_PENDING (5). Poll those out on a short bound instead of calling a
    # pending service "did not run": the failure this file cares about is 1053, which is a
    # definitive answer from the SCM, not a pending state.
    $pollDeadline = (Get-Date).AddSeconds(10)
    while ($win32Error -eq 0 -and $state -in 2, 5 -and (Get-Date) -lt $pollDeadline) {
        Start-Sleep -Milliseconds 250
        $queryError = [EnvManagerServiceControl]::Status('EnvManagerService', [ref]$processId, [ref]$state)
        if ($queryError -ne 0) {
            $win32Error = $queryError
            $failure = 'QueryServiceStatusEx'
            break
        }
    }
    $detail = ''
    if ($win32Error -ne 0) {
        $detail = '{0} failed with {1} ({2})' -f $failure, $win32Error, ([System.ComponentModel.Win32Exception]::new($win32Error)).Message
    }
    elseif ($state -ne 4) {
        $detail = "the SCM accepted the start but the service reports state $state instead of SERVICE_RUNNING (4)"
    }
    if ($win32Error -ne 0 -or $state -ne 4) {
        Write-Host "${Stage}: EnvManagerService did not reach Running - $detail"
        # Only the three fields a caller reads on this branch: the process facts exist only
        # when something ran, and carrying empty placeholders invites reading them anyway.
        return [pscustomobject]@{
            ReachedRunning = $false
            Win32Error = [int]$win32Error
            Detail = $detail
        }
    }
    $process = Get-Process -Id ([int]$processId) -ErrorAction SilentlyContinue
    if (-not $process) {
        throw "${Stage}: the SCM reports EnvManagerService Running with process $processId, but no such process is visible"
    }
    # The hash below is the evidence the upgrade replaced the file this service held open,
    # so the running image has to be the file that gets hashed. Reading it from the process
    # rather than assuming the install directory is what makes that a fact.
    try { $runningImage = [System.IO.Path]::GetFullPath($process.Path) }
    catch { throw "${Stage}: cannot read the image path of the service process $processId ($($_.Exception.Message)); without it the upgrade's binary-replacement assertion cannot be tied to the service that ran" }
    $expectedImage = [System.IO.Path]::GetFullPath($BinaryPath)
    if (-not [string]::Equals($runningImage, $expectedImage, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "${Stage}: the running service is $runningImage, expected the installed binary at $expectedImage"
    }
    $startTime = $null
    try { $startTime = $process.StartTime }
    catch { Write-Warning "${Stage}: cannot read the service process start time ($($_.Exception.Message)); the post-upgrade check falls back to the process id alone" }
    Write-Host "${Stage}: EnvManagerService reached Running (pid $processId, $runningImage)"
    return [pscustomobject]@{
        ReachedRunning = $true
        Win32Error = 0
        Detail = ''
        ProcessId = [int]$processId
        ProcessStartTime = $startTime
        BinaryHash = (Get-FileHash -LiteralPath $runningImage -Algorithm SHA256).Hash
    }
}

function Write-ServiceStartSkip {
    # A skip is only allowed with a determinate cause, and it has to survive the job: a
    # console line scrolls away and the job is green, so the reason lands in the log
    # directory the workflow uploads as the cell's artifact and in the run's job summary -
    # the place a green matrix is actually read.
    param([string]$Stage, [string]$Reason, [string]$Detail, [string]$RecordPath, [string]$Scenario)
    Write-Warning "${Stage}: SKIP - $Reason"
    if (-not [string]::IsNullOrWhiteSpace($Detail)) { Write-Warning "${Stage}: SKIP - $Detail" }
    $stamp = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    Add-Content -LiteralPath $RecordPath -Value ('{0} scenario={1} stage={2} reason={3} detail={4}' -f $stamp, $Scenario, $Stage, $Reason, $Detail) -Encoding utf8
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_STEP_SUMMARY)) {
        Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Encoding utf8 -Value ('- `{0}` / {1}: skipped, so the assertion there did not run - {2}' -f $Scenario, $Stage, $Reason)
    }
}

function Wait-ForServiceStatus {
    # Poll a service status instead of reading it once: the installer issues its stops with
    # Wait="no", so StopPending at the moment msiexec returns is a normal intermediate state,
    # not a failed stop. Returns the last service object it saw, or $null when the entry is
    # gone; the caller decides what a non-matching status means.
    param([string]$Expected, [int]$TimeoutSeconds = 15)
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ($true) {
        $service = Get-EnvManagerService
        if (-not $service) { return $null }
        if ($service.Status -eq $Expected) { return $service }
        if ((Get-Date) -ge $deadline) { return $service }
        Start-Sleep -Milliseconds 500
    }
}

function Assert-ServiceStopped {
    # Shared by the two places that expect a stopped service; they differ only in why, which
    # the caller names, so the failure message says what the run was doing.
    param([string]$Stage, [string]$Why)
    $service = Wait-ForServiceStatus -Expected 'Stopped' -TimeoutSeconds 15
    if (-not $service) { throw "${Stage}: EnvManagerService SCM entry is missing ($Why)" }
    if ($service.Status -ne 'Stopped') {
        throw "${Stage}: EnvManagerService is $($service.Status) after 15s instead of Stopped ($Why)"
    }
}

function Assert-ServiceStartsAndStops {
    # The new build's own start, after the upgrade replaced the old service. Failure here is
    # a red, with one exception the ticket names: when the runner itself cannot start
    # services (access denied), which applies to any binary and cannot be fixed by a change
    # here - that case is recorded as a skip, exactly as the previous release's is, instead
    # of turning a runner limit into a red that reads like a regression.
    param([string]$Stage, [string]$BinaryPath, [string]$SkipRecordPath, [string]$Scenario)
    $outcome = Invoke-ServiceStart -Stage $Stage -BinaryPath $BinaryPath
    if (-not $outcome.ReachedRunning) {
        $verdict = Get-ServiceStartVerdict -ReachedRunning $outcome.ReachedRunning -Win32Error $outcome.Win32Error -ExpectsProtocol
        if ($verdict.Verdict -eq 'skip') {
            Write-ServiceStartSkip -Stage $Stage -Reason $verdict.Reason -Detail $outcome.Detail -RecordPath $SkipRecordPath -Scenario $Scenario
            return
        }
        $reason = if ([string]::IsNullOrWhiteSpace($verdict.Reason)) { 'this script cannot name a cause for it' } else { $verdict.Reason }
        throw "${Stage}: the upgraded service did not reach Running - $($outcome.Detail); $reason"
    }
    # Stop-Service on a service that already stopped reports "not started", which would read
    # as a stop failure. Check the state first so a service that reports RUNNING and then
    # exits on its own is named as that, not as a stop that did not work.
    $running = Get-EnvManagerService
    if (-not $running) { throw "${Stage}: the SCM entry disappeared after the service reported Running" }
    if ($running.Status -eq 'Stopped') {
        throw "${Stage}: the service reported Running and then stopped on its own; it has to stay running until Stop-Service asks it to stop"
    }
    Stop-Service -Name 'EnvManagerService' -ErrorAction Stop
    Assert-ServiceStopped -Stage $Stage -Why 'Stop-Service must bring it back to Stopped'
    Write-Host "${Stage}: the upgraded service reached Running and answered Stop-Service (Stopped)"
}

function Assert-ServiceStillRunning {
    # The upgrade's stop assertion is only worth something if the service was still running
    # when the upgrade began: a process that exited on its own makes "the upgrade stopped it"
    # and "the process exited during the upgrade" equally true of a service that was already
    # gone. Called immediately before the upgrade, so the pair of assertions brackets msiexec.
    param([string]$Stage, $Before)
    $service = Get-EnvManagerService
    if (-not $service) { throw "${Stage}: EnvManagerService SCM entry is missing before the upgrade" }
    if ($service.Status -ne 'Running') {
        throw "${Stage}: EnvManagerService is $($service.Status) before the upgrade; the stop assertion needs it still Running when the upgrade starts"
    }
    $alive = Get-Process -Id ([int]$Before.ProcessId) -ErrorAction SilentlyContinue
    if (-not $alive) {
        throw "${Stage}: the process the service started as (pid $($Before.ProcessId)) is gone before the upgrade, so the upgrade cannot be what ends it"
    }
    if ($Before.ProcessStartTime) {
        $sameProcess = $true
        try { $sameProcess = ($alive.StartTime -eq $Before.ProcessStartTime) }
        catch { $sameProcess = $true }
        if (-not $sameProcess) { throw "${Stage}: pid $($Before.ProcessId) already belongs to another process before the upgrade" }
    }
    Write-Host "${Stage}: EnvManagerService is still Running as pid $($Before.ProcessId)"
}

function Assert-OldServiceStoppedAndReplaced {
    # What the upgrade owes a service that was running: stop it, then replace the binary it
    # held open. Presence and a correct ImagePath show neither - the entry can point at the
    # right path while the old image still runs and the file swap waits for a reboot, which
    # is the failure mode a shared component GUID produces. What "replaced" means splits on
    # the cell: same-directory cells swap the file in place, so the hash is the evidence;
    # the migration cell moves the product, so the evidence is the binary it ran from being
    # gone while the new directory holds one - comparing hashes across a move would be
    # vacuous, they differ by construction.
    param([string]$Stage, $Before, [string]$PreviousBinaryPath, [string]$CurrentBinaryPath, [switch]$Migrating)
    Assert-ServiceStopped -Stage $Stage -Why 'the major upgrade stops the running old service and does not start the new one'
    $alive = Get-Process -Id ([int]$Before.ProcessId) -ErrorAction SilentlyContinue
    if ($alive) {
        # A reused process id is not the old service. The start time is what tells them
        # apart; when it cannot be read, the id alone is treated as the same process, so an
        # unreadable process never buys a pass.
        $sameProcess = $true
        if ($Before.ProcessStartTime) {
            try { $sameProcess = ($alive.StartTime -eq $Before.ProcessStartTime) }
            catch { $sameProcess = $true }
        }
        if ($sameProcess) {
            throw "${Stage}: the process that ran the old service before the upgrade (pid $($Before.ProcessId)) is still alive; the upgrade left it running"
        }
        Write-Host "${Stage}: pid $($Before.ProcessId) now belongs to another process; the old service process is gone"
    }
    else {
        Write-Host "${Stage}: the old service process (pid $($Before.ProcessId)) exited during the upgrade"
    }
    if ($Migrating) {
        if (Test-Path -LiteralPath $PreviousBinaryPath) {
            throw "${Stage}: the previous release's service binary is still at $PreviousBinaryPath after the upgrade; the migration must move it off the directory the product left"
        }
        if (-not (Test-Path -LiteralPath $CurrentBinaryPath)) {
            throw "${Stage}: the service binary is missing after the upgrade ($CurrentBinaryPath)"
        }
        Write-Host "${Stage}: the binary the service ran from is gone and the new directory holds one ($CurrentBinaryPath)"
        return
    }
    if (-not (Test-Path -LiteralPath $CurrentBinaryPath)) {
        throw "${Stage}: the service binary is missing after the upgrade ($CurrentBinaryPath)"
    }
    $replaced = (Get-FileHash -LiteralPath $CurrentBinaryPath -Algorithm SHA256).Hash
    if ([string]::Equals($replaced, $Before.BinaryHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "${Stage}: the service binary still carries the hash recorded before the upgrade ($CurrentBinaryPath); the file the running service held open was not replaced, so the upgrade left the swap for a reboot instead of stopping the service first"
    }
    Write-Host ("${Stage}: the service binary was replaced ({0}... -> {1}...)" -f $Before.BinaryHash.Substring(0, 12), $replaced.Substring(0, 12))
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
    # The uninstall removes the product this cell installed, so the directory to check is
    # the cell's own. It reads that from script scope rather than taking a parameter that
    # could only ever hold the same value.
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
        # The old service runs into the upgrade on purpose: an upgrade over a stopped
        # service cannot show that the old service is stopped before the binary it holds
        # open is replaced, which is the coverage issue #133 was written for. Whether the
        # installed binary can run at all is read from the binary, not assumed from its
        # version, so the skip below states a checked fact and the same code path turns red
        # by itself once a release that carries the protocol becomes the previous release.
        # The binary it runs is the one the previous release installed, which only the
        # migration cell puts somewhere other than the current install directory; after the
        # upgrade the entry and the start assertion look at the current one.
        $previousServiceBinary = Join-Path $previousDir 'env-manager-service.exe'
        $currentServiceBinary = Join-Path $targetDir 'env-manager-service.exe'
        $skipRecord = Join-Path $LogDir 'service-start-skip.log'
        $previousCarriesProtocol = Test-BinaryCarriesServiceProtocol -Path $previousServiceBinary
        # The previous release registers restart-on-failure actions (three restarts, 60s
        # apart), and a start that never reports RUNNING counts as one of those failures.
        # Attempting the start without clearing them would arm a retry chain at roughly
        # +90s, +150s and +210s - inside this leg's Step 2, Step 2b and Step 3, each of which
        # replaces or deletes the binary the retry would relaunch. Clearing them first keeps
        # those steps independent of a timer this script started; the new ServiceInstall
        # writes its own actions during the upgrade, so nothing under test changes.
        # sc.exe takes the empty action list as a quoted empty argument; a bare "actions=" is
        # rejected as an invalid option.
        $clearOutput = & sc.exe failure EnvManagerService reset= 0 actions= '""' 2>&1 | Out-String
        if ($LASTEXITCODE -ne 0) {
            throw "Step 1: could not clear the previous release's restart-on-failure actions (sc.exe exit $LASTEXITCODE): $($clearOutput.Trim())"
        }
        $oldService = Invoke-ServiceStart -Stage 'Step 1: start the previous release service' -BinaryPath $previousServiceBinary
        $startVerdict = Get-ServiceStartVerdict -ReachedRunning $oldService.ReachedRunning -Win32Error $oldService.Win32Error -ExpectsProtocol:$previousCarriesProtocol
        if ($startVerdict.Verdict -eq 'fail') {
            $because = if ([string]::IsNullOrWhiteSpace($startVerdict.Reason)) { 'this script cannot name a determinate cause for it' } else { $startVerdict.Reason }
            throw "Step 1: EnvManagerService did not reach Running - $($oldService.Detail); $because"
        }
        if ($startVerdict.Verdict -eq 'skip') {
            Write-ServiceStartSkip -Stage 'Step 1' -Reason $startVerdict.Reason -Detail $oldService.Detail -RecordPath $skipRecord -Scenario $Scenario
            # A start that never reported RUNNING can still leave the binary running as an
            # ordinary process - that is what a pre-protocol build does: it serves the pipe
            # and waits. Such a process holds the file the upgrade has to replace, so the
            # skip path clears it the way the uninstall path already does.
            Stop-EnvManagerResidue
        }
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
        if ($oldService.ReachedRunning) {
            # Brackets msiexec: the one assertion before the upgrade and the ones after it
            # only mean something together.
            Assert-ServiceStillRunning -Stage 'Step 2 (before the upgrade)' -Before $oldService
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
        if ($oldService.ReachedRunning) {
            Assert-OldServiceStoppedAndReplaced -Stage 'Step 2' -Before $oldService `
                -PreviousBinaryPath $previousServiceBinary -CurrentBinaryPath $currentServiceBinary `
                -Migrating:$migrating
        }
        if (Test-Path -LiteralPath $legacyProbe) {
            throw "Step 2: the upgrade cleanup left $legacyProbe after the major upgrade"
        }
        Write-Host 'Step 2: the upgrade cleanup removed the seeded legacy log'
        # A replacement that cannot run is not a replacement, and this start is the half of
        # the running-old-service coverage that is reachable while no published release
        # carries the SCM control protocol.
        Assert-ServiceStartsAndStops -Stage 'Step 2: start the upgraded service' -BinaryPath $currentServiceBinary -SkipRecordPath $skipRecord -Scenario $Scenario
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
    # A failure before Step 3 leaves behind whatever the leg started: the skip path cleans
    # up only on its own branch, and the uninstall's cleanup has not run. Clear it here so
    # a red job still ends with no EnvManagerService process of ours running.
    Stop-EnvManagerResidue
    throw
}
Write-Host "=== MSI upgrade matrix PASSED ($Scenario) ==="
exit 0
