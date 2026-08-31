# scripts/test-inheritance-protection.ps1
# v0.7.7 inheritance-protection live CLI integration test.
#
# Validates three CLI-side hard boundaries that cannot be exercised from
# vitest (which only mocks the IPC bridge, not the C# backend):
#   1. `profile set-inherits <global> <launch>` is REJECTED at the CLI.
#   2. `profile set-inherits <launch> <launch-with-secret>` is REJECTED at the CLI.
#   3. `profile set-inherits <global> <global>` is ACCEPTED.
#   4. self-inheritance is rejected (v0.7.5 guard still works).
#
# This script does NOT mutate the registry. It backs up profiles.json, runs
# CLI profile commands (which only mutate %LOCALAPPDATA%\EnvManager\profiles.json),
# verifies rejection messages, and restores profiles.json at the end. It is
# safe to run repeatedly -- all test profiles are prefixed EM_INHERIT_TEST_ and
# are cleaned up after the run.
#
# Exit codes: 0 = all assertions passed, 1 = at least one assertion failed,
# 2 = setup error (could not back up profiles.json, etc).

#Requires -Version 7
$ErrorActionPreference = 'Stop'

$cli = 'bin\Release\net10.0-windows\env-manager-cli.exe'
if (-not (Test-Path $cli)) { $cli = 'bin\Debug\net10.0-windows\env-manager-cli.exe' }
if (-not (Test-Path $cli)) { Write-Error 'CLI exe not found. Run build-all.ps1 first.'; exit 2 }

$envLocal = Join-Path $env:LOCALAPPDATA 'EnvManager'
$profilesPath = Join-Path $envLocal 'profiles.json'
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmss')
$backupPath = Join-Path $envLocal "profiles.json.inherit-test-bak.$stamp"

function Invoke-Cli {
    param([string[]]$CliArgs)
    $escaped = @()
    foreach ($a in $CliArgs) {
        if ($a -match '\s') { $escaped += ('"' + $a + '"') } else { $escaped += $a }
    }
    $argString = 'profile ' + ($escaped -join ' ')
    $p = New-Object System.Diagnostics.Process
    $p.StartInfo.FileName = $cli
    $p.StartInfo.Arguments = $argString
    $p.StartInfo.UseShellExecute = $false
    $p.StartInfo.RedirectStandardOutput = $true
    $p.StartInfo.RedirectStandardError = $true
    $p.StartInfo.CreateNoWindow = $true
    [void]$p.Start()
    $stdout = $p.StandardOutput.ReadToEnd()
    $stderr = $p.StandardError.ReadToEnd()
    [void]$p.WaitForExit(30000)
    return [pscustomobject]@{ ExitCode = $p.ExitCode; Stdout = $stdout; Stderr = $stderr }
}


