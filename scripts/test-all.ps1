# test-all.ps1 - run both test suites, keep the full log on disk, print exit codes only.
#
# Why this exists: reading a test run through `tail` silently discards the failing
# test's name (it happened twice). The log file is the evidence; the console is a
# summary. Full logs: <repo>/.test-logs/<utc>-<suite>.log (gitignored via *.log).
param(
    [string]$LogDir = '',
    [ValidateSet('all', 'vitest', 'engine')]
    [string]$Suite = 'all'
)

# Native tools write progress to stderr; with 'Stop' the first such line becomes a
# terminating error and truncates the log mid-run. Keep 'Continue' and rely on
# $LASTEXITCODE for pass/fail.
$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($LogDir)) { $LogDir = Join-Path $root '.test-logs' }
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssZ')

$allSuites = @(
    @{ Name = 'vitest'; WorkDir = (Join-Path $root 'frontend'); Exe = 'npx'; CliArgs = @('vitest', 'run') },
    @{ Name = 'engine'; WorkDir = $root; Exe = 'dotnet'; CliArgs = @('test', 'tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj') }
)

$selected = $allSuites
if ($Suite -ne 'all') {
    $selected = @($allSuites | Where-Object { $_.Name -eq $Suite })
}
# Never report success for a run that executed nothing.
if ($selected.Count -eq 0) {
    Write-Host ("no suite matched '{0}'" -f $Suite)
    exit 1
}

$summaryPattern = 'Test Files|Tests\s+\d+\s+passed|已通过!|失败!|测试总数|通过数|失败数|跳过数'
$failed = 0

foreach ($entry in $selected) {
    $log = Join-Path $LogDir ("{0}-{1}.log" -f $stamp, $entry.Name)
    $exe = $entry.Exe
    $cliArgs = $entry.CliArgs

    Push-Location $entry.WorkDir
    try {
        $raw = & $exe @cliArgs 2>&1 | ForEach-Object { $_.ToString() }
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    # Strip ANSI colour codes: vitest emits them even when stdout is redirected, and a
    # log full of escape sequences is not greppable - which defeats the point.
    $output = @($raw) | ForEach-Object { $_ -replace '\x1B\[[0-9;]*[A-Za-z]', '' }

    [System.IO.File]::WriteAllLines($log, $output, (New-Object System.Text.UTF8Encoding($false)))

    Write-Host ("{0}_exit={1}  log={2}" -f $entry.Name, $code, $log)
    $output | Select-String -Pattern $summaryPattern | ForEach-Object { Write-Host ("  " + $_.Line.Trim()) }
    if ($code -ne 0) { $failed++ }
}

# Keep the tree bounded: .test-logs gains one file per suite per run and nothing
# else prunes it. Newest 20 files survive (about 10 full runs).
$logs = @(Get-ChildItem -Path $LogDir -Filter '*.log' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending)
if ($logs.Count -gt 20) {
    $logs | Select-Object -Skip 20 | Remove-Item -Force -ErrorAction SilentlyContinue
}

if ($failed -gt 0) {
    Write-Host ("FAILED suites: {0}" -f $failed)
    exit 1
}
Write-Host 'ALL SELECTED SUITES PASSED'
exit 0
