# Legacy build wrapper - delegates to scripts/build.mjs (cross-platform build orchestrator)
# Kept for backward compatibility; new builds should use: node scripts/build.mjs --arch <arch>

$ErrorActionPreference = 'Stop'

# Find project root from this script's location
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Resolve-Path (Join-Path $scriptDir '..\..')

# Stop any running instances
Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process -Force

# Run the cross-platform build orchestrator (default arch = host arch)
$buildScript = Join-Path $projectRoot 'scripts\build.mjs'
if (Test-Path $buildScript) {
    Write-Host "[build-all] Delegating to $buildScript"
    & node $buildScript @args
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit $LASTEXITCODE)" }
} else {
    # Fallback: run the old inline build if build.mjs is absent
    Write-Host "[build-all] build.mjs not found, running legacy inline build"
    Set-Location (Join-Path $projectRoot 'frontend')
    npm ci
    npm run build
    dotnet build (Join-Path $projectRoot 'env-manager.csproj') -c Release
}

# Verify outputs
$portableExe = Join-Path $projectRoot 'release\portable\env-manager.exe'
if (-not (Test-Path $portableExe)) { throw "Portable exe missing: $portableExe" }

$cliExe = Join-Path $projectRoot 'release\portable\env-manager-cli.exe'
if (-not (Test-Path $cliExe)) { throw "CLI exe missing: $cliExe" }

Write-Host "[build-all] Build complete"
