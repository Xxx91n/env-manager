# v1-pipe-dacl.ps1 - PROTOTYPE leg V1. Throwaway; answers one question and exits.
#
#   Q: what is the ACTUAL DACL on \\.\pipe\EnvManager.Service when the service runs as
#      LocalSystem, and if a DACL is applied (post-creation or at creation) do the two
#      shipped client shapes still connect while a non-privileged identity is refused?
#
# Nothing here decides between "apply the DACL" and "delete the helper" (that is issue
# #154's call, and the user's). It only produces the evidence the AC asks for:
#   - the SDDL of the live pipe, from outside and from inside the service,
#   - a connect matrix over identities and over the three SD-application policies,
#   - the raw Win32 result of the post-creation SetNamedSecurityInfoW route.
#
# Run: pwsh -File v1-pipe-dacl.ps1 -CliExe C:\proto\bin\cli\env-manager-cli.exe `
#                         -ServiceExe C:\proto\bin\service\env-manager-service.exe

param(
    [Parameter(Mandatory)][string]$CliExe,
    [Parameter(Mandatory)][string]$ServiceExe,
    [string]$PipeName = 'EnvManager.Service',
    [string]$BackgroundPipeName = 'EnvManager.Background',
    [int]$KeepEvidenceOnly = 0
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib.ps1')

Initialize-ProtoEvidence -Leg 'v1-pipe-dacl'
Add-ProtoNativeType
Get-ProtoHostFacts
Set-ProtoPrivileges
Write-Evidence -Key 'target.cliexe' -Value $CliExe
Write-Evidence -Key 'target.serviceexe' -Value $ServiceExe
Write-Evidence -Key 'target.declared_sddl' -Value $script:ProtoSddlConst -Note 'the SDDL service/src/pipe_dacl.rs declares and never applies'

$low = $null
$bgProc = $null

function Invoke-ConnectMatrix {
    param([Parameter(Mandatory)][string]$Label, [pscustomobject]$Low, [string]$Pipe = $PipeName)
    $rows = [ordered]@{}

    # Client 1: the CLI gateway with --service-pipe (src/ServiceCommand.cs switches the
    # pipe name only on that flag; the default is the Background pipe).
    $cli = Invoke-ProtoProcess -Exe $CliExe -Arguments @('service', 'ping', '--service-pipe') -TimeoutSec 60
    Write-Evidence -Key "connect.cli_admin[$Label]" -Value ("exit=$($cli.Code) dur=$($cli.DurMs)ms out=$($cli.StdOut.Trim()) err=$($cli.StdErr.Trim())")
    $rows['cli_admin'] = "exit=$($cli.Code) $($cli.StdOut.Trim())"

    # Client 2: the GUI watchdog shape - frontend/src-tauri/src/main.rs opens the pipe
    # with std::fs and writes {"method":"ping"}; no CLI in between. Run it against the
    # Service-mode pipe because that is what the AC asks about.
    $watch = Connect-ProtoPipeClient -Pipe $Pipe -Request '{"method":"ping"}' -ConnectTimeoutMs 5000
    Write-Evidence -Key "connect.watchdog_admin[$Label]" -Value ("connected=$($watch.Connected) resp=$($watch.Response) err=$($watch.Error)")
    $rows['watchdog_admin'] = "connected=$($watch.Connected) $($watch.Response)$($watch.Error)"

    # Raw CreateFile with the duplex access mask, the pure access-check outcome.
    $rows['open_admin'] = Test-ProtoPipeOpen -Pipe $Pipe -Label "$Label admin"
    $rows['open_admin_readonly'] = Test-ProtoPipeOpen -Pipe $Pipe -Label "$Label admin-ro" -ReadOnly

    # Non-privileged interactive logon (Start-Process -Credential -> token carries the
    # Interactive logon SID).
    if (-not $Low) {
        Write-Evidence -Key "connect.low[$Label]" -Value 'SKIPPED: no secondary identity available'
        return $rows
    }
    try {
        $i = Invoke-ProtoAsUserInteractive -As $Low -Exe $CliExe -Arguments @('service', 'ping', '--service-pipe') -TimeoutSec 90
        Write-Evidence -Key "connect.cli_low_interactive[$Label]" -Value $i.Out
        $rows['cli_low_interactive'] = ($i.Out -replace '\s+', ' ')
    } catch {
        Write-Evidence -Key "connect.cli_low_interactive[$Label]" -Value ("FAILED to launch: " + $_.Exception.Message)
        $rows['cli_low_interactive'] = 'launch-failed'
    }

    # Same CLI as the same user through Task Scheduler (S4U -> service/network logon SID,
    # NOT the Interactive one). A default DACL that keys on IU admits one and refuses the
    # other, which is why both are recorded.
    foreach ($lt in @('S4U', 'Password')) {
        try {
            $t = Invoke-ProtoAsUserViaTask -As $Low -Exe $CliExe -Arguments @('service', 'ping', '--service-pipe') -LogonType $lt -TimeoutSec 120
            Write-Evidence -Key "connect.cli_low_task_$lt[$Label]" -Value ("taskresult=$($t.LastResult) $($t.Out)")
            $rows["cli_low_task_$lt"] = ($t.Out -replace '\s+', ' ')
        } catch {
            Write-Evidence -Key "connect.cli_low_task_$lt[$Label]" -Value ("FAILED: " + $_.Exception.Message)
            $rows["cli_low_task_$lt"] = 'task-failed'
        }
    }

    # And the watchdog shape (raw open, no CLI process) as the low user, so a refusal is
    # attributed to the access check rather than to the CLI's own error handling.
    $probeScript = Join-Path $PSScriptRoot 'pipe-client-probe.ps1'
    foreach ($lt in @('S4U')) {
        try {
            $t = Invoke-ProtoAsUserViaTask -As $Low -Exe $probeScript -Arguments @('-Pipe', $Pipe, '-Label', "$Label-low-$lt") -LogonType $lt -TimeoutSec 120
            Write-Evidence -Key "connect.probe_low_task_$lt[$Label]" -Value ("taskresult=$($t.LastResult) $($t.Out)")
            $rows["probe_low_task_$lt"] = ($t.Out -replace '\s+', ' ')
        } catch {
            Write-Evidence -Key "connect.probe_low_task_$lt[$Label]" -Value ("FAILED: " + $_.Exception.Message)
        }
    }
    return $rows
}

function Invoke-ServiceCell {
    param([Parameter(Mandatory)][string]$Label, [string]$ExtraServiceArgs = '', [switch]$ApplyExternalAfterStart, [string]$ExternalSddl = $script:ProtoSddlConst)
    Write-Host ''
    Write-Host "---------------- CELL $Label (serviceArgs='--mode=service $ExtraServiceArgs') ----------------"
    Remove-ProtoService
    $svcArgs = '--mode=service'
    if ($ExtraServiceArgs) { $svcArgs = $svcArgs + ' ' + $ExtraServiceArgs }
    $null = New-ProtoService -BinPath $ServiceExe -ServiceArgs $svcArgs
    $state = Start-ProtoService
    if ($state -ne 'Running') {
        Write-Evidence -Key "cell.$Label" -Value "service never reached Running (state=$state)"
        Write-Evidence -Key "cell.$Label.log" -Value (Get-ProtoServiceLogTail -Dir 'C:\ProgramData\EnvManager' -Label $Label)
        Remove-ProtoService
        return
    }
    $answer = Wait-ProtoPipeAnswered -Pipe $PipeName -TimeoutSec 25
    Write-Evidence -Key "cell.$Label.pipe_answer" -Value $answer

    # (1) What is on the pipe right now?
    Get-ProtoPipeSddl -Pipe $PipeName -Label "$Label initial" | Out-Null

    # (2) The post-creation route, from OUTSIDE the service process: exactly what
    #     service/src/pipe_dacl.rs would do, measured as a Win32 result.
    if ($ApplyExternalAfterStart) {
        $applied = Set-ProtoPipeDacl -Pipe $PipeName -Sddl $ExternalSddl -Label "$Label external"
        Write-Evidence -Key "apply.result[$Label]" -Value ($applied | ConvertTo-Json -Compress)
        Get-ProtoPipeSddl -Pipe $PipeName -Label "$Label after-external-apply" | Out-Null
        $null = Invoke-ConnectMatrix -Label "$Label after-external-apply" -Low $low
        # The accept loop creates a FRESH instance per connection, so the instance that
        # served the connect above is gone. Re-read: does the replacement still carry the
        # DACL that was applied to its predecessor?
        Get-ProtoPipeSddl -Pipe $PipeName -Label "$Label next-instance-after-apply" | Out-Null
    }

    # (3) Connect matrix for this policy.
    $null = Invoke-ConnectMatrix -Label "$Label" -Low $low

    # (4) Anti-squatting: a second --mode=service process must not take the name over.
    #     It is not SCM-launched, so main.rs falls through to the same run_server body.
    $squatter = Invoke-ProtoProcess -Exe $ServiceExe -Arguments @('--mode=service') -TimeoutSec 40
    Write-Evidence -Key "antisquat.second_process[$Label]" -Value ("exit=$($squatter.Code) err=$($squatter.StdErr.Trim()) out=$($squatter.StdOut.Trim())") -Note 'PIPE_FIRST_PIPE_INSTANCE must refuse the takeover'

    Write-Evidence -Key "cell.$Label.log_tail" -Value (Get-ProtoServiceLogTail -Dir 'C:\ProgramData\EnvManager' -Label $Label -Lines 300)
    $stop = Stop-ProtoService
    Write-Evidence -Key "cell.$Label.stop" -Value ($stop | ConvertTo-Json -Compress) -Note 'pipe must be gone after stop'
    Start-Sleep -Seconds 1
    Write-Evidence -Key "cell.$Label.after_stop_open" -Value ([ProtoWin]::TryOpen("\\.\pipe\$PipeName", 0x120000))
    Remove-ProtoService
}

try {
    # A secondary identity is what makes the "non-privileged refused" half of the matrix,
    # but losing it must not cost the SDDL evidence: creation is best-effort and the matrix
    # records a skip instead of aborting the leg.
    try {
        $low = New-ProtoLocalUser
        foreach ($d in @('C:\proto', 'C:\proto\bin', 'C:\proto\bin\app', (Get-ProtoScratchDir 'out'))) {
            Grant-ProtoDirAccess -Path $d -User $low.Name
        }
    } catch {
        Write-Evidence -Key 'lowuser.failed' -Value $_.Exception.Message
        $low = $null
    }

    # Control: the Background pipe, created by the same binary in the same session as the
    # probe process. Its SDDL is the reference point for "what the token default DACL
    # produces for a non-LocalSystem creator".
    Write-Host ''
    Write-Host '---------------- CONTROL: Background-mode pipe ----------------'
    $bgProc = Start-Process -FilePath $ServiceExe -ArgumentList '--mode=background' -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 3
    $null = Wait-ProtoPipeAnswered -Pipe $BackgroundPipeName -TimeoutSec 20
    Get-ProtoPipeSddl -Pipe $BackgroundPipeName -Label 'background-control' | Out-Null
    $null = Test-ProtoPipeOpen -Pipe $BackgroundPipeName -Label 'background-control admin'

    # Cell A: untouched main-branch behaviour - no security attributes passed at all.
    Invoke-ServiceCell -Label 'A-default'

    # Cell B: service applies the declared SDDL after each create_instance (the wiring
    # issue #154 describes as option 1's post-creation variant). Env is read once per
    # process, so the flag is carried in ImagePath instead of a machine env var.
    Invoke-ServiceCell -Label 'B-service-post-apply' -ExtraServiceArgs '--proto-pipe-sd=post'

    # Cell C: SD handed to CreateNamedPipeW at creation time
    # (ServerOptions::create_with_security_attributes_raw).
    Invoke-ServiceCell -Label 'C-service-creation-sd' -ExtraServiceArgs '--proto-pipe-sd=creation'

    # Cell D: default service + a DACL applied from outside while it runs, which is the
    # only way to change the live pipe without recompiling the service. Repeats A with the
    # apply step and the fresh-instance re-read.
    Invoke-ServiceCell -Label 'D-default+external-apply' -ApplyExternalAfterStart

    Write-ProtoVerdictBlock -Text @'
V1 observations (no decision attached):
  - sddl.dacl[A-default initial] is the live DACL of a LocalSystem-created pipe with no
    security attributes passed. Read it against host.whoami_groups and the logon SID types.
  - apply.external reports the Win32 result of the post-creation route, and
    [D ... next-instance-after-apply] shows whether the DACL survives the instance the
    accept loop creates after the first connection.
  - B vs C compare "service applies after create" with "SD passed at create" under the
    same client matrix; antisquat.second_process records whether the take-over refusal
    still holds with an SD in play.
  - open[...low...] cells are the access-check outcome per identity/logon type;
    cli_low_* cells show what the shipped gateway reports back to a user in that case.
'@
} catch {
    Write-ProtoFatal $_
} finally {
    if ($bgProc -and -not $bgProc.HasExited) { try { $null = $bgProc.Kill($true) } catch { } }
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'env-manager-service.exe') -TimeoutSec 30
    $null = Invoke-ProtoProcess -Exe taskkill.exe -Arguments @('/F', '/IM', 'env-manager-cli.exe') -TimeoutSec 30
    Remove-ProtoService
    Remove-ProtoLocalUser
    Write-Evidence -Key 'cleanup.done' -Value 'service removed, low user removed, stray processes killed'
}
