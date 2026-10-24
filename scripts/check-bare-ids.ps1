# check-bare-ids.ps1 - fail when a bare ticket/issue number or a version label
# survives outside the archive.
#
# Why this exists: two rounds reported "0 remaining" from a grep that had been run
# before the edit and never re-run after it. A number in a commit message is a claim;
# a check that runs on every PR is evidence. Archive paths are an explicit allow-list,
# not an accident: docs/history/ and CHANGELOG.md are records of what happened, and
# rewriting them would be fabricating history.
#
# The version-label alternative accepts labels with two OR three numeric
# segments before the phase word. The first version of this pattern required
# three segments and was blind to the shorter form, which let the gate
# report PASS while two-segment residue remained in live comments.
#
# Self-test polarity (-SelfTest): this switch answers "did the scanner fire
# when it should have?", not "is the repo clean?". It exits 0 when every bad
# form planted in the tracked fixture scripts/bare-ids-selftest-fixture.md is
# detected AND the fixture's negative-control lines stay unmatched, and exits
# 1 when the scanner stayed blind or fired on legitimate text. CI runs the
# self-test BEFORE the production scan, so a working scanner never fails the
# pipeline on this step; only a broken detector does.
param([switch]$SelfTest)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
$pattern = '\b(?:[Tt]icket|[Ii]ssue)\s+\d+\b|\bv[01]\.[0-9]+(\.[0-9]+)?[^0-9]{0,12}Phase'
$fixture = 'scripts/bare-ids-selftest-fixture.md'
$negativeMarker = 'SELFTESTOK'

if ($SelfTest) {
    # The expected bad forms are read from the fixture itself (each violating
    # line starts with 'BAD ' and ends the form at ' - '), so this script never
    # embeds a literal violating string and the production scan can police it.
    Push-Location $root
    try {
        $badLines = & git grep -nE '^BAD .+ - ' -- $fixture
    }
    finally {
        Pop-Location
    }
    $badForms = @()
    foreach ($bl in @($badLines)) {
        if ($bl -match ':BAD (.+?) - ') { $badForms += $Matches[1] }
    }
    Push-Location $root
    try {
        $hits = & git grep -nP $pattern -- $fixture
        $code = $LASTEXITCODE
    }
    finally {
        Pop-Location
    }
    $lines = @($hits)
    $failures = @()
    if ($badForms.Count -ne 4) {
        $failures += ("fixture yielded {0} 'BAD ... -' form lines; expected 4 (fixture damaged or invisible to git grep)" -f $badForms.Count)
    }
    if ($code -ne 0) {
        $failures += ("git grep on the fixture exited {0}; expected 0 (every bad form is planted, so a match is guaranteed unless the scanner is blind or the tracked fixture is missing)" -f $code)
    }
    foreach ($form in $badForms) {
        $hit = @($lines | Where-Object { $_ -clike ('*' + $form + '*') })
        if ($hit.Count -eq 0) {
            $failures += ("scanner did NOT flag planted bad form: {0}" -f $form)
        }
        else {
            Write-Host ("  detected bad form: {0}" -f $form)
        }
    }
    $negHits = @($lines | Where-Object { $_ -like ('*' + $negativeMarker + '*') })
    if ($negHits.Count -gt 0) {
        $failures += ("scanner fired on {0} negative-control line(s); a pattern that hits legitimate text would break every pipeline" -f $negHits.Count)
        $negHits | ForEach-Object { Write-Host ("  false positive: " + $_) }
    }
    else {
        Write-Host ("  negative controls stayed unmatched (marker {0}): real GitHub issue URL, prose version mention" -f $negativeMarker)
    }
    $unexpected = @(
        $lines | Where-Object {
            $line = $_
            $covered = $false
            foreach ($form in $badForms) { if ($line -clike ('*' + $form + '*')) { $covered = $true } }
            -not $covered
        }
    )
    if ($unexpected.Count -gt 0) {
        $failures += ("scanner matched {0} fixture line(s) carrying no planted bad form" -f $unexpected.Count)
        $unexpected | ForEach-Object { Write-Host ("  unexpected match: " + $_) }
    }
    if ($failures.Count -gt 0) {
        $failures | ForEach-Object { Write-Host ("  SELFTEST FAIL: " + $_) }
        exit 1
    }
    Write-Host ("SELFTEST PASS: scanner fired on all {0} planted bad forms and stayed silent on the negative controls" -f $badForms.Count)
    exit 0
}

Push-Location $root
try {
    $excludeFixture = ':(exclude)' + $fixture
    $hits = & git grep -nP $pattern -- . ':(exclude)docs/history' ':(exclude)CHANGELOG.md' $excludeFixture
    $code = $LASTEXITCODE
}
finally {
    Pop-Location
}

if ($code -eq 1) {
    Write-Host 'PASS: no bare ticket/issue ids or version labels outside the archive'
    exit 0
}
if ($code -ne 0) {
    Write-Host ("git grep failed with exit code {0}" -f $code)
    exit 2
}

@($hits) | ForEach-Object { Write-Host ("  " + $_) }
Write-Host ("FAIL: {0} line(s) carry a bare id or version label" -f @($hits).Count)
Write-Host 'Replace the number with a self-contained sentence, or link a real GitHub issue by URL.'
exit 1
