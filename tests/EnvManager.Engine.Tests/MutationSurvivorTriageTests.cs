using EnvManager;
using System.Text.Json;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 18 (mutation survivor triage) kill tests: one test per missing assertion on
/// the Stryker baseline survivors (see .scratch/architecture-recovery/reports/18-survivor-registry.json).
/// Every test asserts externally observable behavior only - seam state, exit codes, stdout -
/// and runs hermetically: profiles.json and the AppDataDirectory protection stores are
/// redirected to per-test temp directories (SetProfilesFilePathForTests /
/// SetAppDataDirectoryForTests seams), so no real registry or user config is touched.
/// Survivor #4369 (CollectInheritedSecretsFrom visited.Add) is registered as equivalent
/// and intentionally has no kill test.
/// </summary>
[Collection("CliSnapshotSerial")]
public class MutationSurvivorTriageTests : IDisposable
{
    const string FreeName = "EM_TEST_FOO";

    static readonly string TempDir = Path.Combine(Path.GetTempPath(), "em-t18-" + Guid.NewGuid().ToString("N"));

    public MutationSurvivorTriageTests()
    {
        Directory.CreateDirectory(TempDir);
        Program.SetAppDataDirectoryForTests(TempDir);
        Program.SetProfilesFilePathForTests(Path.Combine(TempDir, "profiles.json"));
    }

    public void Dispose()
    {
        Program.SetAppDataDirectoryForTests(null);
        Program.SetProfilesFilePathForTests(null);
        try { Directory.Delete(TempDir, recursive: true); } catch (IOException) { }
    }

    // ---- survivors #5011 / #5012 / #5020: custom protected-vars.json (user-lockable) ----

    /// <summary>A user-locked variable must refuse writes through the REAL protection path
    /// (no synthetic predicate): RunSet's default isProtected delegate reads protected-vars.json
    /// from AppDataDirectory. Two-entry list also pins Any-vs-All (single-entry lists cannot).</summary>
    [Fact]
    public void Set_CustomLockedVariable_Rejected()
    {
        string[] locked = { "EM_TEST_LOCKED_A", "EM_TEST_LOCKED_B" };
        File.WriteAllText(Path.Combine(TempDir, "protected-vars.json"), JsonSerializer.Serialize(locked));
        var env = new InMemoryScope();

        int rc = Program.RunSet(new[] { "set", "EM_TEST_LOCKED_A", "x" }, env);

        Assert.Equal(1, rc);
        Assert.Null(env.ReadValue("EM_TEST_LOCKED_A", "user"));
    }

    // ---- survivors #5053 / #5055: externally editable builtin-protected-vars.json ----

    /// <summary>An externally edited builtin list (strict subset of defaults) must be honored:
    /// a variable the admin removed from the file is no longer builtin-protected. Killing both
    /// the guard flip (seeding defaults over the external file) and the ?? defaults drop.</summary>
    [Fact]
    public void Set_BuiltinProtectedVarsFileExternalEditHonored()
    {
        File.WriteAllText(Path.Combine(TempDir, "builtin-protected-vars.json"), JsonSerializer.Serialize(new[] { "ComSpec" }));
        var env = new InMemoryScope();

        int rc = Program.RunSet(new[] { "set", "TEMP", "x" }, env);

        Assert.Equal(0, rc);
        Assert.Equal("x", env.ReadValue("TEMP", "user")?.Value);
    }

    // ---- survivors #4337 / #4343: pre-flight topology guard (Global inheriting Launch) ----

    static ProfileData Global(string name) => new() { Name = name, ProfileType = "global" };
    static ProfileData Launch(string name) => new() { Name = name, ProfileType = "launch", TargetExecutable = "C:\\em-t18-target.cmd" };

    /// <summary>Global inheriting a SECRETLESS Launch profile must reject on the topology
    /// guard alone. Every pre-existing test used secret-bearing launch parents, where the
    /// inherited-secret union rejects anyway, leaving the guard itself unobserved.</summary>
    [Fact]
    public void Preflight_GlobalInheritsSecretlessLaunch_Rejected()
    {
        var parent = Launch("EM_T18_launch_plain");
        var child = Global("EM_T18_global_child");
        child.Inherits.Add(parent.Name);
        var all = new List<ProfileData> { parent, child };

        bool ok = Program.RunProfilePreflight(child, all);

        Assert.False(ok, "global inheriting a launch profile is rejected by topology regardless of secrets");
    }

    // ---- survivors #4392 / #4396: UnapplyProfile scope routing + delete of backup-less variables ----

    /// <summary>A system-scope variable applied with NO pre-existing value (no backup) must be
    /// deleted from the SYSTEM store on unapply. Kills the Scope ?? "user" drop and the
    /// delete-statement removal: the restore write of an existing test masked both.</summary>
    [Fact]
    public void Unapply_RemovesAppliedSystemVariableWithoutBackup()
    {
        var env = new InMemoryScope();
        var profile = new ProfileData
        {
            Name = "EM_T18_unapply_sys",
            ProfileType = "global",
            Variables = new List<ProfileVariable> { new() { Name = FreeName, Value = "from-profile", Scope = "system" } },
        };
        Program.SaveProfiles(new List<ProfileData> { profile });
        var loaded = Program.LoadProfiles().First(p => p.Name == profile.Name);

        Program.ApplyProfile(loaded, env);
        Assert.Equal("from-profile", env.ReadValue(FreeName, "system")?.Value); // pre-check: apply landed in system store

        Program.UnapplyProfile(loaded, env);

        Assert.Null(env.ReadValue(FreeName, "system"));
        Assert.Null(env.ReadValue(FreeName + "_PowerToys_EM_T18_unapply_sys", "system"));
    }

    // ---- survivor #6376: rename entry-point re-validation of newName ----

    /// <summary>Rename must re-validate newName (defense in depth behind the command-level
    /// guard): an '=' in the new name throws before any seam write and the source survives.</summary>
    [Fact]
    public void Rename_InvalidNewName_ThrowsAndPreservesSource()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "keep", "user");

        Assert.Throws<ArgumentException>(
            () => Program.RunRename(new[] { "rename", FreeName, "EM_TEST=BAD" }, env, (_, _) => false));

        Assert.Equal("keep", env.ReadValue(FreeName, "user")?.Value);
        Assert.Null(env.ReadValue("EM_TEST=BAD", "user"));
    }
}
