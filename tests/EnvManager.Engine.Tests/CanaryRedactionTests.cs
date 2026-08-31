using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: canary redaction regression (ticket 07, launch-injection verification).
/// Contract: a uniquely-tagged fake secret ("canary") fed through the engine's
/// scrub layer must never survive into output; masking placeholders
/// (<redacted>) must appear in its place. These unit tests pin the pure
/// function behavior that the Pester integration suite
/// (tests/canary-redaction.Tests.ps1) verifies end-to-end across CLI sinks.
/// </summary>
public class CanaryRedactionTests
{
    private static string UniqueCanary() =>
        $"password=canary-{Guid.NewGuid():N}";

    [Fact]
    public void Negative_CanaryPasswordPatternDoesNotSurviveScrub()
    {
        var canary = UniqueCanary();
        var scrubbed = Program.ScrubExceptionMessage($"launch failed for {canary}");
        Assert.DoesNotContain(canary, scrubbed);
    }

    [Fact]
    public void Positive_MaskPlaceholderAppearsInScrubbedOutput()
    {
        var scrubbed = Program.ScrubExceptionMessage("connect failed: password=canary-value");
        Assert.Contains("<redacted>", scrubbed);
    }

    [Fact]
    public void Negative_CanaryAfterBearerPatternDoesNotSurviveScrub()
    {
        var canary = $"Bearer canary-{Guid.NewGuid():N}";
        var scrubbed = Program.ScrubExceptionMessage($"auth rejected: {canary}");
        Assert.DoesNotContain("canary-", scrubbed);
    }

    [Fact]
    public void Negative_CanaryAfterVaultTokenPatternDoesNotSurviveScrub()
    {
        var canary = $"canary-{Guid.NewGuid():N}";
        var scrubbed = Program.ScrubExceptionMessage($"VAULT_TOKEN={canary} denied");
        Assert.DoesNotContain(canary, scrubbed);
    }

    [Fact]
    public void Boundary_CanaryWithoutKnownPatternIsNotMasked()
    {
        // The scrub layer is a best-effort pattern allow-list (ADR 0005): a
        // canary that does not match any known secret-bearing pattern passes
        // through. The integration canary suite therefore shapes its fake
        // secret as password=... so a leaking sink trips a scrub pattern too.
        var canary = $"canary-{Guid.NewGuid():N}";
        var scrubbed = Program.ScrubExceptionMessage($"plain value {canary}");
        Assert.Contains(canary, scrubbed);
    }

    [Fact]
    public void Negative_MultipleCanaryOccurrencesAllMasked()
    {
        var scrubbed = Program.ScrubExceptionMessage(
            "first password=canary-aaa then password=canary-bbb");
        Assert.DoesNotContain("canary-aaa", scrubbed);
        Assert.DoesNotContain("canary-bbb", scrubbed);
    }
}
