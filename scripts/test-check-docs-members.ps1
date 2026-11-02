# Self-test for the declaration-aware Class.Member gate. No compiler, no network,
# no registry: synthetic C# fixtures exercise the shared reader, then the real
# check-docs entry point is pointed at temporary documents citing this checkout.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
. (Join-Path $PSScriptRoot 'check-docs-members.ps1')
$failures = New-Object System.Collections.Generic.List[string]
$checks = 0

function Assert-Contains([string[]]$Actual, [string]$Expected, [string]$Label) {
    $script:checks++
    if ($Actual -ccontains $Expected) { Write-Host "  ok: $Label" }
    else { $script:failures.Add("$Label (expected $Expected, got: $($Actual -join ', '))") }
}
function Assert-Missing([string[]]$Actual, [string]$Unexpected, [string]$Label) {
    $script:checks++
    if ($Actual -ccontains $Unexpected) {
        $script:failures.Add("$Label (did not expect $Unexpected)")
    }
    else { Write-Host "  ok: $Label" }
}

$fixture = @'
namespace Fixture;

// CommentOnly must not count.
/* BlockOnly must not count either. */
public partial class Widget
{
    private const string Text = "StringOnly must not count";
    private const string Verbatim = @"VerbatimOnly must not count";
    private const string Raw = """RawOnly must not count""";
    private const string Interpolated = $"{Text} InterpolatedOnly must not count";
    private string EmbeddedQuote = $"x{("EmbeddedQuoteOnly")}y";
    private string NestedInterpolation = $"outer {$"inner {("NestedInterpolationOnly")}"} tail";
    private string CharHole = $"char {'}'}";
    private static readonly System.Text.RegularExpressions.Regex Compiled =
        new("x", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static readonly Widget Instance = new Widget();
    private int first = 1, second = 2;
    private int initialized = Make(), afterInitializer = 3;
    private int leftComparison = 1 > 0, rightComparison = 2 < 3;
    private Options Initialized = new Options { InitializerOnly = true }, AfterOptions = null;
    public string? Name { get; set; }
    public event EventHandler? Changed;
    public int ExpressionBodied => Make();
    public int this[int index] => index;

    public Widget() { }
    public static int Make() => 1;
    public static T Echo<T>(T value) => value;
    public static (int total, int failed) Pair() => (1, 0);
    public static int MultiLine(
        string value,
        int count = 1) => count;
    string IContract.ContractMethod() => "";
    public static Widget operator +(Widget left, Widget right) => left;

    public void Body()
    {
        int LocalOnly = 1;
        var options = new Options { InitializerOnly = true };
        CalledOnly();
    }
    private void CalledOnly() { }

    public class Nested
    {
        public int NestedOnly;
    }
}

public partial class Widget
{
    internal static readonly string SiblingOnly = "ok";
}

public class OtherType
{
    public int OtherOnly;
}

public enum Mode
{
    First,
    Second = 2,
    Third = First | Second
}

public sealed record RecordOnly(string RecordName, int RecordCount);
public record BaseRecord(bool BaseReal);
public record DerivedRecord : BaseRecord(Options.BaseArg);
public interface IContract { string ContractMethod(); }
public class Options { public bool InitializerOnly; public const bool BaseArg = true; }
'@

$decls = @(Get-CSharpDeclarations $fixture)
$widget = @($decls | Where-Object { $_.Type -ceq 'Widget' } | ForEach-Object { $_.Name })
foreach ($name in @(
    'Instance', 'first', 'second', 'initialized', 'afterInitializer', 'Name', 'Changed',
    'ExpressionBodied', 'Item', 'Widget', 'Make', 'Echo', 'Pair', 'MultiLine',
    'ContractMethod', 'op_Addition', 'Body', 'CalledOnly', 'Nested', 'SiblingOnly',
    'Text', 'Verbatim', 'Raw', 'Interpolated', 'EmbeddedQuote', 'NestedInterpolation',
    'CharHole', 'Compiled', 'leftComparison', 'rightComparison', 'Initialized', 'AfterOptions'
)) { Assert-Contains $widget $name "Widget declares $name" }
foreach ($name in @(
    'CommentOnly', 'BlockOnly', 'StringOnly', 'VerbatimOnly', 'RawOnly', 'InterpolatedOnly',
    'EmbeddedQuoteOnly', 'NestedInterpolationOnly', 'RegexOptions',
    'LocalOnly', 'InitializerOnly', 'NestedOnly', 'OtherOnly', 'value', 'count', 'index'
)) { Assert-Missing $widget $name "Widget does not declare $name" }
$mode = @($decls | Where-Object { $_.Type -ceq 'Mode' } | ForEach-Object { $_.Name })
foreach ($name in @('First', 'Second', 'Third')) { Assert-Contains $mode $name "enum Mode declares $name" }
$record = @($decls | Where-Object { $_.Type -ceq 'RecordOnly' } | ForEach-Object { $_.Name })
foreach ($name in @('RecordName', 'RecordCount')) { Assert-Contains $record $name "record declares $name" }
$derived = @($decls | Where-Object { $_.Type -ceq 'DerivedRecord' } | ForEach-Object { $_.Name })
Assert-Missing $derived 'BaseArg' 'record base-constructor argument is not a positional member'

# End-to-end through the real gate, against the tracked C# in this checkout.
$temp = Join-Path ([System.IO.Path]::GetTempPath()) ("check-docs-members-" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $cases = @(
        @{ Name = 'real-named-file'; Text = '`ProfileCommand.ProfileShow` and `DpapiHelper.DecryptSecret`'; Exit = 0; Pattern = 'PASS: docs checks clean' },
        @{ Name = 'real-partial-fallback'; Text = '`Program.SecretStore` and `Program.ScrubExceptionMessage`'; Exit = 0; Pattern = 'PASS: docs checks clean' },
        @{ Name = 'real-multiline-tuple'; Text = '`SecretProviderManager.RotateAll`'; Exit = 0; Pattern = 'PASS: docs checks clean' },
        @{ Name = 'unknown-type-skipped'; Text = '`NoSuchTypeAtAll.Foo`'; Exit = 0; Pattern = 'PASS: docs checks clean' },
        @{ Name = 'bare-filename-not-member'; Text = '`ProfileCommand.cs`'; Exit = 0; Pattern = 'PASS: docs checks clean' },
        # The first three negatives below OCCUR in the resolved file, so the old
        # occurrence-based gate would have passed them; only a declaration-aware
        # reader rejects them. Initializer assignment reached via partial fallback:
        @{ Name = 'fake-partial-initializer'; Text = '`Program.PropertyNameCaseInsensitive`'; Exit = 1; Pattern = 'cited member not found: Program.PropertyNameCaseInsensitive' },
        # A call (and comment) in the named file, not a member of it:
        @{ Name = 'fake-named-file-call'; Text = '`CliRuntime.GetFolderPath`'; Exit = 1; Pattern = 'cited member not found: CliRuntime.GetFolderPath' },
        # Comment-only occurrence in the named file:
        @{ Name = 'fake-named-file-comment'; Text = '`Program.CommandLineToArgvW`'; Exit = 1; Pattern = 'cited member not found: Program.CommandLineToArgvW' },
        # A constructor argument in the named file, not a member declaration:
        @{ Name = 'fake-named-file-constructor-argument'; Text = '`ExpandCommand.RegexOptions`'; Exit = 1; Pattern = 'cited member not found: ExpandCommand.RegexOptions' },
        # Genuinely absent member (typo of the real SecretStore):
        @{ Name = 'missing-member'; Text = '`Program.SecretStorXYZ`'; Exit = 1; Pattern = 'cited member not found: Program.SecretStorXYZ' }
    )
    foreach ($case in $cases) {
        $doc = Join-Path $temp ($case.Name + '.md')
        [System.IO.File]::WriteAllText($doc, $case.Text + "`n", (New-Object System.Text.UTF8Encoding($false)))
        # check-docs.ps1 reports via Write-Host (information stream 6), so merge
        # ALL streams (*>&1), not just the error stream, or the PASS/FAIL and
        # "cited member not found" lines would never reach $output. The child
        # sets its own ErrorActionPreference=Continue; keep the harness at
        # Continue for the call so a redirected stderr record is captured, not
        # promoted to a terminating error by this file's top-level Stop.
        $prevEap = $ErrorActionPreference
        $ErrorActionPreference = 'Continue'
        try {
            $output = @(& (Join-Path $PSScriptRoot 'check-docs.ps1') -Paths $doc *>&1 | ForEach-Object { $_.ToString() })
            $code = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $prevEap }
        $checks++
        if ($code -ne $case.Exit) {
            $failures.Add("$case.Name expected exit $($case.Exit), got $code`: $($output -join ' | ')")
        }
        elseif (-not (($output -join "`n") -match [regex]::Escape($case.Pattern))) {
            $failures.Add("$case.Name output did not contain $($case.Pattern): $($output -join ' | ')")
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
Write-Host "SELFTEST PASS: $checks declaration-aware member checks"
exit 0
