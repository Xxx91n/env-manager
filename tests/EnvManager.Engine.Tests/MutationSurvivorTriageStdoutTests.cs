using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 18 (mutation survivor triage) stdout kill test: change-scope's success
/// confirmation line and the minimal 3-argument auto-detect invocation were unasserted
/// (baseline survivors #6111 / #6126 / #6191). Console redirection joins the
/// CliSnapshotSerial collection so parallel suites cannot interleave captured streams.
/// Runs hermetically: InMemoryScope seam, synthetic protection predicate.
/// </summary>
[Collection("CliSnapshotSerial")]
public class MutationSurvivorTriageStdoutTests
{
    const string FreeName = "EM_TEST_FOO";

    [Fact]
    public void ChangeScope_AutoDetectedScope_MovesAndPrintsConfirmation()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "val", "user");
        env.ResetBroadcastCount();

        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        int rc;
        try
        {
            // Minimal 3-arg form: no --scope flag, source scope auto-detected.
            rc = Program.RunChangeScope(new[] { "change-scope", FreeName, "system" }, env, (_, _) => false);
        }
        finally
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
        }

        Assert.Equal(0, rc);
        Assert.Null(env.ReadValue(FreeName, "user"));
        Assert.Equal("val", env.ReadValue(FreeName, "system")?.Value);
        Assert.Equal(1, env.BroadcastCount);
        Assert.Contains("Changed scope of " + FreeName + " from user to system", stdout.ToString());
    }
}
