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
    # One line per invocation; the ':exit' lines are the same process closing, so counting
    # every line would double each helper start.
    $log = Join-Path $script:ProtoRoot 'stub.log'
    if (-not (Test-Path $log)) { return 0 }
    return @([System.IO.File]::ReadAllLines($log) | Where-Object { $_ -notmatch ':exit' }).Count
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
    # The provider stays sops (an invented provider name is refused by `secret-provider
    # set`); only the stub's hold changes, and the CSV note column carries that.
    Set-StubSleep -Ms $SopsHoldSleepMs
    foreach ($n in @(1, 5, 20)) {
        $null = Invoke-RotateCell -Provider 'sops' -Secrets $n -StateSub 'stubhold' -Note "cli-direct-held${SopsHoldSleepMs}ms"
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
        # Asset names differ across sops releases (and the repo's own L1 provisioner notes a
        # 3.13 windows asset does not exist), so read the release manifest instead of
        # guessing filenames.
        $gotSops = $false
        # The asset name came out of round three's manifest listing (sops-v3.13.3.amd64.exe),
        # so try the direct download first: the unauthenticated API endpoint is rate-limited
        # from the shared runner IP and answered 403 in round four.
        try {
            $u = 'https://github.com/getsops/sops/releases/download/v3.13.3/sops-v3.13.3.amd64.exe'
            Invoke-WebRequest -Uri $u -OutFile (Join-Path $realDir 'sops.exe') -TimeoutSec 180 -ErrorAction Stop
            $gotSops = $true
            Write-Evidence -Key 'real_sops.download' -Value "direct ok size=$((Get-Item (Join-Path $realDir 'sops.exe')).Length)"
        } catch {
            Write-Evidence -Key 'real_sops.download' -Value "direct FAILED $($_.Exception.Message)"
        }
        foreach ($tag in @('latest', 'v3.13.3')) {
            if ($gotSops) { break }
            try {
                $headers = @{ 'User-Agent' = 'proto-p3-observations'; 'Accept' = 'application/vnd.github+json' }
                if ($env:GH_TOKEN) { $headers['Authorization'] = "Bearer $env:GH_TOKEN" }
                $rel = Invoke-RestMethod -Uri ("https://api.github.com/repos/getsops/sops/releases/" + $tag) -Headers $headers -TimeoutSec 60
                # sops names its Windows binary "sops-v<ver>.amd64.exe" - the asset name
                # carries no "windows" token, which is why the repo's L1 provisioner
                # concludes there is no Windows asset. Match the extension, not the OS word.
                $exe = @($rel.assets | Where-Object { $_.name -match '\.exe$' -and $_.name -notmatch 'spdx|sbom|sig' })
                $amd = @($exe | Where-Object { $_.name -match 'amd64|x64' })
                $pick = if ($amd.Count -gt 0) { $amd[0] } elseif ($exe.Count -gt 0) { $exe[0] } else { $null }
                if (-not $pick) { Write-Evidence -Key 'real_sops.manifest' -Value "tag=$tag no exe asset (assets=$(($rel.assets.name) -join ','))"; continue }
                Invoke-WebRequest -Uri $pick.browser_download_url -OutFile (Join-Path $realDir 'sops.exe') -TimeoutSec 180 -ErrorAction Stop
                $gotSops = $true
                Write-Evidence -Key 'real_sops.download' -Value "tag=$tag asset=$($pick.name) size=$($pick.size) ok"
                break
            } catch {
                Write-Evidence -Key 'real_sops.download' -Value "tag=$tag FAILED $($_.Exception.Message)"
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
                    # Put the stub back so the service-pipe cell that follows is comparable
                    # with the stub rows rather than silently measuring the real binary.
                    $target = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops'
                    Copy-Item -Path (Join-Path $StubDir 'sops-stub.exe') -Destination (Join-Path $target 'sops.exe') -Force
                    Write-Evidence -Key 'real_sops.restored_stub' -Value (Join-Path $target 'sops.exe')
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
} catch {
    Write-ProtoFatal $_
} finally {
    if ($script:bgProc -and -not $script:bgProc.HasExited) { try { $null = $script:bgProc.Kill($true) } catch { } }
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'env-manager-service.exe') -TimeoutSec 30
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'sops.exe') -TimeoutSec 30
    try { Remove-Item -LiteralPath (Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops') -Recurse -Force -ErrorAction Stop } catch { Write-Evidence -Key 'cleanup.sops_dir' -Value $_.Exception.Message }
    try { Remove-Item -LiteralPath (Join-Path $script:ProtoRoot 'v3') -Recurse -Force -ErrorAction SilentlyContinue } catch { }
    Write-Evidence -Key 'cleanup.done' -Value 'stub install and v3 state removed'
}