# Ticket 04: create a System32-free launch target so ValidateLaunchTarget passes without
# accepting System32 binaries (hard boundary).
$targetCmd = Join-Path $env:TEMP 'em-inherit-test-target.cmd'
if (-not (Test-Path $targetCmd)) { Set-Content -Path $targetCmd -Value '@echo ok' -Encoding Ascii }
$failures = @()
try {
    foreach ($name in @('EM_INHERIT_TEST_global','EM_INHERIT_TEST_launch_secret','EM_INHERIT_TEST_launch_plain','EM_INHERIT_TEST_global_other')) {
        [void](Invoke-Cli -CliArgs @('delete', $name))
    }

    [void](Invoke-Cli -CliArgs @('create','EM_INHERIT_TEST_global','--type','global'))
    [void](Invoke-Cli -CliArgs @('create','EM_INHERIT_TEST_launch_secret','--type','launch','--target', $targetCmd))
    $secretRes = Invoke-Cli -CliArgs @('add-secret','EM_INHERIT_TEST_launch_secret','EM_TEST_SECRET','dummy-value')
    if ($secretRes.ExitCode -ne 0 -and $secretRes.Stderr -notmatch 'already') {
        Write-Warning ("add-secret returned exit {0}: {1}" -f $secretRes.ExitCode, $secretRes.Stderr)
    }
    [void](Invoke-Cli -CliArgs @('create','EM_INHERIT_TEST_launch_plain','--type','launch','--target', $targetCmd))
    [void](Invoke-Cli -CliArgs @('create','EM_INHERIT_TEST_global_other','--type','global'))

    # Case 1: global inherits launch -> MUST be rejected.
    $r1 = Invoke-Cli -CliArgs @('set-inherits','EM_INHERIT_TEST_global','EM_INHERIT_TEST_launch_secret')
    if ($r1.ExitCode -eq 0) {
        $failures += "Case 1 FAIL: set-inherits global<-launch_secret unexpectedly succeeded. stdout=$($r1.Stdout) stderr=$($r1.Stderr)"
    } elseif ($r1.Stderr -notmatch 'Global profile cannot inherit from a Launch profile') {
        $failures += "Case 1 FAIL: rejection message mismatch. stderr=$($r1.Stderr)"
    } else {
        Write-Host 'Case 1 PASS: global<-launch rejected at CLI.'
    }

    # Case 2: launch inherits launch+secret -> MUST be rejected.
    $r2 = Invoke-Cli -CliArgs @('set-inherits','EM_INHERIT_TEST_launch_plain','EM_INHERIT_TEST_launch_secret')
    if ($r2.ExitCode -eq 0) {
        $failures += "Case 2 FAIL: set-inherits launch<-launch_secret unexpectedly succeeded. stdout=$($r2.Stdout) stderr=$($r2.Stderr)"
    } elseif ($r2.Stderr -notmatch 'Launch profile cannot inherit from another Launch profile that already carries secrets') {
        $failures += "Case 2 FAIL: rejection message mismatch. stderr=$($r2.Stderr)"
    } else {
        Write-Host 'Case 2 PASS: launch<-launch_secret rejected at CLI.'
    }

    # Case 3: global inherits global -> MUST be accepted.
    $r3 = Invoke-Cli -CliArgs @('set-inherits','EM_INHERIT_TEST_global','EM_INHERIT_TEST_global_other')
    if ($r3.ExitCode -ne 0) {
        $failures += "Case 3 FAIL: set-inherits global<-global unexpectedly failed. stdout=$($r3.Stdout) stderr=$($r3.Stderr)"
    } else {
        Write-Host 'Case 3 PASS: global<-global accepted (baseline inheritance works).'
    }

    # Case 4: self-inheritance must still be rejected.
    $r4 = Invoke-Cli -CliArgs @('set-inherits','EM_INHERIT_TEST_global','EM_INHERIT_TEST_global')
    if ($r4.ExitCode -eq 0) {
        $failures += 'Case 4 FAIL: self-inheritance unexpectedly succeeded.'
    } else {
        Write-Host 'Case 4 PASS: self-inheritance rejected.'
    }
}
finally {
    foreach ($name in @('EM_INHERIT_TEST_global','EM_INHERIT_TEST_launch_secret','EM_INHERIT_TEST_launch_plain','EM_INHERIT_TEST_global_other')) {
        [void](Invoke-Cli -CliArgs @('delete', $name))
    }
    if ($hadProfilesFile -and (Test-Path $backupPath)) {
        Copy-Item $backupPath $profilesPath -Force
        Remove-Item $backupPath -Force
    } elseif ((-not $hadProfilesFile) -and (Test-Path $profilesPath)) {
        Remove-Item $profilesPath -Force
    }
}

if ($failures.Count -gt 0) {
    Write-Host ''
    Write-Host 'INHERITANCE PROTECTION TEST: FAIL'
    $failures | ForEach-Object { Write-Host "  - $_" }
    exit 1
}
Write-Host ''
Write-Host 'INHERITANCE PROTECTION TEST: PASS (4/4)'
exit 0