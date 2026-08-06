# v0.9.6: Documentation alignment check — verifies AGENTS.md referenced paths exist.
# Runs in CI (build.yml) to prevent PRs from merging with broken doc references.
# Returns exit 0 if all references valid, exit 1 if any broken.
$ErrorActionPreference = 'Stop'

$root = Resolve-Path (Join-Path $PSScriptRoot '..')
$errors = @()

# 1. Check key files exist
$keyFiles = @(
    'AGENTS.md',
    'AGENTS.cli.md',
    'CONTEXT.md',
    'CHANGELOG.md',
    'README.md',
    'README_CN.md',
    'docs/cli-commands.md',
    'docs/architecture.md',
    'docs/build-and-release.md',
    'docs/secret-providers-guide.md',
    'docs/secret-architecture-blueprint.md',
    'docs/secret-architecture-decision-summary.md',
    'docs/adr/0001-secret-architecture-revision.md',
    'docs/adr/0002-service-watchdog-heartbeat.md',
    'docs/adr/0003-version-single-source-changelog.md'
)

foreach ($f in $keyFiles) {
    $fullPath = Join-Path $root $f
    if (-not (Test-Path $fullPath)) {
        $errors += "Missing required file: $f"
    }
}

# 2. Check README version matches csproj
$csprojRaw = Get-Content (Join-Path $root 'env-manager.csproj') -Raw
$csprojVersion = ([regex]::Match($csprojRaw, '<Version>([^<]+)</Version>')).Groups[1].Value.Trim()
$readmeRaw = Get-Content (Join-Path $root 'README.md') -Raw
if (-not $readmeRaw.Contains($csprojVersion)) {
    # Not necessarily an error if README uses a different format — just a warning
    Write-Host "WARNING: README.md does not contain version '$csprojVersion' (may be using different version display)"
}

# 3. Check AGENTS.md referenced doc paths exist
$agentsRaw = Get-Content (Join-Path $root 'AGENTS.md') -Raw
$docRefs = [regex]::Matches($agentsRaw, '\[([^\]]+)\]\((docs/[^)]+)\)') |
    ForEach-Object { $_.Groups[2].Value } |
    Sort-Object -Unique

foreach ($ref in $docRefs) {
    $fullPath = Join-Path $root $ref
    if (-not (Test-Path $fullPath)) {
        $errors += "AGENTS.md references missing file: $ref"
    }
}

if ($errors.Count -eq 0) {
    Write-Host "=== Doc sync check PASSED ==="
    exit 0
} else {
    Write-Host "=== Doc sync check FAILED ==="
    $errors | ForEach-Object { Write-Host "ERROR: $_" }
    exit 1
}
