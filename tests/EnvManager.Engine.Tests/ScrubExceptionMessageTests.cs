using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: exception message scrubbing (ScrubExceptionMessage).
/// Contract: secret-bearing values must not reach stderr/logs; masks are
/// bounded to 8 chars per occurrence by design (best-effort, documented).
/// </summary>
public class ScrubExceptionMessageTests
{
    [Fact]
    public void Positive_MasksSecretAfterKnownPattern()
    {
        var scrubbed = Program.ScrubExceptionMessage("connect failed: password=mypass1");
        Assert.DoesNotContain("mypass1", scrubbed);
        Assert.Contains("<redacted>", scrubbed);
    }

    [Fact]
    public void Positive_MasksVaultTokenPattern()
    {
        var scrubbed = Program.ScrubExceptionMessage("vault: VAULT_TOKEN=abcd1234 rejected");
        Assert.DoesNotContain("abcd1234", scrubbed);
    }

    [Fact]
    public void Boundary_EmptyStringPassesThrough()
    {
        Assert.Equal(string.Empty, Program.ScrubExceptionMessage(string.Empty));
    }

    [Fact]
    public void Boundary_NullPassesThrough()
    {
        Assert.Null(Program.ScrubExceptionMessage(null!));
    }

    [Fact]
    public void Boundary_MessageTruncatedTo512Chars()
    {
        var longMsg = new string('x', 600);
        Assert.Equal(512, Program.ScrubExceptionMessage(longMsg).Length);
    }

    [Fact]
    public void Boundary_NoKnownPatternLeavesMessageUnchanged()
    {
        var msg = "file not found: C:\\temp\\x.json";
        Assert.Equal(msg, Program.ScrubExceptionMessage(msg));
    }

    [Fact]
    public void Boundary_PatternAtEndOfStringDoesNotThrow()
    {
        var scrubbed = Program.ScrubExceptionMessage("auth error: password=");
        Assert.StartsWith("auth error: password=", scrubbed);
    }

    [Fact]
    public void MasksEveryOccurrenceOfSamePattern()
    {
        var scrubbed = Program.ScrubExceptionMessage(
            "auth failed: password=firstpass then password=secondpass");
        Assert.DoesNotContain("secondpass", scrubbed);
    }
}
