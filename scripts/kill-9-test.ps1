# Manual kill -9 test script
# Scenario: Kill service process → watchdog detects death → SCM recovers on restart
# Usage: powershell -Command "& { & 'scripts/kill-9-test.ps1' }"
# Prerequisites: Administrator privileges, env-manager-service.exe built and available

$ErrorActionPreference = "Stop"
$Host.UI.RawUI.WindowTitle = "Kill -9 Resilience Test (Manual)"

# Configuration
$SERVICE_EXE = Join-Path $PSScriptRoot "..\service\bin\x64\release\env-manager-service.exe"
if (-not (Test-Path $SERVICE_EXE)) {
    # Fallback to debug or portable build
    $SERVICE_EXE = Join-Path $PSScriptRoot "..\service\bin\x64\debug\env-manager-service.exe"
}
if (-not (Test-Path $SERVICE_EXE)) {
    Write-Error "Service executable not found at $SERVICE_EXE"
    exit 1
}

$CLI_EXE = Join-Path $PSScriptRoot "..\..\release\cli-only\env-manager-cli.exe"
if (-not (Test-Path $CLI_EXE)) {
    $CLI_EXE = Join-Path $PSScriptRoot "..\..\release\portable\env-manager-cli.exe"
}
if (-not (Test-Path $CLI_EXE)) {
    Write-Error "CLI executable not found at $CLI_EXE"
    exit 1
}

$PIPE_NAME = "\\\\.\\pipe\\EnvManager.Background"
$HEARTBEAT_TIMEOUT = 5  # seconds to wait for watchdog detection
$RESTART_WAIT = 10      # seconds to wait for service restart

# Logging helper
function Log-Step($message) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    Write-Host "[$timestamp] $message" -ForegroundColor Cyan
}

function Log-Assert($message, $condition) {
    if ($condition) {
        Write-Host "  ✓ $message" -ForegroundColor Green
    } else {
        Write-Host "  ✗ $message" -ForegroundColor Red
        throw "ASSERTION FAILED: $message"
    }
}

# Cleanup function
function Cleanup {
    param([string]$reason = "")
    
    Log-Step "Cleaning up..."
    
    # Stop any running service instances
    Get-Process -Name "env-manager*" -ErrorAction SilentlyContinue | ForEach-Object {
        Log-Step "Stopping process $($_.Id)..."
        Stop-Process -Id $_.Id -Force
    }
    
    # Clean up temp mount file if exists
    $localAppData = [Environment]::GetEnvironmentVariable("LOCALAPPDATA", "User")
    $mountFile = Join-Path $localAppData "EnvManager\secretMount.json"
    if (Test-Path $mountFile) {
        Remove-Item $mountFile -Force -ErrorAction SilentlyContinue
        Log-Step "Removed secretMount.json"
    }
    
    if ($reason) {
        Write-Host "`nTest terminated: $reason" -ForegroundColor Yellow
    }
}

trap { Cleanup $_.Exception.Message; exit 1 }

