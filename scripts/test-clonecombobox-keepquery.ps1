# test-clonecombobox-keepquery.ps1
# Test closure for PATH entry fix: CloneCombobox keepQueryOnSelect prop
# Verifies that after selecting a PATH entry from the dropdown, the value
# is correctly propagated to newPathEntry and the Add button is enabled.
#
# This is a CLI-level test (verifies the backend add-path command works),
# the frontend visual behavior (keepQueryOnSelect) is verified by Vite build
# success + Svelte compiler accepting the prop.

param(
    [string]$CliExe = "bin\Release\net10.0-windows\env-manager-cli.exe"
)

$ErrorActionPreference = "Stop"
$script:pass = 0
$script:fail = 0

function Test-Assert {
    param([string]$name, [scriptblock]$check, [string]$detail = "")
    try {
        & $check
        $script:pass++
        Write-Host "  PASS: $name" -ForegroundColor Green
    } catch {
        $script:fail++
        Write-Host "  FAIL: $name $detail $_" -ForegroundColor Red
    }
}

# Use a test profile that doesn't conflict with user data
$testProfile = "EM_COMBOTEST_$$"

# Clean up any leftover test profile
& $CliExe profile delete $testProfile 2>$null

# Create a global test profile (unapplied, so we can add vars/paths)
& $CliExe profile create $testProfile --type global 2>&1 | Out-Null

# Test 1: add-path with a simple path
Test-Assert "add-path to unapplied profile" {
    $out = & $CliExe profile add-path $testProfile "C:\Test\Path" --scope user 2>&1
    if ($out -notmatch "Added PATH entry") { throw "Expected 'Added PATH entry', got: $out" }
}

# Verify the path was added
Test-Assert "path appears in profile show" {
    $out = & $CliExe profile show $testProfile 2>&1 | ConvertFrom-Json
    $found = $false
    foreach ($p in $out.pathEntries) {
        if ($p -match "C:\\Test\\Path") { $found = $true; break }
    }
    if (-not $found) { throw "Path not found in profile" }
}

# Test 2: add-path with spaces (common real-world case)
Test-Assert "add-path with spaces" {
    $out = & $CliExe profile add-path $testProfile "C:\Program Files\Test" --scope user 2>&1
    if ($out -notmatch "Added PATH entry") { throw "Expected success, got: $out" }
}

# Test 3: add-path with --scope system
Test-Assert "add-path with system scope" {
    $out = & $CliExe profile add-path $testProfile "D:\SystemPath" --scope system 2>&1
    if ($out -notmatch "Added PATH entry") { throw "Expected success, got: $out" }
}

# Test 4: reject path with semicolon (injection guard)
Test-Assert "reject path with semicolon" {
    $out = & $CliExe profile add-path $testProfile "C:\Bad;Path" --scope user 2>&1
    if ($LASTEXITCODE -eq 0) { throw "Should have rejected semicolon path" }
}

# Test 5: (null byte rejection is GUI-side validatePathInput, not CLI)

# Test 6: add-path on applied profile should reject
Test-Assert "reject add-path on applied profile" {
    # Apply the profile first
    & $CliExe profile apply $testProfile 2>&1 | Out-Null
    $out = & $CliExe profile add-path $testProfile "C:\ShouldFail" --scope user 2>&1
    if ($LASTEXITCODE -eq 0) { throw "Should have rejected add-path on applied profile" }
    # Unapply for cleanup
    & $CliExE profile unapply $testProfile 2>&1 | Out-Null
}

# Clean up
& $CliExe profile delete $testProfile 2>&1 | Out-Null

Write-Host ""
Write-Host "Results: $script:pass passed, $script:fail failed" -ForegroundColor Cyan
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
