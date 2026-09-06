using EnvManager;
using System.Text.Json;
using System.Text.RegularExpressions;

using Xunit;
using VerifyXunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: ProfileCommand behavioral characterization baseline (architecture-recovery
/// issue 32, spec Phase 5). Feathers-style safety net ahead of the ticket 26 split of
/// the 1605-line ProfileCommand partial into profile-CRUD / launch / secret domain
/// classes: Verify snapshots pin the exact user-facing output of every profile verb
/// that had zero prior coverage - the launch-injection JSON projections and the
/// per-verb error surfaces - so any drift during the extraction shows up as a
/// reviewable snapshot diff.
///
/// Overlap contract with CliOutputSnapshotTests (issue 14, 17 .verified.txt): the three
/// profile scenarios already pinned there (show-masked-secret, show-unknown-profile,
/// reveal-secret decrypt failure) are NOT repeated here. Every snapshot below targets a
/// distinct verb or error branch.
///
/// Hermeticity: each test seeds profiles.json + audit paths via TempProfileDir and
/// drives Program.RunProfileCommand directly; no registry write, no process spawn, no
/// real secret backend (secret ciphertexts are fixture literals; the real DPAPI
/// encrypt path is Tier-3 Pester territory). The only registry touches are read-only
/// HKCU lookups for EM32_-prefixed names that no machine populates (preview
/// currentValue/conflict fields), keeping projections deterministic.
/// </summary>
[Collection("CliSnapshotSerial")]
public class ProfileCommandCharacterizationTests : VerifyBase
{
    static readonly VerifySettings Settings = CreateSettings();

    static VerifySettings CreateSettings()
    {
        var settings = new VerifySettings();
        settings.DisableDiff();
        return settings;
    }

    public ProfileCommandCharacterizationTests() : base(Settings) { }

    // Scrub: same normalization contract as CliOutputSnapshotTests.Scrub (volatile
    // fields only - version/GUID/audit-id/timestamp - plus the JSON unicode escapes so
    // markers read literally). Copy kept local: the original is private to its suite.
    static string Scrub(string text)
    {
        text = Regex.Replace(text, @"^Env Manager v\d+\.\d+\.\d+$", "Env Manager v<version>",
            RegexOptions.Multiline);
        text = Regex.Replace(text, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<guid>",
            RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[0-9a-f]{32}\b", "<audit-id>", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})", "<timestamp>");
        text = text.Replace("\u003C", "<").Replace("\u003E", ">").Replace("\u0026", "&");
        return text;
    }

    // Temp paths emitted by preview projections (exists=true entry) differ per runner;
    // normalize the temp prefix before it reaches a snapshot. Replacing the JSON-escaped
    // form first is safe: the raw form is not a substring of the escaped form.
    static string ScrubTempPaths(string text)
    {
        string tmp = Path.GetTempPath();
        if (string.IsNullOrEmpty(tmp)) return text;
        text = text.Replace(tmp.Replace("\\", "\\"), "<tmp>").Replace(tmp, "<tmp>");
        return text;
    }

    sealed class Captured
    {
        public StringWriter Stdout = new();
        public StringWriter Stderr = new();
    }

    static Captured CaptureConsole()
    {
        var cap = new Captured();
        Console.SetOut(cap.Stdout);
        Console.SetError(cap.Stderr);
        return cap;
    }

    static string ReleaseConsole(Captured cap)
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        return Scrub(ScrubTempPaths(cap.Stdout.ToString().Replace("\r\n", "\n")));
    }

    static string ReleaseConsoleError(Captured cap)
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        return Scrub(ScrubTempPaths(cap.Stderr.ToString().Replace("\r\n", "\n")));
    }

    static string ReleaseConsoleRaw(Captured cap)
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        return cap.Stdout.ToString().Replace("\r\n", "\n");
    }

    static string ReleaseConsoleErrorRaw(Captured cap)
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        return cap.Stderr.ToString().Replace("\r\n", "\n");
    }

    SettingsTask VerifyText(string text)
    {
        return Verify(text.TrimEnd('\n'), extension: "txt");
    }

    // ---- fixtures --------------------------------------------------------------

    static ProfileData MkProfile(string name, string type, string? target = null,
        List<string>? inherits = null, List<ProfileVariable>? variables = null,
        List<string>? pathEntries = null, List<string>? secretVariables = null,
        bool isEnabled = false)
    {
        return new ProfileData
        {
            Name = name,
            ProfileType = type,
            TargetExecutable = target,
            Inherits = inherits ?? new List<string>(),
            Variables = variables ?? new List<ProfileVariable>(),
            PathEntries = pathEntries ?? new List<string>(),
            SecretVariables = secretVariables ?? new List<string>(),
            IsEnabled = isEnabled,
        };
    }

    static ProfileVariable MkVar(string name, string value, string scope = "user")
    {
        return new ProfileVariable { Name = name, Value = value, Scope = scope };
    }

    static void SeedProfiles(params ProfileData[] profiles)
    {
        Program.SaveProfilesRawForTests(new List<ProfileData>(profiles));
    }

    // ---- launch injection JSON contract (preview projection) --------------------

    /// <summary>
    /// The unit-lane pin of the launch-injection contract: profile preview is the
    /// pre-launch JSON projection of the injection set (Pester Tier-3 asserts the real
    /// env block against the same resolution). Pinned here: parent-then-child variable
    /// order with child override, PATH merge with normalize-dedupe keeping the parent
    /// entry first, scope preserved through resolution, and the secret contract - the
    /// stored (ciphertext) value is what preview reports, plaintext never exists.
    /// </summary>
    [Fact]
    public void Preview_InjectionProjection_JsonContract()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_parent", "global",
                variables: new List<ProfileVariable>
                {
                    MkVar("EM32_INJ_A", "parent-value"),
                    MkVar("EM32_INJ_PARENT_ONLY", "p-only"),
                },
                pathEntries: new List<string> { "%EM32_INJ_P1%", "%EM32_INJ_P1%" }),
            MkProfile("em32_child", "launch", target: "em32-ghost.exe",
                inherits: new List<string> { "em32_parent" },
                variables: new List<ProfileVariable>
                {
                    MkVar("EM32_INJ_A", "child-override"),
                    MkVar("EM32_INJ_SYSTEM_VAR", "s-value", "system"),
                    MkVar("EM32_INJ_SECRET", "em32-fake-envelope-ciphertext"),
                },
                pathEntries: new List<string> { "em32-inj-relative-dir" },
                secretVariables: new List<string> { "EM32_INJ_SECRET" }));

        var cap = CaptureConsole();
        string stdout;
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "preview", "em32_child" });
            Assert.Equal(0, code);
            stdout = ReleaseConsoleRaw(cap);
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        Assert.Equal("em32_child", root.GetProperty("profile").GetString());
        Assert.Equal("em32_parent", root.GetProperty("Inherits")[0].GetString());

        var variables = root.GetProperty("variables");
        Assert.Equal(4, variables.GetArrayLength());
        Assert.Equal("EM32_INJ_A", variables[0].GetProperty("name").GetString());
        Assert.Equal("child-override", variables[0].GetProperty("value").GetString());
        Assert.Equal("EM32_INJ_PARENT_ONLY", variables[1].GetProperty("name").GetString());
        Assert.Equal("p-only", variables[1].GetProperty("value").GetString());
        Assert.Equal("EM32_INJ_SECRET", variables[3].GetProperty("name").GetString());
        // Secret contract: preview reports the stored ciphertext-level value and can
        // never report plaintext (none exists in the store).
        Assert.Equal("em32-fake-envelope-ciphertext", variables[3].GetProperty("value").GetString());
        Assert.DoesNotContain("em32-plaintext-canary", stdout);

        var paths = root.GetProperty("pathEntries");
        Assert.Equal(2, paths.GetArrayLength());
        Assert.Equal("%EM32_INJ_P1%", paths[0].GetProperty("path").GetString());
        Assert.Equal("em32-inj-relative-dir", paths[1].GetProperty("path").GetString());
        // %VAR% chain: expansion is attempted for every entry; an undefined variable
        // stays literal, so expandedPath equals the raw path.
        Assert.Equal("%EM32_INJ_P1%", paths[0].GetProperty("expandedPath").GetString());
        Assert.Equal(paths[1].GetProperty("path").GetString(), paths[1].GetProperty("expandedPath").GetString());
    }

    /// <summary>Snapshot twin of the contract test: the byte shape of the preview
    /// projection (mixed-case property names, null currentValue/conflict on hermetic
    /// names, exists=false for the relative dir) is the reviewed drift surface.</summary>
    [Fact]
    public async Task Preview_InjectionProjection_SnapshotIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_parent", "global",
                variables: new List<ProfileVariable>
                {
                    MkVar("EM32_INJ_A", "parent-value"),
                    MkVar("EM32_INJ_PARENT_ONLY", "p-only"),
                },
                pathEntries: new List<string> { "%EM32_INJ_P1%", "%EM32_INJ_P1%" }),
            MkProfile("em32_child", "launch", target: "em32-ghost.exe",
                inherits: new List<string> { "em32_parent" },
                variables: new List<ProfileVariable>
                {
                    MkVar("EM32_INJ_A", "child-override"),
                    MkVar("EM32_INJ_SYSTEM_VAR", "s-value", "system"),
                    MkVar("EM32_INJ_SECRET", "em32-fake-envelope-ciphertext"),
                },
                pathEntries: new List<string> { "em32-inj-relative-dir" },
                secretVariables: new List<string> { "EM32_INJ_SECRET" }));

        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "preview", "em32_child" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Preview_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "preview", "em32_missing" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- status / list JSON projections -----------------------------------------

    [Fact]
    public async Task Status_UnappliedGlobal_JsonProjectionIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_status", "global",
            variables: new List<ProfileVariable> { MkVar("EM32_STATUS_A", "a"), MkVar("EM32_STATUS_B", "b") }));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "status", "em32_status" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Status_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "status", "em32_missing" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task List_MixedProfileStore_JsonProjectionIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_list_global", "global",
                variables: new List<ProfileVariable> { MkVar("EM32_LIST_A", "a") }),
            MkProfile("em32_list_launch", "launch", target: "em32-target.exe",
                variables: new List<ProfileVariable> { MkVar("EM32_LIST_B", "b", "system") },
                pathEntries: new List<string> { "%EM32_LIST_P%" },
                secretVariables: new List<string> { "EM32_LIST_S" },
                isEnabled: true));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "list" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- set-launch error surface ------------------------------------------------

    [Fact]
    public async Task SetLaunch_UnknownFlag_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_x", "--bogus" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetLaunch_MissingTargetAndType_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_x" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetLaunch_InvalidType_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_x", "--type", "bogus" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetLaunch_ProfileNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_present", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_missing", "--type", "launch" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>v0.6.0 boundary: a Global profile cannot carry a launch target.</summary>
    [Fact]
    public async Task SetLaunch_GlobalWithTarget_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_global", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_global", "--target", "em32-ghost.exe" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Converting a Global profile to Launch without supplying --target is
    /// refused because a Launch profile is unusable without one.</summary>
    [Fact]
    public async Task SetLaunch_ConvertToLaunchRequiresTarget_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_global", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_global", "--type", "launch" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>A launch configuration cannot be mutated while the profile is applied.
    /// Seeded via the raw save seam (the way a hand-edited profiles.json would set
    /// isEnabled=true without going through the apply flow).</summary>
    [Fact]
    public async Task SetLaunch_AppliedProfileGuard_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_applied", "global", isEnabled: true,
            variables: new List<ProfileVariable> { MkVar("EM32_APPLIED_A", "a") }));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_applied", "--type", "launch" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetLaunch_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-old.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-launch", "em32_launch", "--args", "--flag value" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- launch error surface (no spawn) ------------------------------------------

    [Fact]
    public async Task Launch_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "launch", "em32_missing" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>v0.7.1 hard boundary via the dispatch path: only Launch profiles
    /// support `profile launch`.</summary>
    [Fact]
    public async Task Launch_GlobalProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_global", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "launch", "em32_global" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Ticket 19 warn-tier: --strict refuses a dangling launch target up front.
    /// The machine-parseable warn report (preflight/command/profile/strict/warnings) is
    /// the JSON half of the launch injection contract; the human line goes to stderr.
    /// Target is a unique relative name, so File.Exists is false on any runner and no
    /// process is ever spawned.</summary>
    [Fact]
    public async Task Launch_StrictDanglingTarget_WarnReportRefusal_IsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_dangling", "launch", target: "em32-no-such-target-32.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "launch", "em32_dangling", "--strict" });
            Assert.Equal(1, code);
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            string stderr = Scrub(cap.Stderr.ToString().Replace("\r\n", "\n"));
            string stdout = Scrub(cap.Stdout.ToString().Replace("\r\n", "\n"));
            Assert.StartsWith("Warning: Profile pre-flight warnings (strict mode: refusing):", stderr);
            Assert.Contains("\"preflight\": \"warn\"", stdout);
            Assert.Contains("\"command\": \"profile launch\"", stdout);
            Assert.Contains("\"strict\": true", stdout);
            Assert.Contains("Launch target does not exist: em32-no-such-target-32.exe (dangling launch target)", stdout);
            await VerifyText(stderr + "\n" + stdout);
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Decryption-failure refusal path of the injection loop: a launch profile
    /// whose secret value is not a decryptable envelope must fail the whole launch
    /// loudly BEFORE any process spawn (hard boundary: never inject garbage). The
    /// fixture value is provider-routed fail-closed, so no backend is touched.</summary>
    [Fact]
    public async Task Launch_UndecryptableSecret_RefusesWithoutSpawn()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_badsecret", "launch", target: "em32-ghost.exe",
            variables: new List<ProfileVariable> { MkVar("EM32_BADSECRET_S", "not-an-envelope") },
            secretVariables: new List<string> { "EM32_BADSECRET_S" }));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "launch", "em32_badsecret" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- set-inherits error surface ------------------------------------------------

    [Fact]
    public async Task SetInherits_SelfInherit_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_si", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-inherits", "em32_si", "em32_si" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetInherits_Cycle_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_si_a", "global", inherits: new List<string> { "em32_si_b" }),
            MkProfile("em32_si_b", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-inherits", "em32_si_b", "em32_si_a" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>v0.7.7 hard boundary (a): Global inheriting Launch is refused at set time.</summary>
    [Fact]
    public async Task SetInherits_GlobalInheritsLaunch_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_si_global", "global"),
            MkProfile("em32_si_launch", "launch", target: "em32-ghost.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-inherits", "em32_si_global", "em32_si_launch" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>v0.7.7 hard boundary (b): Launch inheriting a secret-carrying Launch is
    /// refused at set time (inherited secret has no in-process decrypt path).</summary>
    [Fact]
    public async Task SetInherits_LaunchInheritsSecretLaunch_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_si_plain_launch", "launch", target: "em32-ghost.exe"),
            MkProfile("em32_si_secret_launch", "launch", target: "em32-ghost.exe",
                secretVariables: new List<string> { "EM32_SI_S" }));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-inherits", "em32_si_plain_launch", "em32_si_secret_launch" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetInherits_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-inherits", "em32_missing", "other" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SetInherits_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_si_child", "global"),
            MkProfile("em32_si_parent", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "set-inherits", "em32_si_child", "em32_si_parent" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- secret subdomain error surface --------------------------------------------

    /// <summary>v0.7.5 hard boundary: secrets are meaningful only on Launch profiles;
    /// ProfileAddSecret rejects a Global profile at entry before any encryption.</summary>
    [Fact]
    public async Task AddSecret_GlobalProfile_RejectedBeforeEncryption()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_global", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-secret", "em32_global", "EM32_S", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task AddSecret_InvalidName_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-ghost.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-secret", "em32_launch", "EM32=BAD", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task AddSecret_ProfileNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-secret", "em32_missing", "EM32_S", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task EditSecret_InvalidName_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-ghost.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "edit-secret", "em32_launch", "EM32_OLD", "EM32=BAD", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task EditSecret_SecretNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-ghost.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "edit-secret", "em32_launch", "EM32_NOPE", "EM32_NEW", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task RemoveSecret_VariableNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-ghost.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "remove-secret", "em32_launch", "EM32_NOPE" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Reveal-secret on a name that exists but is not marked secret. The
    /// DPAPI failure copy is already pinned by CliOutputSnapshotTests; this is the
    /// distinct membership branch.</summary>
    [Fact]
    public async Task RevealSecret_NotAMarkedSecret_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-ghost.exe",
            variables: new List<ProfileVariable> { MkVar("EM32_REGVAR", "plain") }));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "reveal-secret", "em32_launch", "EM32_REGVAR" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task ExportSecrets_MissingOutputArg_UsageCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "export-secrets", "em32_x" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task ExportSecrets_ProfileNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "export-secrets", "em32_missing", "out.bin" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task ImportSecrets_MissingInputArg_UsageCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "import-secrets", "em32_x" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SecretProvider_UnknownSubcommand_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "secret-provider", "warp" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task SecretProvider_MissingSub_UsageCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "secret-provider" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- profile-CRUD error surface --------------------------------------------------

    [Fact]
    public async Task Create_DuplicateName_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_dup", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "create", "em32_dup" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Global profiles refuse launch-only flags (--target/--args/--cwd).</summary>
    [Fact]
    public async Task Create_GlobalWithLaunchFlags_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "create", "em32_new", "--target", "em32-ghost.exe" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Create_UnknownFlag_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "create", "em32_new", "--bogus" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Create_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "create", "em32_fresh" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Delete_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "delete", "em32_missing" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Rename_SourceNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "rename", "em32_missing", "em32_new" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Rename_TargetCollision_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(
            MkProfile("em32_src", "global"),
            MkProfile("em32_dst", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "rename", "em32_src", "em32_dst" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Rename_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_old", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "rename", "em32_old", "em32_new" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Delete_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_doomed", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "delete", "em32_doomed" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- variable / path management error surface ------------------------------------

    [Fact]
    public async Task AddVar_InvalidName_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-var", "em32_p", "EM32=BAD", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task AddVar_InvalidScopeFlag_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-var", "em32_p", "EM32_V", "v", "--scope", "bogus" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>The scope-aware add-var wrapper rejects any extra token after the value
    /// so behavior stays predictable (ProfileAddVarWithScope strict tail).</summary>
    [Fact]
    public async Task AddVar_UnexpectedArgument_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-var", "em32_p", "EM32_V", "v", "extra" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task AddVar_ProfileNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-var", "em32_missing", "EM32_V", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task AddVar_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-var", "em32_p", "EM32_V", "v", "--scope", "system" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>remove-var of an absent variable is a WARNING with exit 0 (idempotent),
    /// not an error - pinned as-is.</summary>
    [Fact]
    public async Task RemoveVar_VariableAbsent_WarnsExitsZero()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "remove-var", "em32_p", "EM32_NOPE" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task RemoveVar_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global",
            variables: new List<ProfileVariable> { MkVar("EM32_V", "v") }));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "remove-var", "em32_p", "EM32_V" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task EditVar_VariableNotFound_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "edit-var", "em32_p", "EM32_NOPE", "EM32_NEW", "v" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task AddPath_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-path", "em32_missing", "C:\\em32-nope" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>PATH fragments containing ';' are invalid; ProfileAddPath surfaces the
    /// ValidatePathFragment rejection through the generic exception contract.</summary>
    [Fact]
    public void AddPath_SemicolonFragment_ThrowsArgumentException()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        Assert.Throws<ArgumentException>(() => Program.RunProfileCommand(
            new[] { "profile", "add-path", "em32_p", "C:\\a;C:\\b" }));
    }

    [Fact]
    public async Task AddPath_Success_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "add-path", "em32_p", "C:\\em32-dir" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task RemovePath_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "remove-path", "em32_missing", "C:\\em32-nope" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>remove-path of an absent entry still reports success (idempotent copy).</summary>
    [Fact]
    public async Task RemovePath_EntryAbsent_StdoutIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_p", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "remove-path", "em32_p", "C:\\em32-nope" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- export / import + dispatch faces ---------------------------------------------

    [Fact]
    public async Task Export_MissingOutputArg_UsageCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "export", "em32_x" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Import_MissingInputArg_UsageCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "import" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Dispatch default branch: unknown profile subcommand via the shared
    /// ArgError seam.</summary>
    [Fact]
    public async Task UnknownSubcommand_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "frobnicate" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Launch-apply hard boundary via the dispatch path: a Launch profile is
    /// refused by profile apply before any registry work (complements the seam-level
    /// LaunchPreflight_ asserts in ProfileSeamValidationTests).</summary>
    [Fact]
    public async Task Apply_LaunchProfileRejected_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_launch", "launch", target: "em32-ghost.exe"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "apply", "em32_launch" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Apply_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "apply", "em32_missing" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Unapply_NotApplied_WarningCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        SeedProfiles(MkProfile("em32_plain", "global"));
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "unapply", "em32_plain" });
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }
}
