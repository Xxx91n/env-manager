# scripts/test-service-kill9-resilience.ps1
# architecture-recovery ticket 33 evidence backfill (round-8 ticket 49).
# kill -9 fault-injection E2E for env-manager-service on a disposable CI runner.
#
# Flow (steady-state -> inject -> detect -> recover -> steady-state):
#   seed secretMount.json -> start `env-manager-service --mode=background` ->
#   `service ping` OK -> taskkill /F (TerminateProcess = kill -9 equivalent) ->
#   liveness probe detects death -> harness restarts the process (playing the
#   production watchdog role; SCM auto-recovery is out of scope because the
#   binary does not implement the service-control-dispatcher protocol) ->
#   `service ping` OK (new PID) + secretMount.json persisted + `service health`
#   reachable.
#
# Isolation: every user-state write lands in the job-private runner profile
# (%LOCALAPPDATA%\EnvManager) and the VM is destroyed after the job - the
# ticket-33 user-state isolation constraint is satisfied by the per-job
# disposable-runner model itself. No secrets required.
#
# Exit 0 = all assertions passed; exit 1 = first failed assertion (fail-fast).

[CmdletBinding()]
param(
    [string]$CliExe = "bin\Release\net10.0-windows\env-manager-cli.exe",
    [string]$ServiceExe = "service\target\release\env-manager-service.exe",
    [int]$StartTimeoutSec = 60,
    [int]$DetectTimeoutSec = 20,
    [int]$RestartTimeoutSec = 60,
    [string]$LogDir = "test-results"
)

$ErrorActionPreference = "Stop"

function Log-Step($message) {
    Write-Host ("[{0}] {1}" -f (Get-Date -Format "HH:mm:ss.fff"), $message)
}

function Assert-True($message, $condition) {
    if ($condition) { Write-Host "  ASSERT PASS: $message" }
    else { Write-Host "  ASSERT FAIL: $message"; throw "ASSERTION FAILED: $message" }
}

function Invoke-Ping {
    # $true when the service answers the IPC ping with ok:true (exit 0).
    $out = & $script:CliExe service ping 2>$null
    return ($LASTEXITCODE -eq 0) -and ("$out" -match '"ok":\s*true')
}

function Wait-ForPing([int]$timeoutSec) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (Invoke-Ping) { return $true }
        Start-Sleep -Milliseconds 300
    }
    return $false
}

function Wait-ForDeath([int]$timeoutSec) {
    # Watchdog-detection semantics: the liveness probe must stop answering
    # ok:true within the window after the kill.
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Invoke-Ping)) { return $true }
        Start-Sleep -Milliseconds 200
    }
    return $false
}

function Start-ServiceProc([string]$tag) {
    $outLog = Join-Path $script:LogDir "service-$tag.out.log"
    $errLog = Join-Path $script:LogDir "service-$tag.err.log"
    Remove-Item $outLog, $errLog -Force -ErrorAction SilentlyContinue
    $p = Start-Process -FilePath $script:ServiceExe -ArgumentList '--mode=background' `
        -RedirectStandardOutput $outLog -RedirectStandardError $errLog `
        -WindowStyle Hidden -PassThru
    Log-Step "service started ($tag), PID=$($p.Id), logs: $outLog / $errLog"
    return $p
}

