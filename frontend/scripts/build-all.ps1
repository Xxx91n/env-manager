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

$CliDir = Join-Path $ProjectRoot "bin\Release\net10.0"
$CliExe = Join-Path $CliDir "env-manager-cli.exe"
if (-not (Test-Path $CliExe)) { throw "CLI exe not found: $CliExe" }

Write-Host "[build] Step 2: Build Tauri GUI (release)" -ForegroundColor Cyan
if (-not $SkipGui) {
  Push-Location "$ProjectRoot\frontend"
  npm run tauri-build
  if ($LASTEXITCODE -ne 0) { throw "GUI build failed" }
  Pop-Location
}

# Locate the GUI exe across possible target triples
$TargetBase = Join-Path $ProjectRoot "frontend\src-tauri\target"
$GuiExe = $null
$TargetTriple = $null
foreach ($triple in @("x86_64-pc-windows-gnu", "x86_64-pc-windows-msvc")) {
  $candidate = Join-Path $TargetBase "$triple\release\env-manager.exe"
  if (Test-Path $candidate) {
    $GuiExe = $candidate
    $TargetTriple = $triple
    break
  }
}
if (-not $GuiExe) { throw "GUI exe not found under $TargetBase" }
Write-Host "[build] Found GUI exe: $GuiExe" -ForegroundColor Green

Write-Host "[build] Step 3: Assemble portable package" -ForegroundColor Cyan
# Copy GUI exe
Copy-Item $GuiExe -Destination $PortableDir -Force
# Copy all CLI runtime files (exe + dlls + json) alongside GUI
Get-ChildItem $CliDir | Where-Object { $_.Extension -in '.exe', '.dll', '.json' } | ForEach-Object {
  Copy-Item $_.FullName -Destination $PortableDir -Force
}
# Copy WebView2 runtime loader if Tauri bundled it alongside
$WebViewLoader = Join-Path (Split-Path -Parent $GuiExe) "WebView2Loader.dll"
if (Test-Path $WebViewLoader) {
  Copy-Item $WebViewLoader -Destination $PortableDir -Force
}

Write-Host "[build] Step 4: Collect MSI installer" -ForegroundColor Cyan
$MsiSearchDir = Join-Path (Split-Path -Parent $GuiExe) "bundle\msi"
if (Test-Path $MsiSearchDir) {
  $msiFiles = Get-ChildItem -Path $MsiSearchDir -Filter "*.msi"
  foreach ($msi in $msiFiles) {
    Copy-Item $msi.FullName -Destination $MsiDir -Force
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
