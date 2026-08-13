# Test script for profile add-var and add-path CLI commands
# Validates: 1) add-var works when profile is unapplied  2) add-var rejects when applied  3) add-path works when unapplied
# Usage: pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-profile-add-var.ps1
# This test does NOT touch the registry. It only mutates profiles.json (backed up + restored).

param(
  [string]$CliExe = "$PSScriptRoot\..\bin\Release\net10.0-windows\env-manager-cli.exe"
)

# Fallback to Debug build if Release not found
if (-not (Test-Path $CliExe)) {
  $CliExe = "$PSScriptRoot\..\bin\Debug\net10.0-windows\env-manager-cli.exe"
}
if (-not (Test-Path $CliExe)) {
  Write-Host "FAIL: CLI exe not found at $CliExe" -ForegroundColor Red
  exit 1
}

$profilesPath = Join-Path $env:LOCALAPPDATA "EnvManager" "profiles.json"
$backupPath = "$profilesPath.bak.test-addvar"

# Backup
if (Test-Path $profilesPath) {
  Copy-Item $profilesPath $backupPath -Force
}

$testProfileName = "EM_TEST_ADDVAR_$([DateTimeOffset]::UtcNow.Ticks)"
$failCount = 0
$passCount = 0

function Test-Assert($name, $condition, $detail = "") {
  if ($condition) {
    Write-Host "PASS: $name" -ForegroundColor Green
    $script:passCount++
  } else {
    Write-Host "FAIL: $name $detail" -ForegroundColor Red
    $script:failCount++
  }
}

try {
  # Create a test profile
  & $CliExe profile create $testProfileName 2>&1 | Out-Null

  # Test 1: add-var on unapplied profile should succeed
  $out = & $CliExe profile add-var $testProfileName TEST_VAR "test value" --scope user 2>&1
  $exitCode = $LASTEXITCODE
  Test-Assert "add-var on unapplied profile succeeds" ($exitCode -eq 0) "exit=$exitCode out=$out"

  # Test 2: verify the variable was added
  $out = & $CliExe profile show $testProfileName 2>&1
  Test-Assert "variable TEST_VAR appears in profile" ($out -match "TEST_VAR") "out=$out"

  # Test 3: add-var with invalid characters should fail (validation)
  $out = & $CliExe profile add-var $testProfileName "BAD=VAR" "val" --scope user 2>&1
  $exitCode = $LASTEXITCODE
  Test-Assert "add-var with = in name rejected" ($exitCode -ne 0) "exit=$exitCode out=$out"

  # Test 4: apply profile, then add-var should fail
  & $CliExe profile apply $testProfileName 2>&1 | Out-Null
  $out = & $CliExe profile add-var $testProfileName TEST_VAR2 "val2" --scope user 2>&1
  $exitCode = $LASTEXITCODE
  Test-Assert "add-var on applied profile rejected" ($exitCode -ne 0 -and $out -match "Unapply the profile") "exit=$exitCode out=$out"

  # Unapply
  & $CliExe profile unapply $testProfileName 2>&1 | Out-Null

  # Test 5: add-path on unapplied profile should succeed
  $out = & $CliExe profile add-path $testProfileName "C:\TestPath" --scope user 2>&1
  $exitCode = $LASTEXITCODE
  Test-Assert "add-path on unapplied profile succeeds" ($exitCode -eq 0) "exit=$exitCode out=$out"

  # Test 6: add-path with semicolon should fail (validation)
  $out = & $CliExe profile add-path $testProfileName "C:\Bad;Path" --scope user 2>&1
  $exitCode = $LASTEXITCODE
  Test-Assert "add-path with semicolon rejected" ($exitCode -ne 0) "exit=$exitCode out=$out"

  # Test 7: apply profile, then add-path should fail
  & $CliExe profile apply $testProfileName 2>&1 | Out-Null
  $out = & $CliExe profile add-path $testProfileName "C:\TestPath2" --scope user 2>&1
  $exitCode = $LASTEXITCODE
  Test-Assert "add-path on applied profile rejected" ($exitCode -ne 0 -and $out -match "Unapply the profile") "exit=$exitCode out=$out"

  # Unapply
  & $CliExe profile unapply $testProfileName 2>&1 | Out-Null

} finally {
  # Cleanup: delete test profile
  & $CliExe profile delete $testProfileName 2>&1 | Out-Null
  # Restore profiles.json
  if (Test-Path $backupPath) {
    Copy-Item $backupPath $profilesPath -Force
    Remove-Item $backupPath -Force
  }
}

Write-Host ""
Write-Host "Results: $passCount passed, $failCount failed" -ForegroundColor $(if ($failCount -eq 0) { 'Green' } else { 'Red' })
if ($failCount -gt 0) { exit 1 }
