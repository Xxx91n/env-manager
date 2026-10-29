# v2-helper-residue.ps1 - PROTOTYPE leg V2. Throwaway; answers one question and exits.
#
#   Q: when the service stops while a delegated rotate is in flight, does the provider
#      helper survive it, and does it leave a plaintext temp file behind?
#
# This is the existence proof for issue #160: the ticket claims the stop path only converges
# the direct CLI child (service/src/reconcile.rs, "Scope of the guarantee"), and that sops'
# file-in/file-out design puts plaintext on disk that only the now-dead CLI would delete.
# If the residue does NOT appear, the job-object half of Q3 is unnecessary.
#
# Two cells, because the mechanism and the identity differ:
#   Background - same stop chain (token cancel -> accept_loop join -> run_cli_child kill),
#               run as the probe user, so temp/state live where the probe can read them.
#   Service    - the real SCM stop (`sc stop`) under LocalSystem, which is where the
#               residue actually lands (C:\Windows\Temp) in an installed product.
#
# The sops binary is the prototype stub: a cipher-less stand-in that can hold still. The
# marker string it round-trips is what proves a residue file is plaintext. The stub is not
# a cipher, so the marker is also visible inside profiles.json for these cells - that is a
# stub artifact, not a finding about the real provider.
#
# Run: pwsh -File v2-helper-residue.ps1 -CliExe C:\proto\bin\app\env-manager-cli.exe `
#        -ServiceExe C:\proto\bin\app\env-manager-service.exe `
#        -StubDir C:\proto\bin\stub

param(
    [Parameter(Mandatory)][string]$CliExe,
    [Parameter(Mandatory)][string]$ServiceExe,
    [Parameter(Mandatory)][string]$StubDir,
    [ValidateSet('Both', 'Service', 'Background')][string]$Mode = 'Both',
    [int]$SecretCount = 1,
    [int]$HoldSleepMs = 20000,
    [int]$QuickSleepMs = 1500,
    [int]$StopDelaySec = 6
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

Initialize-ProtoEvidence -Leg 'v2-helper-residue'
Add-ProtoNativeType
Get-ProtoHostFacts
Write-Evidence -Key 'stub.hold_sleep_ms' -Value $HoldSleepMs -Note "must stay under the provider's own 30s WaitForExit so the CLI does not kill it first"
Write-Evidence -Key 'stub.quick_sleep_ms' -Value $QuickSleepMs
Write-Evidence -Key 'secrets_per_cell' -Value $SecretCount
Write-Evidence -Key 'scope.stub_not_a_cipher' -Value 'marker strings appear in profiles.json too; that is a stub artifact, not a provider finding'

$sopsInstallDir = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::ProgramFiles)) 'sops'
$controlFile = Join-Path $script:ProtoRoot 'stub.json'
$stubLog = Join-Path $script:ProtoRoot 'stub.log'
$bgProc = $null

function Set-StubSleep {
    param([Parameter(Mandatory)][int]$Ms)
    $json = @{ sleepMs = $Ms; log = $stubLog } | ConvertTo-Json -Compress
    [System.IO.File]::WriteAllText($controlFile, $json)
    Write-Evidence -Key 'stub.control' -Value "sleepMs=$Ms -> $controlFile"
    return (Get-Content -Raw $controlFile)
}

function Get-StubSpawnCount {
    param([Parameter(Mandatory)][int]$SinceLines)
    if (-not (Test-Path $stubLog)) { return 0 }
    return @([System.IO.File]::ReadAllLines($stubLog)).Count - $SinceLines
}

function Get-StubLogTail {
    param([int]$Lines = 30)
    if (-not (Test-Path $stubLog)) { return '(no stub log yet)' }
    return (([System.IO.File]::ReadAllLines($stubLog) | Select-Object -Last $Lines) -join ' | ')
}

