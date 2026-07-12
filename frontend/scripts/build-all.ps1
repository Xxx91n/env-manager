# Env Manager - Consolidated Build Script
# Produces release/portable/ and release/msi/ at project root.
param(
  [switch]$SkipCli,
  [switch]$SkipGui
)

$ErrorActionPreference = "Stop"
# scripts/ -> frontend/ -> env-manager/
$ProjectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ReleaseDir = Join-Path $ProjectRoot "release"
$PortableDir = Join-Path $ReleaseDir "portable"
$MsiDir = Join-Path $ReleaseDir "msi"

# Clean previous output
if (Test-Path $ReleaseDir) {
  Remove-Item $ReleaseDir -Recurse -Force
}
New-Item -ItemType Directory -Path $PortableDir -Force | Out-Null
New-Item -ItemType Directory -Path $MsiDir -Force | Out-Null

Write-Host "[build] Step 1: Build C# CLI" -ForegroundColor Cyan
if (-not $SkipCli) {
  Push-Location $ProjectRoot
  dotnet build -c Release
  if ($LASTEXITCODE -ne 0) { throw "CLI build failed" }
  Pop-Location
}

# Auto-detect TFM output directory (net10.0, net10.0-windows, etc.)
$ReleaseBase = Join-Path $ProjectRoot "bin\Release"
$CliDir = $null
Get-ChildItem -Path $ReleaseBase -Directory -ErrorAction SilentlyContinue | ForEach-Object {
  if (Test-Path (Join-Path $_.FullName "env-manager-cli.dll")) {
    $CliDir = $_.FullName
  }
}
if (-not $CliDir) { throw "CLI output directory not found under $ReleaseBase" }

$CliExe = Join-Path $CliDir "env-manager-cli.exe"
if (-not (Test-Path $CliExe)) { throw "CLI exe not found: $CliExe" }

Write-Host "[build] Step 2: Build Tauri GUI (release)" -ForegroundColor Cyan
if (-not $SkipGui) {
  Push-Location "$ProjectRoot\frontend"
  npm run tauri-build
  if ($LASTEXITCODE -ne 0) { throw "GUI build failed" }
  Pop-Location
}

# Locate the GUI exe. Tauri outputs to:
#   - target/release/env-manager.exe               (host default, no --target)
#   - target/<triple>/release/env-manager.exe      (when --target is specified)
# We auto-detect instead of hardcoding a triple so the script works on any
# machine (GNU, MSVC, or other) and in CI/CD.
$TargetBase = Join-Path $ProjectRoot "frontend\src-tauri\target"
$GuiExe = $null

# 1. Check host-default location first (no --target flag used)
$HostDefault = Join-Path $TargetBase "release\env-manager.exe"
if (Test-Path $HostDefault) {
  $GuiExe = $HostDefault
  Write-Host "[build] Found GUI exe (host default): $GuiExe" -ForegroundColor Green
}

# 2. Scan all triple-named subdirectories
if (-not $GuiExe) {
  $releaseDirs = Get-ChildItem -Path $TargetBase -Directory -ErrorAction SilentlyContinue
  foreach ($dir in $releaseDirs) {
    $candidate = Join-Path $dir.FullName "release\env-manager.exe"
    if (Test-Path $candidate) {
      $GuiExe = $candidate
      Write-Host "[build] Found GUI exe ($($dir.Name)): $GuiExe" -ForegroundColor Green
      break
    }
  }
}

if (-not $GuiExe) { throw "GUI exe not found under $TargetBase" }

Write-Host "[build] Step 3: Assemble portable package" -ForegroundColor Cyan
# Copy GUI exe
Copy-Item $GuiExe -Destination $PortableDir -Force
# Copy all CLI runtime files (exe + dlls + json) alongside GUI
Get-ChildItem $CliDir | Where-Object { $_.Extension -in '.exe', '.dll', '.json' } | ForEach-Object {
  Copy-Item $_.FullName -Destination $PortableDir -Force
}
# Copy AGENTS.cli.md alongside CLI for agent distribution
  $AgentsMd = Join-Path $ProjectRoot "AGENTS.cli.md"
  if (Test-Path $AgentsMd) {
    Copy-Item $AgentsMd -Destination $PortableDir -Force
    Write-Host "[build] AGENTS.cli.md copied to portable"
  }

# Copy WebView2 runtime loader if Tauri placed it alongside the GUI exe
$WebViewLoader = Join-Path (Split-Path -Parent $GuiExe) "WebView2Loader.dll"
if (Test-Path $WebViewLoader) {
  Copy-Item $WebViewLoader -Destination $PortableDir -Force
}

Write-Host "[build] Step 4: Collect MSI installer" -ForegroundColor Cyan
$MsiSearchDir = Join-Path (Split-Path -Parent $GuiExe) "bundle\msi"
if (Test-Path $MsiSearchDir) {
  $msiFiles = Get-ChildItem -Path $MsiSearchDir -Filter "*.msi"
  foreach ($msi in $msiFiles) {
    # Strip the _en-US language suffix (e.g. "Env Manager_0.4.0_x64_en-US.msi" -> "Env Manager_0.4.0_x64.msi")
    $cleanName = $msi.Name -replace '_\w{2}-\w{2}\.', '.'
    $destPath = Join-Path $MsiDir $cleanName
    Copy-Item $msi.FullName -Destination $destPath -Force
  }
  Write-Host "[build] MSI copied: $($msiFiles.Count) file(s)" -ForegroundColor Green
} else {
  Write-Host "[build] WARNING: No MSI bundle directory found at $MsiSearchDir" -ForegroundColor Yellow
}

Write-Host ""
Write-Host "[build] Done. Output:" -ForegroundColor Green
Write-Host "  Portable: $PortableDir"
Get-ChildItem $PortableDir | ForEach-Object { Write-Host "    $($_.Name)" }
Write-Host "  MSI:      $MsiDir"
Get-ChildItem $MsiDir | ForEach-Object { Write-Host "    $($_.Name)" }
