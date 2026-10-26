# check-docs.ps1 - the cheap gate for a docs-only change.
#
# Why this exists: a prose diff used to pay the full two-suite test bill, and
# several live docs still asserted facts the code contradicted (a singleton
# plaintext-stdout path, "plaintext never on disk", "secrets never in
# profiles.json"). This script runs the gates that can actually fail on prose:
#
#   1. scripts/check-bare-ids.ps1  (production scan)
#   2. git diff --check            (whitespace errors)
#   3. backticked repo-relative file paths exist
#   4. backticked Class.Member citations resolve to a real member
#      (types/namespaces with no local class or file are skipped)
#   5. banned stale claims that keep coming back ("only plaintext output path",
#      "唯一明文输出路径", "Plaintext is NEVER written to")
#
# Scope: README.md, AGENTS.md, GLOSSARY.md and docs/**/*.md except docs/history.
# Pass -Paths to check only specific files. Source changes still run
# scripts/test-all.ps1.
#
# Usage: powershell -NoProfile -File scripts/check-docs.ps1 [-Paths docs/architecture.md]
param([string[]]$Paths)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
$failures = New-Object System.Collections.Generic.List[string]
$script:memberCache = @{}

function Add-Failure([string]$Message) { $script:failures.Add($Message) | Out-Null }

function Get-DocLines([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return [System.IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Path).Path, [System.Text.Encoding]::UTF8)
}

function Resolve-SymbolFiles([string]$Left) {
    if ($script:memberCache.ContainsKey($Left)) { return $script:memberCache[$Left] }
    $files = @(& git ls-files "*/$Left.cs" 2>$null)
    if ($files.Count -eq 0) {
        $classPattern = ('class {0}\b' -f $Left)
        $files = @(& git grep -l -E $classPattern -- '*.cs' 2>$null)
    }
    $script:memberCache[$Left] = $files
    return $files
}

try {
    # 1. bare ticket ids / version labels
    & (Join-Path $PSScriptRoot 'check-bare-ids.ps1') | Out-Host
    if ($LASTEXITCODE -ne 0) { Add-Failure 'check-bare-ids.ps1 reported findings' }

    # 2. whitespace: the working-tree and staged diffs catch local edits; the
    # per-line check below covers the clean checkout CI runs on, where
    # `git diff --check` has nothing to compare.
    foreach ($diffArgs in @('', '--cached')) {
        if ($diffArgs -eq '') {
            $diffOutput = @(& git diff --check 2>&1)
        }
        else {
            $diffOutput = @(& git diff --check $diffArgs 2>&1)
        }
        if ($LASTEXITCODE -ne 0) {
            foreach ($line in $diffOutput) { Add-Failure ('git diff --check: ' + $line) }
        }
    }

    # target files
    if ($Paths -and $Paths.Count -gt 0) {
        $files = @($Paths)
    }
    else {
        $files = @(& git ls-files 'README.md' 'AGENTS.md' 'GLOSSARY.md' 'docs/**/*.md' |
            Where-Object { $_ -notlike 'docs/history/*' })
    }

    $pathPattern = '`([^`\r\n]+\.(?:md|cs|rs|ts|tsx|svelte|json|ya?ml|ps1|psm1|wxs|mjs|js))`'
    $symbolPattern = '`([A-Z][A-Za-z0-9_]+)\.([A-Za-z_][A-Za-z0-9_]*)`'
    $bannedPatterns = @(
        'only plaintext output path',
        'only plaintext-to-stdout',
        '唯一明文输出路径',
        'Plaintext is NEVER written to'
    )
    $roots = @('src/', 'docs/', 'service/', 'scripts/', 'tests/', 'frontend/', '.github/')

    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file)) {
            Add-Failure ("{0}: requested document does not exist" -f $file)
            continue
        }
        $lines = @(Get-DocLines $file)
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $lineNo = $i + 1

            # trailing whitespace (works on a clean CI checkout too)
            if ($line -match '[ \t]+$') {
                Add-Failure ("{0}:{1}: trailing whitespace" -f $file, $lineNo)
            }

            # 3. cited file paths
            foreach ($m in [regex]::Matches($line, $pathPattern)) {
                $token = $m.Groups[1].Value
                if ($token -match '[<*>%|''"]') { continue }
                if (-not ($roots | Where-Object { $token.StartsWith($_) })) { continue }
                $candidate = $token.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
                if (-not (Test-Path -LiteralPath (Join-Path $root $candidate))) {
                    Add-Failure ("{0}:{1}: cited path does not exist: {2}" -f $file, $lineNo, $token)
                }
            }

            # 4. cited Class.Member symbols
            foreach ($m in [regex]::Matches($line, $symbolPattern)) {
                $left = $m.Groups[1].Value
                $right = $m.Groups[2].Value
                $declaring = Resolve-SymbolFiles $left
                if ($declaring.Count -eq 0) { continue }
                $memberPattern = ('\b{0}\b' -f $right)
                $found = @($declaring | Where-Object {
                        $null -ne (& git grep -l -E $memberPattern -- $_ 2>$null)
                    })
                if ($found.Count -eq 0) {
                    Add-Failure ("{0}:{1}: cited member not found: {2}.{3}" -f $file, $lineNo, $left, $right)
                }
            }

            # 5. banned stale claims
            foreach ($banned in $bannedPatterns) {
                if ($line.IndexOf($banned, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                    Add-Failure ("{0}:{1}: stale claim: {2}" -f $file, $lineNo, $banned)
                }
            }
        }
    }

    if ($failures.Count -gt 0) {
        foreach ($f in $failures) { Write-Host ('  ' + $f) }
        Write-Host ("FAIL: {0} docs issue(s) across {1} file(s)" -f $failures.Count, $files.Count)
        exit 1
    }
    Write-Host ("PASS: docs checks clean across {0} file(s)" -f $files.Count)
    exit 0
}
finally {
    Pop-Location
}