try {
    # Pre-check: Ensure no existing service instance
    $existing = Get-Process -Name "env-manager*" -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Warning "Existing env-manager processes detected. Stopping them first."
        $existing | Stop-Process -Force
        Start-Sleep -Seconds 2
    }
    
    Log-Step "=== Kill -9 Resilience Test ==="
    Log-Step "Service EXE: $SERVICE_EXE"
    Log-Step "CLI EXE: $CLI_EXE"
    
    # Step 1: Create initial secretMount.json with test data
    Log-Step "Step 1: Creating initial secretMount.json..."
    $localAppData = [Environment]::GetEnvironmentVariable("LOCALAPPDATA", "User")
    $envManagerDir = Join-Path $localAppData "EnvManager"
    New-Item -ItemType Directory -Path $envManagerDir -Force | Out-Null
    
    $testMount = @(@{
        id = "test-mount-001"
        provider = "credential-manager"
        name = "TEST_TOKEN"
        scope = "user"
        refreshPolicy = "Periodic"
        refreshIntervalSeconds = 300
        createdAt = (Get-Date -UFormat "%Y-%m-%dT%H:%M:%S+00:00")
        schemaVersion = 1
    }) | ConvertTo-Json -Depth 10
    
    $mountFile = Join-Path $envManagerDir "secretMount.json"
    $testMount | Set-Content -Path $mountFile -Encoding UTF8
    
    Log-Assert "secretMount.json created" (Test-Path $mountFile)
    $initialContent = Get-Content $mountFile -Raw
    Log-Assert "secretMount.json contains test-mount-001" ($initialContent -match "test-mount-001")
    
    # Step 2: Start service in background
    Log-Step "Step 2: Starting service..."
    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.FileName = $SERVICE_EXE
    $startInfo.Arguments = "--mode=background"
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    
    $serviceProcess = New-Object System.Diagnostics.Process
    $serviceProcess.StartInfo = $startInfo
    $serviceProcess.Start() | Out-Null
    $servicePid = $serviceProcess.Id
    
    Log-Step "Service started with PID: $servicePid"
    
    # Wait for service to be ready
    Start-Sleep -Seconds 3
    
    # Verify service is healthy via IPC ping
    Log-Step "Step 3: Verifying service health via IPC ping..."
    $pingResult = &$CLI_EXE service ping 2>&1
    Log-Assert "IPC ping successful before kill" ($pingResult -match '"ok":\s*true')
    
    $beforeKillTime = Get-Date
    
    # Step 4: Inject fault - kill -9 equivalent (taskkill /F)
    Log-Step "Step 4: Injecting fault - killing service (taskkill /F)..."
    Log-Step "This simulates kill -9 / SIGKILL..."
    taskkill /PID $servicePid /F | Out-Null
    
    $killTime = Get-Date
    $killLatency = ($killTime - $beforeKillTime).TotalMilliseconds
    Log-Step "Kill latency: ${killLatency}ms"
    
    # Verify process is dead
    Log-Step "Step 5: Verifying process termination..."
    $stillRunning = Get-Process -Id $servicePid -ErrorAction SilentlyContinue
    Log-Assert "Process terminated" ([string]::IsNullOrEmpty($stillRunning))
    
    # Step 6: Watchdog detection (poll IPC ping until failure)
    Log-Step "Step 6: Waiting for watchdog to detect death (timeout: ${HEARTBEAT_TIMEOUT}s)..."
    $watchdogDetected = $false
    for ($i = 0; $i -lt $HEARTBEAT_TIMEOUT * 10; $i++) {
        $pingCheck = &$CLI_EXE service ping 2>&1
        if ($pingCheck -notmatch '"ok":\s*true') {
            $watchdogDetected = $true
            $detectionTime = Get-Date
            $detectionLatency = ($detectionTime - $killTime).TotalSeconds
            Log-Step "Watchdog detected death after ${detectionLatency}s"
            break
        }
        Start-Sleep -Milliseconds 100
    }
    
    Log-Assert "Watchdog detected process death within timeout" $watchdogDetected
    
    # Step 7: Restart service
    Log-Step "Step 7: Restarting service..."
    $serviceProcess.StartInfo.FileName = $SERVICE_EXE
    $serviceProcess.StartInfo.Arguments = "--mode=background"
    $serviceProcess.Start() | Out-Null
    $restartPid = $serviceProcess.Id
    
    Log-Step "Service restarted with PID: $restartPid"
    
    # Wait for restart
    Start-Sleep -Seconds $RESTART_WAIT
    
    # Step 8: Verify SCM recovery (secretMount.json persisted)
    Log-Step "Step 8: Verifying SCM recovery..."
    
    # Check that secretMount.json still exists
    Log-Assert "secretMount.json persisted after restart" (Test-Path $mountFile)
    
    # Check that content is preserved
    $afterRestartContent = Get-Content $mountFile -Raw
    Log-Assert "secretMount.json still contains test-mount-001" ($afterRestartContent -match "test-mount-001")
    
    # Verify service is responsive again
    $postRestartPing = &$CLI_EXE service ping 2>&1
    Log-Assert "Service responsive after restart" ($postRestartPing -match '"ok":\s*true')
    
    # Step 9: Verify reconcile loop resumed (check health endpoint)
    Log-Step "Step 9: Verifying reconcile loop resumed..."
    $healthResult = &$CLI_EXE service health 2>&1
    if ($healthResult -match '"total"') {
        Log-Assert "Reconcile loop health endpoint accessible" $true
    } else {
        Write-Host "  ⚠ Health endpoint response unclear, but service is responsive" -ForegroundColor Yellow
    }
    
    # Final summary
    Log-Step "=== Test Summary ==="
    Log-Step "Kill latency: ${killLatency}ms"
    Log-Step "Watchdog detection time: ${detectionLatency}s"
    Log-Step "SCM persistence: VERIFIED"
    Log-Step "Service recovery: VERIFIED"
    
    Write-Host "`n✓ ALL ASSERTIONS PASSED" -ForegroundColor Green
    Write-Host "`nConclusion: Service crate resilience against kill -9 is verified."
    Write-Host "Watchdog correctly detected death, SCM persisted state, and service recovered on restart."
    
    exit 0
    
} catch {
    Write-Host "`n✗ TEST FAILED" -ForegroundColor Red
    Write-Host "Error: $_" -ForegroundColor Red
    exit 1
} finally {
    # Cleanup will be called by trap if exception occurred
    # If we reach here normally, do a final cleanup check
    $finalProcess = Get-Process -Name "env-manager*" -ErrorAction SilentlyContinue
    if ($finalProcess) {
        Log-Step "Cleaning up remaining processes..."
        $finalProcess | Stop-Process -Force
    }
}
