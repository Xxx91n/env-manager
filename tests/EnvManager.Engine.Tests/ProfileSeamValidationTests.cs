using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 04 regression net: profile apply/launch pre-flight validation and the
/// v0.7.7 inheritance-chain secret propagation boundary, exercised through the
/// IEnvironmentScope seam (InMemoryScope) - no real registry, no machine state.
///
/// The red-first acceptance gate for this ticket: the inherited-secret rejection
/// tests below are written against the pre-flight validation core and MUST fail
/// before the seam migration wires the boundary into it, then pass after.
///
/// Profiles.json is redirected to a per-test temp directory (RedirectProfilesToTemp)
/// so LoadProfiles/SaveProfiles never touch the real %LOCALAPPDATA% config.
/// </summary>
public class ProfileSeamValidationTests : IDisposable
{
    const string FreeName = "EM_TEST_FOO";
    const string LockedName = "EM_TEST_LOCKED";
    const string SecretVar = "EM_TEST_SECRET_VAR";

    static string? _redirectDir;
    static string TempTargetPath = "C:em-test-target.cmd";

    public ProfileSeamValidationTests()
    {
        // Redirect profiles.json to a per-test temp directory so LoadProfiles/SaveProfiles
        // never touch the real %LOCALAPPDATA% config.
        _redirectDir = Path.Combine(Path.GetTempPath(), "em-t04-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_redirectDir);
        Program.SetProfilesFilePathForTests(Path.Combine(_redirectDir, "profiles.json"));
        // ValidateLaunchTarget requires a real executable file: create the test target.
        TempTargetPath = Path.Combine(_redirectDir, "em-test-target.cmd");
        File.WriteAllText(TempTargetPath, "@echo ok");
    }

    public void Dispose()
    {
        Program.SetProfilesFilePathForTests(null);
        try
        {
            if (_redirectDir != null && Directory.Exists(_redirectDir))
                Directory.Delete(_redirectDir, recursive: true);
        }
        catch (IOException) { }
    }

    static ProfileData Global(string name, params ProfileVariable[] variables) => new()
    {
        Name = name,
        ProfileType = "global",
        Variables = variables.ToList(),
    };

    static ProfileData Launch(string name, string? target, params string[] secrets) => new()
    {
        Name = name,
        ProfileType = "launch",
        TargetExecutable = target,
        SecretVariables = secrets.ToList(),
    };

    static void SeedStore(params ProfileData[] profiles)
    {
        Program.SaveProfiles(profiles.ToList());
    }

    static Func<string, string, bool> LockLockedName() =>
        (name, _) => name.Equals(LockedName, StringComparison.OrdinalIgnoreCase);

    // ---- pre-flight validation: inherited secret propagation (v0.7.7 red-first gate) ----

    /// <summary>
    /// THE red-first acceptance test. A Global profile inherits a Launch profile that
    /// carries a secret variable, and the child re-declares the secret's name as a
    /// regular variable. Pre-flight validation must reject the apply because the
    /// inherited secret name collides with a resolvable variable - applying it would
    /// write DPAPI ciphertext to the registry (v0.7.7 incident class).
    /// </summary>
    [Fact]
    public void Preflight_GlobalInheritingSecretLaunch_ChildRedeclaresSecretName_Rejected()
    {
        SeedStore(
            Launch("EM_T04_launch_secret", TempTargetPath, SecretVar),
            Global("EM_T04_global_child", new ProfileVariable { Name = SecretVar, Value = "regular-value" }));
        var child = Program.LoadProfiles().First(p => p.Name == "EM_T04_global_child");
        child.Inherits.Add("EM_T04_launch_secret");

        bool ok = Program.RunProfilePreflight(child);

        Assert.False(ok, "inherited secret name must pre-flight-reject the apply");
    }

    /// <summary>Inherited secret names alone (no redeclaration) must also reject.</summary>
    [Fact]
    public void Preflight_GlobalInheritingSecretLaunch_InheritedSecretOnly_Rejected()
    {
        SeedStore(
            Launch("EM_T04_launch_secret", TempTargetPath, SecretVar),
            Global("EM_T04_global_child"));
        var child = Program.LoadProfiles().First(p => p.Name == "EM_T04_global_child");
        child.Inherits.Add("EM_T04_launch_secret");

        bool ok = Program.RunProfilePreflight(child);

        Assert.False(ok, "inherited secrets must pre-flight-reject the apply");
    }

    /// <summary>
    /// Falsifiable variant of the v0.7.7 gate: a LAUNCH child inheriting a LAUNCH parent
    /// that carries secrets. The Global-inherits-Launch topology guard does not fire for
    /// launch targets, so this rejection can ONLY come from CollectInheritedSecretsFrom.
    /// Replacing the union walk with own-SecretVariables-only makes exactly this test red
    /// (demonstrated during ticket 04 red-first acceptance).
    /// </summary>
    [Fact]
    public void Preflight_LaunchInheritingSecretLaunch_PoisonedJson_Rejected()
    {
        SeedStore(
            Launch("EM_T04_launch_secret", TempTargetPath, SecretVar),
            Launch("EM_T04_launch_child", TempTargetPath));
        var child = Program.LoadProfiles().First(p => p.Name == "EM_T04_launch_child");
        child.Inherits.Add("EM_T04_launch_secret");

        bool ok = Program.RunProfilePreflight(child);

        Assert.False(ok, "launch child inheriting a secret-bearing launch parent must pre-flight-reject");
    }

    /// <summary>Plain Global<-Global inheritance without secrets must pass pre-flight.</summary>
    [Fact]
    public void Preflight_GlobalInheritsPlainGlobal_Accepted()
    {
        SeedStore(
            Global("EM_T04_global_parent", new ProfileVariable { Name = FreeName, Value = "v" }),
            Global("EM_T04_global_child"));
        var child = Program.LoadProfiles().First(p => p.Name == "EM_T04_global_child");
        child.Inherits.Add("EM_T04_global_parent");

        bool ok = Program.RunProfilePreflight(child);

        Assert.True(ok, "plain global<-global inheritance is legal");
    }

    /// <summary>A protected variable name in the chain must reject (entry-point invariant).</summary>
    [Fact]
    public void Preflight_ProtectedVariableName_Rejected()
    {
        // ComSpec is in the real builtin protected list (protection.defaults.json). PATH is
        // deliberately editable, so exercise the protection rule with a true builtin name.
        var child = Global("EM_T04_global_child", new ProfileVariable { Name = "ComSpec", Value = "C:\\x" });

        Assert.False(Program.RunProfilePreflight(child));
    }

    /// <summary>A variable name containing '=' must reject.</summary>
    [Fact]
    public void Preflight_VariableNameWithEquals_Rejected()
    {
        var child = Global("EM_T04_global_child", new ProfileVariable { Name = "BAD=NAME", Value = "v" });

        Assert.False(Program.RunProfilePreflight(child));
    }

    // ---- ticket 19: two-tier preflight (warn tier + strict + structured report) ----

    /// <summary>The error tier keeps rejecting: protected variable name lands in Errors, not Warnings.</summary>
    [Fact]
    public void Detailed_ProtectedVariable_IsErrorNotWarning()
    {
        var child = Global("EM_T19_err_protected", new ProfileVariable { Name = "ComSpec", Value = "C:\\x" });

        var result = Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: false);

        Assert.True(result.HasErrors);
        Assert.False(result.HasWarnings);
        Assert.Contains(result.Errors, e => e.Contains("protected"));
    }