function Invoke-ResidueCheck {
    param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string]$Root, [Parameter(Mandatory)][string]$Marker, [switch]$AsSystem, [switch]$Delete)
    $probe = Join-Path $PSScriptRoot 'residue-probe.ps1'
    $probeArgs = @('-Root', $Root, '-Marker', $Marker)
    if ($Delete) { $probeArgs += '-Delete' }
    if ($AsSystem) {
        $r = Invoke-ProtoAsSystemViaTask -ScriptPath $probe -Arguments $probeArgs -TimeoutSec 120
        Write-Evidence -Key "residue[$Label]" -Value $r.Out
        return $r.Out
    }
    $r = Invoke-ProtoProcess -Exe 'pwsh.exe' -Arguments (@('-NoProfile', '-File', $probe) + $probeArgs) -TimeoutSec 90
    Write-Evidence -Key "residue[$Label]" -Value $r.StdOut
    return $r.StdOut
}

function Invoke-Sample {
    param([Parameter(Mandatory)][string]$Label, [Parameter(Mandatory)][string[]]$Roots, [Parameter(Mandatory)][string]$Marker, [bool]$AsSystem)
    Write-Evidence -Key "sample[$Label]" -Value (Get-ProtoProcessSnapshot -Names @('sops.exe', 'env-manager-cli.exe', 'env-manager-service.exe')) -Note 'ppid of a helper whose parent is gone is the orphan proof'
    foreach ($root in $Roots) {
        $null = Invoke-ResidueCheck -Label "$Label root=$root" -Root $root -Marker $Marker -AsSystem:$AsSystem
    }
    return (Get-ProtoProcessSnapshot -Names @('sops.exe'))
}

function Invoke-BackgroundCell {
    Write-Host ''
    Write-Host '---------------- V2 CELL Background (probe user, IPC shutdown) ----------------'
    $state = Join-Path $script:ProtoRoot 'state-bg'
    $temp = Join-Path $script:ProtoRoot 'temp-bg'
    New-Item -ItemType Directory -Force -Path $state, $temp | Out-Null
    $envMap = @{ ENVMANAGER_LOCALAPPDATA = $state; LOCALAPPDATA = $state; TEMP = $temp; TMP = $temp }
    $marker = "PROTO-V2-BG-$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $mountPath = Join-Path $state 'EnvManager\secretMount.json'

    $seedArgs = @('-NoProfile', '-File', (Join-Path $PSScriptRoot 'seed-state.ps1'), '-CliExe', $CliExe, '-SecretCount', "$SecretCount", '-Provider', 'sops', '-MountPath', $mountPath, '-MarkerPrefix', $marker)
    $seed = Invoke-ProtoProcess -Exe 'pwsh.exe' -Arguments $seedArgs -TimeoutSec 300 -Environment $envMap
    Write-Evidence -Key 'seed.background' -Value $seed.StdOut

    $script:bgProc = Start-ProtoDetached -Exe $ServiceExe -Arguments @('--mode=background') -Environment $envMap
    $answer = Wait-ProtoPipeAnswered -Pipe 'EnvManager.Background' -TimeoutSec 30
    Write-Evidence -Key 'background.answer' -Value $answer

    # Control: rotate to completion, nothing interrupted.
    Set-StubSleep -Ms $QuickSleepMs
    $linesBefore = Get-StubSpawnCount -SinceLines 0
    $t0 = Get-Date
    $ctrl = Invoke-ProtoProcess -Exe $CliExe -Arguments @('service', 'rotate', 'proto-v2-mount') -TimeoutSec 240
    Write-Evidence -Key 'control.rotate' -Value ("exit=$($ctrl.Code) dur_ms=$([math]::Round(((Get-Date)-$t0).TotalMilliseconds)) out=$($ctrl.StdOut.Trim()) err=$($ctrl.StdErr.Trim())")
    Write-Evidence -Key 'control.helper_spawns' -Value ((Get-StubSpawnCount -SinceLines $linesBefore)) -Note "expected 4 per secret: probe+run for Decrypt, probe+run for Encrypt"
    $null = Invoke-ResidueCheck -Label 'control-after-complete' -Root $temp -Marker $marker

    # Interrupted: hold the helper, start a rotate, then stop the service mid-rotation.
    Set-StubSleep -Ms $HoldSleepMs
    $job = Start-Job -ScriptBlock {
        param($cli, $id)
        & $cli service rotate $id 2>&1 | Out-String
    } -ArgumentList @($CliExe, 'proto-v2-mount')
    Start-Sleep -Seconds $StopDelaySec
    Write-Evidence -Key 'interrupt.helper_during' -Value (Get-ProtoProcessSnapshot -Names @('sops.exe', 'env-manager-cli.exe'))
    $tShut = Get-Date
    $shut = Invoke-ProtoProcess -Exe $CliExe -Arguments @('service', 'shutdown') -TimeoutSec 30
    Write-Evidence -Key 'interrupt.shutdown_ipc' -Value ("exit=$($shut.Code) dur_ms=$(((Get-Date)-$tShut).TotalMilliseconds) out=$($shut.StdOut.Trim())")
    $null = Invoke-Sample -Label 'interrupt-bg-+0s' -Roots @($temp) -Marker $marker -AsSystem $false
    Start-Sleep -Seconds 3
    $null = Invoke-Sample -Label 'interrupt-bg-+3s' -Roots @($temp) -Marker $marker -AsSystem $false
    Start-Sleep -Seconds 5
    $null = Invoke-Sample -Label 'interrupt-bg-+8s' -Roots @($temp) -Marker $marker -AsSystem $false
    Start-Sleep -Seconds 14
    $null = Invoke-Sample -Label 'interrupt-bg-+22s' -Roots @($temp) -Marker $marker -AsSystem $false
    $rotateOut = (Receive-Job -Job $job -Wait -ErrorAction SilentlyContinue) -join ' '
    Write-Evidence -Key 'interrupt.bg_rotate_client_saw' -Value $rotateOut
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    Write-Evidence -Key 'interrupt.bg_service_log' -Value (Get-ProtoServiceLogTail -Dir (Join-Path $state 'EnvManager') -Label 'bg' -Lines 200)
    if ($script:bgProc -and -not $script:bgProc.HasExited) { try { $null = $script:bgProc.Kill($true) } catch { } }
    $script:bgProc = $null
    $null = Invoke-ResidueCheck -Label 'cleanup-bg' -Root $temp -Marker $marker -Delete
    return $marker
}

