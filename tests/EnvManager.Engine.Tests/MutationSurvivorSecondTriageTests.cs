using EnvManager;
using System.Text.Json;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 50 (round-8 second survivor triage) kill tests: one assertion target per
/// currently-surviving Stryker mutant from the dispatched baseline (build.yml stryker job,
/// run 34771955013). Dispositions are registered in
/// .scratch/round8-backlog-grill/reports/50-survivor-registry.md - 13 killed here via
/// existing seams only (InMemoryScope, SetProfilesFilePathForTests,
/// SetAppDataDirectoryForTests, SaveProfilesRawForTests, console capture), 3 registered
/// as LLM-detection reserves (#3462/#3463 conditional-equivalent target resolution,
/// #3525 visited.Add cycle guard) with no kill test by design.
/// Hermetic: profiles.json and the AppDataDirectory protection stores are redirected to
/// a per-test temp directory; no real registry or user config is touched.
/// </summary>
[Collection("CliSnapshotSerial")]
public class MutationSurvivorSecondTriageTests : IDisposable
{
    const string FreeName = "EM_TEST_FOO";
    static readonly string Name255 = "EM" + new string('A', 253); // exactly 255 chars - the >=255 boundary

    static readonly string TempDir = Path.Combine(Path.GetTempPath(), "em-t50-" + Guid.NewGuid().ToString("N"));
    static string BuiltinFile => Path.Combine(TempDir, "builtin-protected-vars.json");

    public MutationSurvivorSecondTriageTests()
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

    static ProfileData Global(string name) { var p = new ProfileData(); p.SetName(name); p.SetProfileType("global"); p.SetSchemaVersion(2); return p; }
    static ProfileData Launch(string name) { var p = new ProfileData(); p.SetName(name); p.SetProfileType("launch"); p.SetLaunchTarget("C:\\em-t50-target.cmd"); p.SetSchemaVersion(2); return p; }

    static int CaptureProfileStatus(string profileName, out string stdout)
    {
        var writer = new StringWriter();
        Console.SetOut(writer);
        int rc;
        try { rc = Program.RunProfileCommand(new[] { "profile", "status", profileName }); }
        finally { Console.SetOut(new StringWriter()); }
        stdout = writer.ToString();
        return rc;
    }

    // ---- survivor #3364 (ProfileEffective.cs:53, negated global-topology guard in
    // ---- IsProfileApplicable - the poisoned-store re-check, distinct from the
    // ---- RunProfilePreflight twin already killed by ticket 18) ----

    /// <summary>A poisoned store where a Global profile inherits a SECRETLESS Launch
    /// parent must still report isApplicable=false - the set-inherits command-level guard
    /// cannot retro-fix a hand-edited profiles.json, so IsProfileApplicable is the last
    /// guard before re-apply. The mutant drops the topology block entirely.</summary>
    [Fact]
    public void Status_GlobalInheritsSecretlessLaunch_NotApplicable()
    {
        var parent = Launch("EM_T50_launch_plain");
        var child = Global("EM_T50_global_child");
        child.SetInherits(new[] { parent.Name });
        Program.SaveProfilesRawForTests(new List<ProfileData> { parent, child });

        int rc = CaptureProfileStatus(child.Name, out string stdout);

        Assert.Equal(0, rc);
        Assert.Matches("\"isApplicable\":\\s*false", stdout);
    }

    // ---- survivor #3382 (ProfileEffective.cs:66, >=255 -> >255 in IsProfileApplicable) ----

    /// <summary>A 255-character variable name is rejected (>=255 boundary), not just
    /// 256+. Asserted through the status projection so the poisoned-store applicability
    /// guard is observed, not bypassed.</summary>
    [Fact]
    public void Status_VariableName255_NotApplicable()
    {
        var profile = Global("EM_T50_len255");
        profile.AddVariable(Name255, "x");
        Program.SaveProfilesRawForTests(new List<ProfileData> { profile });

        int rc = CaptureProfileStatus(profile.Name, out string stdout);

        Assert.Equal(0, rc);
        Assert.Matches("\"isApplicable\":\\s*false", stdout);
    }

