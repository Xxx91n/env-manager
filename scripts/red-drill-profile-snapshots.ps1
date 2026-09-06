# Ticket 32 (architecture-recovery): red-first drill for the ProfileCommand
# characterization snapshot net. Injects one user-facing wording drift into
# src/ProfileCommand.cs, runs the affected Verify snapshot test expecting RED,
# reverts byte-exactly, and re-runs expecting GREEN. Proves the net actually
# binds the pinned copy instead of passing vacuously.
#
# CI-only discipline (2026-09-04 mandate): this script is EXECUTED IN CI (or a
# disposable checkout) by the verification workflow - never as a local
# self-certification path. The sub-window that authored the suite does not run it.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot),
    [string]$TestFilter = "FullyQualifiedName~ProfileCommandCharacterizationTests.AddVar_Success_StdoutIsStable"
)

$ErrorActionPreference = "Stop"
$target = Join-Path $RepoRoot "src/ProfileCommand.cs"
$testProject = Join-Path $RepoRoot "tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj"

# Anchor strings are single-quoted (PS: double the single quotes); the C# side is an
# interpolated string, so {varName}/{profileName} are literal braces here.
$needle = '        Console.WriteLine($"Added variable ''{varName}'' to profile ''{profileName}''");';
$drift  = '        Console.WriteLine($"Added variable [''{varName}''] to profile ''{profileName}'' (drill)");';

$original = [System.IO.File]::ReadAllText($target)
if (-not $original.Contains($needle)) {
    throw "Drill anchor string not found in src/ProfileCommand.cs - production copy drifted; aborting WITHOUT any change."
}

$failedAsExpected = $false
try {
    [System.IO.File]::WriteAllText($target, $original.Replace($needle, $drift), (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "[red-drill] drift injected: add-var success copy now carries a [bracket] wording change"
    dotnet test $testProject -c Debug --nologo --filter $TestFilter
    if ($LASTEXITCODE -eq 0) {
        throw "DRILL INCONCLUSIVE: snapshot test PASSED despite injected wording drift - the characterization net is not binding this copy."
    }
    $failedAsExpected = $true
    Write-Host "[red-drill] RED confirmed: Verify snapshot failed under injected drift (exit $LASTEXITCODE)."
}
finally {
    [System.IO.File]::WriteAllText($target, $original, (New-Object System.Text.UTF8Encoding($false)))
    $restored = [System.IO.File]::ReadAllText($target)
    if (-not $restored.Contains($needle)) {
        throw "CRITICAL: source restore verification failed - src/ProfileCommand.cs does not contain the original anchor. Restore from git/but before proceeding."
    }
    Write-Host "[red-drill] source byte-restored (anchor present)."
}

if (-not $failedAsExpected) { throw "Drill did not reach the red assertion." }
dotnet test $testProject -c Debug --nologo --filter $TestFilter
if ($LASTEXITCODE -ne 0) { throw "Post-revert run failed (exit $LASTEXITCODE) - check for drill residue." }
Write-Host "[red-drill] GREEN confirmed after revert: the characterization net is binding and clean."
exit 0
