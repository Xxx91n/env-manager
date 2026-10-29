# v3-rotate-timing.ps1 - PROTOTYPE leg V3. Throwaway; produces a duration distribution.
#
#   Q: how long does one real rotate actually take, as a function of secret count and
#      provider type?
#
# #161 has no fixed timeout for the delegated CLI rotate child, and 120s is currently a
# guess ("C# provider 30s ceiling x serial mounts"). This leg measures the shape of the
# cost so the constant is a reading, not a guess: a fixed per-invocation floor (process
# start + config read) plus a per-secret marginal that is dominated by how many helper
# processes a provider needs per secret.
#
# Everything here runs the CLI directly, as the probe user, with a redirected state dir,
# so no service and no machine-wide state is involved. One cell at the end goes through the
# service pipe to show the end-to-end the client actually waits for.
#
# Run: pwsh -File v3-rotate-timing.ps1 -CliExe C:\proto\bin\app\env-manager-cli.exe `
#        -ServiceExe C:\proto\bin\app\env-manager-service.exe -StubDir C:\proto\bin\stub

param(
    [Parameter(Mandatory)][string]$CliExe,
    [Parameter(Mandatory)][string]$ServiceExe,
    [Parameter(Mandatory)][string]$StubDir,
    [int[]]$SecretCounts = @(0, 1, 5, 20, 50),
    [string[]]$Providers = @('dpapi-current-user', 'credential-manager', 'sops'),
    [int]$SopsHoldSleepMs = 1200,
    [int]$ServiceCellSecrets = 20,
    [switch]$SkipRealSops
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

Initialize-ProtoEvidence -Leg 'v3-rotate-timing'
Add-ProtoNativeType
Get-ProtoHostFacts
Write-Evidence -Key 'v3.secret_counts' -Value ($SecretCounts -join ',')
Write-Evidence -Key 'v3.providers' -Value ($Providers -join ',')
Write-Evidence -Key 'v3.client_budgets' -Value 'C# provider WaitForExit(30000) per helper call; cli_gateway read timeout 30s; reconcile tick 300s; no timeout on the rotate child'

$csvPath = Join-Path $script:ProtoEvidenceDir 'v3-rotate-timing.csv'
'provider,secrets,path,durMs,helperSpawns,failures,note' | Set-Content -Path $csvPath -Encoding utf8

function Add-Row {
    param([string]$Provider, [int]$Secrets, [string]$Path, [int]$DurMs, [int]$Spawns, [string]$Failures, [string]$Note)
    '{0},{1},{2},{3},{4},{5},"{6}"' -f $Provider, $Secrets, $Path, $DurMs, $Spawns, $Failures, ($Note -replace '"', "'") | Add-Content -Path $csvPath -Encoding utf8
}

function Get-StubLineCount {
    $log = Join-Path $script:ProtoRoot 'stub.log'
    if (-not (Test-Path $log)) { return 0 }
    return @([System.IO.File]::ReadAllLines($log)).Count
}

function Set-StubSleep {
    param([Parameter(Mandatory)][int]$Ms)
    $json = @{ sleepMs = $Ms; log = (Join-Path $script:ProtoRoot 'stub.log') } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText((Join-Path $script:ProtoRoot 'stub.json'), $json)
}

function Install-StubSops {
    param([Parameter(Mandatory)][string]$Dir)
    $target = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops'
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    Copy-Item -Path (Join-Path $Dir '*') -Destination $target -Recurse -Force
    $exe = Join-Path $target 'sops-stub.exe'
    if (Test-Path $exe) { Rename-Item -Path $exe -NewName 'sops.exe' -Force }
    Write-Evidence -Key 'stub.installed' -Value (Join-Path $target 'sops.exe')
    return (Join-Path $target 'sops.exe')
}

function Invoke-RotateCell {
    param([Parameter(Mandatory)][string]$Provider, [Parameter(Mandatory)][int]$Secrets, [string]$StateSub, [string]$Note = 'cli-direct')
    $state = Join-Path (Join-Path $script:ProtoRoot 'v3') ($StateSub + '-p' + $Provider + '-n' + $Secrets)
    New-Item -ItemType Directory -Force -Path $state | Out-Null
    $envMap = @{ ENVMANAGER_LOCALAPPDATA = $state; LOCALAPPDATA = $state }
    $seedArgs = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'seed-state.ps1'), '-CliExe', $CliExe, '-ProfileName', 'proto-v3', '-SecretCount', "$Secrets", '-Provider', $Provider, '-MarkerPrefix', 'PROTO-V3')
    $seed = Invoke-ProtoProcess -Exe 'pwsh.exe' -Arguments $seedArgs -TimeoutSec 600 -Environment $envMap
    if ($seed.StdOut -match 'SEED FAILED=(\S+)') {
        Write-Evidence -Key "seed.fail[$Provider/$Secrets]" -Value $Matches[1]
        return
    }
    $before = Get-StubLineCount
    # The exact argv the service delegates to: ROTATE_ARGS in service/src/reconcile.rs.
    $r = Invoke-ProtoProcess -Exe $CliExe -Arguments @('profile', 'secret-provider', 'rotate') -TimeoutSec 900 -Environment $envMap
    $spawns = (Get-StubLineCount) - $before
    $fail = if ($r.StdOut -match 'failed=(\d+)') { $Matches[1] } elseif ($r.Code -ne 0) { "exit$($r.Code)" } else { '0' }
    Write-Evidence -Key "rotate[$Provider secrets=$Secrets]" -Value ("dur_ms=$($r.DurMs) exit=$($r.Code) helper_log_lines=$spawns out=$($r.StdOut.Trim() -replace '\s+',' ')")
    Add-Row -Provider $Provider -Secrets $Secrets -Path $Note -DurMs $r.DurMs -Spawns $spawns -Failures $fail -Note $r.StdOut.Trim()
    return [pscustomobject]@{ DurMs = $r.DurMs; Spawns = $spawns; Out = $r.StdOut }
}