    // ---- survivor #3414 (ProfileEffective.cs:110, same >=255 -> >255 in
    // ---- RunProfilePreflight, the apply-path twin) ----

    [Fact]
    public void Preflight_VariableName255_Rejected()
    {
        var profile = Global("EM_T50_preflight_len255");
        profile.AddVariable(Name255, "x");

        Assert.False(Program.RunProfilePreflight(profile, new List<ProfileData> { profile }));
    }

    // ---- survivor #3446 (ProfileEffective.cs:166, Any -> All in the warn-tier
    // ---- "reference defined by a profile-declared variable" clause) ----

    /// <summary>%EM_TEST_A% is defined because variable A exists in the same profile.
    /// Under the All() mutant the reference only counts as defined when EVERY resolved
    /// variable is named EM_TEST_A, so a spurious "undefined %VAR%" warning appears.</summary>
    [Fact]
    public void PreflightDetailed_ProfileDeclaredVarSatisfiesReference_NoWarning()
    {
        var profile = Global("EM_T50_warn_any");
        profile.AddVariable("EM_TEST_A", "x");
        profile.AddVariable("EM_TEST_B", "%EM_TEST_A%");

        var result = Program.RunProfilePreflightDetailed(profile, new List<ProfileData> { profile }, strict: false);

        Assert.Empty(result.Warnings);
    }

    // ---- survivor #3469 (ProfileEffective.cs:200, negated global-topology guard in
    // ---- RunProfilePreflightDetailed - the detailed-variant twin of #3364) ----

