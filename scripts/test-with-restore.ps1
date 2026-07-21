# Env Manager - live CLI smoke harness with exact registry rollback.
# Every real-registry mutation is transactional: snapshot all values, verify exact
# equality, and reconcile both accessible environment hives on failure.
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
$SystemRegBackup = Join-Path $BackupDir "system-env-$Stamp.reg"
$SystemJsonBackup = Join-Path $BackupDir "system-env-$Stamp.json"
$UserEnvSubKey = "Environment"
$SystemEnvSubKey = "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
$SystemEnvKey = "HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
$TestPrefix = "EM_TEST_"
$InternalConfigNames = @(
  "profiles.json",
  "audit.json",
  "protected-vars.json",
  "protected-paths.json",
  "builtin-protected-vars.json",
  "builtin-protected-paths.json"
)
$InternalConfigDir = Join-Path $BackupDir "internal-configs-$Stamp"
$InternalConfigSnapshot = Join-Path $InternalConfigDir "snapshot.json"
$SystemSnapshotAvailable = $false

function ConvertTo-PlainHashtable($Value) {
  if ($null -eq $Value) { return $null }
  if ($Value -is [System.Collections.IDictionary]) {
    $map = @{}
    foreach ($key in $Value.Keys) { $map[[string]$key] = ConvertTo-PlainHashtable $Value[$key] }
    return $map
  }
  if ($Value -is [System.Management.Automation.PSCustomObject]) {
    $map = @{}
    foreach ($property in $Value.PSObject.Properties) { $map[$property.Name] = ConvertTo-PlainHashtable $property.Value }
    return $map
  }
  if ($Value -is [System.Collections.IEnumerable] -and -not ($Value -is [string])) {
    return @($Value | ForEach-Object { ConvertTo-PlainHashtable $_ })
  }
  return $Value
}

function Read-JsonHashtable([string]$Path) {
  return ConvertTo-PlainHashtable (Get-Content -Raw -Path $Path | ConvertFrom-Json)
}

function Get-InternalConfigSnapshot {
  $configDir = Join-Path $env:LOCALAPPDATA "EnvManager"
  $files = @{}
  foreach ($name in $InternalConfigNames) {
    $source = Join-Path $configDir $name
    if (Test-Path -LiteralPath $source -PathType Leaf) {
      $files[$name] = @{ Exists = $true; Content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($source)) }
    } else {
      $files[$name] = @{ Exists = $false; Content = $null }
    }
  }
  return @{ Files = $files }
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
  [IO.File]::WriteAllText($Path, $Content, (New-Object Text.UTF8Encoding($false)))
}

function Save-InternalConfigSnapshot {
  New-Item -ItemType Directory -Path $InternalConfigDir -Force | Out-Null
  Write-Utf8NoBom $InternalConfigSnapshot ((Get-InternalConfigSnapshot | ConvertTo-Json -Depth 5))
}

function Restore-InternalConfigSnapshot {
  if (-not (Test-Path -LiteralPath $InternalConfigSnapshot)) { throw "Internal configuration snapshot missing" }
  $snapshot = Read-JsonHashtable $InternalConfigSnapshot
  $configDir = Join-Path $env:LOCALAPPDATA "EnvManager"
  New-Item -ItemType Directory -Path $configDir -Force | Out-Null
  foreach ($name in $InternalConfigNames) {
    $target = Join-Path $configDir $name
    $entry = $snapshot.Files[$name]
    if ($entry.Exists) {
      [IO.File]::WriteAllBytes($target, [Convert]::FromBase64String([string]$entry.Content))
    } elseif (Test-Path -LiteralPath $target) {
      Remove-Item -LiteralPath $target -Force
    }
  }
}

function Compare-InternalConfigSnapshot {
  if (-not (Test-Path -LiteralPath $InternalConfigSnapshot)) { return @{ Match = $false; Diff = "internal configuration snapshot missing" } }
  $expected = Read-JsonHashtable $InternalConfigSnapshot
  $actual = Get-InternalConfigSnapshot
  foreach ($name in $InternalConfigNames) {
    $left = $expected.Files[$name]
    $right = $actual.Files[$name]
    if ($left.Exists -ne $right.Exists -or [string]$left.Content -cne [string]$right.Content) {
      return @{ Match = $false; Diff = "internal configuration changed: $name" }
    }
  }
  return @{ Match = $true; Diff = "" }
}

