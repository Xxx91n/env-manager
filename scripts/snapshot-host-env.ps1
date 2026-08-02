<#
.SYNOPSIS
  Per-session forensic snapshot of the host machine's environment registry
  hives and Env Manager's internal config files.

.DESCRIPTION
  Exports HKCU\Environment and HKLM\SYSTEM\CurrentControlSet\Control\Session
  Manager\Environment to <repo-root>\.env_bak\<UTC-timestamp>\ as .reg files,
  and copies Env Manager's internal config files from %LOCALAPPDATA%\EnvManager
  into .env_bak\<UTC-timestamp>\internal-configs\. Intended to be run ONCE at
  the start of any local dev session that touches the CLI or build, as a
  forensic safety net. NOT auto-cleaned. Keep the last few snapshots manually.

.PARAMETER KeepLast
  Optional. Number of most-recent snapshots to keep. Older ones are pruned.
  Defaults to 10. Set to 0 to disable pruning.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\snapshot-host-env.ps1
  powershell -NoProfile -ExecutionPolicy Bypass -File scripts\snapshot-host-env.ps1 -KeepLast 5
#>
[CmdletBinding()]
param(
  [int]$KeepLast = 10
)

$ErrorActionPreference = 'Stop'

# Resolve repo root from script location (scripts/ is one level under repo root).
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$RepoRoot = Split-Path -Parent $ScriptDir
$BackupRoot = Join-Path $RepoRoot '.env_bak'

$Timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmssZ')
$SnapshotDir = Join-Path $BackupRoot $Timestamp
New-Item -ItemType Directory -Force -Path $SnapshotDir | Out-Null
$InternalDir = Join-Path $SnapshotDir 'internal-configs'
New-Item -ItemType Directory -Force -Path $InternalDir | Out-Null

function Export-Hive {
  param([string]$Key, [string]$OutFile)
  try {
    # reg.exe export produces UTF-16LE .reg files; that is fine for restore.
    & reg.exe export $Key $OutFile /y 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0 -and (Test-Path $OutFile)) {
      Write-Host "[ok] exported $Key -> $OutFile"
      return $true
    } else {
      Write-Warning "[warn] failed to export $Key (exit $LASTEXITCODE). HKLM may require admin; continuing."
      return $false
    }
  } catch {
    Write-Warning "[warn] exception exporting ${Key}: $($_.Exception.Message)"
    return $false
  }
}

function Copy-InternalConfig {
  param([string]$Name)
  $Src = Join-Path $env:LOCALAPPDATA "EnvManager\$Name"
  if (Test-Path $Src -PathType Leaf) {
    Copy-Item -LiteralPath $Src -Destination (Join-Path $InternalDir $Name) -Force
    Write-Host "[ok] copied internal config $Name"
  } else {
    Write-Host "[skip] internal config $Name not present"
  }
}

Write-Host '=== Env Manager host environment snapshot ==='
Write-Host "Snapshot dir: $SnapshotDir"

# HKCU (no admin needed)
$null = Export-Hive -Key 'HKCU\Environment' -OutFile (Join-Path $SnapshotDir 'HKCU-Environment.reg')

# HKLM (may fail without admin - non-fatal)
$null = Export-Hive -Key 'HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment' `
  -OutFile (Join-Path $SnapshotDir 'HKLM-Environment.reg')

# Internal configs
Write-Host '--- internal configs ---'
Copy-InternalConfig 'profiles.json'
Copy-InternalConfig 'secretMount.json'
Copy-InternalConfig 'audit.json'
Copy-InternalConfig 'protected-vars.json'
Copy-InternalConfig 'protected-paths.json'
Copy-InternalConfig 'builtin-protected-vars.json'
Copy-InternalConfig 'builtin-protected-paths.json'

# Prune old snapshots
if ($KeepLast -gt 0) {
  $Snapshots = Get-ChildItem $BackupRoot -Directory | Sort-Object Name -Descending
  if ($Snapshots.Count -gt $KeepLast) {
    $Snapshots | Select-Object -Skip $KeepLast | ForEach-Object {
      Write-Host "[prune] removing old snapshot $($_.Name)"
      Remove-Item -LiteralPath $_.FullName -Recurse -Force
    }
  }
}

Write-Host '=== snapshot complete ==='
Write-Host "To restore: reg import $SnapshotDir\HKCU-Environment.reg  (and HKLM if present, requires admin)"
