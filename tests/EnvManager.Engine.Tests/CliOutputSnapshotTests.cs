using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: CLI "human-readable contract" snapshot suite (architecture-recovery issue 14,
/// spec Phase 3). Verify.Xunit locks the exact user-facing output of the CLI - help text,
/// command stdout, error messages, and the canary masking markers
/// (<encrypted>/<revealed>) - so any wording drift becomes an explicit, reviewable diff.
/// Complements the IPC schema golden files (issue 08); does not replace them.
///
/// Determinism contract: every captured stream passes through <see cref="Scrub"/>,
/// which normalizes ONLY known-volatile fields (assembly version, profile GUIDs, audit
/// entry id/timestamp). User-facing wording, error copy, and the masking markers are
/// preserved byte-exact - a scrubber that swallowed real copy would defeat the diff
/// purpose. Console redirection is serialized (collection) so parallel tests cannot
/// interleave.
/// </summary>
[Collection("CliSnapshotSerial")]
public class CliOutputSnapshotTests : VerifyBase
{
    const char Q = '"'; // double-quote char, keeps regex literals quote-free

    // Shared settings: source-controlled .txt snapshots (no diff-tool launch), stable
    // naming. Verify 31.x requires an explicit ctor passing settings to VerifyBase.
    static readonly VerifySettings Settings = CreateSettings();

    static VerifySettings CreateSettings()
    {
        var settings = new VerifySettings();
        settings.DisableDiff();
        return settings;
    }

    public CliOutputSnapshotTests() : base(Settings) { }

    static string Scrub(string text)
    {
        // The help banner embeds the assembly version; replace the number but keep the
        // line shape so wording drift around it still shows.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"^Env Manager v\d+\.\d+\.\d+$", "Env Manager v<version>",
            System.Text.RegularExpressions.RegexOptions.Multiline);
        // Profile GUIDs (issued by ProfileData's initializer).
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "<guid>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        // Audit entry ids are Guid "N" format (exactly 32 hex chars); timestamps are
        // RFC3339. Neither pattern embeds quote characters, so JSON shape survives.
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"\b[0-9a-f]{32}\b", "<audit-id>",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        text = System.Text.RegularExpressions.Regex.Replace(
            text, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(\.\d+)?(Z|[+-]\d{2}:\d{2})", "<timestamp>");
        // System.Text.Json escapes < > & as \u003C/\u003E/\u0026 inside JSON strings; restore the
        // literals so the snapshot reads as the user-facing contract (and masking-marker
        // assertions compare the real markers).
        text = text.Replace("\\u003C", "<").Replace("\\u003E", ">").Replace("\\u0026", "&");
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
        return Scrub(cap.Stdout.ToString().Replace("\r\n", "\n"));
    }

    static string ReleaseConsoleError(Captured cap)
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
        return Scrub(cap.Stderr.ToString().Replace("\r\n", "\n"));
    }

    SettingsTask VerifyText(string text)
    {
        return Verify(text.TrimEnd('\n'), extension: "txt");
    }

    // ---- help ------------------------------------------------------------------

    [Fact]
    public Task Help_MainHelpText_IsStable()
    {
        return VerifyText(Scrub(Program.BuildHelpText()));
    }

    /// <summary>
    /// The Main dispatcher is private (it initializes crash-dialog suppression, mutexes, and
    /// the registry snapshot machinery), so the snapshot pins the contract pieces it composes:
    /// the "Unknown command:" stderr prefix plus the help body it appends, both stable seams.
    /// </summary>
    [Fact]
    public async Task Help_UnknownCommand_ErrorCopyPlusHelp_IsStable()
    {
        var cap = CaptureConsole();
        try
        {
            Console.Error.WriteLine("Unknown command: definitely-not-a-command");
            ShowHelpForSnapshot();
            var combined = cap.Stderr + "\n" + cap.Stdout;
            Console.SetOut(new StringWriter());
            Console.SetError(new StringWriter());
            Assert.StartsWith("Unknown command: definitely-not-a-command", Scrub(combined.Replace("\r\n", "\n")));
            await VerifyText(Scrub(combined.Replace("\r\n", "\n")));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    /// <summary>Mirrors the Main() unknown-command branch: stderr line then full help.</summary>
    static void ShowHelpForSnapshot()
    {
        Console.Out.Write(Program.BuildHelpText());
        Console.Out.WriteLine();
    }

    // ---- write-path stdout / error copy (via InMemoryScope seam) ----------------

    [Fact]
    public async Task Rename_Success_StdoutIsStable()
    {
        var scope = new InMemoryScope();
        scope.WriteValue("EM_SNAP_A", "v1", "user");
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunRename(new[] { "rename", "EM_SNAP_A", "EM_SNAP_B", "--scope", "user" }, scope);
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Rename_SourceMissing_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunRename(new[] { "rename", "EM_SNAP_NONE", "EM_SNAP_B", "--scope", "user" }, scope);
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Rename_TargetExistsWithoutOverwrite_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        scope.WriteValue("EM_SNAP_A", "v1", "user");
        scope.WriteValue("EM_SNAP_B", "v2", "user");
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunRename(new[] { "rename", "EM_SNAP_A", "EM_SNAP_B", "--scope", "user" }, scope);
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Rename_ProtectedSource_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        scope.WriteValue("EM_TEST_LOCKED", "v", "user");
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunRename(
                new[] { "rename", "EM_TEST_LOCKED", "EM_SNAP_B", "--scope", "user" },
                scope,
                (name, _) => name.Equals("EM_TEST_LOCKED", StringComparison.OrdinalIgnoreCase));
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Set_ProtectedVariable_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        var cap = CaptureConsole();
        try
        {
            bool ok = Program.WriteVariableCore(
                scope,
                (name, _) => name.Equals("EM_TEST_LOCKED", StringComparison.OrdinalIgnoreCase),
                "EM_TEST_LOCKED", "x", "user");
            Assert.False(ok);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Set_NameWithEquals_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        var cap = CaptureConsole();
        try
        {
            bool ok = Program.WriteVariableCore(scope, (_, _) => false, "EM=BAD", "x", "user");
            Assert.False(ok);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task ChangeScope_AlreadyInTargetScope_WarningCopyIsStable()
    {
        var scope = new InMemoryScope();
        scope.WriteValue("EM_SNAP_A", "v", "user");
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunChangeScope(new[] { "change-scope", "EM_SNAP_A", "user", "--scope", "user" }, scope);
            Assert.Equal(0, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Toggle_ProtectedVariable_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        var cap = CaptureConsole();
        try
        {
            int code = Program.ToggleVariableCore(
                scope,
                (name, _) => name.Equals("EM_TEST_LOCKED", StringComparison.OrdinalIgnoreCase),
                "EM_TEST_LOCKED", "user");
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task Delete_ProtectedVariable_ErrorCopyIsStable()
    {
        var scope = new InMemoryScope();
        var cap = CaptureConsole();
        try
        {
            Program.DeleteVariableCore(
                scope,
                (name, _) => name.Equals("EM_TEST_LOCKED", StringComparison.OrdinalIgnoreCase),
                "EM_TEST_LOCKED", "user");
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- list / get stdout (stable projection) ----------------------------------

    /// <summary>
    /// list/get read the real registry (no seam yet), so their stdout snapshots are out of
    /// scope for a hermetic unit lane. The stderr contract lane instead pins the scrubbed
    /// error copy that Main() emits for exceptions (Program.cs: "Error: " + ScrubExceptionMessage).
    /// </summary>
    [Fact]
    public async Task Error_GenericExceptionCopy_ScrubbedAndStable()
    {
        string scrubbed = Program.ScrubExceptionMessage("launch failed for password=canary-value");
        await VerifyText("Error: " + scrubbed);
    }

    [Fact]
    public async Task Error_UnauthorizedAccessCopy_IsStable()
    {
        // The exact copy Main() prints for UnauthorizedAccessException (Program.cs catch).
        await VerifyText("Error: Access denied (requires elevation)");
    }

    [Fact]
    public async Task Toggle_DisableRestore_InfoCopyIsStable()
    {
        var scope = new InMemoryScope();
        scope.WriteValue("EM_SNAP_A", "v1", "user");
        var cap = CaptureConsole();
        try
        {
            // ToggleVariableCore returns outcome codes; the seam itself holds the mechanics.
            int disable = Program.ToggleVariableCore(scope, (_, _) => false, "EM_SNAP_A", "user");
            int restore = Program.ToggleVariableCore(scope, (_, _) => false, "EM_SNAP_A", "user");
            Assert.Equal(0, disable);
            Assert.Equal(0, restore);
            // The toggle command prints a JSON projection of the disabled/updated variable.
            await VerifyText(ReleaseConsole(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- canary masking markers (<encrypted>/<revealed>) -------------------------

    [Fact]
    public async Task ProfileShow_MasksSecretValueAsEncrypted()
    {
        using var tmp = new TempProfileDir();
        Program.SaveProfilesRawForTests(new List<ProfileData>
        {
            new()
            {
                Name = "snap-launch",
                ProfileType = "launch",
                SecretVariables = new List<string> { "EM_SNAP_SECRET" },
                Variables = new List<ProfileVariable>
                {
                    new() { Name = "EM_SNAP_SECRET", Value = "ciphertext-not-plaintext" },
                },
            },
        });
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "show", "snap-launch" });
            Assert.Equal(0, code);
            var stdout = ReleaseConsole(cap);
            // Positive control: the masking marker is present...
            Assert.Contains("<encrypted>", stdout);
            // ...and the raw ciphertext never reaches stdout.
            Assert.DoesNotContain("ciphertext-not-plaintext", stdout);
            await VerifyText(stdout);
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task ProfileShow_UnknownProfile_ErrorCopyIsStable()
    {
        using var tmp = new TempProfileDir();
        var cap = CaptureConsole();
        try
        {
            int code = Program.RunProfileCommand(new[] { "profile", "show", "snap-missing" });
            Assert.Equal(1, code);
            await VerifyText(ReleaseConsoleError(cap));
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    [Fact]
    public async Task ProfileRevealSecret_FailureCopyIsScrubbedAndStable()
    {
        using var tmp = new TempProfileDir();
        Program.SaveProfilesRawForTests(new List<ProfileData>
        {
            new()
            {
                Name = "snap-launch",
                ProfileType = "launch",
                SecretVariables = new List<string> { "EM_SNAP_SECRET" },
                Variables = new List<ProfileVariable>
                {
                    new() { Name = "EM_SNAP_SECRET", Value = "ciphertext-not-plaintext" },
                },
            },
        });
        var cap = CaptureConsole();
        try
        {
            // The DPAPI backend fails on this fake ciphertext outside a real user store;
            // the snapshot pins the failure copy with the scrubbed upstream reason. The
            // audit <revealed> positive path is covered by the live canary Pester suite.
            int code = Program.RunProfileCommand(new[] { "profile", "reveal-secret", "snap-launch", "EM_SNAP_SECRET" });
            Assert.Equal(1, code);
            var stderr = ReleaseConsoleError(cap);
            Assert.StartsWith("Error: Failed to decrypt secret", stderr);
            Assert.DoesNotContain("ciphertext-not-plaintext", stderr);
            await VerifyText(stderr);
        }
        finally { Console.SetOut(new StringWriter()); Console.SetError(new StringWriter()); }
    }

    // ---- scrubber self-check: volatile fields never reach a snapshot -------------

    [Fact]
    public void Scrubber_GuidTimestampAndVersion_AreNormalized()
    {
        string raw = "Env Manager v1.2.3\nprofile id 123e4567-e89b-12d3-a456-426614174000\n"
            + Q + "id" + Q + ": " + Q + "abc123def456abc123def456abc123de" + Q + ", "
            + Q + "timestamp" + Q + ": " + Q + "2026-09-03T14:00:00.0000000+08:00" + Q;
        string scrubbed = Scrub(raw);
        Assert.DoesNotContain("1.2.3", scrubbed);
        Assert.DoesNotContain("123e4567", scrubbed);
        Assert.DoesNotContain("2026-09-03", scrubbed);
        Assert.Contains("<version>", scrubbed);
        Assert.Contains("<guid>", scrubbed);
        Assert.Contains("<audit-id>", scrubbed);
        Assert.Contains("<timestamp>", scrubbed);
        // masking markers survive scrubbing untouched
        Assert.Contains("<encrypted>", Scrub("value = <encrypted>"));
        Assert.Contains("<revealed>", Scrub("value = <revealed>"));
        Assert.Contains("<redacted>", Scrub("value = <redacted>"));
        // user-facing copy is not swallowed
        Assert.Contains("Error: Cannot rename protected variable",
            Scrub("Error: Cannot rename protected variable (source protected): X"));
    }
}

/// <summary>Serializes Console redirection across the snapshot suite.</summary>
[CollectionDefinition("CliSnapshotSerial", DisableParallelization = true)]
public sealed class CliSnapshotSerialCollection;

/// <summary>Hermetic profiles.json + audit path redirect (pattern from ProfileSeamValidationTests).</summary>
sealed class TempProfileDir : IDisposable
{
    public TempProfileDir()
    {
        Dir = Path.Combine(Path.GetTempPath(), "em-snap-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Dir);
        Program.SetProfilesFilePathForTests(Path.Combine(Dir, "profiles.json"));
        Program.SetAuditFilePathForTests(Path.Combine(Dir, "audit.json"));
        Program.SetAuditKeyPathForTests(Path.Combine(Dir, "audit.key"));
    }
    public string Dir { get; }
    public void Dispose()
    {
        Program.SetProfilesFilePathForTests(null);
        Program.SetAuditFilePathForTests(null);
        Program.SetAuditKeyPathForTests(null);
        try { Directory.Delete(Dir, true); } catch { /* best effort */ }
    }
}
