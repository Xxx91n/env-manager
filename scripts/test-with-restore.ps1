# Env Manager - Live CLI test harness with registry backup / verify / restore.
# Implements the industry-standard "two-pronged gate" pattern.
param(
  [string]$CliPath = (Join-Path $PSScriptRoot "..\release\cli-only\env-manager-cli.exe"),
  [switch]$KeepBackup
)

$ErrorActionPreference = "Stop"
$ProjectRoot = Split-Path -Parent $PSScriptRoot
$BackupDir = Join-Path $ProjectRoot ".test-backups"
if (-not (Test-Path $BackupDir)) { New-Item -ItemType Directory -Path $BackupDir -Force | Out-Null }

$Stamp = (Get-Date -Format "yyyyMMdd-HHmmss")
$UserRegBackup = Join-Path $BackupDir "user-env-$Stamp.reg"
$UserJsonBackup = Join-Path $BackupDir "user-env-$Stamp.json"
$TestPrefix = "EM_TEST_"

function Invoke-Cli([string[]]$CliArgs) {
  & $CliPath @CliArgs
  return $LASTEXITCODE
}

function Backup-UserRegistry {
  Write-Host "[test-with-restore] Backing up HKCU\Environment ..." -ForegroundColor Cyan
  $regExport = reg export "HKCU\Environment" $UserRegBackup /y 2>&1
  if ($LASTEXITCODE -ne 0) {
    throw "reg export HKCU\Environment failed: $regExport"
  }
  $snap = @{}
  try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
    if ($null -ne $key) {
      foreach ($name in $key.GetValueNames()) {
        if ($name -like "$TestPrefix*") {
          $snap[$name] = @{ Value = $key.GetValue($name, "", [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); Kind = $key.GetValueKind($name).ToString() }
        }
      }
      $key.Close()
    }
  } catch {
    Write-Warning "Failed to read HKCU\Environment for snapshot: $($_.Exception.Message)"
  }
  $snap | ConvertTo-Json -Depth 5 | Set-Content -Path $UserJsonBackup -Encoding UTF8
  Write-Host "[test-with-restore] Backup OK: $UserRegBackup"
}

function Compare-UserRegistry {
  param([string]$JsonSnapshotPath)
  if (-not (Test-Path $JsonSnapshotPath)) { return @{ Match = $false; Diff = "snapshot missing" } }
  $before = Get-Content $JsonSnapshotPath -Raw | ConvertFrom-Json
  $after = @{}
  try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $false)
    if ($null -ne $key) {
      foreach ($name in $key.GetValueNames()) {
        if ($name -like "$TestPrefix*") {
          $after[$name] = @{ Value = $key.GetValue($name, "", [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames); Kind = $key.GetValueKind($name).ToString() }
        }
      }
      $key.Close()
    }
  } catch {
    return @{ Match = $false; Diff = "read failed: $($_.Exception.Message)" }
  }
  $beforeKeys = @()
  foreach ($p in $before.PSObject.Properties) { $beforeKeys += $p.Name }
  $afterKeys = @()
  foreach ($k in $after.Keys) { $afterKeys += $k }
  $leftover = $afterKeys | Where-Object { $_ -notin $beforeKeys }
  if ($leftover.Count -gt 0) {
    return @{ Match = $false; Diff = "leftover EM_TEST_ keys: $($leftover -join ', ')" }
  }
  foreach ($k in $beforeKeys) {
    $b = $before.$k
    $a = $after[$k]
    if ($null -eq $a) { continue }
    if ($a.Value -ne $b.Value -or $a.Kind -ne $b.Kind) {
      return @{ Match = $false; Diff = "modified key $k" }
    }
  }
  return @{ Match = $true; Diff = "" }
}

