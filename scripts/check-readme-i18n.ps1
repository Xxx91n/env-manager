# check-readme-i18n.ps1 — i18n README drift detection (ADR-0007-style)
# Verifies structural consistency between README.md (English authority) and all
# docs/i18n/README.*.md locale files. Exits 0 if all checks pass, 1 on drift.
#
# Checks:
# 1. H2 heading count matches README.md (allowing translated headings)
# 2. README-I18N:START/END switcher block present in every locale file
# 3. No inline version strings (v0.9.x pattern) — version belongs in CHANGELOG
# 4. hero.gif referenced in every locale file
# 5. demo.gif referenced in every locale file (Demos section)
# 6. For AI Agents section (or translated equivalent) present
# 7. Documentation section (or translated equivalent) present

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$errors = @()

# --- 1. Extract expected H2 count from README.md ---
$readmePath = Join-Path $root 'README.md'
$readmeContent = Get-Content $readmePath -Raw
$readmeH2 = [regex]::Matches($readmeContent, '(?m)^## .+$')
$expectedH2Count = $readmeH2.Count

Write-Host "Reference README.md: $expectedH2Count H2 headings"

# --- 2. Find all i18n locale files ---
$i18nDir = Join-Path $root 'docs' 'i18n'
$localeFiles = Get-ChildItem -Path $i18nDir -Filter 'README.*.md' | Sort-Object Name

if ($localeFiles.Count -eq 0) {
    $errors += "No i18n locale files found in docs/i18n/"
}

Write-Host "Found $($localeFiles.Count) locale files"

# --- 3. Check each locale file ---
foreach ($file in $localeFiles) {
    $content = Get-Content $file.FullName -Raw
    $localeName = $file.Name

    # Check 3a: H2 heading count
    $localeH2 = [regex]::Matches($content, '(?m)^## .+$')
    if ($localeH2.Count -ne $expectedH2Count) {
        $errors += "${localeName}: H2 count $($localeH2.Count) != expected $expectedH2Count (drift detected)"
    }

    # Check 3b: i18n switcher block
    if (-not $content.Contains('<!-- README-I18N:START -->')) {
        $errors += "${localeName}: missing README-I18N:START switcher block"
    }
    if (-not $content.Contains('<!-- README-I18N:END -->')) {
        $errors += "${localeName}: missing README-I18N:END switcher block"
    }

    # Check 3c: No inline version strings (v0.9.x or v0.8.x etc.)
    $versionMatches = [regex]::Matches($content, 'v0\.\d+\.\d+')
    if ($versionMatches.Count -gt 0) {
        $errors += "${localeName}: contains $($versionMatches.Count) inline version string(s) — move to CHANGELOG.md"
    }

    # Check 3d: hero.gif referenced
    if (-not $content.Contains('hero.gif')) {
        $errors += "${localeName}: missing hero.gif reference"
    }

    # Check 3e: demo.gif referenced (Demos section)
    if (-not $content.Contains('demo.gif')) {
        $errors += "${localeName}: missing demo.gif reference (Demos section)"
    }

    # Check 3f: For AI Agents section (check for agent-related content)
    $agentPattern = 'AI Agent|agent|Agent|agent-native|AGENTS'
    if (-not ($content -match $agentPattern)) {
        $errors += "${localeName}: missing For AI Agents section content"
    }

    # Check 3g: Documentation section (check for doc links)
    $docPattern = 'Documentation|CHANGELOG|cli-commands|architecture'
    if (-not ($content -match $docPattern)) {
        $errors += "${localeName}: missing Documentation section content"
    }
}

# --- 4. Report ---
if ($errors.Count -eq 0) {
    Write-Host ""
    Write-Host "=== i18n README drift check PASSED ===" -ForegroundColor Green
    exit 0
} else {
    Write-Host ""
    Write-Host "=== i18n README drift check FAILED ===" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host "  ERROR: $e" -ForegroundColor Yellow
    }
    exit 1
}