function Convert-RegistryValueToSnapshotValue($Value) {
  if ($Value -is [string[]]) { return @{ Type = "stringArray"; Value = @($Value) } }
  if ($Value -is [byte[]]) { return @{ Type = "byteArray"; Value = [Convert]::ToBase64String($Value) } }
  return @{ Type = "scalar"; Value = $Value }
}

function Convert-SnapshotValueToRegistryValue($SnapshotValue) {
  switch ($SnapshotValue.Type) {
    "stringArray" { return [string[]]@($SnapshotValue.Value) }
    "byteArray" { return [Convert]::FromBase64String([string]$SnapshotValue.Value) }
    default { return $SnapshotValue.Value }
  }
}

function Get-RegistrySnapshot([Microsoft.Win32.RegistryHive]$Hive, [string]$SubKey) {
  $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Default)
  try {
    $key = $base.OpenSubKey($SubKey, $false)
    if ($null -eq $key) { throw "Registry key not found: $Hive\$SubKey" }
    try {
      $values = @{}
      foreach ($name in $key.GetValueNames()) {
        $raw = $key.GetValue($name, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $values[$name] = @{
          Kind = $key.GetValueKind($name).ToString()
          Data = Convert-RegistryValueToSnapshotValue $raw
        }
      }
      return @{ Values = $values }
    } finally {
      $key.Dispose()
    }
  } finally {
    $base.Dispose()
  }
}

function Save-Snapshot([hashtable]$Snapshot, [string]$Path) {
  Write-Utf8NoBom $Path ($Snapshot | ConvertTo-Json -Depth 8)
}

function Read-Snapshot([string]$Path) {
  $raw = Read-JsonHashtable $Path
  if ($null -eq $raw -or $null -eq $raw.Values) { throw "Invalid snapshot: $Path" }
  return $raw
}

function Get-SnapshotKeys([hashtable]$Snapshot) {
  return @($Snapshot.Values.Keys | Sort-Object)
}

function Test-SnapshotValueEqual($Expected, $Actual) {
  if ($Expected.Kind -ne $Actual.Kind -or $Expected.Data.Type -ne $Actual.Data.Type) { return $false }
  if ($Expected.Data.Type -eq "stringArray") {
    $expectedItems = @($Expected.Data.Value)
    $actualItems = @($Actual.Data.Value)
    if ($expectedItems.Count -ne $actualItems.Count) { return $false }
    for ($i = 0; $i -lt $expectedItems.Count; $i++) {
      if ([string]$expectedItems[$i] -cne [string]$actualItems[$i]) { return $false }
    }
    return $true
  }
  return [string]$Expected.Data.Value -ceq [string]$Actual.Data.Value
}

function Compare-RegistrySnapshot([Microsoft.Win32.RegistryHive]$Hive, [string]$SubKey, [string]$SnapshotPath) {
  if (-not (Test-Path $SnapshotPath)) { return @{ Match = $false; Diff = "snapshot missing" } }
  try {
    $before = Read-Snapshot $SnapshotPath
    $after = Get-RegistrySnapshot $Hive $SubKey
  } catch {
    return @{ Match = $false; Diff = "snapshot read failed: $($_.Exception.GetType().Name)" }
  }

  $beforeKeys = Get-SnapshotKeys $before
  $afterKeys = Get-SnapshotKeys $after
  $added = @($afterKeys | Where-Object { $_ -notin $beforeKeys })
  $removed = @($beforeKeys | Where-Object { $_ -notin $afterKeys })
  $changed = @()
  foreach ($name in $beforeKeys) {
    if ($name -in $afterKeys -and -not (Test-SnapshotValueEqual $before.Values[$name] $after.Values[$name])) {
      $changed += $name
    }
  }

  if ($added.Count -eq 0 -and $removed.Count -eq 0 -and $changed.Count -eq 0) {
    return @{ Match = $true; Diff = "" }
  }

  $parts = @()
  if ($added.Count -gt 0) { $parts += "added=$($added -join ',')" }
  if ($removed.Count -gt 0) { $parts += "removed=$($removed -join ',')" }
  if ($changed.Count -gt 0) { $parts += "changed=$($changed -join ',')" }
  return @{ Match = $false; Diff = ($parts -join '; ') }
}

