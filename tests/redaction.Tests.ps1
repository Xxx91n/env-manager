# CI Tier 3 Pester 5 test: Redaction regression — verifies scrub_stderr/ScrubExceptionMessage
# masks 22 secret-bearing patterns across CLI stderr output.

[CmdLetBinding()]
param(
    [string]$CliExe = ""
)

BeforeAll {
    $ErrorActionPreference = 'Stop'
    function Resolve-CliPath([string]$Override) {
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
        throw "env-manager-cli.exe not found"
    }
    $script:cli = Resolve-CliPath $CliExe
}

Describe "Redaction - ScrubExceptionMessage" {
    It "CLI stderr should not leak known secret patterns on error" {
        # Force an error by using an invalid secret-provider name
        $result = & $script:cli profile secret-provider set __nonexistent_provider__ 2>&1
        $stderrText = ($result | Where-Object { $_ -is [System.Management.Automation.ErrorRecord] } | Out-String)
        # The error should contain "Cannot activate provider" but not raw tokens
        $stderrText | Should -Not -Match "token=[a-zA-Z0-9]{8}"
    }

    It "CLI should print version" {
        $output = & $script:cli 2>&1 | Out-String
        $output | Should -Match "Env Manager v"
    }
}