    [Fact]
    public void PreflightDetailed_GlobalInheritsSecretlessLaunch_Errors()
    {
        var parent = Launch("EM_T50_launch_plain2");
        var child = Global("EM_T50_global_child2");
        child.SetInherits(new[] { parent.Name });
        var all = new List<ProfileData> { parent, child };

        var result = Program.RunProfilePreflightDetailed(child, all, strict: false);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("cannot inherit from a Launch profile"));
    }

    // ---- survivor #3491 (ProfileEffective.cs:215, >=255 -> >255 in the detailed variant) ----

    [Fact]
    public void PreflightDetailed_VariableName255_Errors()
    {
        var profile = Global("EM_T50_detailed_len255");
        profile.AddVariable(Name255, "x");

        var result = Program.RunProfilePreflightDetailed(profile, new List<ProfileData> { profile }, strict: false);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("255"));
    }

    // ---- survivor #3505 (ProfileEffective.cs:226, ValidatePathFragment call removed -
    // ---- invalid PATH fragments stop producing error-tier findings) ----

    [Fact]
    public void PreflightDetailed_InvalidPathFragment_Errors()
    {
        var profile = Global("EM_T50_badpath");
        profile.SetPathEntries(new[] { "bad;entry" }); // ';' is rejected by ValidatePathFragment

        var result = Program.RunProfilePreflightDetailed(profile, new List<ProfileData> { profile }, strict: false);

        Assert.True(result.HasErrors);
        Assert.Contains(result.Errors, e => e.Contains("Invalid PATH entry"));
    }

    // ---- survivor #3517 (ProfileEffective.cs:256, conditional-true: the non-strict
    // ---- warn report always prints "strict mode: refusing") ----

    /// <summary>The non-strict warn report must say "continuing", not "refusing" -
    /// the stderr clause is the human-facing half of the ticket-19 two-tier contract.</summary>
    [Fact]
    public void WarnReport_NonStrict_PrintsContinuing()
    {
        var result = new Program.PreflightResult();
        result.Warnings.Add("w1");
        var stderr = new StringWriter();
        var stdout = new StringWriter();
        Console.SetError(stderr);
        Console.SetOut(stdout);
        try { Program.EmitPreflightWarnReport("profile apply", "EM_T50_p", result, strict: false); }
        finally { Console.SetError(new StringWriter()); Console.SetOut(new StringWriter()); }

        Assert.Contains("continuing", stderr.ToString());
        Assert.DoesNotContain("refusing", stderr.ToString());
    }

    // ---- survivor #3524 (ProfileEffective.cs:271, negated visited.Contains -
    // ---- CollectInheritedSecretsFrom returns empty on every call, so the
    // ---- inherited-secret union guard is neutered) ----

    /// <summary>A profile that inherits a secret-bearing parent but declares no secret
    /// of its own must be rejected (no in-process decrypt path for the inherited name).
    /// The mutant makes the union always empty, so the rejection never fires.</summary>
    [Fact]
    public void Preflight_InheritedSecretUndeclared_Rejected()
    {
        var parent = Global("EM_T50_parent_secret");
        parent.SetSecretVariables(new[] { "EM_T50_SECRET" });
        var child = Global("EM_T50_child_nosecret");
        child.SetInherits(new[] { parent.Name });
        var all = new List<ProfileData> { parent, child };

        Assert.False(Program.RunProfilePreflight(child, all));
    }

    // ---- survivor #3528 (ProfileEffective.cs:279, parent != null flipped to == null -
    // ---- a missing parent now recurses into CollectInheritedSecretsFrom(null, ...) and
    // ---- throws NullReferenceException instead of failing closed) ----

    /// <summary>An inheritance reference to a non-existent parent must fail closed
    /// (InvalidDataException -> false), not crash. The mutant dereferences the null
    /// parent inside the secret-union walk.</summary>
    [Fact]
    public void Preflight_MissingParent_FailsClosedNotCrash()
    {
        var child = Global("EM_T50_child_missingparent");
        child.SetInherits(new[] { "EM_T50_NO_SUCH_PARENT" });

        Assert.False(Program.RunProfilePreflight(child, new List<ProfileData> { child }));
    }

    // ---- survivor #4474 (ProtectionCommand.cs:221, !File.Exists flip - an existing
    // ---- externally-edited builtin list is overwritten with defaults on every read) ----

    /// <summary>An admin-edited builtin-protected-vars.json (strict subset of defaults)
    /// must survive a protection read unchanged. The mutant overwrites it with the
    /// embedded defaults whenever the file exists, silently reverting the admin edit.</summary>
    [Fact]
    public void BuiltinProtectedVarsFile_ExternalEditNotReverted()
    {
        File.WriteAllText(BuiltinFile, JsonSerializer.Serialize(new[] { "ComSpec" }));
        var env = new InMemoryScope();

        int rc = Program.RunSet(new[] { "set", FreeName, "x", "--scope", "system" }, env);

        Assert.Equal(0, rc); // EM_TEST_FOO is not protected in any scope
        Assert.DoesNotContain("TEMP", File.ReadAllText(BuiltinFile));
    }

    // ---- survivor #4475 (ProtectionCommand.cs:222, AtomicWriteJson seed removed -
    // ---- the externally-editable builtin file is never created on first use) ----

    /// <summary>builtin-protected-vars.json is documented as "created on first run from
    /// protection.defaults.json if missing" - the file must exist after the first
    /// protection read so admins can see and edit it. The mutant drops the seeding write.</summary>
    [Fact]
    public void BuiltinProtectedVarsFile_SeededWhenMissing()
    {
        var env = new InMemoryScope();

        int rc = Program.RunSet(new[] { "set", "TEMP", "x", "--scope", "system" }, env);

        Assert.Equal(1, rc); // TEMP is builtin-protected in system scope
        Assert.True(File.Exists(BuiltinFile));
    }

    // ---- survivor #4476 (ProtectionCommand.cs:223, ?? defaults removed - a null-JSON
    // ---- builtin file now returns null instead of falling back to the embedded
    // ---- defaults, and the HashSet ctor throws ArgumentNullException) ----

    /// <summary>A corrupt (literal-null) builtin-protected-vars.json must fall back to
    /// the embedded defaults, keeping TEMP protected in system scope. The mutant
    /// propagates null into the HashSet constructor.</summary>
    [Fact]
    public void BuiltinProtectedVarsFile_NullJsonFallsBackToDefaults()
    {
        File.WriteAllText(BuiltinFile, "null");
        var env = new InMemoryScope();

        int rc = Program.RunSet(new[] { "set", "TEMP", "x", "--scope", "system" }, env);

        Assert.Equal(1, rc);
        Assert.Null(env.ReadValue("TEMP", "system"));
    }
}