function Convert-RegistryKind([string]$Kind) {
  return [Microsoft.Win32.RegistryValueKind]::$Kind
}

function Restore-RegistrySnapshot([Microsoft.Win32.RegistryHive]$Hive, [string]$SubKey, [string]$SnapshotPath) {
  $snapshot = Read-Snapshot $SnapshotPath
  $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Default)
  try {
    $key = $base.CreateSubKey($SubKey, $true)
    try {
      foreach ($name in @($key.GetValueNames())) {
        if (-not $snapshot.Values.ContainsKey($name)) {
          $key.DeleteValue($name, $false)
        }
      }
      foreach ($name in $snapshot.Values.Keys) {
        $entry = $snapshot.Values[$name]
        $key.SetValue($name, (Convert-SnapshotValueToRegistryValue $entry.Data), (Convert-RegistryKind $entry.Kind))
      }
    } finally {
      $key.Dispose()
    }
  } finally {
    $base.Dispose()
  }
}

function Broadcast-EnvironmentChange {
  $typeName = "EnvManagerTest.NativeMethods"
  if ($null -eq ($typeName -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace EnvManagerTest {
  public static class NativeMethods {
    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
  }
}
'@
  }
  $result = [UIntPtr]::Zero
  $null = [EnvManagerTest.NativeMethods]::SendMessageTimeout([IntPtr]0xffff, 0x1A, [UIntPtr]::Zero, "Environment", 2, 5000, [ref]$result)
}

function Invoke-Cli([string[]]$CliArgs) {
  # Discard CLI text so callers receive exactly one value: the native exit code.
  # This avoids PowerShell treating normal success output as a failed comparison.
  & $CliPath @CliArgs 2>$null | Out-Null
  return [int]$LASTEXITCODE
}

function Invoke-CliExit([string[]]$CliArgs) {
  $stdout = [IO.Path]::GetTempFileName()
  $stderr = [IO.Path]::GetTempFileName()
  try {
    $process = Start-Process -FilePath $CliPath -ArgumentList $CliArgs -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
    return [int]$process.ExitCode
  } finally {
    Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
  }
}

function Backup-Registry {
  Write-Host "[test-with-restore] Capturing exact HKCU\Environment snapshot ..." -ForegroundColor Cyan
  Save-Snapshot (Get-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::CurrentUser) $UserEnvSubKey) $UserJsonBackup
  $userExport = reg export "HKCU\Environment" $UserRegBackup /y 2>&1
  if ($LASTEXITCODE -ne 0) {
    Write-Warning "HKCU .reg export failed; the exact JSON snapshot remains the rollback source."
  }

  Write-Host "[test-with-restore] Capturing exact $SystemEnvKey snapshot ..." -ForegroundColor Cyan
  try {
    Save-Snapshot (Get-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::LocalMachine) $SystemEnvSubKey) $SystemJsonBackup
    $script:SystemSnapshotAvailable = $true
    $systemExport = reg export $SystemEnvKey $SystemRegBackup /y 2>&1
    if ($LASTEXITCODE -ne 0) {
      Write-Warning "HKLM .reg export failed; the exact JSON snapshot remains the rollback source."
    }
  } catch {
    Write-Warning "HKLM snapshot unavailable; system hive will not be tested or mutated: $($_.Exception.GetType().Name)"
  }

  Save-InternalConfigSnapshot
}

function Restore-AllSnapshots {
  $errors = @()
  try {
    Write-Warning "[test-with-restore] Reconciling HKCU\Environment to its pre-test snapshot ..."
    Restore-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::CurrentUser) $UserEnvSubKey $UserJsonBackup
  } catch {
    $errors += "HKCU rollback failed: $($_.Exception.GetType().Name)"
  }

  if ($SystemSnapshotAvailable) {
    try {
      Write-Warning "[test-with-restore] Reconciling $SystemEnvKey to its pre-test snapshot ..."
      Restore-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::LocalMachine) $SystemEnvSubKey $SystemJsonBackup
    } catch {
      $errors += "HKLM rollback failed: $($_.Exception.GetType().Name)"
    }
  }

  try {
    Write-Warning "[test-with-restore] Restoring Env Manager internal configuration ..."
    Restore-InternalConfigSnapshot
  } catch {
    $errors += "internal configuration rollback failed: $($_.Exception.GetType().Name)"
  }

  try {
    Broadcast-EnvironmentChange
  } catch {
    $errors += "environment broadcast failed: $($_.Exception.GetType().Name)"
  }

  return $errors
}

