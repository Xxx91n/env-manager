# Self-test for the glossary avoid-word gate. No compiler, no network, no
# registry: temporary documents prove both directions - prose containing an
# avoided word fails, while a fenced command example that uses a command name
# such as `service reload` passes untouched.
$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

$fenced = @'
# Fence probe

```bash
env-manager-cli service reload
```
'@

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("check-docs-vocabulary-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $cases = @(
        @{ Name = 'prose-avoid-word'; Text = 'This backend is planted.'; Exit = 1; Pattern = 'avoided word: backend' },
        @{ Name = 'prose-avoid-plural'; Text = 'The profile reads config files.'; Exit = 1; Pattern = 'avoided word: config files' },
        @{ Name = 'fenced-command-example'; Text = $fenced; Exit = 0; Pattern = 'PASS: docs checks clean' }
    )
    foreach ($case in $cases) {
        $doc = Join-Path $temp ($case.Name + '.md')
        [System.IO.File]::WriteAllText($doc, $case.Text + "`n", (New-Object System.Text.UTF8Encoding($false)))
        # check-docs.ps1 reports through Write-Host (information stream 6), so
        # merge ALL streams to read its verdict. The child sets its own
        # ErrorActionPreference; keep the harness at Continue for the call so a
        # redirected stderr record is captured, not promoted to a terminating error.
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = @(& (Join-Path $PSScriptRoot 'check-docs.ps1') -Paths $doc *>&1 | ForEach-Object { $_.ToString() })
            $code = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $prevEap }
        $checks++
        if ($code -ne $case.Exit) {
            $failures.Add("$($case.Name) expected exit $($case.Exit), got $code`: $($output -join ' | ')")
        }
        elseif (-not (($output -join "`n") -match [regex]::Escape($case.Pattern))) {
            $failures.Add("$($case.Name) output did not contain $($case.Pattern): $($output -join ' | ')")
        }
        else { Write-Host "  ok: gate $($case.Name)" }
    }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "SELFTEST FAIL: $_" }
    exit 1
}
Write-Host "SELFTEST PASS: $checks glossary avoid-word checks"
exit 0
