# check-bare-ids.ps1 - fail when a bare ticket/issue number or a version label
# survives outside the archive.
#
# Why this exists: two rounds reported "0 remaining" from a grep that had been run
# before the edit and never re-run after it. A number in a commit message is a claim;
# a check that runs on every PR is evidence. Archive paths are an explicit allow-list,
# not an accident: docs/history/ and CHANGELOG.md are records of what happened, and
# rewriting them would be fabricating history.
param()

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $pattern = '\b(?:[Tt]icket|[Ii]ssue)\s+\d+\b|\bv[01]\.[0-9]+\.[0-9]+[^0-9]{0,12}Phase'
    $hits = & git grep -nP $pattern -- . ':(exclude)docs/history' ':(exclude)CHANGELOG.md'
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
