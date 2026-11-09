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
#   4. backticked Class.Member citations resolve to a declared member
#      (types/namespaces with no local class or file are skipped; a member
#      declared in a sibling `partial class` file still counts. Comments,
#      literals, calls, initializers and local variables are not declarations.)
#   5. banned stale claims that keep coming back ("only plaintext output path",
#      "唯一明文输出路径", "Plaintext is NEVER written to")
#   6. words the glossary says to avoid, collected from its _Avoid_ lines and
#      checked in the in-scope English documents. Inline code spans and link
#      targets are skipped: a command name such as `service reload` is part of
#      the CLI surface and must not be renamed by a prose rule.
#   7. translation structure and sync metadata: each translated README under
#      docs/i18n mirrors the canonical English README section order and
#      heading levels, and carries the required top comments pointing to the
#      canonical file and recording the latest English commit SHA.
#
# Scope: README.md, AGENTS.md, AGENTS.cli.md, SECURITY.md, GLOSSARY.md, every
# .md under docs/ except docs/history, and the three repository-hosting docs
# under .github/. The full scan re-derives that set independently of how it was
# enumerated and fails loudly on anything missing - silence is how this gate
# lost its "green means clean" reading once already.
# Pass -Paths to check only specific files. Source changes still run
# scripts/test-all.ps1.
#
# Usage: powershell -NoProfile -File scripts/check-docs.ps1 [-Paths docs/architecture.md]
param([string[]]$Paths)

$ErrorActionPreference = 'Continue'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'check-docs-members.ps1')
Push-Location $root
$failures = New-Object System.Collections.Generic.List[string]
$script:memberCache = @{}
$script:partialCache = @{}
$script:declarationCache = @{}
$script:typeNameCache = @{}
$hostingDocs = @('.github/CONTRIBUTING.md', '.github/PULL_REQUEST_TEMPLATE.md', '.github/CODE_OF_CONDUCT.md')

function Add-Failure([string]$Message) { $script:failures.Add($Message) | Out-Null }

function Get-DocLines([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return @() }
    return [System.IO.File]::ReadAllLines((Resolve-Path -LiteralPath $Path).Path, [System.Text.Encoding]::UTF8)
}

function Get-GlossaryAvoidTerms {
    # The glossary is the only source: each `_Avoid_:` line names the synonyms a
    # reader must not use for that term. Reading them here keeps the word list
    # editable in one place instead of copied into this script.
    $terms = New-Object System.Collections.Generic.List[string]
    foreach ($line in @(Get-DocLines 'GLOSSARY.md')) {
        $m = [regex]::Match($line, '^_Avoid_:\s*(.+)$')
        if (-not $m.Success) { continue }
        foreach ($part in ($m.Groups[1].Value -split ',')) {
            $term = $part.Trim().TrimEnd('.')
            if ($term.Length -gt 0 -and -not $terms.Contains($term)) { $terms.Add($term) }
        }
    }
    return $terms.ToArray()
}

function Get-AvoidWordVariants([string]$Term) {
    # A word list catches the forms a writer actually uses: "backend"/"backends",
    # "spawn"/"spawned"/"spawning", "config file"/"config files". A phrase only
    # takes a plural; a single word takes the common verb inflections too. A
    # derived noun such as "deployment" is a different word and stays out.
    $variants = New-Object System.Collections.Generic.List[string]
    $variants.Add($Term)
    if ($Term -match ' ') {
        # A phrase takes a plural ("config files"), except a phrase that names
        # an action with its object ("start a profile"), where the plural is
        # not a word a writer would use.
        if ($Term -notlike 'start *') { $variants.Add($Term + 's') }
        return $variants.ToArray()
    }
    $variants.Add($Term + 's')
    # Only the avoided verbs take -ed/-ing; adding those endings to a noun
    # ("connectioned", "backending") would just pad the regex with noise.
    $verbTerms = @('activate', 'deploy', 'execute', 'poll', 're-fetch', 'rekey', 'reload', 'spawn')
    if ($verbTerms -contains $Term) {
        if ($Term.EndsWith('e') -and $Term.Length -gt 1) {
            $stem = $Term.Substring(0, $Term.Length - 1)
            $variants.Add($Term + 'd')
            foreach ($suffix in @('ed', 'ing')) { $variants.Add($stem + $suffix) }
        }
        else {
            foreach ($suffix in @('ed', 'ing')) { $variants.Add($Term + $suffix) }
        }
    }
    return $variants.ToArray()
}