function Restore-UserRegistry {
  Write-Warning "[test-with-restore] Restoring HKCU\Environment from backup ..."
  $regImport = reg import $UserRegBackup 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Error "reg import failed: $regImport"
  }
  $sig = '[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)] public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint Msg, UIntPtr wParam, string lParam, uint fuFlags, uint uTimeout, out UIntPtr lpdwResult);'
  $null = Add-Type -MemberDefinition $sig -Name "Win32SendMessage" -Namespace "EnvManager.Test" -ErrorAction SilentlyContinue
  $HWND_BROADCAST = [IntPtr]0xffff
  $WM_SETTINGCHANGE = 0x1A
  $result = [UIntPtr]::Zero
  $null = [EnvManager.Test.Win32SendMessage]::SendMessageTimeout($HWND_BROADCAST, $WM_SETTINGCHANGE, [UIntPtr]::Zero, "Environment", 2, 5000, [ref]$result)
  Write-Host "[test-with-restore] Restore complete." -ForegroundColor Yellow
}

function Cleanup-TestKeys {
  try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $true)
    if ($null -ne $key) {
      $toDelete = @()
      foreach ($name in $key.GetValueNames()) {
        if ($name -like "$TestPrefix*") { $toDelete += $name }
      }
      foreach ($name in $toDelete) {
        try { $key.DeleteValue($name, $false) } catch { }
      }
      $key.Close()
    }
  } catch { }
}

if (-not (Test-Path $CliPath)) {
  throw "CLI not found at $CliPath. Build first: powershell -File frontend\scripts\build-all.ps1"
}

Write-Host "[test-with-restore] CLI: $CliPath" -ForegroundColor Cyan
Write-Host "[test-with-restore] Test prefix: $TestPrefix" -ForegroundColor Cyan

Cleanup-TestKeys
Backup-UserRegistry

$failures = @()
$testCount = 0

function Run-Test([string]$Name, [scriptblock]$Body) {
  $script:testCount++
  Write-Host "[test] $Name ... " -NoNewline -ForegroundColor Cyan
  try {
    & $Body
    Write-Host "OK" -ForegroundColor Green
  } catch {
    Write-Host "FAIL: $($_.Exception.Message)" -ForegroundColor Red
    $script:failures += @{ Name = $Name; Error = $_.Exception.Message }
  }
}

Run-Test "set+get+delete round-trip" {
  $exit1 = Invoke-Cli @("set", "EM_TEST_FOO", "bar123", "--scope", "user")
  if ($exit1 -ne 0) { throw "set failed (exit $exit1)" }
  $getOut = & $CliPath get EM_TEST_FOO 2>&1
  if ($LASTEXITCODE -ne 0) { throw "get failed (exit $LASTEXITCODE): $getOut" }
  if ($getOut -notmatch "bar123") { throw "value mismatch: $getOut" }
  $exit2 = Invoke-Cli @("delete", "EM_TEST_FOO", "--scope", "user")
  if ($exit2 -ne 0) { throw "delete failed (exit $exit2)" }
}

Run-Test "rename contract" {
  $null = Invoke-Cli @("set", "EM_TEST_SRC", "v1", "--scope", "user")
  $null = Invoke-Cli @("rename", "EM_TEST_SRC", "EM_TEST_DST", "--scope", "user")
  if ($LASTEXITCODE -ne 0) { throw "rename failed" }
  $dst = & $CliPath get EM_TEST_DST 2>&1
  if ($LASTEXITCODE -ne 0 -or $dst -notmatch "v1") { throw "rename value mismatch: $dst" }
  $null = Invoke-Cli @("delete", "EM_TEST_DST", "--scope", "user")
}

