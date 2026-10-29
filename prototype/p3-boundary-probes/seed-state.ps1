# seed-state.ps1 - PROTOTYPE helper: build the CLI-side state a delegated rotate needs,
# as whatever identity the caller runs it under (admin for the Background cell, LocalSystem
# for the Service cell). Running it as that identity is the point: profiles.json and
# secret-providers.json land exactly where the service's CLI child will look for them,
# which removes machine-wide environment changes from the experiment.
#
# Self-contained: dot-sources nothing, because it runs under a different account.

# The launch target only has to satisfy the CLI's own profile rules; a target under
# \Windows\System32 is refused outright ("rejected to prevent system32 hijacking"), so the
# probe uses the stub binary staged under C:\proto. Space-free, because Run-Cli joins argv.
param(
    [Parameter(Mandatory)][string]$CliExe,
    [string]$ProfileName = 'proto-v2',
    [int]$SecretCount = 1,
    [string]$Provider = 'sops',
    [string]$MountPath = '',
    [string]$MountId = 'proto-v2-mount',
    [string]$MarkerPrefix = 'PROTO-ROTATE-MARKER',
    [string]$LaunchTarget = 'C:\proto\bin\stub\sops-stub.exe'
)

$ErrorActionPreference = 'Continue'
if ($LaunchTarget -eq $CliExe -or -not (Test-Path $LaunchTarget)) {
    # Fall back to the CLI binary itself if the stub is not staged; a launch profile only
    # needs a target path that passes the create-time rules, and nothing launches it here.
    $LaunchTarget = $CliExe
}

# The exit code is the ONLY thing this returns: writing the log line to the success stream
# too made every "$rc = Run-Cli ..." a two-element array, so "-ne 0" filtered instead of
# comparing and the seed reported failure after a successful CLI call.
function Run-Cli([string[]]$a) {
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = $CliExe
    $psi.Arguments = ($a -join ' ')
    $psi.UseShellExecute = $false
    $psi.RedirectStandardOutput = $true
    $psi.RedirectStandardError = $true
    $psi.CreateNoWindow = $true
    $p = [System.Diagnostics.Process]::Start($psi)
    $o = $p.StandardOutput.ReadToEnd()
    $e = $p.StandardError.ReadToEnd()
    $p.WaitForExit()
    [Console]::Out.WriteLine("CLI argv=[" + ($a -join ' ') + "] exit=" + $p.ExitCode + " out=" + ($o -replace '\s+', ' ').Trim() + " err=" + ($e -replace '\s+', ' ').Trim())
    return $p.ExitCode
}

$identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$stateDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'EnvManager'
Write-Output "SEED identity=$identity"
Write-Output "SEED state_dir=$stateDir"
Write-Output "SEED ENVMANAGER_LOCALAPPDATA=$env:ENVMANAGER_LOCALAPPDATA"
Write-Output "SEED temp=$([System.IO.Path]::GetTempPath())"
Write-Output "SEED sops_probe_path=$env:SOPS_PATH"
$sopsCandidates = @(
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) 'Programs\sops\sops.exe'),
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops\sops.exe'),
    (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFilesX86)) 'sops\sops.exe')
)
$sopsStates = ($sopsCandidates | ForEach-Object { "$_=" + (Test-Path $_) }) -join ' '
Write-Output "SEED sops_common_paths=$sopsStates"

# Start from a clean profile name so repeated cells do not accumulate secrets.
$null = Run-Cli @('profile', 'delete', $ProfileName)

$rc = Run-Cli @('profile', 'create', $ProfileName, '--type', 'launch', '--target', $LaunchTarget)
if ($rc -ne 0) { Write-Output 'SEED FAILED=profile-create'; exit 1 }

$rc = Run-Cli @('profile', 'secret-provider', 'set', $Provider)
if ($rc -ne 0) { Write-Output 'SEED FAILED=provider-set'; exit 1 }

$guid = [guid]::NewGuid().ToString('N').Substring(0, 8)
for ($i = 1; $i -le $SecretCount; $i++) {
    $value = "$MarkerPrefix-$guid-$i"
    $null = Run-Cli @('profile', 'add-secret', $ProfileName, "PROTO_SECRET_$i", $value)
}
$null = Run-Cli @('profile', 'secret-provider', 'list')

if ($MountPath) {
    $dir = Split-Path $MountPath -Parent
    if (-not (Test-Path $dir)) { $null = New-Item -ItemType Directory -Force -Path $dir }
    $mount = @(
        [ordered]@{
            id                     = $MountId
            provider               = $Provider
            name                   = 'PROTO_SECRET_1'
            scope                  = 'user'
            refreshPolicy          = 'CreatedOnly'
            createdAt              = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ss.0000000Z')
            schemaVersion          = 2
            profileName            = $ProfileName
        }
    )
    ($mount | ConvertTo-Json -Depth 5) | Set-Content -Path $MountPath -Encoding utf8
    Write-Output "SEED mount_file=$MountPath exists=$(Test-Path $MountPath)"
}

$profilesFile = Join-Path $stateDir 'profiles.json'
if (Test-Path $profilesFile) {
    $len = (Get-Item $profilesFile).Length
    Write-Output "SEED profiles=$profilesFile bytes=$len"
    $txt = Get-Content -Raw -Path $profilesFile
    $secretCount = ([regex]::Matches($txt, 'PROTO_SECRET_')).Count
    Write-Output "SEED profile_secret_occurrences=$secretCount"
    Write-Output "SEED profiles_contains_marker=$($txt -match [regex]::Escape($MarkerPrefix))"
} else {
    Write-Output "SEED profiles=MISSING at $profilesFile"
}
$provFile = Join-Path $stateDir 'secret-providers.json'
if (Test-Path $provFile) { Write-Output "SEED provider_config=$(Get-Content -Raw $provFile)" }
Write-Output 'SEED done'
