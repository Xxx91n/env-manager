# scripts/migrate-audit-to-ledger.ps1
# v1.0.0 Phase E: One-shot migration from audit.json to audit-ledger.jsonl.
# Reads existing audit.json entries, converts them to ledger events with hash-chain,
# writes to audit-ledger.jsonl. After migration, audit.json is renamed to audit.json.bak.
# See ADR 0001 A10/A11.
#
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File scripts/migrate-audit-to-ledger.ps1

$ErrorActionPreference = 'Stop'

$localApp = $env:LOCALAPPDATA
if (-not $localApp) { $localApp = [System.IO.Path]::GetTempPath() }
$envManagerDir = Join-Path $localApp 'EnvManager'
$auditJsonPath = Join-Path $envManagerDir 'audit.json'
$ledgerPath = Join-Path $envManagerDir 'audit-ledger.jsonl'

if (-not (Test-Path $auditJsonPath)) {
    Write-Host "No audit.json found. Nothing to migrate."
    exit 0
}

# Don't re-migrate if ledger already exists and is non-empty.
if ((Test-Path $ledgerPath) -and (Get-Item $ledgerPath).Length -gt 0) {
    Write-Host "audit-ledger.jsonl already exists and is non-empty. Skipping migration."
    exit 0
}

Add-Type -AssemblyName 'System.Security.Cryptography'

$entries = Get-Content $auditJsonPath -Raw | ConvertFrom-Json
if (-not $entries -or $entries.Count -eq 0) {
    Write-Host "audit.json is empty. Nothing to migrate."
    exit 0
}

Write-Host "Migrating $($entries.Count) audit entries to audit-ledger.jsonl..."

$prevHash = '0' * 64  # genesis hash
$lines = @()

foreach ($entry in $entries) {
    $eventId = [Guid]::NewGuid().ToString()
    $timestamp = if ($entry.timestamp) { $entry.timestamp } else { (Get-Date -Format 'o') }
    $command = if ($entry.command) { $entry.command } else { 'unknown' }
    $scope = if ($entry.scope) { $entry.scope } else { $null }

    $eventForHash = @{
        id = $eventId
        timestamp = $timestamp
        actor = 'CLI'  # all existing audit entries were CLI-originated
        action = $command
        provider = $null
        mountId = $null
        profileName = if ($entry.profileName) { $entry.profileName } else { $null }
        reason = $null
        prevHash = $prevHash
    } | ConvertTo-Json -Compress

    $hashInput = $prevHash + $eventForHash
    $sha256 = [SHA256]::Create()
    $hashBytes = $sha256.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($hashInput))
    $currentHash = [BitConverter]::ToString($hashBytes) -replace '-', '' | ForEach-Object { $_.ToLower() } | Join-Object ''

    $ledgerEvent = @{
        id = $eventId
        timestamp = $timestamp
        actor = 'CLI'
        provider = $null
        mountId = $null
        profileName = if ($entry.profileName) { $entry.profileName } else { $null }
        action = $command
        reason = $null
        prevHash = $prevHash
        hash = $currentHash
        ledgerSchemaVersion = 1
    } | ConvertTo-Json -Compress

    $lines += $ledgerEvent
    $prevHash = $currentHash
}

# Write ledger file (atomically: write to tmp, rename).
$tmpPath = "$ledgerPath.migrate.tmp"
$lines | Out-File -FilePath $tmpPath -Encoding utf8
Move-Item -Path $tmpPath -Destination $ledgerPath -Force

# Rename audit.json to audit.json.bak (read-only after migration).
$bakPath = "$auditJsonPath.bak"
if (Test-Path $bakPath) { Remove-Item $bakPath -Force }
Rename-Item -Path $auditJsonPath -Destination $bakPath

Write-Host "Migration complete: $($lines.Count) entries written to $ledgerPath"
Write-Host "Original audit.json renamed to audit.json.bak (read-only archive)"
Write-Host "Verify: run 'env-manager-cli audit list' to see migrated entries"