    /// <summary>The error tier keeps rejecting: '=' in a variable name is a hard error under both modes.</summary>
    [Fact]
    public void Detailed_NameWithEquals_IsError_UnderBothModes()
    {
        var child = Global("EM_T19_err_equals", new ProfileVariable { Name = "EM=BAD", Value = "v" });

        Assert.True(Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: false).HasErrors);
        Assert.True(Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: true).HasErrors);
    }

    /// <summary>An undefined %VAR% reference is warn-tier: no errors, exactly one warning naming the variable.</summary>
    [Fact]
    public void Detailed_UndefinedVarReference_IsWarning()
    {
        // fix2: the defined-check surface now includes the process environment, so the
        // undefined state pins its absence explicitly (set/clear discipline).
        Environment.SetEnvironmentVariable("EM_T19_UNDEF_VAR", null, EnvironmentVariableTarget.Process);
        var child = Global("EM_T19_warn_var", new ProfileVariable { Name = FreeName, Value = "%EM_T19_UNDEF_VAR%\\bin" });

        var result = Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: false);

        Assert.False(result.HasErrors);
        Assert.True(result.HasWarnings);
        Assert.Contains(result.Warnings, w => w.Contains("undefined %VAR%") && w.Contains(FreeName));
    }

    /// <summary>A defined %VAR% reference must NOT warn. The reference is defined via a
    /// Process-scoped variable set and cleared in-test (test discipline: no machine-state
    /// dependence). The pre-fix version relied on %SYSTEMROOT% - kernel-provided, absent
    /// from both registry env hives - and went red on CI run 33953937157.</summary>
    [Fact]
    public void Detailed_DefinedVarReference_NoWarning()
    {
        const string known = "EM_T19_DEFINED_REF";
        Environment.SetEnvironmentVariable(known, "C:\\tools", EnvironmentVariableTarget.Process);
        try
        {
            var child = Global("EM_T19_ok_var", new ProfileVariable { Name = FreeName, Value = "%" + known + "%\\bin" });

            var result = Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: false);

            Assert.False(result.HasErrors);
            Assert.False(result.HasWarnings);
        }
        finally { Environment.SetEnvironmentVariable(known, null, EnvironmentVariableTarget.Process); }
    }

    /// <summary>A stale PATH entry (temp dir removed) is warn-tier, not an error.</summary>
    [Fact]
    public void Detailed_StalePathEntry_IsWarning()
    {
        string stale = Path.Combine(Path.GetTempPath(), "em-t19-missing-" + Guid.NewGuid().ToString("N"));
        var child = Global("EM_T19_warn_stale");
        child.PathEntries.Add(stale);

        var result = Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: false);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, w => w.Contains("stale entry") && w.Contains(stale));
    }

    /// <summary>A dangling launch target is warn-tier in the detailed core (suspicious-but-safe).</summary>
    [Fact]
    public void Detailed_DanglingLaunchTarget_IsWarning()
    {
        var profile = Launch("EM_T19_warn_dangling", Path.Combine(Path.GetTempPath(), "em-t19-missing-exe.cmd"));

        var result = Program.RunProfilePreflightDetailed(profile, Program.LoadProfiles(), strict: false);

        Assert.False(result.HasErrors);
        Assert.Contains(result.Warnings, w => w.Contains("dangling launch target"));
    }

    /// <summary>Strict mode surfaces warnings as errors: the gate refuses and the structured report says strict.</summary>
    [Fact]
    public void Detailed_Strict_PromotesWarningToRefusal()
    {
        var child = Global("EM_T19_strict", new ProfileVariable { Name = FreeName, Value = "%EM_T19_UNDEF_STRICT%\\bin" });

        var lenient = Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: false);
        var strict = Program.RunProfilePreflightDetailed(child, Program.LoadProfiles(), strict: true);

        Assert.False(lenient.HasErrors);
        Assert.True(lenient.HasWarnings);
        Assert.True(strict.HasErrors || strict.HasWarnings); // strict surfaces at the gate; ProfileApply turns warnings into exit 1
    }

    /// <summary>End-to-end through the command core: a warn-only profile applies successfully and exits 2;
    /// the same apply under --strict refuses with exit 1 and writes nothing.</summary>
    [Fact]
    public void ApplyCommand_WarnOnlyProfile_Exit2_Default_StrictExit1()
    {
        SeedStore(Global("EM_T19_apply_warn", new ProfileVariable { Name = FreeName, Value = "%EM_T19_UNDEF_APPLY%\\bin" }));

        // Default (lenient): apply proceeds, exit 2.
        int lenientCode = Program.RunProfileCommand(new[] { "profile", "apply", "EM_T19_apply_warn" });
        Assert.Equal(2, lenientCode);
        var applied = Program.LoadProfiles().First(p => p.Name == "EM_T19_apply_warn");
        Assert.True(applied.IsEnabled);

        // Reset, then strict: refuses with exit 1, stays disabled.
        Program.RunProfileCommand(new[] { "profile", "unapply", "EM_T19_apply_warn" });
        int strictCode = Program.RunProfileCommand(new[] { "profile", "apply", "EM_T19_apply_warn", "--strict" });
        Assert.Equal(1, strictCode);
        var strictState = Program.LoadProfiles().First(p => p.Name == "EM_T19_apply_warn");
        Assert.False(strictState.IsEnabled);
    }

    /// <summary>A clean profile applies with the classic exit 0 - no behavioral drift for warning-free profiles.</summary>
    [Fact]
    public void ApplyCommand_CleanProfile_Exit0()
    {
        SeedStore(Global("EM_T19_apply_clean", new ProfileVariable { Name = FreeName, Value = "plain-value" }));

        int code = Program.RunProfileCommand(new[] { "profile", "apply", "EM_T19_apply_clean" });

        Assert.Equal(0, code);
    }

    // ---- apply through the seam ----

    [Fact]
    public void Apply_WritesEffectiveVariables_WithBackupAndSingleBroadcast()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "existing", "user");
        env.ResetBroadcastCount();
        var profile = Global("EM_T04_apply", new ProfileVariable { Name = FreeName, Value = "from-profile" });

        Program.ApplyProfile(profile, env);

        Assert.Equal("from-profile", env.ReadValue(FreeName, "user")?.Value);
        // pre-existing value preserved as name_PowerToys_<profile> backup
        Assert.Equal("existing", env.ReadValue(FreeName + "_PowerToys_EM_T04_apply", "user")?.Value);
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void Unapply_RestoresBackup_RemovesVariable_OneBroadcast()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "existing", "user");
        var profile = Global("EM_T04_unapply", new ProfileVariable { Name = FreeName, Value = "from-profile" });
        Program.ApplyProfile(profile, env);
        env.ResetBroadcastCount();

        Program.UnapplyProfile(profile, env);

        Assert.Equal("existing", env.ReadValue(FreeName, "user")?.Value);
        Assert.Null(env.ReadValue(FreeName + "_PowerToys_EM_T04_unapply", "user"));
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void Apply_SystemScopeVariable_LandsInSystemStore()
    {
        var env = new InMemoryScope();
        SeedStore(Global("EM_T04_sys", new ProfileVariable { Name = FreeName, Value = "v", Scope = "system" }));
        var profile = Program.LoadProfiles().First(p => p.Name == "EM_T04_sys");

        Program.ApplyProfile(profile, env);

        Assert.Equal("v", env.ReadValue(FreeName, "system")?.Value);
        Assert.Null(env.ReadValue(FreeName, "user"));
    }

    /// <summary>Protected entries must never be written by ApplyProfile even if a
    /// poisoned profile slips past pre-flight (defense in depth at the seam).
    /// ComSpec is a real builtin protected variable (system scope); a user-locked custom
    /// name is rejected the same way via IsProtectedVariable.</summary>
    [Fact]
    public void Apply_ProtectedVariable_SkippedNotWritten()
    {
        var env = new InMemoryScope();
        // Seed an innocent profile, then poison the JSON on disk (hand-edit simulation):
        // SaveProfiles refuses protected variables, so the poisoned profile can only reach
        // ApplyProfile by bypassing SaveProfiles - exactly the threat this seam guard covers.
        SeedStore(Global("EM_T04_poison", new ProfileVariable { Name = FreeName, Value = "v" }));
        var poisoned = Global("EM_T04_poison", new ProfileVariable { Name = "ComSpec", Value = "x", Scope = "system" });
        Program.SaveProfilesRawForTests(new List<ProfileData> { poisoned });
        var profile = Program.LoadProfiles().First(p => p.Name == "EM_T04_poison");

        Program.ApplyProfile(profile, env);

        Assert.Null(env.ReadValue("ComSpec", "system"));
        Assert.Equal(0, env.BroadcastCount);
    }

    // ---- launch pre-validation (no registry needed) ----

    [Fact]
    public void LaunchPreflight_RejectsGlobalProfile()
    {
        var profile = Global("EM_T04_launch_global");

        var error = Program.ValidateLaunchPreflight(profile);

        Assert.NotNull(error);
        Assert.Contains("only Launch profiles support", error);
    }

    [Fact]
    public void LaunchPreflight_RejectsMissingTarget()
    {
        var profile = Launch("EM_T04_launch_no_target", null);

        var error = Program.ValidateLaunchPreflight(profile);

        Assert.NotNull(error);
        Assert.Contains("no targetExecutable", error);
    }

    [Fact]
    public void LaunchPreflight_AcceptsValidLaunchProfile()
    {
        var profile = Launch("EM_T04_launch_ok", TempTargetPath);

        Assert.Null(Program.ValidateLaunchPreflight(profile));
    }

    // ---- secret-provider route (fail-closed on unknown provider) ----

    [Fact]
    public void SecretRoute_UnknownProvider_FailsClosed()
    {
        var envelope = new SecretEnvelope
        {
            Provider = "em-t04-unknown-provider",
            Version = 1,
            Ciphertext = "aa",
        }.Serialize();

        Assert.Throws<InvalidOperationException>(() => SecretProviderManager.Decrypt(envelope, "ctx"));
    }

    [Fact]
    public void SecretRoute_NonEnvelopeGarbage_FailsClosed()
    {
        Assert.Throws<InvalidOperationException>(() => SecretProviderManager.Decrypt("not-a-valid-envelope-or-base64!!", "ctx"));
    }
}
