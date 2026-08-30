using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: argument tokenizing (LenientArgs.Tokenize pure overload).
/// The lenient scanner recovers the Windows "trailing backslash + quote" hazard.
/// </summary>
public class LenientArgsTests
{
    [Fact]
    public void Positive_SplitsSimpleCommandLine()
    {
        Assert.Equal(new[] { "set", "FOO=bar", "--scope", "user" },
            LenientArgs.Tokenize("\"C:\\tools\\env-manager-cli.exe\" set FOO=bar --scope user"));
    }

    [Fact]
    public void Boundary_RecoversTrailingBackslashInsideQuotes()
    {
        Assert.Equal(new[] { "path", "add", "C:\\Program Files\\PS7\\", "--scope", "user" },
            LenientArgs.Tokenize("\"C:\\tools\\env-manager-cli.exe\" path add \"C:\\Program Files\\PS7\\\" --scope user"));
    }

    [Fact]
    public void Boundary_EmptyQuotedStringCountsAsToken()
    {
        Assert.Equal(new[] { "a", "", "b" }, LenientArgs.Tokenize("cli a \"\" b"));
    }

    [Fact]
    public void Boundary_NoArgumentsAfterProgramPathYieldsEmpty()
    {
        Assert.Equal(Array.Empty<string>(), LenientArgs.Tokenize("\"C:\\tools\\env-manager-cli.exe\""));
    }

    [Fact]
    public void Boundary_CollapsesMultipleSpaces()
    {
        Assert.Equal(new[] { "one", "two" }, LenientArgs.Tokenize("cli  one   two "));
    }
}