Run-Test "protected variable rejection" {
  # PATHEXT is a built-in protected system variable - must never be modified at system scope.
  # Use Start-Process to capture stdout+stderr cleanly without PowerShell redirection noise.
  $info = Start-Process -FilePath $CliPath -ArgumentList @("set", "PATHEXT", ".x", "--scope", "system") -Wait -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\em_test_stdout.txt" -RedirectStandardError "$env:TEMP\em_test_stderr.txt"
  $so = Get-Content "$env:TEMP\em_test_stdout.txt" -Raw -ErrorAction SilentlyContinue
  $se = Get-Content "$env:TEMP\em_test_stderr.txt" -Raw -ErrorAction SilentlyContinue
  Remove-Item "$env:TEMP\em_test_stdout.txt", "$env:TEMP\em_test_stderr.txt" -ErrorAction SilentlyContinue
  $combined = "$so$se"
  if ($info.ExitCode -eq 0 -and -not ($combined -match "protected")) {
    throw "set PATHEXT on system scope must be rejected (non-zero exit or 'protected' output); got exit=$($info.ExitCode) out=$combined"
  }
  if (-not ($combined -match "protected")) {
    throw "set PATHEXT output was unexpected; got exit=$($info.ExitCode) out=$combined"
  }
  # toggle of a built-in protected name (e.g., SystemRoot) must also be rejected, even at user scope.
  $info2 = Start-Process -FilePath $CliPath -ArgumentList @("toggle", "SystemRoot", "--scope", "user") -Wait -PassThru -NoNewWindow -RedirectStandardOutput "$env:TEMP\em_test_stdout.txt" -RedirectStandardError "$env:TEMP\em_test_stderr.txt"
  $so2 = Get-Content "$env:TEMP\em_test_stdout.txt" -Raw -ErrorAction SilentlyContinue
  $se2 = Get-Content "$env:TEMP\em_test_stderr.txt" -Raw -ErrorAction SilentlyContinue
  Remove-Item "$env:TEMP\em_test_stdout.txt", "$env:TEMP\em_test_stderr.txt" -ErrorAction SilentlyContinue
  $combined2 = "$so2$se2"
  if ($info2.ExitCode -eq 0 -and -not ($combined2 -match "protected")) {
    throw "toggle SystemRoot should be rejected; got exit=$($info2.ExitCode) out=$combined2"
  }
}

Run-Test "profile no-registry-mutation" {
  $null = Invoke-Cli @("profile", "create", "EM_TEST_PROFILE")
  $null = Invoke-Cli @("profile", "add-var", "EM_TEST_PROFILE", "EM_TEST_PVAR", "pval")
  $null = Invoke-Cli @("profile", "delete", "EM_TEST_PROFILE")
}

Run-Test "secrets never in registry" {
  $null = Invoke-Cli @("profile", "create", "EM_TEST_SEC")
  $null = Invoke-Cli @("profile", "add-secret", "EM_TEST_SEC", "S", "topsecret")
  $applyExit = Invoke-Cli @("profile", "apply", "EM_TEST_SEC")
  if ($applyExit -eq 0) { throw "profile apply on a secrets-bearing profile should be rejected" }
  $regVal = $null
  try {
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey("Environment", $false)
    if ($key) { $regVal = $key.GetValue("S", $null); $key.Close() }
  } catch { }
  if ($null -ne $regVal) { throw "secret value leaked to HKCU\Environment" }
  $null = Invoke-Cli @("profile", "delete", "EM_TEST_SEC")
}

Write-Host ""
Write-Host "[test-with-restore] Verifying registry integrity ..." -ForegroundColor Cyan
$verify = Compare-UserRegistry -JsonSnapshotPath $UserJsonBackup

$allPass = ($failures.Count -eq 0) -and $verify.Match

if ($allPass) {
  Write-Host "[test-with-restore] ALL TESTS PASS + registry intact." -ForegroundColor Green
  if (-not $KeepBackup) {
    Remove-Item $UserRegBackup -Force -ErrorAction SilentlyContinue
    Remove-Item $UserJsonBackup -Force -ErrorAction SilentlyContinue
    Write-Host "[test-with-restore] Backups deleted (clean run)."
  } else {
    Write-Host "[test-with-restore] Backups retained (-KeepBackup)."
  }
  exit 0
} else {
  Write-Warning "[test-with-restore] FAILED. Restoring registry before exit."
  if (-not $verify.Match) {
    Write-Warning "[test-with-restore] Registry drift detected: $($verify.Diff)"
  }
  if ($failures.Count -gt 0) {
    Write-Warning "[test-with-restore] Test failures: $($failures.Count)"
    foreach ($f in $failures) { Write-Warning "  - $($f.Name): $($f.Error)" }
  }
  Restore-UserRegistry
  Write-Host "[test-with-restore] Backups kept for forensics: $BackupDir"
  exit 1
}