$script:CliExe = (Resolve-Path $CliExe).Path
# Repo .cargo/config.toml sets build.target = x86_64-pc-windows-msvc, so cargo
# places artifacts under target/<triple>/release rather than target/release.
# If the caller-passed path misses, probe any target/*/release dir for the exe.
if (-not (Test-Path $ServiceExe)) {
    $targetRoot = Split-Path (Split-Path $ServiceExe -Parent) -Parent
    $probe = Get-ChildItem -Path $targetRoot -Recurse -Filter (Split-Path $ServiceExe -Leaf) `
        -ErrorAction SilentlyContinue | Where-Object { $_.FullName -match 'release' } | Select-Object -First 1
    if ($probe) { $ServiceExe = $probe.FullName }
}
$script:ServiceExe = (Resolve-Path $ServiceExe).Path
New-Item -ItemType Directory -Force -Path $LogDir | Out-Null
$script:LogDir = (Resolve-Path $LogDir).Path

Log-Step "=== kill -9 resilience E2E (tickets 33/49) ==="
Log-Step "CLI:     $CliExe"
Log-Step "Service: $ServiceExe"

$mountDir = Join-Path $env:LOCALAPPDATA "EnvManager"
$mountFile = Join-Path $mountDir "secretMount.json"
$backupFile = $null
$service = $null
$service2 = $null
$script:exitCode = 1

try {
    # Pre-existing user state (dev-machine safety): back up, never destroy.
    if (Test-Path $mountFile) {
        $backupFile = "$mountFile.ticket49.bak"
        Copy-Item $mountFile $backupFile -Force
        Log-Step "pre-existing secretMount.json backed up to $backupFile"
    }
    New-Item -ItemType Directory -Force -Path $mountDir | Out-Null
    @(
        @{
            id = "test-mount-001"
            provider = "credential-manager"
            name = "TEST_TOKEN"
            scope = "user"
            refreshPolicy = "Periodic"
            refreshIntervalSeconds = 300
            createdAt = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
            schemaVersion = 1
        }
    ) | ConvertTo-Json -Depth 10 | Set-Content -Path $mountFile -Encoding UTF8
    Assert-True "secretMount.json seeded" (Test-Path $mountFile)
    Assert-True "seed contains test-mount-001" ((Get-Content $mountFile -Raw) -match "test-mount-001")

    # ---- steady state ----
    $service = Start-ServiceProc "pre-kill"
    Assert-True "service answers ping within ${StartTimeoutSec}s of start" (Wait-ForPing $StartTimeoutSec)

    # ---- inject: kill -9 (TerminateProcess, no cleanup handlers) ----
    $killTime = Get-Date
    taskkill /PID $service.Id /F | Out-Null
    Log-Step "taskkill /F issued for PID $($service.Id)"
    $service.WaitForExit(15000) | Out-Null
    Assert-True "process terminated" ($service.HasExited)

    # ---- detect: liveness probe must notice the death ----
    Assert-True "liveness probe detected death within ${DetectTimeoutSec}s" (Wait-ForDeath $DetectTimeoutSec)
    $detectLatency = ((Get-Date) - $killTime).TotalSeconds
    Log-Step ("death detected after {0:N1}s" -f $detectLatency)

    # ---- recover: harness restarts (watchdog role) ----
    $service2 = Start-ServiceProc "post-restart"
    Assert-True "service answers ping within ${RestartTimeoutSec}s of restart" (Wait-ForPing $RestartTimeoutSec)
    Assert-True "restarted instance is a NEW process" ($service2.Id -ne $service.Id)

    # ---- steady-state re-check ----
    Assert-True "secretMount.json persisted across kill+restart" (Test-Path $mountFile)
    Assert-True "test-mount-001 survived" ((Get-Content $mountFile -Raw) -match "test-mount-001")
    $health = & $CliExe service health 2>$null
    Assert-True "service health endpoint reachable after restart" ("$health" -match '"ok":\s*true')

    Log-Step "=== ALL ASSERTIONS PASSED ==="
    Log-Step ("detection latency {0:N1}s; recovery verified on PID {1}" -f $detectLatency, $service2.Id)
    $script:exitCode = 0
}
catch {
    Write-Host "TEST FAILED: $_"
}
finally {
    foreach ($p in @($service2, $service)) {
        if ($p -and -not $p.HasExited) {
            try { & $CliExe service shutdown 2>$null | Out-Null; $p.WaitForExit(5000) | Out-Null } catch {}
            if (-not $p.HasExited) { try { Stop-Process -Id $p.Id -Force } catch {} }
        }
    }
    if ($backupFile -and (Test-Path $backupFile)) {
        Copy-Item $backupFile $mountFile -Force
        Remove-Item $backupFile -Force
        Log-Step "pre-existing secretMount.json restored"
    } elseif (Test-Path $mountFile) {
        # Remove only the fixture this script seeded (runner VM dies anyway).
        if ((Get-Content $mountFile -Raw) -match "test-mount-001") { Remove-Item $mountFile -Force }
    }
}

exit $script:exitCode
