# scripts/test-launch-env.ps1
# Test tool: verify that a launch profile injects the correct variables and
# secrets into a child process environment. Uses a probe executable that
# dumps its own environment variables to stdout, then compares against
# `profile preview` output.
#
# Usage:
#   pwsh -NoProfile -File scripts/test-launch-env.ps1 -Profile <name>
#
# What it does:
# 1. Run `env-manager-cli profile preview <name>` → get expected variables
# 2. For each variable in preview, check if it is in SecretVariables → reveal-secret
# 3. Create a temporary .bat probe that echoes all env vars to a temp file
# 4. Run `env-manager-cli profile launch <name>` on the probe (sets target to .bat)
# 5. Read the probe output, compare expected vs actual
# 6. Report mismatches, missing variables, or decryption failures
#
# This is a READ-ONLY test — it does NOT modify the registry or profiles.json.
# It creates a temporary launch target that self-terminates.

param(
  [Parameter(Mandatory=$true)]
  [string]$Profile,

  [string]$CliExe = "bin\Release\net10.0-windows\env-manager-cli.exe"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$cli = Join-Path $projectRoot $CliExe
if (-not (Test-Path $cli)) {
  # Try Debug build
  $debugCli = Join-Path $projectRoot "bin\Debug\net10.0-windows\env-manager-cli.exe"
  if (Test-Path $debugCli) { $cli = $debugCli }
  else { Write-Host "ERROR: CLI not found at $cli" -ForegroundColor Red; exit 1 }
}

Write-Host "`n=== Launch Environment Inspector ===" -ForegroundColor Cyan
Write-Host "Profile: $Profile"
Write-Host "CLI: $cli`n"

# Step 1: Get profile preview
$previewJson = & $cli profile preview $Profile 2>&1
if ($LASTEXITCODE -ne 0) {
  Write-Host "ERROR: profile preview failed: $previewJson" -ForegroundColor Red
  exit 1
}
$preview = $previewJson | ConvertFrom-Json

Write-Host "--- Expected Variables (from preview) ---" -ForegroundColor Yellow
$expectedVars = @{}
foreach ($v in $preview.variables) {
  $expectedVars[$v.name] = $v.value
  $secret = $preview.profile.SecretVariables -contains $v.name
  if ($secret) {
    Write-Host "  $($v.name) = <secret> (decrypt needed)"
  } else {
    $valPreview = $v.value
    if ($valPreview.Length -gt 60) { $valPreview = $valPreview.Substring(0,60) + "..." }
    Write-Host "  $($v.name) = $valPreview"
  }
}

Write-Host "`n--- PATH Entries ---" -ForegroundColor Yellow
foreach ($p in $preview.pathEntries) {
  $status = if ($p.exists) { "OK" } else { "MISSING" }
  Write-Host "  [$status] $($p.path)"
}

# Step 2: Reveal secrets (if any)
$revealedSecrets = @{}
$secretNames = @()
# Check if preview has SecretVariables field
if ($preview.profile.SecretVariables) {
  $secretNames = $preview.profile.SecretVariables
}
# Also check via profile show
$showJson = & $cli profile show $Profile 2>&1
if ($LASTEXITCODE -eq 0) {
  $show = $showJson | ConvertFrom-Json
  if ($show.SecretVariables) { $secretNames = $show.SecretVariables }
}

if ($secretNames.Count -gt 0) {
  Write-Host "`n--- Secret Decryption Test ---" -ForegroundColor Yellow
  foreach ($secretName in $secretNames) {
    try {
      $plaintext = & $cli profile reveal-secret $Profile $secretName 2>&1
      if ($LASTEXITCODE -eq 0) {
        $revealedSecrets[$secretName] = $plaintext
        $masked = if ($plaintext.Length -gt 8) { $plaintext.Substring(0,4) + "****" + $plaintext.Substring($plaintext.Length-4) } else { "****" }
        Write-Host "  [OK] $secretName = $masked" -ForegroundColor Green
      } else {
        Write-Host "  [FAIL] $secretName: $plaintext" -ForegroundColor Red
      }
    } catch {
      Write-Host "  [FAIL] $secretName: $_" -ForegroundColor Red
    }
  }
} else {
  Write-Host "`n--- No secrets in this profile ---" -ForegroundColor DarkGray
}

# Step 3: Create a probe batch file that dumps env vars
$tempDir = [System.IO.Path]::GetTempPath()
$probeId = "em-test-" + [Guid]::NewGuid().ToString("N").Substring(0,8)
$probeBat = Join-Path $tempDir "$probeId.bat"
$probeOut = Join-Path $tempDir "$probeId.out"

# The probe writes NAME=VALUE lines for all env vars
@"
@echo off
set > "$probeOut"
"@ | Set-Content -Path $probeBat -Encoding ASCII

# Step 4: Temporarily set the profile target to the probe, launch, then restore
# Save original target
$originalTarget = $show.targetExecutable
$originalCwd = $show.workingDirectory

Write-Host "`n--- Launch Probe Test ---" -ForegroundColor Yellow
Write-Host "Probe: $probeBat"

# Set the probe as launch target temporarily
& $cli profile set-launch $Profile --target $probeBat 2>&1 | Out-Null

try {
  # Launch the profile (this will inject vars and run the .bat)
  $launchResult = & $cli profile launch $Profile 2>&1
  $launchExit = $LASTEXITCODE

  if ($launchExit -ne 0) {
    Write-Host "  [FAIL] profile launch exited with code $launchExit" -ForegroundColor Red
    Write-Host "  Output: $launchResult"
  } else {
    Write-Host "  [OK] profile launch succeeded" -ForegroundColor Green

    # Wait a moment for the .bat to finish writing
    Start-Sleep -Milliseconds 500

    if (Test-Path $probeOut) {
      $actualVars = @{}
      Get-Content $probeOut | ForEach-Object {
        $idx = $_.IndexOf('=')
        if ($idx -gt 0) {
          $actualVars[$_.Substring(0,$idx)] = $_.Substring($idx+1)
        }
      }

      Write-Host "`n--- Variable Verification ---" -ForegroundColor Yellow
      $allMatch = $true
      foreach ($name in $expectedVars.Keys) {
        $expected = $expectedVars[$name]
        if ($revealedSecrets.ContainsKey($name)) {
          $expected = $revealedSecrets[$name]
        }
        if ($actualVars.ContainsKey($name)) {
          $actual = $actualVars[$name]
          if ($actual -eq $expected) {
            $valPreview = if ($expected.Length -gt 40) { $expected.Substring(0,20) + "..." } else { $expected }
            Write-Host "  [MATCH] $name = $valPreview" -ForegroundColor Green
          } else {
            Write-Host "  [MISMATCH] $name" -ForegroundColor Red
            Write-Host "    Expected: $expected" -ForegroundColor DarkYellow
            Write-Host "    Actual:   $actual" -ForegroundColor DarkRed
            $allMatch = $false
          }
        } else {
          Write-Host "  [MISSING] $name (not in child env)" -ForegroundColor Red
          $allMatch = $false
        }
      }

      Write-Host ""
      if ($allMatch) {
        Write-Host "=== ALL VARIABLES MATCH ===" -ForegroundColor Green
      } else {
        Write-Host "=== MISMATCHES DETECTED ===" -ForegroundColor Red
      }
    } else {
      Write-Host "  [FAIL] Probe output file not found: $probeOut" -ForegroundColor Red
    }
  }
} finally {
  # Restore original target
  if ($originalTarget) {
    & $cli profile set-launch $Profile --target $originalTarget 2>&1 | Out-Null
  }
  # Clean up probe files
  Remove-Item $probeBat -ErrorAction SilentlyContinue
  Remove-Item $probeOut -ErrorAction SilentlyContinue
}
