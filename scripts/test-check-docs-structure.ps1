# Self-test for the translation structure and sync metadata gate. No compiler,
# no network, no registry: synthetic documents prove both directions -
# valid matching translations pass, while missing sections, reordered
# sections, missing top comments, or stale sync commits fail loudly.
$ErrorActionPreference = 'Stop'
$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

$baseDoc = @'
<!-- canonical: ../../README.md -->
<!-- sync: ee7b425f38df -->

# Env Manager
## Showcase
### GUI
docs/assets/gallery/01-variables.webp
## Features
docs/plaintext-sinks.md
## For AI agents
AGENTS.cli.md
## Security
SECURITY.md
## Install
### MSI Installer
env-manager.msi
### Portable
portable.zip
### CLI-Only
env-manager-cli
### Prerequisites
.NET 10.0 WebView2
### winget
winget install
### From Source
docs/build-and-release.md
## Usage
### CLI
env-manager-cli list
### GUI
env-manager.exe
## Architecture
Tauri Svelte
## Secret providers
KeepassXC Bitwarden
## Service mode
service reconcile
## Documentation
docs/architecture.md
## Maintainers
https://github.com/Xxx91n
## Contributing
AGENTS.md
## License
LICENSE
'@
$baseDoc = $baseDoc -replace "\r\n", "`n"

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("check-docs-structure-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $cases = @(
        @{
            Name = 'valid-translation'
            Text = $baseDoc
            Exit = 0
            Pattern = 'PASS: docs checks clean'
        },
        @{
            Name = 'missing-section'
            Text = $baseDoc.Replace("### winget`nwinget install`n", "")
            Exit = 1
            Pattern = 'missing section'
        },
        @{
            Name = 'reordered-sections'
            Text = $baseDoc.Replace("## Maintainers`nhttps://github.com/Xxx91n`n## Contributing`nAGENTS.md", "## Contributing`nAGENTS.md`n## Maintainers`nhttps://github.com/Xxx91n")
            Exit = 1
            Pattern = 'sections out of order'
        },
        @{
            Name = 'missing-canonical-comment'
            Text = $baseDoc.Replace("<!-- canonical: ../../README.md -->`n", "")
            Exit = 1
            Pattern = 'missing canonical top comment'
        },
        @{
            Name = 'missing-sync-comment'
            Text = $baseDoc.Replace("<!-- sync: ee7b425f38df -->`n", "")
            Exit = 1
            Pattern = 'missing sync top comment'
        },
        @{
            Name = 'stale-sync-comment'
            Text = $baseDoc.Replace("<!-- sync: ee7b425f38df -->", "<!-- sync: 000000000000 -->")
            Exit = 1
            Pattern = 'stale or unknown sync commit'
        }
    )
    foreach ($case in $cases) {
        $doc = Join-Path $temp ($case.Name + '.md')
        [System.IO.File]::WriteAllText($doc, ($case.Text.Trim() + "`n"), (New-Object System.Text.UTF8Encoding($false)))
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

    # Verify that the full repository check passes across all 9 tracked translations
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $repoOutput = @(& (Join-Path $PSScriptRoot 'check-docs.ps1') *>&1 | ForEach-Object { $_.ToString() })
        $repoCode = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $prevEap }
    $checks++
    if ($repoCode -ne 0) {
        $failures.Add("clean repository check expected exit 0, got $repoCode`: $($repoOutput -join ' | ')")
    }
    elseif (-not (($repoOutput -join "`n") -match 'PASS: docs checks clean')) {
        $failures.Add("clean repository check output did not contain 'PASS: docs checks clean': $($repoOutput -join ' | ')")
    }
    else { Write-Host "  ok: gate all-repository-translations" }
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}

if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Host "SELFTEST FAIL: $_" }
    exit 1
}
Write-Host "SELFTEST PASS: $checks translation structure checks"
exit 0