function Invoke-ServiceCell {
    Write-Host ''
    Write-Host '---------------- V2 CELL Service (LocalSystem, real sc stop) ----------------'
    $marker = "PROTO-V2-SVC-$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $mountPath = 'C:\ProgramData\EnvManager\secretMount.json'
    # A LocalSystem process gets %Windir%\SystemTemp from Path.GetTempPath() on current
    # Windows, not %Windir%\Temp - and the seed run recorded exactly that. Both roots are
    # sampled because which one holds the plaintext decides who can read it afterwards.
    $winTemp = 'C:\Windows\Temp'
    $systemTemp = 'C:\Windows\SystemTemp'
    New-Item -ItemType Directory -Force -Path 'C:\ProgramData\EnvManager' | Out-Null

    # Seed as LocalSystem so profiles.json / secret-providers.json land where the service's
    # own CLI child resolves them, with no machine-wide environment changes.
    $seedArgs = @('-CliExe', $CliExe, '-SecretCount', "$SecretCount", '-Provider', 'sops', '-MountPath', $mountPath, '-MarkerPrefix', $marker)
    $seed = Invoke-ProtoAsSystemViaTask -ScriptPath (Join-Path $PSScriptRoot 'seed-state.ps1') -Arguments $seedArgs -TimeoutSec 420
    Write-Evidence -Key 'seed.service_system_task' -Value $seed.Out
    Write-Evidence -Key 'seed.stray_cli' -Value (Get-ProtoProcessSnapshot -Names @('env-manager-cli.exe')) -Note 'any CLI left from seeding must be gone before the rotate cells'
    if ($seed.Out -match 'SEED profiles=MISSING') {
        Write-Evidence -Key 'seed.warning' -Value 'the SYSTEM seed found no profiles.json in the LocalSystem state dir; the rotate child will see zero secrets'
    }

    Remove-ProtoService
    $null = New-ProtoService -BinPath $ServiceExe -ServiceArgs '--mode=service'
    $state = Start-ProtoService
    if ($state -ne 'Running') {
        Write-Evidence -Key 'service.cell.aborted' -Value "state=$state log=$(Get-ProtoServiceLogTail -Dir 'C:\ProgramData\EnvManager' -Label 'svc-abort')"
        Remove-ProtoService
        return $marker
    }
    $null = Wait-ProtoPipeAnswered -Pipe 'EnvManager.Service' -TimeoutSec 30

    # Control: a full rotate under LocalSystem, nothing interrupted.
    Set-StubSleep -Ms $QuickSleepMs
    $linesBefore = Get-StubSpawnCount -SinceLines 0
    $t0 = Get-Date
    $ctrl = Invoke-ProtoProcess -Exe $CliExe -Arguments @('service', 'rotate', 'proto-v2-mount', '--service-pipe') -TimeoutSec 240
    Write-Evidence -Key 'service.control.rotate' -Value ("exit=$($ctrl.Code) dur_ms=$([math]::Round(((Get-Date)-$t0).TotalMilliseconds)) out=$($ctrl.StdOut.Trim()) err=$($ctrl.StdErr.Trim())")
    Write-Evidence -Key 'service.control.helper_spawns' -Value (Get-StubSpawnCount -SinceLines $linesBefore)
    $null = Invoke-ResidueCheck -Label 'service.control.after-complete' -Root $winTemp -Marker $marker -AsSystem
    $null = Invoke-ResidueCheck -Label 'service.control.after-complete' -Root $systemTemp -Marker $marker -AsSystem

    # Interrupted: hold the helper, start the rotate, sc stop inside the hold window.
    Set-StubSleep -Ms $HoldSleepMs
    $job = Start-Job -ScriptBlock {
        param($cli, $id)
        & $cli service rotate $id --service-pipe 2>&1 | Out-String
    } -ArgumentList @($CliExe, 'proto-v2-mount')
    Start-Sleep -Seconds $StopDelaySec
    Write-Evidence -Key 'service.interrupt.before_stop' -Value (Get-ProtoProcessSnapshot -Names @('sops.exe', 'env-manager-cli.exe', 'env-manager-service.exe'))
    $stop = Stop-ProtoService
    Write-Evidence -Key 'service.interrupt.sc_stop' -Value ($stop | ConvertTo-Json -Compress)
    $null = Invoke-Sample -Label 'service.interrupt-+0s' -Roots @($winTemp, $systemTemp) -Marker $marker -AsSystem $true
    Start-Sleep -Seconds 3
    $null = Invoke-Sample -Label 'service.interrupt-+3s' -Roots @($winTemp, $systemTemp) -Marker $marker -AsSystem $true
    Start-Sleep -Seconds 5
    $null = Invoke-Sample -Label 'service.interrupt-+8s' -Roots @($winTemp, $systemTemp) -Marker $marker -AsSystem $true
    Start-Sleep -Seconds 14
    $null = Invoke-Sample -Label 'service.interrupt-+22s' -Roots @($winTemp, $systemTemp) -Marker $marker -AsSystem $true
    $rotateOut = (Receive-Job -Job $job -Wait -ErrorAction SilentlyContinue) -join ' '
    Write-Evidence -Key 'service.interrupt.rotate_client_saw' -Value $rotateOut
    Remove-Job -Job $job -Force -ErrorAction SilentlyContinue
    Write-Evidence -Key 'service.interrupt.log' -Value (Get-ProtoServiceLogTail -Dir 'C:\ProgramData\EnvManager' -Label 'svc-interrupt' -Lines 250)
    Write-Evidence -Key 'service.interrupt.stub_log_tail' -Value (Get-StubLogTail -Lines 20)
    Remove-ProtoService
    $null = Invoke-ResidueCheck -Label 'service.cleanup' -Root $winTemp -Marker $marker -AsSystem -Delete
    $null = Invoke-ResidueCheck -Label 'service.cleanup' -Root $systemTemp -Marker $marker -AsSystem -Delete
    return $marker
}