$sopsStubExe = $null
$bgProc = $null
try {
    $sopsStubExe = Install-StubSops -Dir $StubDir
    Set-StubSleep -Ms 0

    # --- stub-sops floor: what one rotate costs when the helper does no work at all ---
    Set-StubSleep -Ms 0
    foreach ($n in $SecretCounts) {
        $null = Invoke-RotateCell -Provider 'sops' -Secrets $n -StateSub 'stub0'
    }

    # --- stub-sops with a held helper: the cost of a provider that is slow per call ---
    Set-StubSleep -Ms $SopsHoldSleepMs
    foreach ($n in @(1, 5, 20)) {
        $null = Invoke-RotateCell -Provider "sops-held$SopsHoldSleepMs" -Secrets $n -StateSub 'stubhold'
    }
    Set-StubSleep -Ms 0

    # --- the two providers that need no external binary at all ---
    foreach ($p in @('dpapi-current-user', 'credential-manager')) {
        if ($Providers -notcontains $p) { continue }
        foreach ($n in $SecretCounts) {
            $null = Invoke-RotateCell -Provider $p -Secrets $n -StateSub 'native'
        }
    }

    # --- optional: real sops + age, if the assets can be fetched on this runner ---
    if (-not $SkipRealSops) {
        $realDir = Join-Path $script:ProtoRoot 'real-sops'
        New-Item -ItemType Directory -Force -Path $realDir | Out-Null
        $sopsCandidates = @(
            'https://github.com/getsops/sops/releases/download/v3.9.0/sops-v3.9.0.windows.amd64.exe',
            'https://github.com/getsops/sops/releases/download/v3.9.0/sops.v3.9.0.windows.amd64.exe'
        )
        $gotSops = $false
        foreach ($u in $sopsCandidates) {
            try {
                Invoke-WebRequest -Uri $u -OutFile (Join-Path $realDir 'sops.exe') -TimeoutSec 120 -ErrorAction Stop
                $gotSops = $true
                Write-Evidence -Key 'real_sops.download' -Value "$u ok"
                break
            } catch {
                Write-Evidence -Key 'real_sops.download' -Value "$u FAILED $($_.Exception.Message)"
            }
        }
        $gotAge = $false
        try {
            $zip = Join-Path $realDir 'age.zip'
            Invoke-WebRequest -Uri 'https://github.com/FiloSottile/age/releases/download/v1.2.0/age-v1.2.0-windows-amd64.zip' -OutFile $zip -TimeoutSec 120
            Expand-Archive -Path $zip -DestinationPath $realDir -Force
            $ageExe = @(Get-ChildItem -Path $realDir -Filter 'age.exe' -Recurse)[0]
            if ($ageExe) {
                $keyFile = Join-Path $realDir 'age-key.txt'
                $null = & $ageExe.FullName --generate-key 2>&1 | Out-File -FilePath $keyFile -Encoding ascii
                $gotAge = Test-Path $keyFile
            }
            Write-Evidence -Key 'real_sops.age_keygen' -Value "ok=$gotAge"
        } catch {
            Write-Evidence -Key 'real_sops.age_keygen' -Value "FAILED $($_.Exception.Message)"
        }
        if ($gotSops -and $gotAge) {
            $pub = $null
            try {
                $keyContent = Get-Content -Raw (Join-Path $realDir 'age-key.txt')
                $ageExe = @(Get-ChildItem -Path $realDir -Filter 'age.exe' -Recurse)[0]
                $pub = ((& $ageExe.FullName -p (Join-Path $realDir 'age-key.txt')) | Select-Object -First 1)
            } catch { Write-Evidence -Key 'real_sops.pubkey' -Value "FAILED $($_.Exception.Message)" }
            if ($pub -and $pub -match '^age1') {
                $target = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops'
                Copy-Item -Path (Join-Path $realDir 'sops.exe') -Destination (Join-Path $target 'sops.exe') -Force
                Write-Evidence -Key 'real_sops.installed' -Value (Join-Path $target 'sops.exe')
                $state = Join-Path (Join-Path $script:ProtoRoot 'v3') 'realsops-p-sops-n5'
                New-Item -ItemType Directory -Force -Path $state | Out-Null
                $envMap = @{
                    ENVMANAGER_LOCALAPPDATA = $state
                    LOCALAPPDATA            = $state
                    SOPS_PATH               = (Join-Path $target 'sops.exe')
                    SOPS_AGE_KEY_FILE       = (Join-Path $realDir 'age-key.txt')
                    SOPS_AGE_RECIPIENTS     = $pub
                }
                $seedArgs = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'seed-state.ps1'), '-CliExe', $CliExe, '-ProfileName', 'proto-v3-real', '-SecretCount', '5', '-Provider', 'sops', '-MarkerPrefix', 'PROTO-V3-REAL')
                $seed = Invoke-ProtoProcess -Exe 'pwsh.exe' -Arguments $seedArgs -TimeoutSec 600 -Environment $envMap
                Write-Evidence -Key 'real_sops.seed' -Value ($seed.StdOut -replace '\s+', ' ')
                if ($seed.StdOut -notmatch 'SEED FAILED') {
                    $r = Invoke-ProtoProcess -Exe $CliExe -Arguments @('profile', 'secret-provider', 'rotate') -TimeoutSec 900 -Environment $envMap
                    Write-Evidence -Key 'real_sops.rotate' -Value ("dur_ms=$($r.DurMs) exit=$($r.Code) out=$($r.StdOut.Trim() -replace '\s+',' ') err=$($r.StdErr.Trim() -replace '\s+',' ')")
                    Add-Row -Provider 'sops(real)' -Secrets 5 -Path 'cli-direct' -DurMs $r.DurMs -Spawns 0 -Failures 'n/a' -Note $r.StdOut.Trim()
                }
            } else {
                Write-Evidence -Key 'real_sops' -Value 'skipped: no usable age recipient'
            }
        }
    }

    # --- end-to-end through the service pipe, the shape the client waits on ---
    Write-Host ''
    Write-Host '---------------- V3 CELL: rotate through the service pipe ----------------'
    $state = Join-Path (Join-Path $script:ProtoRoot 'v3') 'svc-pipe'
    New-Item -ItemType Directory -Force -Path $state | Out-Null
    $envMap = @{ ENVMANAGER_LOCALAPPDATA = $state; LOCALAPPDATA = $state }
    $seedArgs = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'seed-state.ps1'), '-CliExe', $CliExe, '-ProfileName', 'proto-v3-svc', '-SecretCount', "$ServiceCellSecrets", '-Provider', 'sops', '-MountPath', (Join-Path $state 'EnvManager\secretMount.json'), '-MarkerPrefix', 'PROTO-V3-SVC')
    $seed = Invoke-ProtoProcess -Exe 'pwsh.exe' -Arguments $seedArgs -TimeoutSec 600 -Environment $envMap
    Write-Evidence -Key 'svc_pipe.seed' -Value ($seed.StdOut -replace '\s+', ' ')
    $script:bgProc = Start-ProtoDetached -Exe $ServiceExe -Arguments @('--mode=background') -Environment $envMap
    $null = Wait-ProtoPipeAnswered -Pipe 'EnvManager.Background' -TimeoutSec 30
    Set-StubSleep -Ms $SopsHoldSleepMs
    $t0 = Get-Date
    $ipc = Invoke-ProtoProcess -Exe $CliExe -Arguments @('service', 'rotate', 'proto-v2-mount') -TimeoutSec 600 -Environment $envMap
    $svcDurMs = [int]$ipc.DurMs
    Write-Evidence -Key 'svc_pipe.rotate' -Value ("dur_ms=$svcDurMs exit=$($ipc.Code) out=$($ipc.StdOut.Trim() -replace '\s+',' ') err=$($ipc.StdErr.Trim() -replace '\s+',' ')")
    Add-Row -Provider 'sops-held' -Secrets $ServiceCellSecrets -Path 'service-pipe' -DurMs $svcDurMs -Spawns 0 -Failures 'n/a' -Note $ipc.StdOut.Trim()
    Set-StubSleep -Ms 0
    Write-Evidence -Key 'svc_pipe.service_log' -Value (Get-ProtoServiceLogTail -Dir (Join-Path $state 'EnvManager') -Label 'v3-svc' -Lines 200)
    if ($script:bgProc -and -not $script:bgProc.HasExited) { try { $null = $script:bgProc.Kill($true) } catch { } }

    Write-Evidence -Key 'csv' -Value $csvPath
    Write-ProtoVerdictBlock -Text @"
V3 observations (a range, not a pass/fail):
  - stub0 rows are the floor: one CLI process plus config I/O, and 4 helper log lines per
    secret (SopsProvider probes --version before EVERY Encrypt and Decrypt).
  - stubheld rows add ${SopsHoldSleepMs}ms per helper call, i.e. 4 x ${SopsHoldSleepMs}ms per secret.
  - dpapi/credential-manager rows are the no-external-binary providers.
  - service-pipe row is what a rotate caller waits for end to end; compare it with the
    client budgets recorded above to see where a timeout has to land.
"@
} finally {
    if ($script:bgProc -and -not $script:bgProc.HasExited) { try { $null = $script:bgProc.Kill($true) } catch { } }
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'env-manager-service.exe') -TimeoutSec 30
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'sops.exe') -TimeoutSec 30
    try { Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops') -Recurse -Force -ErrorAction Stop } catch { Write-Evidence -Key 'cleanup.sops_dir' -Value $_.Exception.Message }
    try { Remove-Item -LiteralPath (Join-Path $script:ProtoRoot 'v3') -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    Write-Evidence -Key 'cleanup.done' -Value 'stub install and v3 state removed'
}
