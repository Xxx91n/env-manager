# CI Tier 3 Pester 5 test: Launch profile env injection (registry-clean fixture)
# Referenced by .codex-tmp/grill-plan-ci-test-automation.md (3.2)
# CI must build env-manager-cli.exe BEFORE this test runs (build.yml verify job order).
#
# Ticket 07 (architecture-recovery) upgrade: this file now implements the
# four-layer launch-verification pattern from spec.md:
#   1. golden env diff — the injected set must exactly match the profile's
#      resolved variables (no more, no less), against an allowlist of variables
#      that cmd.exe itself synthesizes in the child (COMSPEC/PATHEXT/PROMPT).
#   2. probe-process echo — the probe child re-reads its own injected values.
#   3. unapply invariant — a Launch profile never writes the registry, so the
#      injected names must be absent from HKCU\Environment after launch.
# The canary-redaction negative suite lives in tests/canary-redaction.Tests.ps1.
# The pre-existing probe pattern (scripts/test-launch-env.ps1) is the seed this
# suite upgrades; it is kept as a manual inspector tool, not replaced.

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

    # Variables cmd.exe itself materializes inside ANY child cmd process, even when
    # the launcher hands it an empty env block (measured empirically on Win11:
    # env_clear child shows only COMSPEC/PATHEXT/PROMPT plus injected names).
    # The golden diff ignores exactly these; anything else unaccounted fails.
    $script:cmdIntrinsicVars = @('COMSPEC', 'PATHEXT', 'PROMPT')

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
    # The canary value is deliberately format-shaped (password=<canary>) so a
    # leaking sink would ALSO match the CLI scrub patterns, and uniquely tagged
    # so no other process's output can false-positive.
    $script:regVarName = "EM_CI_REGVAR_$($script:probeId)"
    $script:regVarValue = "registry-value-$($script:probeId)"
    $script:secVarName = "EM_CI_SECRET_$($script:probeId)"
    $script:secVarValue = "password=canary-$($script:probeId)"

    # Create the launch profile pointing at the probe .bat so profile launch
    # spawn-injects and the .bat writes the actual injected env to probeOut.
    & $script:cli profile create $script:profName --type launch --target $script:probeBat 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile create failed: exit=$LASTEXITCODE" }

    & $script:cli profile add-var $script:profName $script:regVarName $script:regVarValue 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile add-var failed: exit=$LASTEXITCODE" }

    & $script:cli profile add-secret $script:profName $script:secVarName $script:secVarValue 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile add-secret failed: exit=$LASTEXITCODE" }
}

function script:Get-ProbeEnv {
    # Runs profile launch against the probe and returns the child's actual
    # environment as a hashtable parsed from NAME=VALUE lines. Declared with a
    # script: scope qualifier so Pester's It blocks (child scopes) resolve it
    # under Pester 6's InModuleScope behavior.
    param([int]$WaitSeconds = 10)
    Remove-Item $script:probeOut -ErrorAction SilentlyContinue
    & $script:cli profile launch $script:profName 2>&1 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "profile launch failed: exit=$LASTEXITCODE" }
    $tries = 0
    while (-not (Test-Path $script:probeOut) -and $tries -lt ($WaitSeconds * 20)) {
        Start-Sleep -Milliseconds 50
        $tries++
    }
    if (-not (Test-Path $script:probeOut)) { throw "probe output file was not produced: $($script:probeOut)" }
    $actual = @{}
    Get-Content $script:probeOut | ForEach-Object {
        $i = $_.IndexOf('=')
        if ($i -gt 0) { $actual[$_.Substring(0, $i)] = $_.Substring($i + 1) }
    }
    return $actual
}

Describe "Launch profile env injection" -Tag CI {
    It "Profile preview lists the regular variable" {
        $preview = & $script:cli profile preview $script:profName 2>&1 | ConvertFrom-Json
        $found = $preview.variables | Where-Object { $_.name -eq $script:regVarName }
        $found | Should -Not -BeNullOrEmpty
        $found.value | Should -Be $script:regVarValue
    }

    It "Profile preview does NOT expose the secret plaintext (stores ciphertext)" {
        # preview emits stored values; the secret's stored form is provider
        # ciphertext / mount reference. The canary plaintext must not appear.
        # Pester quirk: [regex]::Escape(...) must be pre-computed into a variable;
        # passed inline, Pester receives the literal text '[regex]::Escape'.
        $canaryPattern = [regex]::Escape($script:secVarValue)
        $preview = & $script:cli profile preview $script:profName 2>&1 | Out-String
        $preview | Should -Not -Match $canaryPattern
    }

    It "Profile show lists the secret variable as <encrypted>" {
        $show = & $script:cli profile show $script:profName 2>&1 | ConvertFrom-Json
        $show.SecretVariables | Should -Contain $script:secVarName
        $maskedEntry = $show.Variables | Where-Object { $_.name -eq $script:secVarName }
        $maskedEntry | Should -Not -BeNullOrEmpty
        $maskedEntry.value | Should -Be '<encrypted>'
    }

    It "profile launch injects both regular and secret variables into the spawned child process env" {
        # Layer 2: probe-process echo — the child re-reads its injected values.
        $actual = Get-ProbeEnv
        $actual.ContainsKey($script:regVarName) | Should -Be $true
        $actual[$script:regVarName] | Should -Be $script:regVarValue

        $actual.ContainsKey($script:secVarName) | Should -Be $true
        $actual[$script:secVarName] | Should -Be $script:secVarValue
    }

    It "golden env diff: injected set exactly matches the profile's resolved variables" {
        # Layer 1: golden diff — every profile variable must arrive, and apart
        # from cmd.exe's intrinsic child-process defaults nothing else may.
        $preview = & $script:cli profile preview $script:profName 2>&1 | ConvertFrom-Json
        $expected = @{}
        foreach ($v in $preview.variables) { $expected[$v.name] = $v.value }
        $expected[$script:secVarName] = $script:secVarValue  # preview stores ciphertext; expected child value is plaintext

        $actual = Get-ProbeEnv

        $unexpected = @($actual.Keys | Where-Object {
            $_ -notin $script:cmdIntrinsicVars -and -not $expected.ContainsKey($_)
        })
        $unexpected | Should -BeNullOrEmpty -Because "env_clear + inject must not leak parent env into the child (found: $($unexpected -join ', '))"

        foreach ($name in $expected.Keys) {
            $actual.ContainsKey($name) | Should -Be $true -Because "profile variable '$name' must be injected"
            if ($actual.ContainsKey($name)) {
                $actual[$name] | Should -Be $expected[$name] -Because "profile variable '$name' value must match the resolved profile"
            }
        }
    }

    It "launch never writes the registry: injected names absent from HKCU\Environment" {
        # Layer 3: registry invariant — Launch profiles are local-only (AGENTS.md
        # hard boundary). Read HKCU\Environment directly; the profile-scoped names
        # must not exist there after launch. (Read-only; no registry mutation.)
        $userEnv = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
        try {
            foreach ($name in @($script:regVarName, $script:secVarName)) {
                $userEnv.GetValue($name) | Should -BeNullOrEmpty -Because "Launch profile variable '$name' must never be persisted to the registry"
            }
        } finally {
            $userEnv.Dispose()
        }
    }
}

AfterAll {
    if ($script:profName -and $script:cli) {
        & $script:cli profile delete $script:profName 2>&1 | Out-Null
    }
    Remove-Item $script:probeBat -ErrorAction SilentlyContinue
    Remove-Item $script:probeOut -ErrorAction SilentlyContinue
}