try {
    # Install the stub where SopsProvider.FindSopsBinary looks without any environment:
    # %ProgramFiles%\sops\sops.exe, discovered identically by the seeding identity and by
    # the service's LocalSystem CLI child.
    if (-not (Test-Path (Join-Path $StubDir 'sops-stub.exe'))) { throw "stub publish dir missing: $StubDir" }
    New-Item -ItemType Directory -Force -Path $sopsInstallDir | Out-Null
    Copy-Item -Path (Join-Path $StubDir '*') -Destination $sopsInstallDir -Recurse -Force
    Rename-Item -Path (Join-Path $sopsInstallDir 'sops-stub.exe') -NewName 'sops.exe' -Force
    Write-Evidence -Key 'stub.installed' -Value (Join-Path $sopsInstallDir 'sops.exe') -Note 'SopsProvider discovers it via the ProgramFiles common path, so no env var crosses a session boundary'
    $probe = Invoke-ProtoProcess -Exe (Join-Path $sopsInstallDir 'sops.exe') -Arguments @('--version') -TimeoutSec 30
    Write-Evidence -Key 'stub.version_probe' -Value ("exit=$($probe.Code) out=$($probe.StdOut.Trim())")

    if ($Mode -in @('Both', 'Background')) { $null = Invoke-BackgroundCell }
    if ($Mode -in @('Both', 'Service')) { $null = Invoke-ServiceCell }

    Write-ProtoVerdictBlock -Text @'
V2 observations (no decision attached; the question is binary: residue / no residue):
  - interrupt.helper_during shows the helper alive inside the hold window.
  - service.interrupt.sc_stop records when SERVICE_STOPPED landed; every
    sample[<label>] after it is the answer to "does the helper outlive the stop".
  - residue[service.interrupt-+Ns] shows whether
    C:\Windows\Temp\env-manager-sops-<guid>\secret.dec.json survives, and
    contains_marker=True means those bytes are the plaintext the provider decrypted.
  - control.rotate / control.helper_spawns give the per-secret helper process count,
    which is what V3 turns into a duration model.
  - the Background cell is the same stop chain minus the SCM, run where the probe can
    read the files; the Service cell is the installed-product shape.
'@
} catch {
    Write-ProtoFatal $_
} finally {
    if ($script:bgProc -and -not $script:bgProc.HasExited) { try { $null = $script:bgProc.Kill($true) } catch { } }
    Remove-ProtoService
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'sops.exe') -TimeoutSec 30
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'env-manager-cli.exe') -TimeoutSec 30
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'env-manager-service.exe') -TimeoutSec 30
    $null = Invoke-ResidueCheck -Label 'final-cleanup-wintemp' -Root 'C:\Windows\Temp' -Marker '' -AsSystem -Delete
    $null = Invoke-ResidueCheck -Label 'final-cleanup-systemtemp' -Root 'C:\Windows\SystemTemp' -Marker '' -AsSystem -Delete
    $null = Invoke-ResidueCheck -Label 'final-cleanup-prototemp' -Root (Join-Path $script:ProtoRoot 'temp-bg') -Marker '' -Delete
    try { Remove-Item -LiteralPath 'C:\ProgramData\EnvManager' -Recurse -Force -ErrorAction Stop } catch { Write-Evidence -Key 'cleanup.programdata' -Value $_.Exception.Message }
    if (Test-Path $sopsInstallDir) { try { Remove-Item $sopsInstallDir -Recurse -Force } catch { Write-Evidence -Key 'cleanup.stub_dir' -Value "FAILED $($_.Exception.Message)" } }
    Write-Evidence -Key 'cleanup.done' -Value 'service, helper install, temp residue and stray processes removed'
}
