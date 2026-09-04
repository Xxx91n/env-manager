# Env Manager - integration-test residue self-check (read-only).
#
# Lists integration-test residue left behind by the harness (architecture-recovery
# issue 22):
#   - registry values whose names start with the harness prefix (default EM_TEST_)
#     under HKCU\Environment and, when readable, the HKLM system environment key
#   - profiles with harness-prefix names in the Env Manager profiles store
#
# This script NEVER mutates anything; it only enumerates. Removal is a deliberate
# user-side operation - see docs/build-and-release.md ("Test residue hygiene").
#
# Exit code: 0 = no residue found, 1 = residue found.

[CmdletBinding()]
param(
  [string]$Prefix = "EM_TEST_"
)

$ErrorActionPreference = "Stop"

$UserEnvSubKey = "Environment"
$SystemEnvSubKey = "SYSTEM\CurrentControlSet\Control\Session Manager\Environment"
$found = @()

function Scan-EnvHive([Microsoft.Win32.RegistryHive]$Hive, [string]$SubKey, [string]$Label) {
  try {
    $base = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Default)
    try {
      $key = $base.OpenSubKey($SubKey, $false)
      if ($null -eq $key) { return }
      try {
        foreach ($name in @($key.GetValueNames() | Sort-Object)) {
          if ($name -like "$Prefix*") {
            $script:found += [pscustomobject]@{
              Area = "registry"
              Location = "$Label\$SubKey"
              Name = $name
              Detail = $key.GetValueKind($name).ToString()
            }
          }
        }
      } finally {
        $key.Dispose()
      }
    } finally {
      $base.Dispose()
    }
  } catch {
    Write-Warning "[check-test-residue] $Label scan skipped ($($_.Exception.GetType().Name))"
  }
}

Scan-EnvHive ([Microsoft.Win32.RegistryHive]::CurrentUser) $UserEnvSubKey "HKCU"
Scan-EnvHive ([Microsoft.Win32.RegistryHive]::LocalMachine) $SystemEnvSubKey "HKLM"

# Profiles with harness-prefix names (user-state store; read-only parse).
# The CLI stores profiles as a top-level JSON array of objects with a "name" field.
$profilesPath = Join-Path $env:LOCALAPPDATA "EnvManager\profiles.json"
if (Test-Path $profilesPath) {
  try {
    $store = Get-Content -LiteralPath $profilesPath -Raw | ConvertFrom-Json
    foreach ($profile in @($store)) {
      $name = [string]$profile.name
      if ($name -and ($name -like "$Prefix*")) {
        $found += [pscustomobject]@{
          Area = "profile"
          Location = $profilesPath
          Name = $name
          Detail = "profile entry"
        }
      }
    }
  } catch {
    Write-Warning "[check-test-residue] profiles store scan skipped ($($_.Exception.GetType().Name))"
  }
}

if ($found.Count -eq 0) {
  Write-Host "[check-test-residue] OK: no '$Prefix*' residue found." -ForegroundColor Green
  exit 0
}

Write-Host "[check-test-residue] $($found.Count) residue item(s) found (prefix '$Prefix'):" -ForegroundColor Yellow
$found | Format-Table -AutoSize | Out-String | ForEach-Object { Write-Host $_ }
Write-Host "Removal is a deliberate user-side operation; see docs/build-and-release.md (Test residue hygiene)." -ForegroundColor Yellow
exit 1
