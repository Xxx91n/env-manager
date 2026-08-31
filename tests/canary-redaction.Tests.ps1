# CI Tier 3 Pester 5 test: Canary redaction negative assertions (ticket 07).
# Injects a uniquely-tagged fake secret (canary) into a throwaway launch
# profile, then scans EVERY output sink of the secret lifecycle for the canary
# plaintext: zero occurrence = pass, one occurrence = red. Also asserts the
# masking placeholders (<encrypted>) appear where the contract requires them.
#
# Sink inventory (spec.md user story 14 "all output sinks"):
#   S1 profile show            — masked view of the profile (stdout)
#   S2 profile preview         — resolved-variable view (stdout; stores ciphertext)
#   S3 profile list            — profile summaries (stdout)
#   S4 audit trail             — `history list` output (audit.json is AES-GCM
#                                encrypted at rest; records NAME + <redacted>/<encrypted> markers only)
#   S5 launch stdout           — profile launch console output
#   S6 launch child-probe dump — everything the launched child persisted
#                                (deliberately contains the secret: it is the
#                                documented injection channel, and proves the
#                                canary DID flow — guards against a silent
#                                false-green if decryption broke)
#   S7 stderr on failure       — forced-error stderr (scrubbed by ScrubExceptionMessage)
#
# `profile reveal-secret` is the ONLY designed plaintext-stdout path (AGENTS.md
# hard boundary) and is intentionally not scanned as a sink; the S6 assertion
# proves the canary flows end-to-end without it.

[CmdLetBinding()]
param(
    [string]$CliExe = ""
)

BeforeAll {
    $ErrorActionPreference = 'Stop'

    function script:Resolve-CliPath([string]$Override) {
        $envCli = $env:EM_CLI_EXE
        if ($envCli -and (Test-Path $envCli)) { return (Resolve-Path $envCli).Path }
        if ($Override -and (Test-Path $Override)) { return (Resolve-Path $Override).Path }
        $projectRoot = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
        $candidates = @(
            (Join-Path $projectRoot 'release\portable\env-manager-cli.exe'),
            (Join-Path $projectRoot 'release\cli-only\env-manager-cli.exe'),
            (Join-Path $projectRoot 'bin\Release\net10.0-windows\env-manager-cli.exe'),
            (Join-Path $projectRoot 'bin\Debug\net10.0-windows\env-manager-cli.exe')
        )
        foreach ($c in $candidates) {
            if (Test-Path $c) { return (Resolve-Path $c).Path }
        }
        throw "env-manager-cli.exe not found in any expected location. Build the CLI first (dotnet build -c Release) or pass -CliExe."
    }

    $script:cli = Resolve-CliPath $CliExe

    # Unique canary: format-shaped (password=...) so a leak would also match the
    # CLI scrub patterns, uniquely tagged so no other output can false-positive.
    $script:canaryId = [Guid]::NewGuid().ToString('N').Substring(0, 12)
    $script:canary = "password=canary-$($script:canaryId)"

    $script:profName = "EM_CI_CANARY_$($script:canaryId)"
    $tempDir = [System.IO.Path]::GetTempPath()
    $script:probeBat = Join-Path $tempDir "emcanary_$($script:canaryId).bat"
    $script:probeOut = Join-Path $tempDir "emcanary_$($script:canaryId).out"
    @"
@echo off
set > "$($script:probeOut)"
"@ | Set-Content -Path $script:probeBat -Encoding ASCII

    $script:canaryVar = "EM_CANARY_SECRET_$($script:canaryId)"

    # Fixture: launch profile holding ONLY the canary secret.
    & $script:cli profile create $script:profName --type launch --target $script:probeBat 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile create failed: exit=$LASTEXITCODE" }
    & $script:cli profile add-secret $script:profName $script:canaryVar $script:canary 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile add-secret failed: exit=$LASTEXITCODE" }

    # Pre-execute all sinks once so each It scans captured text.
    $script:sinkShow = (& $script:cli profile show $script:profName 2>&1 | Out-String)
    $script:sinkPreview = (& $script:cli profile preview $script:profName 2>&1 | Out-String)
    $script:sinkList = (& $script:cli profile list 2>&1 | Out-String)
    # S4 audit: perform a reveal (audited action) then read the audit trail back.
    & $script:cli profile reveal-secret $script:profName $script:canaryVar 2>&1 | Out-Null
    $script:sinkAudit = (& $script:cli history list 2>&1 | Out-String)
    # S5 launch stdout + S6 child probe dump.
    Remove-Item $script:probeOut -ErrorAction SilentlyContinue
    $script:sinkLaunch = (& $script:cli profile launch $script:profName 2>&1 | Out-String)
    $tries = 0
    while (-not (Test-Path $script:probeOut) -and $tries -lt 200) {
        Start-Sleep -Milliseconds 50
        $tries++
    }
    $script:sinkChildDump = if (Test-Path $script:probeOut) { (Get-Content $script:probeOut -Raw) } else { "" }
    # S7 forced-error stderr.
    $script:sinkErrorStderr = (& $script:cli profile reveal-secret $script:profName "EM_NO_SUCH_VAR_$($script:canaryId)" 2>&1 | Out-String)
}