function Remove-Backups {
  Remove-Item $UserRegBackup, $UserJsonBackup, $SystemRegBackup, $SystemJsonBackup -Force -ErrorAction SilentlyContinue
  Remove-Item $InternalConfigDir -Recurse -Force -ErrorAction SilentlyContinue
}

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

function Test-RawTrailingBackslashInvocation {
  $value = 'C:\Program Files\PowerShell\7\'
  $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($UserEnvSubKey, $false)
  try {
    $beforePath = if ($key) { [string]$key.GetValue("PATH", "", [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { "" }
  } finally {
    if ($key) { $key.Dispose() }
  }
  $beforeMatchCount = @($beforePath.Split(';', [StringSplitOptions]::RemoveEmptyEntries) | Where-Object { $_ -ceq $value }).Count

  # Invoke through cmd.exe to preserve the raw command-line form that caused the bug.
  # The outer PowerShell process never reparses the target CLI argument list.
  $escapedCli = '"' + $CliPath.Replace('"', '""') + '"'
  $raw = $escapedCli + ' path add "C:\Program Files\PowerShell\7\" --scope user'
  cmd.exe /d /s /c $raw | Out-Null
  if ($LASTEXITCODE -ne 0) { throw "raw trailing-backslash path add failed (exit $LASTEXITCODE)" }

  $listJson = & $CliPath path list --scope user 2>&1 | Out-String
  if ($LASTEXITCODE -ne 0) { throw "path list failed (exit $LASTEXITCODE)" }
  $entries = $listJson | ConvertFrom-Json
  $matches = @($entries | Where-Object { $_.path -ceq $value })
  $expectedMatchCount = [Math]::Max(1, $beforeMatchCount)
  if ($matches.Count -ne $expectedMatchCount) { throw "unexpected raw trailing-backslash PATH entry count" }
  if (@($entries | Where-Object { $_.path -match '--scope' }).Count -ne 0) {
    throw "PATH contains a swallowed --scope token"
  }

  $currentPath = ($entries | ForEach-Object path) -join ';'
  if ($beforePath -notmatch [regex]::Escape($value)) {
    $removeRaw = $escapedCli + ' path remove "C:\Program Files\PowerShell\7\" --scope user'
    cmd.exe /d /s /c $removeRaw | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "raw trailing-backslash cleanup failed (exit $LASTEXITCODE)" }
  }

  $afterKey = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($UserEnvSubKey, $false)
  try {
    $afterPath = if ($afterKey) { [string]$afterKey.GetValue("PATH", "", [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames) } else { "" }
  } finally {
    if ($afterKey) { $afterKey.Dispose() }
  }
  if ($afterPath -cne $beforePath) { throw "trailing-backslash test did not restore the original user PATH" }
}

if (-not (Test-Path $CliPath)) {
  throw "CLI not found at $CliPath. Build first: powershell -NoProfile -ExecutionPolicy Bypass -File frontend\scripts\build-all.ps1"
}

if (Get-Process -Name "env-manager" -ErrorAction SilentlyContinue) {
  throw "Env Manager GUI is running. Close it before a live registry test so internal configuration cannot change concurrently."
}

Write-Host "[test-with-restore] CLI: $CliPath" -ForegroundColor Cyan
Write-Host "[test-with-restore] Test prefix: $TestPrefix" -ForegroundColor Cyan

$failures = @()
$testCount = 0
$backupCompleted = $false
$allPass = $false

try {
  # Register the finally handler before taking snapshots. Once this completes,
  # every subsequent test mutation has registry and internal-config rollback.
  Backup-Registry
  $backupCompleted = $true

  Run-Test "set+get+delete round-trip" {
    if ((Invoke-Cli @("set", "EM_TEST_FOO", "bar123", "--scope", "user")) -ne 0) { throw "set failed" }
    $getOut = & $CliPath get EM_TEST_FOO 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $getOut -notmatch "bar123") { throw "get mismatch" }
    if ((Invoke-Cli @("delete", "EM_TEST_FOO", "--scope", "user")) -ne 0) { throw "delete failed" }
  }

  Run-Test "rename contract" {
    $sourceName = "EM_TEST_SRC_$Stamp"
    $targetName = "EM_TEST_DST_$Stamp"
    if ((Invoke-Cli @("set", $sourceName, "v1", "--scope", "user")) -ne 0) { throw "seed failed" }
    if ((Invoke-Cli @("rename", $sourceName, $targetName, "--scope", "user")) -ne 0) { throw "rename failed" }
    $dst = & $CliPath get $targetName 2>&1 | Out-String
    if ($LASTEXITCODE -ne 0 -or $dst -notmatch "v1") { throw "rename value mismatch" }
    if ((Invoke-Cli @("delete", $targetName, "--scope", "user")) -ne 0) { throw "cleanup failed" }
  }

  Run-Test "protected variable rejection" {
    if ((Invoke-CliExit @("set", "PATHEXT", ".x", "--scope", "system")) -eq 0) { throw "system protected variable was not rejected" }
    if ((Invoke-CliExit @("toggle", "SystemRoot", "--scope", "user")) -eq 0) { throw "protected toggle was not rejected" }
  }

  Run-Test "toggle exact value and kind recovery" {
    $toggleName = "EM_TEST_TOGGLE_$Stamp"
    $backupName = "${toggleName}_EnvManager_disabled"
    $expectedValue = "%USERPROFILE%\EnvManager-toggle-smoke"
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($UserEnvSubKey, $true)
    if (-not $key) { throw "cannot open HKCU environment key" }
    try {
      $key.SetValue($toggleName, $expectedValue, [Microsoft.Win32.RegistryValueKind]::ExpandString)
      if ((Invoke-Cli @("toggle", $toggleName, "--scope", "user")) -ne 0) { throw "disable failed" }
      if ($null -ne $key.GetValue($toggleName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) { throw "original value still exists after disable" }
      $backupValue = $key.GetValue($backupName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
      if ([string]$backupValue -cne $expectedValue -or $key.GetValueKind($backupName) -ne [Microsoft.Win32.RegistryValueKind]::ExpandString) { throw "disabled backup lost the raw value or registry value kind" }

      $items = (& $CliPath list 2>$null | Out-String | ConvertFrom-Json)
      $disabledItem = @($items | Where-Object { $_.name -ceq $toggleName -and $_.scope -ceq "user" })
      if ($disabledItem.Count -ne 1 -or -not $disabledItem[0].isDisabled) { throw "list did not project the disabled variable exactly once" }

      if ((Invoke-Cli @("toggle", $toggleName, "--scope", "user")) -ne 0) { throw "enable failed" }
      $restoredValue = $key.GetValue($toggleName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
      if ([string]$restoredValue -cne $expectedValue -or $key.GetValueKind($toggleName) -ne [Microsoft.Win32.RegistryValueKind]::ExpandString) { throw "restored value or registry value kind differs from the original" }
      if ($null -ne $key.GetValue($backupName, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)) { throw "disabled backup remains after successful restore" }
    } finally {
      $key.Dispose()
    }
    if ((Invoke-Cli @("delete", $toggleName, "--scope", "user")) -ne 0) { throw "toggle smoke cleanup failed" }
  }

  $profileName = "EM_TEST_PROFILE_$Stamp"
  $secretProfileName = "EM_TEST_SEC_$Stamp"

  Run-Test "profile no-registry-mutation" {
    if ((Invoke-Cli @("profile", "create", $profileName)) -ne 0) { throw "profile create failed" }
    if ((Invoke-Cli @("profile", "add-var", $profileName, "EM_TEST_PVAR", "pval")) -ne 0) { throw "profile add-var failed" }
    if ((Invoke-Cli @("profile", "delete", $profileName)) -ne 0) { throw "profile delete failed" }
  }

  Run-Test "secrets never in registry" {
    if ((Invoke-Cli @("profile", "create", $secretProfileName)) -ne 0) { throw "profile create failed" }
    if ((Invoke-Cli @("profile", "add-secret", $secretProfileName, "S", "topsecret")) -ne 0) { throw "add-secret failed" }
    $applyExit = Invoke-CliExit @("profile", "apply", $secretProfileName)
    if ($applyExit -eq 0) { throw "secrets-bearing profile apply must be rejected" }
    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($UserEnvSubKey, $false)
    try {
      if ($key -and $null -ne $key.GetValue("S", $null)) { throw "secret value leaked to HKCU" }
    } finally {
      if ($key) { $key.Dispose() }
    }
    if ((Invoke-Cli @("profile", "delete", $secretProfileName)) -ne 0) { throw "profile delete failed" }
  }

  Run-Test "trailing-backslash + quote recovery" { Test-RawTrailingBackslashInvocation }
} catch {
  $failures += @{ Name = "harness"; Error = $_.Exception.Message }
} finally {
  if (-not $backupCompleted) {
    Write-Warning "[test-with-restore] Snapshot setup failed before any test mutation; retained partial backups for forensics: $BackupDir"
    exit 1
  }

  try {
    Restore-InternalConfigSnapshot
    $internalVerify = Compare-InternalConfigSnapshot
  } catch {
    $internalVerify = @{ Match = $false; Diff = "internal configuration restore failed: $($_.Exception.GetType().Name)" }
  }

  $userVerify = Compare-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::CurrentUser) $UserEnvSubKey $UserJsonBackup
  $systemVerify = if ($SystemSnapshotAvailable) { Compare-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::LocalMachine) $SystemEnvSubKey $SystemJsonBackup } else { @{ Match = $true; Diff = "not tested (no accessible snapshot)" } }
  $allPass = ($failures.Count -eq 0) -and $internalVerify.Match -and $userVerify.Match -and $systemVerify.Match

  if (-not $allPass) {
    Write-Warning "[test-with-restore] Failure or drift detected; restoring snapshots."
    if (-not $internalVerify.Match) { Write-Warning "[test-with-restore] Internal configuration drift: $($internalVerify.Diff)" }
    if (-not $userVerify.Match) { Write-Warning "[test-with-restore] HKCU drift: $($userVerify.Diff)" }
    if (-not $systemVerify.Match) { Write-Warning "[test-with-restore] HKLM drift: $($systemVerify.Diff)" }
    $restoreErrors = @(Restore-AllSnapshots)
    $internalRestored = Compare-InternalConfigSnapshot
    $userRestored = Compare-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::CurrentUser) $UserEnvSubKey $UserJsonBackup
    $systemRestored = if ($SystemSnapshotAvailable) { Compare-RegistrySnapshot ([Microsoft.Win32.RegistryHive]::LocalMachine) $SystemEnvSubKey $SystemJsonBackup } else { @{ Match = $true; Diff = "not tested (no accessible snapshot)" } }
    if ($restoreErrors.Count -gt 0 -or -not $internalRestored.Match -or -not $userRestored.Match -or -not $systemRestored.Match) {
      foreach ($restoreError in $restoreErrors) { Write-Warning "[test-with-restore] $restoreError" }
      if (-not $internalRestored.Match) { Write-Warning "[test-with-restore] Internal config rollback verification failed: $($internalRestored.Diff)" }
      if (-not $userRestored.Match) { Write-Warning "[test-with-restore] HKCU rollback verification failed: $($userRestored.Diff)" }
      if (-not $systemRestored.Match) { Write-Warning "[test-with-restore] HKLM rollback verification failed: $($systemRestored.Diff)" }
    }
  }

  if ($allPass) {
    Write-Host "[test-with-restore] ALL TESTS PASS + exact registry and internal-config snapshots match." -ForegroundColor Green
    if (-not $KeepBackup) { Remove-Backups; Write-Host "[test-with-restore] Backups deleted (clean run)." } else { Write-Host "[test-with-restore] Backups retained (-KeepBackup)." }
  } else {
    Write-Host "[test-with-restore] Backups kept for forensics: $BackupDir"
    foreach ($failure in $failures) { Write-Warning "[test-with-restore] $($failure.Name): $($failure.Error)" }
    exit 1
  }
}
