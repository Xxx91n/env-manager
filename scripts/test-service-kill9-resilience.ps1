# scripts/test-service-kill9-resilience.ps1
# architecture-recovery evidence backfill (round-8).
# kill -9 fault-injection E2E for env-manager-service on a disposable CI runner.
#
# Flow (steady-state -> inject -> detect -> recover -> steady-state):
#   seed secretMount.json -> start `env-manager-service --mode=background` ->
#   `service ping` OK -> `service rotate <mount>` through the GUI's own pipe path, with
#   the session write lock now held by the delegated child instead of the gateway (#164) ->
#   `service ping` OK -> taskkill /F (TerminateProcess = kill -9 equivalent) ->
#   liveness probe detects death -> harness restarts the process (playing the
#   production watchdog role; SCM auto-recovery stays out of scope here because this
#   harness drives a background-mode process, not an SCM-managed one, even though the
#   binary now joins the SCM control protocol - see service/src/scm.rs) ->
#   `service ping` OK (new PID) + secretMount.json persisted + `service health`
#   reachable.
#
# Isolation: every user-state write lands in the job-private runner profile
# (%LOCALAPPDATA%\EnvManager) and the VM is destroyed after the job - the
# user-state isolation constraint is satisfied by the per job
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

Log-Step "=== kill -9 resilience E2E ==="
Log-Step "CLI:     $CliExe"
Log-Step "Service: $ServiceExe"

$mountDir = Join-Path $env:LOCALAPPDATA "EnvManager"
$mountFile = Join-Path $mountDir "secretMount.json"
$backupFile = $null
$backupCreated = $false
$service = $null
$service2 = $null
$script:exitCode = 1

try {
    # Pre-existing user state (dev-machine safety): back up, never destroy.
    # A leftover backup from a crashed earlier run must never be overwritten
    # or removed, so the path embeds a timestamp plus the PID, the copy is
    # refused outright if the path somehow still exists, and the restore /
    # cleanup below only runs when THIS run actually created the backup -
    # a path check alone cannot tell a leftover from this run's own file.
    if (Test-Path $mountFile) {
        $backupFile = "$mountFile.bak-{0:yyyyMMddHHmmss}-{1}" -f (Get-Date), $PID
        if (Test-Path $backupFile) {
            throw "backup path already exists, refusing to overwrite: $backupFile"
        }
        [System.IO.File]::Copy($mountFile, $backupFile, $false)
        $backupCreated = $true
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

    # ---- Background-mode rotate (#164) ----
    # The GUI's rotate path, and the observation that separates Background from Service
    # mode: here the service runs in the caller's session, so the CLI child it delegates to
    # shares the `Local\` write-lock namespace with the gateway that asked for the rotate;
    # in Service mode that child lands in session 0. While the gateway classified `service`
    # as a write it took that mutex at its own entry, the delegate blocked on it, and the
    # call returned after the full 30 s budget with "Another Env Manager write operation is
    # still running" - the same rotate taking 7.0 s in Service mode. Rationale:
    # src/CliRuntime.cs, IsWriteInvocation.
    #
    # Deliberately NOT asserted: how many secrets this rotates. The fixture mount is not
    # referenced by any profile, so `RotateAll` reports 0/0, and pinning a count would
    # freeze the separate "each provider unit silently skips one secret" defect into an
    # expectation. What this step owes is that the delegated child actually ran: the
    # summary line it prints is the presence check, the budget is the timing check, and the
    # contention text is the exact regression the ticket names.
    $rotateWatch = [System.Diagnostics.Stopwatch]::StartNew()
    # A failing rotate explains itself on stderr ("Error: ..."); this harness has to read
    # that text, so the capture runs under 'Continue' - same reason test-all.ps1 keeps
    # 'Continue' for native tools, where 'Stop' turns the first stderr line into a
    # terminating error and destroys the evidence.
    $rotateEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $rotateOut = (& $CliExe service rotate test-mount-001 2>&1 | Out-String)
        $rotateExit = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $rotateEap
    }
    $rotateWatch.Stop()
    $rotateSeconds = $rotateWatch.Elapsed.TotalSeconds
    Log-Step ("service rotate exit={0} in {1:N1}s: {2}" -f $rotateExit, $rotateSeconds, $rotateOut.Trim())
    Assert-True ("Background rotate returned in {0:N1}s, inside the 30 s mutex budget" -f $rotateSeconds) ($rotateSeconds -lt 20)
    Assert-True "Background rotate reported no write-lock contention" (-not ($rotateOut -match 'Another Env Manager write operation is still running'))
    Assert-True "Background rotate succeeded (exit 0)" ($rotateExit -eq 0)
    # Presence, not absence: the delegated CLI prints this line whenever it runs, so it
    # fails if the service never reached its child - which exit 0 alone cannot tell apart
    # from a rotate that was never attempted.
    Assert-True "service rotate response carries the delegated CLI's rotation summary" ($rotateOut -match 'Rotation complete')

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
            try { & $CliExe service shutdown 2>$null | Out-Null; $p.WaitForExit(5000) | Out-Null } catch { Write-Verbose "service shutdown ignored: $_" }
            if (-not $p.HasExited) { try { Stop-Process -Id $p.Id -Force } catch { Write-Verbose "force stop ignored: $_" } }
        }
    }
    if ($backupCreated -and (Test-Path $backupFile)) {
        Copy-Item $backupFile $mountFile -Force
        Remove-Item $backupFile -Force
        Log-Step "pre-existing secretMount.json restored"
    } elseif (Test-Path $mountFile) {
        # Remove only the fixture this script seeded (runner VM dies anyway).
        if ((Get-Content $mountFile -Raw) -match "test-mount-001") { Remove-Item $mountFile -Force }
    }
}

exit $script:exitCode