function Test-IsTranslationDoc([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    if ($Path -like 'docs/i18n/*') { return $true }
    $firstLines = @(Get-DocLines $Path | Select-Object -First 10)
    foreach ($l in $firstLines) {
        if ($l -match '<!--\s*(canonical|sync):') { return $true }
    }
    return $false
}

function Test-VocabularyFile([string]$Path) {
    # English documents only. The glossary owns the avoid list itself, the nine
    # translations are handled by their own tickets, and docs/assets carries
    # brand material that is not product prose.
    if ($Path -ceq 'GLOSSARY.md') { return $false }
    if ($Path -clike 'docs/i18n/*') { return $false }
    if ($Path -clike 'docs/assets/*') { return $false }
    if ($Path -clike 'docs/history/*') { return $false }
    if (Test-IsTranslationDoc $Path) { return $false }
    return $true
}

function Get-TrackedPaths([string[]]$Pathspec = @()) {
    # git quotes non-ASCII paths in line output ("docs/\345...md"), and the
    # trailing quote drops them from the *.md filter and the expected-set
    # cross-check alike: the file ends up silently unscanned. NUL-delimited
    # output is never quoted, so split on NUL instead of on newlines.
    $nul = [string][char]0
    $blob = @(& git ls-files -z @Pathspec 2>$null) -join $nul
    return @($blob -split $nul | Where-Object { $_ -ne '' })
}

function Resolve-CachedFiles([hashtable]$Cache, [string]$Key, [string]$Pattern) {
    if ($Cache.ContainsKey($Key)) { return $Cache[$Key] }
    $files = @(& git grep -l -E $Pattern -- '*.cs' 2>$null)
    $Cache[$Key] = $files
    return $files
}

function Resolve-SymbolFiles([string]$Left) {
    if ($script:memberCache.ContainsKey($Left)) { return $script:memberCache[$Left] }
    $files = @(& git ls-files "*/$Left.cs" 2>$null)
    if ($files.Count -eq 0) {
        $files = @(Resolve-CachedFiles $script:memberCache $Left ('class {0}\b' -f $Left))
    }
    else {
        $script:memberCache[$Left] = $files
    }
    return $files
}

function Resolve-PartialFiles([string]$Left) {
    # Anchor on the declaration, not the phrase: four files mention
    # `partial class Program` inside /// comments and would otherwise join the
    # fallback set, letting any stray occurrence of a member name count as a
    # hit - the same dilution this gate exists to prevent.
    $pattern = ('^[[:space:]]*([a-z]+[[:space:]]+)*partial[[:space:]]+class[[:space:]]+{0}\b' -f $Left)
    return @(Resolve-CachedFiles $script:partialCache $Left $pattern)
}

function Get-CachedDeclarations([string]$Path) {
    if ($script:declarationCache.ContainsKey($Path)) { return $script:declarationCache[$Path] }
    $full = Join-Path $root $Path
    $declarations = @()
    if (Test-Path -LiteralPath $full -PathType Leaf) {
        $source = [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
        $declarations = @(Get-CSharpDeclarations $source)
    }
    $script:declarationCache[$Path] = $declarations
    return $declarations
}

function Test-CSharpTypeInFile([string]$Path, [string]$Type) {
    if (-not $script:typeNameCache.ContainsKey($Path)) {
        $full = Join-Path $root $Path
        $names = @()
        if (Test-Path -LiteralPath $full -PathType Leaf) {
            $source = [System.IO.File]::ReadAllText($full, [System.Text.Encoding]::UTF8)
            $names = @(Get-CSharpTypeSpans (Get-MaskedCSharpSource $source) | ForEach-Object { $_.Name })
        }
        $script:typeNameCache[$Path] = $names
    }
    return @($script:typeNameCache[$Path] | Where-Object { $_ -ceq $Type }).Count -gt 0
}

function Find-MemberFiles([string[]]$Files, [string]$Type, [string]$Member, [switch]$TypeScoped) {
    $found = New-Object System.Collections.Generic.List[string]
    foreach ($file in $Files) {
        # If the candidate file actually declares the cited type, attribute
        # members only to that type. Otherwise keep the filename-as-module
        # convention (ProfileCommand.cs holds members of partial Program).
        # The partial fallback is always type-scoped.
        $scope = $TypeScoped -or (Test-CSharpTypeInFile $file $Type)
        foreach ($declaration in @(Get-CachedDeclarations $file)) {
            if ($declaration.Name -ceq $Member -and
                ((-not $scope -and -not $declaration.IsNested) -or
                 ($scope -and $declaration.Type -ceq $Type))) {
                $found.Add($file)
                break
            }
        }
    }
    return $found.ToArray()
}

$script:sectionMarkers = @(
    @{ Name = 'Env Manager'; Pattern = '(?i)env\s*manager' },
    @{ Name = 'Showcase'; Pattern = $null },
    @{ Name = 'GUI'; Pattern = '(?i)assets/gallery|gallery/' },
    @{ Name = 'Features'; Pattern = '(?i)plaintext-sinks' },
    @{ Name = 'For AI agents'; Pattern = '(?i)AGENTS\.cli\.md' },
    @{ Name = 'Security'; Pattern = '(?i)code-signing-policy\.md|SECURITY\.md' },
    @{ Name = 'Install'; Pattern = $null },
    @{ Name = 'MSI Installer'; Pattern = '(?i)releases|\.msi' },
    @{ Name = 'Portable'; Pattern = '(?i)releases|\.zip' },
    @{ Name = 'CLI-Only'; Pattern = '(?i)env-manager-cli' },
    @{ Name = 'Prerequisites'; Pattern = '(?i)WebView2|\.NET' },
    @{ Name = 'winget'; Pattern = '(?i)winget' },
    @{ Name = 'From Source'; Pattern = '(?i)build-and-release\.md' },
    @{ Name = 'Usage'; Pattern = $null },
    @{ Name = 'CLI'; Pattern = '(?i)env-manager-cli' },
    @{ Name = 'GUI'; Pattern = '(?i)env-manager\.exe' },
    @{ Name = 'Architecture'; Pattern = '(?i)Tauri|Svelte' },
    @{ Name = 'Secret providers'; Pattern = '(?i)KeepassXC|Bitwarden|1Password' },
    @{ Name = 'Service mode'; Pattern = '(?i)reconcile|sc\.exe|service' },
    @{ Name = 'Documentation'; Pattern = '(?i)architecture\.md' },
    @{ Name = 'Maintainers'; Pattern = '(?i)github\.com/Xxx91n' },
    @{ Name = 'Contributing'; Pattern = '(?i)AGENTS\.md' },
    @{ Name = 'License'; Pattern = '(?i)LICENSE' }
)

function Get-MarkdownSections([string[]]$Lines) {
    $sections = New-Object System.Collections.Generic.List[psobject]
    $inFence = $false
    $curHeading = $null
    $curBody = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $Lines.Count; $i++) {
        $line = $Lines[$i]
        $trimmed = $line.Trim()
        if ($trimmed -match '^(```|~~~)') {
            $inFence = -not $inFence
            if ($curHeading) { $curBody.Add($line) }
            continue
        }
        if (-not $inFence -and $line -match '^(\#{1,6})[ \t]+(.+)$') {
            if ($curHeading) {
                $sections.Add([pscustomobject]@{
                    LineNo = $curHeading.LineNo
                    Level = $curHeading.Level
                    Title = $curHeading.Title
                    Body = $curBody.ToArray()
                })
            }
            $curHeading = [pscustomobject]@{
                LineNo = $i + 1
                Level = $matches[1].Length
                Title = $matches[2].Trim()
            }
            $curBody = New-Object System.Collections.Generic.List[string]
        }
        elseif ($curHeading) {
            $curBody.Add($line)
        }
    }
    if ($curHeading) {
        $sections.Add([pscustomobject]@{
            LineNo = $curHeading.LineNo
            Level = $curHeading.Level
            Title = $curHeading.Title
            Body = $curBody.ToArray()
        })
    }
    return $sections.ToArray()
}

function Test-TranslationStructure([string]$Path, [string]$Root) {
    if (-not (Test-Path -LiteralPath $Path)) {
        Add-Failure ("{0}: translation file does not exist" -f $Path)
        return
    }
    $lines = @(Get-DocLines $Path)

    # 1. Parse top comments (canonical and sync)
    $canonical = $null
    $sync = $null
    foreach ($line in $lines) {
        if ($line -match '^\s*#') { break }
        if (-not $canonical -and $line -match '<!--\s*canonical:\s*([^\s>]+)\s*-->') {
            $canonical = $matches[1].Trim()
        }
        if (-not $sync -and $line -match '<!--\s*sync:\s*([0-9a-fA-F]+)\s*-->') {
            $sync = $matches[1].Trim()
        }
    }

    if (-not $canonical) {
        Add-Failure ("{0}: missing canonical top comment (expected '<!-- canonical: <path> -->')" -f $Path)
    }
    if (-not $sync) {
        Add-Failure ("{0}: missing sync top comment (expected '<!-- sync: <commit> -->')" -f $Path)
    }

    # 2. Resolve canonical path
    $canonicalPath = $null
    if ($canonical) {
        $parentDir = Split-Path -Parent (Resolve-Path -LiteralPath $Path).Path
        $cand1 = Join-Path $parentDir $canonical
        if (Test-Path -LiteralPath $cand1) {
            $canonicalPath = (Resolve-Path -LiteralPath $cand1).Path
        }
        else {
            $cand2 = Join-Path $Root $canonical
            if (Test-Path -LiteralPath $cand2) {
                $canonicalPath = (Resolve-Path -LiteralPath $cand2).Path
            }
            else {
                $trimmed = $canonical -replace '^\.\./\.\./', ''
                $cand3 = Join-Path $Root $trimmed
                if (Test-Path -LiteralPath $cand3) {
                    $canonicalPath = (Resolve-Path -LiteralPath $cand3).Path
                }
                else {
                    Add-Failure ("{0}: canonical target does not exist: {1}" -f $Path, $canonical)
                }
            }
        }
    }

    # 3. Check sync commit
    if ($sync) {
        $resolvedSync = (& git rev-parse --verify --quiet "$($sync)^{commit}" 2>$null)
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedSync)) {
            $isShallow = (& git rev-parse --is-shallow-repository 2>$null)
            if ($isShallow -eq 'true') {
                & git fetch --unshallow --quiet 2>$null
                $resolvedSync = (& git rev-parse --verify --quiet "$($sync)^{commit}" 2>$null)
            }
        }
        if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($resolvedSync)) {
            Add-Failure ("{0}: stale or unknown sync commit: {1}" -f $Path, $sync)
        }
        elseif ($canonicalPath) {
            $canonicalRel = Resolve-Path -LiteralPath $canonicalPath | ForEach-Object {
                $p = $_.Path
                if ($p.StartsWith($Root)) {
                    $p.Substring($Root.Length).TrimStart([System.IO.Path]::DirectorySeparatorChar, '/')
                } else { $p }
            }
            $canonicalCommit = (& git log -1 --format=%H -- $canonicalRel 2>$null)
            if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($canonicalCommit)) {
                $cSha = $canonicalCommit.Trim()
                $sSha = $resolvedSync.Trim()
                if ($sSha -ne $cSha) {
                    Add-Failure ("{0}: stale sync commit {1}; canonical {2} is at {3}" -f $Path, $sync, $canonicalRel, $cSha)
                }
            }
        }
    }

    # 4. Compare section structure
    if ($canonicalPath) {
        $canonLines = @(Get-DocLines $canonicalPath)
        $canonSections = @(Get-MarkdownSections $canonLines)
        $transSections = @(Get-MarkdownSections $lines)

        if ($transSections.Count -ne $canonSections.Count) {
            if ($transSections.Count -lt $canonSections.Count) {
                Add-Failure ("{0}: missing section (found {1} section(s), expected {2})" -f $Path, $transSections.Count, $canonSections.Count)
            }
            else {
                Add-Failure ("{0}: extra section (found {1} section(s), expected {2})" -f $Path, $transSections.Count, $canonSections.Count)
            }
        }

        $minCount = [Math]::Min($transSections.Count, $canonSections.Count)
        for ($i = 0; $i -lt $minCount; $i++) {
            $cs = $canonSections[$i]
            $ts = $transSections[$i]
            if ($ts.Level -ne $cs.Level) {
                Add-Failure ("{0}:{1}: section level mismatch at section {2}: got level {3}, expected level {4} for '{5}'" -f $Path, $ts.LineNo, ($i + 1), $ts.Level, $cs.Level, $cs.Title)
            }
        }

        if ($canonicalPath.EndsWith('README.md') -and $canonSections.Count -eq $script:sectionMarkers.Count) {
            for ($i = 0; $i -lt $minCount; $i++) {
                $marker = $script:sectionMarkers[$i]
                if ($marker.Pattern) {
                    $ts = $transSections[$i]
                    $secText = $ts.Title + "`n" + ($ts.Body -join "`n")
                    if ($secText -notmatch $marker.Pattern) {
                        Add-Failure ("{0}:{1}: sections out of order at section {2}: expected '{3}'" -f $Path, $ts.LineNo, ($i + 1), $marker.Name)
                    }
                }
            }
        }
    }
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
        # Do not lean on git's `**` pathspec here: on common git builds
        # docs/**/*.md silently skips the docs that sit directly in docs/ -
        # docs/architecture.md among them, which is how three dead citations
        # outlived every full run. Enumerate the tree and filter here instead.
        $files = @(Get-TrackedPaths -Pathspec 'README.md', 'AGENTS.md', 'AGENTS.cli.md', 'SECURITY.md', 'GLOSSARY.md', 'docs/' |
            Where-Object { $_ -like '*.md' -and $_ -notlike 'docs/history/*' })
        $files += @(Get-TrackedPaths -Pathspec $hostingDocs)
        # Independent cross-check: re-derive the set from the whole index and
        # filter here, rather than trusting the pathspec above, so a git glob
        # quirk surfaces as a hard failure naming the files instead of as
        # silent under-coverage.
        $expected = @(Get-TrackedPaths |
            Where-Object { $_ -like 'docs/*.md' -and $_ -notlike 'docs/history/*' })
        $expectedHosting = @(Get-TrackedPaths -Pathspec $hostingDocs)
        $missed = @(@($expected) + @($expectedHosting) | Where-Object { $files -notcontains $_ })
        if ($missed.Count -gt 0) {
            Add-Failure ('docs enumeration missed {0} file(s): {1}' -f $missed.Count, ($missed -join ', '))
        }
        # Floor on what "scanned" must mean. Both sets above parse git output
        # the same way, so a parsing failure empties them together and the
        # cross-check has nothing to miss; a doc deleted outright leaves it
        # equally silent. These anchors are the structural contract - if one
        # is renamed on purpose, update this list in the same change.
        foreach ($anchor in @('README.md', 'AGENTS.md', 'AGENTS.cli.md', 'SECURITY.md', 'GLOSSARY.md', 'docs/architecture.md') + $hostingDocs) {
            if ($files -notcontains $anchor) {
                Add-Failure ("docs enumeration missed required document: {0}" -f $anchor)
            }
        }
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
    $avoidTerms = @(Get-GlossaryAvoidTerms)
    $avoidVariants = @($avoidTerms | ForEach-Object { Get-AvoidWordVariants $_ } | Sort-Object -Unique)
    $avoidPattern = if ($avoidVariants.Count -gt 0) {
        '(?i)\b(?:' + (($avoidVariants | ForEach-Object { [regex]::Escape($_) }) -join '|') + ')\b'
    }
    else { $null }
    $vocabularyFiles = @($files | Where-Object { Test-VocabularyFile $_ })

    foreach ($file in $files) {
        if (-not (Test-Path -LiteralPath $file)) {
            Add-Failure ("{0}: requested document does not exist" -f $file)
            continue
        }
        $lines = @(Get-DocLines $file)
        $inFence = $false
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            $lineNo = $i + 1
            # Fenced blocks are literal examples (command lines, JSON, code), not
            # prose. Toggle on the delimiter line so a documented `service reload`
            # example is not renamed by the vocabulary rule.
            $isFenceLine = $line -match '^\s*(```|~~~)'
            if ($isFenceLine) { $inFence = -not $inFence }

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
                # A bare filename such as `ProfileCommand.cs` also looks like
                # Class.Member to the regex. It is a file citation, not a claim
                # that the type declares a member named "cs"; leave it to the
                # path gate (which only validates repo-rooted paths today).
                # Case-sensitive: `Output.Json` is a member claim, not the
                # lowercase filename extension `Output.json`.
                if ($m.Value -cmatch $pathPattern) { continue }
                $left = $m.Groups[1].Value
                $right = $m.Groups[2].Value
                $declaring = Resolve-SymbolFiles $left
                if ($declaring.Count -eq 0) { continue }
                $found = @(Find-MemberFiles $declaring $left $right)
                if ($found.Count -eq 0) {
                    # A partial class keeps the member in a sibling declaration
                    # (Program spans src/Program.cs and src/CliRuntime.cs): retry
                    # across those before calling the citation dead. This arm is
                    # only reachable once a declaring file resolved and missed,
                    # so the "no local class or file, skip it" leniency above
                    # stays exactly as it was. Both arms share the same
                    # declaration-aware reader; an identifier occurrence in a
                    # comment, literal, call or initializer is not a member.
                    $partialFiles = @(Resolve-PartialFiles $left)
                    if ($partialFiles.Count -gt 0) {
                        $found = @(Find-MemberFiles $partialFiles $left $right -TypeScoped)
                    }
                }
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

            # 6. glossary avoid words. Inline code spans keep their own names
            # (command names, argument names, identifiers, real error text), so
            # strip them before matching; link targets are not prose either.
            if ($avoidPattern -and -not $isFenceLine -and -not $inFence -and ($vocabularyFiles -contains $file)) {
                $prose = [regex]::Replace($line, '`[^`]*`', ' ')
                $prose = [regex]::Replace($prose, '\]\([^)]*\)', ']')
                $hits = @([regex]::Matches($prose, $avoidPattern) | ForEach-Object { $_.Value } | Sort-Object -Unique)
                foreach ($hit in $hits) {
                    Add-Failure ("{0}:{1}: avoided word: {2}" -f $file, $lineNo, $hit)
                }
            }
        }
    }

    # 7. translation structure and sync metadata check: each translated README
    # under docs/i18n mirrors the canonical English README section order and
    # heading levels, and carries the required top comments pointing to the
    # canonical file and recording the latest English commit SHA.
    $expectedLocales = @('ar', 'de', 'es', 'fr', 'ja', 'ko', 'pt', 'ru', 'zh_CN')
    $translationFiles = New-Object System.Collections.Generic.List[string]

    if ($Paths -and $Paths.Count -gt 0) {
        foreach ($p in $files) {
            if ($p -like 'docs/i18n/*.md' -or (Test-IsTranslationDoc $p)) {
                $translationFiles.Add($p)
            }
        }
    }
    else {
        foreach ($loc in $expectedLocales) {
            $expectedDoc = "docs/i18n/README.$loc.md"
            if (-not (Test-Path -LiteralPath (Join-Path $root $expectedDoc))) {
                Add-Failure ("missing required translation document: {0}" -f $expectedDoc)
            }
            else {
                $translationFiles.Add($expectedDoc)
            }
        }
    }

    foreach ($tFile in $translationFiles) {
        Test-TranslationStructure -Path $tFile -Root $root
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
