using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 20 (architecture-recovery, spec Phase 4) regression net: `profile create --help`
/// (and the -h/-?/? flag variants) is a help request, not a profile name. The pre-fix
/// parser took args[2] verbatim as the profile name and persisted a profile literally
/// named "--help" into profiles.json. Pinned contract: exit 0, usage on stdout, empty
/// stderr, and zero profiles.json / audit writes. The no-name usage-error and
/// unknown-flag error paths are pinned unchanged; the bare word "help" stays a legal
/// profile name (only flag forms are intercepted - `profile help` covers word-form help).
/// </summary>
[Collection("CliSnapshotSerial")]
public class ProfileCreateHelpTests
{
    const string Usage =
        "Usage: env-manager profile create <name> [--type global|launch] [--target <exe>] [--args <args>] [--cwd <dir>]";

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("-?")]
    [InlineData("/?")]
    [InlineData("--HELP")]
    [InlineData("-H")]
    public void Create_HelpVariant_ShowsUsageExit0_WritesNothing(string flag)
    {
        using var tmp = new TempProfileDir();
        var (code, stdout, stderr) = RunProfile("create", flag);

        Assert.Equal(0, code);
        Assert.Equal(Usage, stdout.TrimEnd('\r', '\n'));
        Assert.Equal(string.Empty, stderr);
        Assert.False(File.Exists(Path.Combine(tmp.Dir, "profiles.json")),
            "a help request must not write profiles.json");
        Assert.False(File.Exists(Path.Combine(tmp.Dir, "audit.json")),
            "a help request must not record a mutation audit entry");
    }

    /// <summary>Acceptance 2: the pre-existing no-name usage error is unchanged (stderr + exit 1).</summary>
    [Fact]
    public void Create_NoName_KeepsUsageError()
    {
        using var tmp = new TempProfileDir();
        var (code, stdout, stderr) = RunProfile("create");

        Assert.Equal(1, code);
        Assert.Equal(Usage, stderr.TrimEnd('\r', '\n'));
        Assert.Equal(string.Empty, stdout);
        Assert.False(File.Exists(Path.Combine(tmp.Dir, "profiles.json")));
    }

    /// <summary>Acceptance 2: a flag after a real name still fails as an unknown flag.</summary>
    [Fact]
    public void Create_UnknownFlagAfterName_KeepsUnknownFlagError()
    {
        using var tmp = new TempProfileDir();
        var (code, _, stderr) = RunProfile("create", "em-t20-plain", "--bogus");

        Assert.Equal(1, code);
        Assert.Equal("Unknown flag: --bogus", stderr.TrimEnd('\r', '\n'));
        Assert.False(File.Exists(Path.Combine(tmp.Dir, "profiles.json")));
    }

    /// <summary>
    /// Scope pin for the recognition set: the bare word "help" is NOT intercepted -
    /// creating a profile literally named "help" stays legal. Guards against a future
    /// broadening of the help-variant set swallowing a legitimate name.
    /// </summary>
    [Fact]
    public void Create_BareHelpWord_RemainsALegalProfileName()
    {
        using var tmp = new TempProfileDir();
        var (code, stdout, _) = RunProfile("create", "help");

        Assert.Equal(0, code);
        Assert.Contains("Created global profile: help", stdout);
        Assert.True(File.Exists(Path.Combine(tmp.Dir, "profiles.json")));
    }

    static (int Code, string Stdout, string Stderr) RunProfile(params string[] args)
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        Console.SetOut(stdout);
        Console.SetError(stderr);
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile" }.Concat(args).ToArray());
            return (code, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
        }
    }
}
