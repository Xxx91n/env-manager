# scripts/run-ci-tests.ps1
# CI Tier 3 orchestrator: runs integration tests AFTER the vitest unit-test
# step in build.yml's verify job.
#
# Four integration suites:
#   1. tests/launch-env-injection.Tests.ps1   (Pester; self-contained fixture)
#   2. tests/canary-redaction.Tests.ps1       (Pester; ticket 07 canary zero-leak + mask assertions)
#   3. scripts/test-inheritance-protection.ps1 (raw ps1; AGENTS.md hard boundary)
#   4. scripts/test-with-restore.ps1             (raw ps1 with registry rollback)
# Suites 3 and 4 are invoked as subprocesses to preserve their already-correct
# setup/teardown semantics. Pester 6 ( Run.Script removed) means the new test
# file auto-discovers its CLI path so no parameter passing is needed.

[CmdLetBinding()]
param(
    [string]$CliExe = "",
    [string]$ResultsDir = "test-results"
)

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
New-Item -ItemType Directory -Path (Join-Path $projectRoot $ResultsDir) -Force | Out-Null

# Honour -CliExe for non-standard CLI locations via environment variable; the
# Pester test file checks $env:EM_CLI_EXE first and falls back to its
# well-known resolution order. Survives both Pester 5 and 6 because we do not
# pass arguments through the Pester config object.
if ($CliExe) { $env:EM_CLI_EXE = $CliExe }

# Ensure Pester 5+ (Pester 6 is also accepted; the syntax we use is shared).
$pester = Get-Module -ListAvailable Pester | Where-Object { $_.Version -ge [Version]'5.0.0' } | Select-Object -First 1
if (-not $pester) {
    Write-Host "Pester 5+ not found; installing..." -ForegroundColor Yellow
    Install-Module Pester -MinimumVersion 5.0 -Force -SkipPublisherCheck -Scope CurrentUser -AcceptLicense
    $pester = Get-Module -ListAvailable Pester | Where-Object { $_.Version -ge [Version]'5.0.0' } | Select-Object -First 1
}
if (-not $pester) { throw "Pester 5+ not available after install attempt." }
Import-Module Pester -MinimumVersion 5.0 -Force
Write-Host "Using Pester $($pester.Version)" -ForegroundColor Cyan

$failures = @()

# --- Suite 1: LaunchInjectionTests (Pester) ---
$pesterPath = Join-Path $projectRoot 'tests\launch-env-injection.Tests.ps1'
if (Test-Path $pesterPath) {
    $xml = Join-Path $projectRoot "$ResultsDir\launch-env-injection.junit.xml"
    $cfg = New-PesterConfiguration
    $cfg.Run.Path = $pesterPath
    $cfg.Run.Exit = $false
    $cfg.Output.Verbosity = 'Normal'
    $cfg.TestResult.Enabled = $true
    $cfg.TestResult.OutputPath = $xml
    $cfg.Filter.Tag = 'CI'
    $r = Invoke-Pester -Configuration $cfg
    if ($r.Result -ne 'Passed' -and $r.FailedCount -gt 0) {
        $failures += "launch-env-injection.Tests.ps1: $($r.FailedCount) test(s) failed"
    }
} else {
    Write-Warning "launch-env-injection.Tests.ps1 not found; skipping"
}

# --- Suite 2: CanaryRedaction (Pester; ticket 07 zero-leak across sinks) ---
$canaryPath = Join-Path $projectRoot 'tests\canary-redaction.Tests.ps1'
if (Test-Path $canaryPath) {
    $xml = Join-Path $projectRoot "$ResultsDir\canary-redaction.junit.xml"
    $cfg = New-PesterConfiguration
    $cfg.Run.Path = $canaryPath
    $cfg.Run.Exit = $false
    $cfg.Output.Verbosity = 'Normal'
    $cfg.TestResult.Enabled = $true
    $cfg.TestResult.OutputPath = $xml
    $cfg.Filter.Tag = 'CI'
    $r = Invoke-Pester -Configuration $cfg
    if ($r.Result -ne 'Passed' -and $r.FailedCount -gt 0) {
        $failures += "canary-redaction.Tests.ps1: $($r.FailedCount) test(s) failed"
    }
} else {
    Write-Warning "canary-redaction.Tests.ps1 not found; skipping"
}

# --- Suite 3: InheritanceProtection (raw script; AGENTS.md hard boundary) ---
$inhPath = Join-Path $projectRoot 'scripts\test-inheritance-protection.ps1'
if (Test-Path $inhPath) {
    Write-Host "`n=== InheritanceProtection ===" -ForegroundColor Cyan
    & pwsh -NoProfile -File $inhPath
    if ($LASTEXITCODE -ne 0) { $failures += "test-inheritance-protection.ps1 exited $LASTEXITCODE" }
} else {
    Write-Warning "test-inheritance-protection.ps1 not found; skipping"
}

# --- Suite 4: RegistryTxTests (raw script with rollback; AGENTS.md hard boundary) ---
$regPath = Join-Path $projectRoot 'scripts\test-with-restore.ps1'
if (Test-Path $regPath) {
    Write-Host "`n=== RegistryTxTests ===" -ForegroundColor Cyan
    $regArgs = @()
    if ($CliExe) { $regArgs += @("-CliPath", $CliExe) }
    & pwsh -NoProfile -File $regPath @regArgs
    if ($LASTEXITCODE -ne 0) { $failures += "test-with-restore.ps1 exited $LASTEXITCODE" }
} else {
    Write-Warning "test-with-restore.ps1 not found; skipping"
}

if ($failures.Count -gt 0) {
    Write-Host "`n=== CI test tier FAILED ===" -ForegroundColor Red
    $failures | ForEach-Object { Write-Host " - $_" -ForegroundColor Red }
    exit 1
}
Write-Host "`n=== CI test tier PASSED ===" -ForegroundColor Green
exit 0