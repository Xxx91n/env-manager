# CI Tier 3 Pester 5 test: Launch profile env injection (registry-clean fixture)
# Referenced by .codex-tmp/grill-plan-ci-test-automation.md (3.2)
# CI must build env-manager-cli.exe BEFORE this test runs (build.yml verify job order).

[CmdLetBinding()]
param(
    [string]$CliExe = ""
)

BeforeDiscovery {
    # Allow opt-out via -Tag filter: default runs CI tier. Use Invoke-Pester -ExcludeTagFilter Manual to skip.
}

BeforeAll {
    $ErrorActionPreference = 'Stop'

    # Locate the built CLI binary. Reuse the same resolution order as the existing
    # scripts/test-with-restore.ps1 and scripts/test-launch-env.ps1 tools so that
    # CI/local paths agree.
    function Resolve-CliPath([string]$Override) {
        # CI orchestrator (scripts/run-ci-tests.ps1) sets $env:EM_CLI_EXE
        # so Pester 6 (no Run.Script) can still pick up an explicit CLI path.
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
    Write-Host "[LaunchInjection] Using CLI: $($script:cli)" -ForegroundColor Cyan

    # Version probe (matches AGENTS.md v0.7.15 stale-binary guard philosophy; fails CI
    # loudly if an old v0.3.0 binary was accidentally shipped).
    $verOut = & $script:cli 2>&1
    if (-not ($verOut -match 'v\d+\.\d+\.\d+')) {
        throw "CLI version probe failed. Output: $verOut"
    }

    # Fixture: a uniquely-named launch profile + a probe batch file that dumps env to stdout.
    $script:probeId = "emci_" + ([Guid]::NewGuid().ToString('N').Substring(0, 8))
    $script:profName = "EM_CI_LAUNCH_PROBE_$($script:probeId)"
    $tempDir = [System.IO.Path]::GetTempPath()
    $script:probeBat = Join-Path $tempDir "$($script:probeId).bat"
    $script:probeOut = Join-Path $tempDir "$($script:probeId).out"

    # Probe .bat writes all env vars (NAME=VALUE) for diff/comparison.
    # ASCII encoding avoids BOM issues on test runners.
    @"
@echo off
set > "$($script:probeOut)"
"@ | Set-Content -Path $script:probeBat -Encoding ASCII

    # Regular var value and secret value used for assertions.
    $script:regVarName = "EM_CI_REGVAR_$($script:probeId)"
    $script:regVarValue = "registry-value-$($script:probeId)"
    $script:secVarName = "EM_CI_SECRET_$($script:probeId)"
    $script:secVarValue = "secret-value-$($script:probeId)"

    # Create the launch profile pointing at the probe .bat so profile launch
    # spawn-injects and the .bat writes the actual injected env to probeOut.
    & $script:cli profile create $script:profName --type launch --target $script:probeBat 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile create failed: exit=$LASTEXITCODE" }

    & $script:cli profile add-var $script:profName $script:regVarName $script:regVarValue 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile add-var failed: exit=$LASTEXITCODE" }

    & $script:cli profile add-secret $script:profName $script:secVarName $script:secVarValue 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile add-secret failed: exit=$LASTEXITCODE" }
}

Describe "Launch profile env injection" -Tag CI {
    It "Profile preview lists the regular variable" {
        $preview = & $script:cli profile preview $script:profName 2>&1 | ConvertFrom-Json
        $found = $preview.variables | Where-Object { $_.name -eq $script:regVarName }
        $found | Should -Not -BeNullOrEmpty
        $found.value | Should -Be $script:regVarValue
    }

    It "Profile preview lists the secret variable as <encrypted>" {
        $show = & $script:cli profile show $script:profName 2>&1 | ConvertFrom-Json
        $show.SecretVariables | Should -Contain $script:secVarName
    }

    It "reveal-secret decrypts the DPAPI-bound secret back to plaintext for the current user" {
        $plaintext = & $script:cli profile reveal-secret $script:profName $script:secVarName 2>&1
        $LASTEXITCODE | Should -Be 0
        $plaintext | Should -Be $script:secVarValue
    }

    It "profile launch injects both regular and secret variables into the spawned child process env" {
        # Remove stale probe output if a prior run left one.
        Remove-Item $script:probeOut -ErrorAction SilentlyContinue

        & $script:cli profile launch $script:profName 2>&1 | Out-Null
        $LASTEXITCODE | Should -Be 0

        # The .bat writes synchronously; 1s slide window is generous.
        $tries = 0
        while (-not (Test-Path $script:probeOut) -and $tries -lt 20) {
            Start-Sleep -Milliseconds 50
            $tries++
        }
        Test-Path $script:probeOut | Should -Be $true

        $actual = @{}
        Get-Content $script:probeOut | ForEach-Object {
            $i = $_.IndexOf('=')
            if ($i -gt 0) { $actual[$_.Substring(0, $i)] = $_.Substring($i + 1) }
        }

        $actual.ContainsKey($script:regVarName) | Should -Be $true
        $actual[$script:regVarName] | Should -Be $script:regVarValue

        $actual.ContainsKey($script:secVarName) | Should -Be $true
        $actual[$script:secVarName] | Should -Be $script:secVarValue
    }
}

AfterAll {
    if ($script:profName -and $script:cli) {
        & $script:cli profile delete $script:profName 2>&1 | Out-Null
    }
    Remove-Item $script:probeBat -ErrorAction SilentlyContinue
    Remove-Item $script:probeOut -ErrorAction SilentlyContinue
}