function script:Assert-NoCanary {
    param([string]$SinkName, [string]$Text)
    if ($Text -match [regex]::Escape($script:canary)) {
        throw "CANARY LEAK in sink '$SinkName': plaintext secret reached output. Redacting the mask did not hold."
    }
}

Describe "Canary redaction - zero leak across all output sinks" -Tag CI {
    It "S1 profile show output does not contain the canary" {
        Assert-NoCanary -SinkName 'profile show' -Text $script:sinkShow
    }

    It "S2 profile preview output does not contain the canary" {
        Assert-NoCanary -SinkName 'profile preview' -Text $script:sinkPreview
    }

    It "S3 profile list output does not contain the canary" {
        Assert-NoCanary -SinkName 'profile list' -Text $script:sinkList
    }

    It "S4 audit trail (history list) does not contain the canary" {
        Assert-NoCanary -SinkName 'history list' -Text $script:sinkAudit
    }

    It "S5 launch stdout does not contain the canary" {
        Assert-NoCanary -SinkName 'launch stdout' -Text $script:sinkLaunch
    }

    It "S6 child probe dump DOES contain the canary (injection channel works)" {
        # Positive control: the canary must reach the child env block, else the
        # zero-leak assertions above are trivially green. Re-read the dump file
        # here (not only in BeforeAll) so a slow child that finishes after
        # BeforeAll's wait window still lands; poll up to 15s before failing.
        # Pester quirk: [regex]::Escape(...) must be pre-computed into a variable;
        # passed inline, Pester receives the literal text '[regex]::Escape'.
        $canaryPattern = [regex]::Escape($script:canary)
        $dump = $script:sinkChildDump
        $tries = 0
        while (-not ($dump -match $canaryPattern) -and $tries -lt 150 -and (Test-Path $script:probeOut)) {
            Start-Sleep -Milliseconds 100
            $tries++
            $dump = Get-Content $script:probeOut -Raw
        }
        $dump | Should -Match $canaryPattern
    }

    It "S7 error stderr does not contain the canary" {
        Assert-NoCanary -SinkName 'error stderr' -Text $script:sinkErrorStderr
    }
}

Describe "Canary redaction - masking placeholders present" -Tag CI {
    It "profile show masks the secret value with <encrypted>" {
        # CLI stdout is JSON with < > escaped as \u003C \u003E, so the literal
        # '<encrypted>' string never appears raw in the stream; assert the
        # escaped form (verified byte-level against the real output).
        $script:sinkShow | Should -Match '\\u003Cencrypted\\u003E'
    }

    It "audit records the reveal as <revealed> marker, never plaintext" {
        # The audit entry embeds its JSON summary inside the outer JSON, so the
        # marker is double-escaped in the raw stream: \u0022...\\u003Crevealed\\u003E.
        # The literal substring 'u003Crevealed' matches both escape depths and
        # proves the placeholder (not plaintext) was recorded.
        $script:sinkAudit | Should -Match 'profile reveal-secret'
        $script:sinkAudit | Should -Match 'u003Crevealed'
    }
}

AfterAll {
    if ($script:profName -and $script:cli) {
        & $script:cli profile delete $script:profName 2>&1 | Out-Null
    }
    Remove-Item $script:probeBat -ErrorAction SilentlyContinue
    Remove-Item $script:probeOut -ErrorAction SilentlyContinue
}
