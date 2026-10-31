# assert-test-outcomes.ps1 - require named tests to have PASSED in a TRX result file
# License: Apache-2.0
#
# Why this exists: "dotnet test" prints "Passed!" for a run whose tests were all
# skipped, and at default verbosity it never names the tests that passed. The real
# sops round-trip sat in that blind spot on every CI lane. This step reads the TRX the
# test step wrote and fails unless every named test ran and reported Passed - so a
# Skipped (or NotExecuted, or missing) evidence test turns the job red, and the step log
# lists each one by name as the evidence a run link points to.
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$TrxPath,
    [Parameter(Mandatory)][string[]]$Tests
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if (-not (Test-Path $TrxPath)) {
    throw "assert-test-outcomes: TRX not found at $TrxPath (did the test step run with the trx logger?)"
}

[xml]$trx = Get-Content -Raw -Path $TrxPath
$ns = New-Object System.Xml.XmlNamespaceManager($trx.NameTable)
$ns.AddNamespace('t', $trx.DocumentElement.NamespaceURI)
$results = $trx.SelectNodes('//t:UnitTestResult', $ns)

$failures = @()
foreach ($name in $Tests) {
    # testName is the fully qualified method name; accept a trailing match so callers
    # can pass Class.Method without the namespace.
    $matched = @($results | Where-Object { $_.testName -eq $name -or $_.testName.EndsWith('.' + $name) })
    if ($matched.Count -eq 0) {
        $failures += "$name : NOT FOUND in TRX (filtered out or never discovered)"
        continue
    }
    foreach ($r in $matched) {
        if ($r.outcome -eq 'Passed') {
            Write-Host "PASSED  $($r.testName)"
        } else {
            $msg = ''
            $node = $r.SelectSingleNode('t:Output/t:ErrorInfo/t:Message', $ns)
            if ($null -eq $node) { $node = $r.SelectSingleNode('t:Output/t:StdOut', $ns) }
            if ($null -ne $node) { $msg = ' - ' + $node.InnerText.Trim() }
            $failures += "$($r.testName) : $($r.outcome)$msg"
        }
    }
}

if ($failures.Count -gt 0) {
    Write-Host "assert-test-outcomes: $($failures.Count) evidence test(s) did not pass - a skip is not a pass:"
    $failures | ForEach-Object { Write-Host "  $_" }
    exit 1
}
Write-Host "assert-test-outcomes: all $($Tests.Count) evidence test(s) passed"
