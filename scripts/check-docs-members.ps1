# Declaration-aware C# member lookup shared by both check-docs search arms.
# This is a deliberately small heuristic, not a C# compiler. It masks comments
# and literals, walks top-level type bodies, and reads direct member headers.
# Method/property bodies, object initializers and nested type members therefore
# cannot impersonate members of the containing type. Nested type names count as
# members; their own members do not. No dependency on dotnet or an SDK is needed.

# --- Source masking ---------------------------------------------------------
# Comments and every string form are replaced with spaces (offsets and newlines
# preserved) so only real code reaches the declaration reader. A character
# scanner is used instead of one regex because an interpolated string may embed
# quoted strings inside its { ... } holes (e.g. $"x{foo("y")}z"); a flat regex
# stops at the inner quote and would leak code. Expressions inside a hole are
# uses, never declarations of the enclosing type, so the whole string is opaque.
# Each helper is a pure (source, index) -> end-index function; they call one
# another recursively but carry no shared mutable state.

function Skip-CSharpCharLiteral([string]$Source, [int]$Index) {
    $n = $Source.Length
    $j = $Index + 1
    while ($j -lt $n) {
        if ($Source[$j] -eq '\') { $j += 2; continue }
        if ($Source[$j] -eq "'") { return $j + 1 }
        $j++
    }
    return $n
}

function Skip-CSharpString([string]$Source, [int]$Index) {
    $n = $Source.Length
    $start = $Index
    $dollars = 0; $verbatim = $false
    $i = $Index
    while ($i -lt $n -and ($Source[$i] -eq '$' -or $Source[$i] -eq '@')) {
        if ($Source[$i] -eq '$') { $dollars++ } else { $verbatim = $true }
        $i++
    }
    if ($i -ge $n -or $Source[$i] -ne '"') { return $start + 1 }
    $q = $i
    while ($q -lt $n -and $Source[$q] -eq '"') { $q++ }
    $qcount = $q - $i
    if ($qcount -ge 3 -and -not $verbatim) {
        # Raw string: close at the first run of at least $qcount quotes. Raw and
        # verbatim are mutually exclusive, so @"""" is a verbatim escaped quote.
        $j = $q
        while ($j -lt $n) {
            if ($Source[$j] -eq '"') {
                $r = $j
                while ($r -lt $n -and $Source[$r] -eq '"') { $r++ }
                if ($r - $j -ge $qcount) { return $r }
                $j = $r
            }
            else { $j++ }
        }
        return $n
    }
    $interpolated = $dollars -gt 0
    $j = $i + 1
    while ($j -lt $n) {
        $c = $Source[$j]
        if ($verbatim) {
            if ($c -eq '"') {
                if ($j + 1 -lt $n -and $Source[$j + 1] -eq '"') { $j += 2; continue }
                return $j + 1
            }
            if ($interpolated -and $c -eq '{') {
                if ($j + 1 -lt $n -and $Source[$j + 1] -eq '{') { $j += 2; continue }
                $j = Skip-CSharpInterpolationHole $Source ($j + 1)
                continue
            }
            $j++
        }
        else {
            if ($c -eq '\') { $j += 2; continue }
            if ($c -eq '"') { return $j + 1 }
            if ($interpolated -and $c -eq '{') {
                if ($j + 1 -lt $n -and $Source[$j + 1] -eq '{') { $j += 2; continue }
                $j = Skip-CSharpInterpolationHole $Source ($j + 1)
                continue
            }
            $j++
        }
    }
    return $n
}

function Skip-CSharpInterpolationHole([string]$Source, [int]$Index) {
    # $Index is just after the opening '{'. Return just after the matching '}'.
    # Holes may nest braces and contain their own string/char literals/comments.
    $n = $Source.Length
    $depth = 1
    $j = $Index
    while ($j -lt $n -and $depth -gt 0) {
        $c = $Source[$j]
        if ($c -eq '{') { $depth++; $j++ }
        elseif ($c -eq '}') { $depth--; $j++ }
        elseif ($c -eq '"' -or $c -eq '$' -or $c -eq '@') { $j = Skip-CSharpString $Source $j }
        elseif ($c -eq "'") { $j = Skip-CSharpCharLiteral $Source $j }
        elseif ($c -eq '/' -and $j + 1 -lt $n -and $Source[$j + 1] -eq '/') {
            while ($j -lt $n -and $Source[$j] -ne "`n" -and $Source[$j] -ne "`r") { $j++ }
        }
        elseif ($c -eq '/' -and $j + 1 -lt $n -and $Source[$j + 1] -eq '*') {
            $j += 2
            while ($j + 1 -lt $n -and -not ($Source[$j] -eq '*' -and $Source[$j + 1] -eq '/')) { $j++ }
            $j += 2
        }
        else { $j++ }
    }
    return $j
}

function Get-MaskedCSharpSource([string]$Source) {
    $n = $Source.Length
    $out = $Source.ToCharArray()
    $i = 0
    while ($i -lt $n) {
        $c = $out[$i]
        if ($c -eq '/' -and $i + 1 -lt $n -and $out[$i + 1] -eq '/') {
            $j = $i
            while ($j -lt $n -and $out[$j] -ne "`n" -and $out[$j] -ne "`r") { $j++ }
            for ($k = $i; $k -lt $j; $k++) { if ($out[$k] -ne "`r") { $out[$k] = [char]' ' } }
            $i = $j
            continue
        }
        if ($c -eq '/' -and $i + 1 -lt $n -and $out[$i + 1] -eq '*') {
            $j = $i + 2
            while ($j + 1 -lt $n -and -not ($out[$j] -eq '*' -and $out[$j + 1] -eq '/')) { $j++ }
            $end = [Math]::Min($n, $j + 2)
            for ($k = $i; $k -lt $end; $k++) { if ($out[$k] -ne "`r" -and $out[$k] -ne "`n") { $out[$k] = [char]' ' } }
            $i = $end
            continue
        }
        if ($c -eq '"' -or $c -eq '$' -or $c -eq '@') {
            $end = Skip-CSharpString $Source $i
            # A leading '@' may be a verbatim identifier (@class), not a string;
            # Skip-CSharpString returns Index+1 in that case, so only mask when a
            # quote actually followed. '"' and '$' always begin a string here.
            $isString = ($end -gt $i) -and ($c -eq '"' -or $c -eq '$' -or ($c -eq '@' -and $end -gt $i + 1))
            if ($isString) {
                for ($k = $i; $k -lt $end; $k++) { if ($out[$k] -ne "`r" -and $out[$k] -ne "`n") { $out[$k] = [char]' ' } }
                $i = $end
                continue
            }
            $i++
            continue
        }
        if ($c -eq "'") {
            $end = Skip-CSharpCharLiteral $Source $i
            for ($k = $i; $k -lt $end; $k++) { if ($out[$k] -ne "`r" -and $out[$k] -ne "`n") { $out[$k] = [char]' ' } }
            $i = $end
            continue
        }
        $i++
    }
    return ($out -join '')
}

function Get-CSharpClosingIndex([string]$Source, [int]$Start, [char]$Open, [char]$Close) {
    $depth = 0
    for ($i = $Start; $i -lt $Source.Length; $i++) {
        if ($Source[$i] -eq $Open) { $depth++ }
        elseif ($Source[$i] -eq $Close) {
            $depth--
            if ($depth -eq 0) { return $i }
        }
    }
    return -1
}

function Test-CSharpGenericAngle([string]$Source, [int]$Index) {
    # Distinguish a generic argument list from a comparison in an initializer.
    # A generic list has a matching '>' and its top level contains no assignment
    # or arithmetic/logic operators; `a < b, c = d > e` is not a generic list.
    $depth = 0
    for ($i = $Index; $i -lt $Source.Length; $i++) {
        $c = $Source[$i]
        if ($c -eq '<') { $depth++ }
        elseif ($c -eq '>') {
            $depth--
            if ($depth -eq 0) { return $true }
        }
        elseif ($depth -eq 1 -and '=;{}+-*/%&|^!~'.IndexOf($c) -ge 0) { return $false }
    }
    return $false
}

function Split-CSharpTopLevel([string]$Source, [switch]$EnumMembers) {
    # Commas in generic arguments, tuples, attributes, array ranks, object
    # initializers and default parameter expressions are not field/parameter
    # separators. Keep tracking nesting inside a field initializer so a comma
    # in `new("x", RegexOptions.Compiled)` cannot invent a `RegexOptions`
    # declarator; only a comma back at depth zero starts the next declarator.
    $parts = New-Object System.Collections.Generic.List[string]
    $begin = 0; $declaratorEnd = -1
    $paren = 0; $bracket = 0; $brace = 0; $angle = 0
    $initializer = $false
    for ($i = 0; $i -lt $Source.Length; $i++) {
        $c = $Source[$i]
        if ($c -eq ',' -and $paren -eq 0 -and $bracket -eq 0 -and $brace -eq 0 -and $angle -eq 0) {
            $end = if ($initializer) { $declaratorEnd } else { $i }
            $parts.Add($Source.Substring($begin, $end - $begin))
            $begin = $i + 1
            $initializer = $false
            continue
        }
        # Skip two-character operators so their <, >, = characters do not look
        # like generic delimiters or the start of an initializer. A lambda `=>`
        # inside an initializer is part of the expression, not an expression-
        # bodied member terminator.
        if ($i + 1 -lt $Source.Length) {
            $two = $Source.Substring($i, 2)
            if ($two -eq '=>') {
                if (-not $initializer) {
                    $parts.Add($Source.Substring($begin, $i - $begin))
                    return $parts.ToArray()
                }
                $i++
                continue
            }
            if ($two -in @('==', '!=', '<=', '>=', '&&', '||', '??', '<<')) { $i++; continue }
            if ($two -eq '>>' -and $angle -eq 0) { $i++; continue }
        }
        if ($c -eq '(') { $paren++ }
        elseif ($c -eq ')') { $paren-- }
        elseif ($c -eq '[') { $bracket++ }
        elseif ($c -eq ']') { $bracket-- }
        elseif ($c -eq '{') { $brace++ }
        elseif ($c -eq '}') { $brace-- }
        elseif (-not $EnumMembers -and $c -eq '<') {
            if (Test-CSharpGenericAngle $Source $i) { $angle++ }
        }
        elseif (-not $EnumMembers -and $c -eq '>' -and $angle -gt 0) { $angle-- }
        elseif ($c -eq '=' -and -not $initializer -and
                $paren -eq 0 -and $bracket -eq 0 -and $brace -eq 0 -and $angle -eq 0) {
            if (($i -eq 0 -or '=!<>'.IndexOf($Source[$i - 1]) -lt 0) -and
                ($i + 1 -eq $Source.Length -or $Source[$i + 1] -ne '=')) {
                $initializer = $true
                $declaratorEnd = $i
            }
        }
    }
    $end = if ($initializer) { $declaratorEnd } else { $Source.Length }
    $parts.Add($Source.Substring($begin, $end - $begin))
    return $parts.ToArray()
}

function Test-CSharpInitializerHeader([string]$Header) {
    $paren = 0; $bracket = 0; $angle = 0
    for ($i = 0; $i -lt $Header.Length; $i++) {
        $c = $Header[$i]
        if ($c -eq '(') { $paren++ }
        elseif ($c -eq ')') { $paren-- }
        elseif ($c -eq '[') { $bracket++ }
        elseif ($c -eq ']') { $bracket-- }
        elseif ($c -eq '<') { $angle++ }
        elseif ($c -eq '>') { $angle-- }
        elseif ($c -eq '=' -and $paren -eq 0 -and $bracket -eq 0 -and $angle -eq 0) {
            if ($i + 1 -lt $Header.Length -and $Header[$i + 1] -eq '>') { return $true }
            if (($i -eq 0 -or '=!<>'.IndexOf($Header[$i - 1]) -lt 0) -and
                ($i + 1 -eq $Header.Length -or $Header[$i + 1] -ne '=')) { return $true }
        }
    }
    return $false
}

function Get-CSharpMethodName([string]$Part) {
    # The method name is the identifier immediately before the parameter list.
    # The parameter list is the first top-level (...) whose close is followed by
    # a body, terminator, expression arrow, constraint or constructor initializer.
    # A tuple RETURN type also opens a top-level (...), but its close is followed
    # by the method name, so it does not qualify and is skipped. This is why
    # `static (int total, int failed) RotateAll(...)` yields RotateAll, not static.
    for ($i = 0; $i -lt $Part.Length; $i++) {
        if ($Part[$i] -ne '(') { continue }
        $close = Get-CSharpClosingIndex $Part $i '(' ')'
        if ($close -lt 0) { return $null }
        $rest = $Part.Substring($close + 1).TrimStart()
        $isParamList = ($rest.Length -eq 0) -or ($rest[0] -eq '{') -or ($rest[0] -eq ';') -or
            ($rest[0] -eq '=') -or ($rest[0] -eq ':') -or ($rest -match '^where\b')
        if ($isParamList) {
            $k = $i - 1
            while ($k -ge 0 -and [char]::IsWhiteSpace($Part[$k])) { $k-- }
            # Skip a generic parameter list: Echo<T>(...) -> Echo.
            if ($k -ge 0 -and $Part[$k] -eq '>') {
                $angle = 0
                while ($k -ge 0) {
                    if ($Part[$k] -eq '>') { $angle++ }
                    elseif ($Part[$k] -eq '<') { $angle--; if ($angle -eq 0) { $k--; break } }
                    $k--
                }
                while ($k -ge 0 -and [char]::IsWhiteSpace($Part[$k])) { $k-- }
            }
            $end = $k + 1
            while ($k -ge 0 -and $Part[$k] -match '[A-Za-z0-9_]') { $k-- }
            $name = $Part.Substring($k + 1, $end - ($k + 1))
            if ($name -match '^[A-Za-z_][A-Za-z0-9_]*$') { return $name }
            return $null
        }
        $i = $close
    }
    return $null
}

function Get-CSharpMemberNames([string]$Header, [string]$Kind) {
    $Header = [regex]::Replace($Header, '^\s*(\[[^\]]*\]\s*)*', '')
    if ($Kind -eq 'enum') {
        $entry = [regex]::Match($Header, '^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\b')
        if ($entry.Success) { return @($entry.Groups['name'].Value) }
        return @()
    }
    # Only the prefix before a parameter list, initializer or body can name a
    # nested type. Generic constraints (`where T : class where U : struct`) and
    # expression bodies (`from record in ...`) are not type declarations.
    $typePrefix = [regex]::Replace($Header, '(?s)[(={].*$', '')
    $nested = [regex]::Match($typePrefix, '\b(?:class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b')
    if ($nested.Success) { return @($nested.Groups['name'].Value) }
    if ($Header -match '\bthis\s*\[') { return @('Item') }
    $operator = [regex]::Match($Header, '\boperator\s+(?<op>\+\+|--|==|!=|<=|>=|<<|>>|[-+*/%&|^!~<>]|true|false)\s*\(')
    if ($operator.Success) {
        $map = @{
            '+' = 'op_Addition'; '-' = 'op_Subtraction'; '*' = 'op_Multiply'; '/' = 'op_Division'
            '%' = 'op_Modulus'; '&' = 'op_BitwiseAnd'; '|' = 'op_BitwiseOr'; '^' = 'op_ExclusiveOr'
            '<<' = 'op_LeftShift'; '>>' = 'op_RightShift'; '==' = 'op_Equality'; '!=' = 'op_Inequality'
            '<' = 'op_LessThan'; '>' = 'op_GreaterThan'; '<=' = 'op_LessThanOrEqual'; '>=' = 'op_GreaterThanOrEqual'
            '!' = 'op_LogicalNot'; '~' = 'op_OnesComplement'; '++' = 'op_Increment'; '--' = 'op_Decrement'
            'true' = 'op_True'; 'false' = 'op_False'
        }
        $op = $operator.Groups['op'].Value
        if ($map.ContainsKey($op)) { return @($map[$op]) }
        return @()
    }
    if ($Header -match '\b(?:implicit|explicit)\s+operator\b') {
        if ($Header -match '\bimplicit\s+operator\b') { return @('op_Implicit') }
        return @('op_Explicit')
    }

    $names = New-Object System.Collections.Generic.List[string]
    $declarators = @(Split-CSharpTopLevel $Header)
    for ($i = 0; $i -lt $declarators.Count; $i++) {
        $part = $declarators[$i]
        # A method header includes its parameter list. Take the identifier just
        # before that list, not a parameter name or a modifier sitting before a
        # tuple return type. Explicit interface members, constructors and generic
        # methods use this arm too.
        if ($i -eq 0) {
            $method = Get-CSharpMethodName $part
            if ($null -ne $method) {
                $names.Add($method)
                break
            }
        }
        if ($i -gt 0) {
            $field = [regex]::Match($part, '^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\b')
        }
        else {
            # The last identifier in a field/property/event header is its name;
            # preceding identifiers belong to its type or modifiers.
            $fields = [regex]::Matches($part, '\b(?<name>[A-Za-z_][A-Za-z0-9_]*)\b')
            $field = if ($fields.Count -gt 0) { $fields[$fields.Count - 1] } else { $null }
        }
        if ($null -ne $field -and $field.Success -and $field.Groups['name'].Value -cne 'this') {
            $names.Add($field.Groups['name'].Value)
        }
    }
    return $names.ToArray()
}

function Get-CSharpTypeSpans([string]$Masked) {
    $pattern = '\b(?<kind>class|struct|interface|enum|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b'
    $spans = New-Object System.Collections.Generic.List[object]
    foreach ($match in [regex]::Matches($Masked, $pattern)) {
        $end = $match.Index + $match.Length
        while ($end -lt $Masked.Length -and $Masked[$end] -ne '{' -and $Masked[$end] -ne ';') { $end++ }
        if ($end -eq $Masked.Length) { continue }
        $close = $end
        if ($Masked[$end] -eq '{') {
            $close = Get-CSharpClosingIndex $Masked $end '{' '}'
            if ($close -lt 0) { continue }
        }
        $spans.Add([pscustomobject]@{
            Name = $match.Groups['name'].Value
            Kind = $match.Groups['kind'].Value
            Start = $match.Index
            HeaderEnd = $end
            BodyStart = $(if ($Masked[$end] -eq '{') { $end } else { -1 })
            End = $close
        })
    }
    return $spans.ToArray()
}

function Get-CSharpDeclarations([string]$Source) {
    $masked = Get-MaskedCSharpSource $Source
    $types = @(Get-CSharpTypeSpans $masked)
    $result = New-Object System.Collections.Generic.List[object]
    foreach ($type in $types) {
        # A file may contain several top-level types. Keep the existing
        # filename-as-module citation convention, but never attribute a nested
        # type's fields to its parent (or to the filename).
        $nestedIn = @($types | Where-Object {
            $_.Start -lt $type.Start -and $_.BodyStart -ge 0 -and $_.End -gt $type.Start
        })
        if ($nestedIn.Count -gt 0) { continue }
        $kind = if ($type.Kind -eq 'enum') { 'enum' } else { 'type' }
        if ($type.Kind -like 'record*') {
            # Only a parameter list immediately after the record name (and any
            # generic arguments) is positional. `record Foo : Base(Bar)` has no
            # positional members; Bar is a base-constructor argument.
            $nameEnd = $type.Start + $type.Kind.Length
            while ($nameEnd -lt $type.HeaderEnd -and [char]::IsWhiteSpace($masked[$nameEnd])) { $nameEnd++ }
            if ($nameEnd -lt $type.HeaderEnd -and $masked[$nameEnd] -eq '@') { $nameEnd++ }
            while ($nameEnd -lt $type.HeaderEnd -and $masked[$nameEnd] -match '[A-Za-z0-9_]') { $nameEnd++ }
            while ($nameEnd -lt $type.HeaderEnd -and [char]::IsWhiteSpace($masked[$nameEnd])) { $nameEnd++ }
            if ($nameEnd -lt $type.HeaderEnd -and $masked[$nameEnd] -eq '<') {
                $genericEnd = Get-CSharpClosingIndex $masked $nameEnd '<' '>'
                if ($genericEnd -ge 0) {
                    $nameEnd = $genericEnd + 1
                    while ($nameEnd -lt $type.HeaderEnd -and [char]::IsWhiteSpace($masked[$nameEnd])) { $nameEnd++ }
                }
            }
            $open = if ($nameEnd -lt $type.HeaderEnd -and $masked[$nameEnd] -eq '(') { $nameEnd } else { -1 }
            if ($open -ge 0) {
                $close = Get-CSharpClosingIndex $masked $open '(' ')'
                if ($close -gt $open -and $close -le $type.HeaderEnd) {
                    $parameters = $masked.Substring($open + 1, $close - $open - 1)
                    foreach ($parameter in @(Split-CSharpTopLevel $parameters)) {
                        foreach ($name in @(Get-CSharpMemberNames $parameter 'field')) {
                            $result.Add([pscustomobject]@{ Type = $type.Name; Name = $name })
                        }
                    }
                }
            }
        }
        if ($type.BodyStart -lt 0) { continue }
        if ($kind -eq 'enum') {
            $body = $masked.Substring($type.BodyStart + 1, $type.End - $type.BodyStart - 1)
            foreach ($entry in @(Split-CSharpTopLevel $body -EnumMembers)) {
                foreach ($name in @(Get-CSharpMemberNames $entry $kind)) {
                    $result.Add([pscustomobject]@{ Type = $type.Name; Name = $name })
                }
            }
            continue
        }
        $begin = $type.BodyStart + 1
        $header = ''
        for ($i = $begin; $i -lt $type.End; $i++) {
            $c = $masked[$i]
            # A member attribute is metadata, not a body. Skip it whole so a
            # collection initializer inside an argument cannot leak a type name
            # as a member (e.g. [Attr(new List<int> { 1 })] public int Real;).
            if ($c -eq '[' -and $header.Trim().Length -eq 0) {
                $attributeEnd = Get-CSharpClosingIndex $masked $i '[' ']'
                if ($attributeEnd -ge 0) { $i = $attributeEnd; continue }
            }
            if ($c -eq '{') {
                $close = Get-CSharpClosingIndex $masked $i '{' '}'
                if ($close -lt 0) { break }
                $header += ' {} '
                $i = $close
                foreach ($name in @(Get-CSharpMemberNames $header $kind)) {
                    $result.Add([pscustomobject]@{ Type = $type.Name; Name = $name })
                }
                # Blocks in field initializers/expression bodies belong to the
                # declaration and must not hide a following comma declarator.
                if (-not (Test-CSharpInitializerHeader $header)) { $header = '' }
            }
            elseif ($c -eq ';') {
                # An initializer after an accessor body is not a new member.
                if ($header -notmatch '^\s*=') {
                    foreach ($name in @(Get-CSharpMemberNames $header $kind)) {
                        $result.Add([pscustomobject]@{ Type = $type.Name; Name = $name })
                    }
                }
                $header = ''
            }
            else { $header += $c }
        }
    }
    return $result.ToArray()
}
