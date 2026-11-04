using EnvManager;
using System.Reflection;
using System.Text.Json;
using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: command-path characterization for the three dispatchers that had zero test
/// reach ahead of the complexity-ramp extraction (parent spec: cognitive-complexity top
/// tier, five-method decomposition). The `protection` dispatcher and the `audit`
/// dispatcher (existing AuditVerifyTests call AuditVerify.Run directly, bypassing the
/// dispatch switch) had no test executing them; the `history undo` profile branch -
/// TryUndoProfileAudit - was likewise never executed by a test. These tests pin the
/// current externally observable behavior (stdout/stderr copy, exit codes, store side
/// effects) through the real command handlers so the upcoming extraction has a safety
/// net at the command seam.
///
/// The handlers are private static on the Program partial (Main initializes
/// crash-dialog suppression and mutexes, so it is not a test seam); they are invoked
/// via reflection, the same NonPublic precedent as ProgramMethodBudgetTests. The
/// non-undoable audit fixture is seeded through the real RecordProfileAudit recorder
/// with the exact command string the add-secret verb records, because every command
/// that records a non-undoable profile entry needs a live secret backend. Hermetic:
/// profiles.json, audit.json, audit.key and the protection stores are redirected to a
/// per-test temp directory through the existing ForTests seams; no registry write, no
/// process spawn, no real secret backend.
/// </summary>
[Collection("CliSnapshotSerial")]
public class ComplexityRampCharacterizationTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "em-ramp-" + Guid.NewGuid().ToString("N"));

    public ComplexityRampCharacterizationTests()
    {
        Directory.CreateDirectory(_dir);
        Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", _dir);
        Program.SetAppDataDirectoryForTests(_dir);
        Program.SetProfilesFilePathForTests(Path.Combine(_dir, "profiles.json"));
        Program.SetAuditFilePathForTests(Path.Combine(_dir, "audit.json"));
        Program.SetAuditKeyPathForTests(Path.Combine(_dir, "audit.key"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", null);
        Program.SetAppDataDirectoryForTests(null);
        Program.SetProfilesFilePathForTests(null);
        Program.SetAuditFilePathForTests(null);
        Program.SetAuditKeyPathForTests(null);
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    // ---- seams ------------------------------------------------------------------

    /// <summary>Invoke a private static command handler on the Program partial.</summary>
    static int Invoke(string method, params string[] args)
    {
        var mi = typeof(Program).GetMethod(method,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("missing command handler: " + method);
        return (int)mi.Invoke(null, new object[] { args })!;
    }

    /// <summary>Read the encrypted audit history through the production loader.</summary>
    static List<AuditEntry> LoadHistory()
    {
        var mi = typeof(Program).GetMethod("LoadAuditHistory",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("missing LoadAuditHistory");
        return (List<AuditEntry>)mi.Invoke(null, null)!;
    }

    /// <summary>Seed one profile-scoped audit entry through the real recorder (encrypted at rest).</summary>
    static void RecordProfileAudit(string command, string profileName, string? oldSummary = null)
    {
        var mi = typeof(Program).GetMethod("RecordProfileAudit",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("missing RecordProfileAudit");
        mi.Invoke(null, new object?[] { command, profileName, oldSummary, null });
    }

    sealed class Captured
    {
        public TextWriter PreviousOut = Console.Out;
        public TextWriter PreviousErr = Console.Error;
        public StringWriter Stdout = new();
        public StringWriter Stderr = new();
    }

    /// <summary>Redirect stdout/stderr into string writers for the duration of one command invocation.</summary>
    static Captured CaptureConsole()
    {
        var cap = new Captured();
        Console.SetOut(cap.Stdout);
        Console.SetError(cap.Stderr);
        return cap;
    }

    /// <summary>Restore the console writers that were active before the capture (xUnit v2 does not restore them itself).</summary>
    static void ReleaseConsole(Captured cap)
    {
        Console.SetOut(cap.PreviousOut);
        Console.SetError(cap.PreviousErr);
    }

    /// <summary>Read one custom protection store straight from disk (the loader falls back to empty on corrupt JSON).</summary>
    List<string> ReadCustomProtectionFile(string fileName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_dir, fileName)));
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);
        return doc.RootElement.EnumerateArray().Select(v => v.GetString()!).ToList();
    }

    // ---- history undo: the profile branch (TryUndoProfileAudit) ------------------

    /// <summary>Undoing a profile-create audit entry deletes the profile and exits zero.</summary>
    [Fact]
    public void Undo_ProfileCreate_DeletesProfileAndExitsZero()
    {
        Assert.Equal(0, Program.RunProfileCommand(new[] { "profile", "create", "em_ramp_a" }));
        var history = LoadHistory();
        Assert.Single(history);
        var entry = history[0];
        Assert.Equal("profile create", entry.Command);
        Assert.Equal("profile", entry.Scope);
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", entry.Id); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, exit);
        Assert.Contains("\"undone\"", cap.Stdout.ToString());
        Assert.Contains("em_ramp_a", cap.Stdout.ToString());
        Assert.DoesNotContain(Program.LoadProfiles(), p => p.Name == "em_ramp_a");
    }

    /// <summary>Undoing a profile-delete audit entry restores every captured profile field.</summary>
    [Fact]
    public void Undo_ProfileDelete_RestoresProfileFromCapturedState()
    {
        // Seeded through the raw save seam so the fixture carries non-default captured
        // fields without setup verbs that would add audit entries. isEnabled stays false
        // on purpose: deleting an applied profile calls UnapplyProfile, which writes the
        // real HKCU\Environment and would break this suite's no-registry-write contract.
        var parent = new ProfileData();
        parent.SetName("em_ramp_parent");
        parent.SetProfileType("global");
        var child = new ProfileData();
        child.SetName("em_ramp_b");
        child.SetProfileType("launch");
        child.SetLaunchTarget("C:\\em-ramp-target.exe");
        child.SetLaunchArguments("--from-audit");
        child.SetWorkingDirectory("C:\\em-ramp-work");
        child.SetAppliedAt(123456789);
        child.SetSecretVariables(new[] { "EM_RAMP_SECRET" });
        child.SetSchemaVersion(2);
        child.SetInherits(new[] { "em_ramp_parent" });
        child.AddPathEntry("C:\\em-ramp-path", "system");
        child.AddVariable("EM_RAMP_B_VAR", "b-value", "system");
        Program.SaveProfilesRawForTests(new List<ProfileData> { parent, child });

        Assert.Equal(0, Program.RunProfileCommand(new[] { "profile", "delete", "em_ramp_b" }));
        // Prove the delete landed: UndoDelete no-ops when the id still exists, so a
        // delete that left the profile in place would satisfy the restore assertions.
        Assert.DoesNotContain(Program.LoadProfiles(), p => p.Name == "em_ramp_b");
        var history = LoadHistory();
        var deleteEntry = Assert.Single(history);
        Assert.Equal("profile delete", deleteEntry.Command);

        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", deleteEntry.Id); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, exit);

        var restored = Program.LoadProfiles().Single(p => p.Name == "em_ramp_b");
        Assert.Equal(child.Id, restored.Id);
        Assert.False(restored.IsEnabled);
        Assert.Equal(123456789, restored.AppliedAt);
        Assert.Equal(new[] { "em_ramp_parent" }, restored.Inherits);
        Assert.Equal(new[] { "C:\\em-ramp-path" }, restored.PathEntries);
        Assert.Equal(new[] { "system" }, restored.PathScopes);
        Assert.Equal("launch", restored.ProfileType);
        Assert.Equal("C:\\em-ramp-target.exe", restored.TargetExecutable);
        Assert.Equal("--from-audit", restored.LaunchArguments);
        Assert.Equal("C:\\em-ramp-work", restored.WorkingDirectory);
        Assert.Equal(new[] { "EM_RAMP_SECRET" }, restored.SecretVariables);
        Assert.Equal(2, restored.SchemaVersion);
        var variable = Assert.Single(restored.Variables);
        Assert.Equal("EM_RAMP_B_VAR", variable.Name);
        Assert.Equal("b-value", variable.Value);
        Assert.Equal("system", variable.Scope);
    }

    /// <summary>Older audit summaries still restore with the legacy model defaults for fields they omitted.</summary>
    [Fact]
    public void Undo_ProfileDelete_LegacySummaryUsesDefaultsForMissingFields()
    {
        string legacySummary = JsonSerializer.Serialize(new
        {
            id = "legacy-profile-id",
            name = "em_ramp_legacy",
            isEnabled = false,
            inherits = Array.Empty<string>(),
            pathEntries = new[] { "C:\\legacy-path" },
            variables = Array.Empty<ProfileVariable>()
        });
        RecordProfileAudit("profile delete", "em_ramp_legacy", legacySummary);
        var entry = Assert.Single(LoadHistory());

        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", entry.Id); }
        finally { ReleaseConsole(cap); }

        Assert.Equal(0, exit);
        var restored = Assert.Single(Program.LoadProfiles());
        Assert.Equal("legacy-profile-id", restored.Id);
        Assert.Equal("em_ramp_legacy", restored.Name);
        Assert.Equal(new[] { "C:\\legacy-path" }, restored.PathEntries);
        Assert.Equal(new[] { "user" }, restored.PathScopes);
        Assert.Equal("global", restored.ProfileType);
        Assert.Null(restored.TargetExecutable);
        Assert.Null(restored.LaunchArguments);
        Assert.Null(restored.WorkingDirectory);
    }

    /// <summary>A non-undoable profile command refuses with the explicit no-undo-path error copy.</summary>
    [Fact]
    public void Undo_NonUndoableProfileCommand_RefusesWithClearError()
    {
        RecordProfileAudit("profile add-secret", "em_ramp_c");
        var history = LoadHistory();
        Assert.Single(history);
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", history[0].Id); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("has no undo path", cap.Stderr.ToString());
        Assert.Contains("cannot be undone", cap.Stderr.ToString());
    }

    /// <summary>Undoing an unknown audit id exits one with the not-found error copy.</summary>
    [Fact]
    public void Undo_UnknownEntry_ExitsOneWithNotFoundCopy()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", "no-such-entry"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Error: Audit entry not found", cap.Stderr.ToString());
    }

    // ---- protection dispatcher ----------------------------------------------------

    /// <summary>A fresh store lists seeded built-ins with empty custom collections.</summary>
    [Fact]
    public void Protection_List_FreshStore_EmitsBuiltInsAndEmptyCustom()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "list"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(cap.Stdout.ToString());
        var vars = doc.RootElement.GetProperty("protectedVars");
        Assert.Equal(0, vars.GetProperty("custom").GetArrayLength());
        Assert.Contains(vars.GetProperty("builtIn").EnumerateArray(), v => v.GetString() == "windir");
        var paths = doc.RootElement.GetProperty("protectedPaths");
        Assert.Equal(0, paths.GetProperty("custom").GetArrayLength());
    }

    /// <summary>add-var then remove-var round-trips the custom protected-variable store.</summary>
    [Fact]
    public void Protection_AddVar_ThenRemoveVar_RoundTripsCustomStore()
    {
        var cap = CaptureConsole();
        int add;
        try { add = Invoke("RunProtectionCommand", "protection", "add-var", "EM_RAMP_X"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, add);
        Assert.Contains("Added protected variable: EM_RAMP_X", cap.Stdout.ToString());
        Assert.True(File.Exists(Path.Combine(_dir, "protected-vars.json")));
        Assert.Equal(new[] { "EM_RAMP_X" }, ReadCustomProtectionFile("protected-vars.json"));

        cap = CaptureConsole();
        int remove;
        try { remove = Invoke("RunProtectionCommand", "protection", "remove-var", "EM_RAMP_X"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, remove);
        Assert.Contains("Removed protected variable: EM_RAMP_X", cap.Stdout.ToString());
        Assert.Empty(ReadCustomProtectionFile("protected-vars.json"));
    }

    /// <summary>add-path then remove-path round-trips the custom protected-PATH store.</summary>
    [Fact]
    public void Protection_AddPath_ThenRemovePath_RoundTripsCustomStore()
    {
        var cap = CaptureConsole();
        int add;
        try { add = Invoke("RunProtectionCommand", "protection", "add-path", "C:\\em-ramp\\dir"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, add);
        Assert.Contains("Added protected PATH entry: C:\\em-ramp\\dir", cap.Stdout.ToString());
        Assert.Equal(new[] { "C:\\em-ramp\\dir" }, ReadCustomProtectionFile("protected-paths.json"));

        cap = CaptureConsole();
        int remove;
        try { remove = Invoke("RunProtectionCommand", "protection", "remove-path", "C:\\em-ramp\\dir"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, remove);
        Assert.Contains("Removed protected PATH entry: C:\\em-ramp\\dir", cap.Stdout.ToString());
        Assert.Empty(ReadCustomProtectionFile("protected-paths.json"));
    }

    /// <summary>Removing a built-in protected variable is rejected with the cannot-remove copy.</summary>
    [Fact]
    public void Protection_RemoveVar_BuiltIn_Rejected()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "remove-var", "windir"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Cannot remove built-in protected variable", cap.Stderr.ToString());
    }

    /// <summary>Adding a PATH entry containing a semicolon is rejected as invalid.</summary>
    [Fact]
    public void Protection_AddPath_InvalidEntry_Rejected()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "add-path", "C:\\a;b"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Invalid PATH entry", cap.Stderr.ToString());
        Assert.False(File.Exists(Path.Combine(_dir, "protected-paths.json")));
    }

    /// <summary>An unknown protection subcommand exits one with the usage line.</summary>
    [Fact]
    public void Protection_UnknownSub_UsageError()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "bogus"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Usage: env-manager protection", cap.Stderr.ToString());
    }

    // ---- audit dispatcher ----------------------------------------------------------

    /// <summary>Bare audit exits one with the subcommand usage block.</summary>
    [Fact]
    public void Audit_NoSubcommand_UsageError()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Usage: env-manager audit", cap.Stderr.ToString());
    }

    /// <summary>An unknown audit subcommand exits one naming the subcommand.</summary>
    [Fact]
    public void Audit_UnknownSubcommand_Rejected()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "bogus"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Unknown audit subcommand: bogus", cap.Stderr.ToString());
    }

    /// <summary>verify with no ledger file reports verified=true, events=0, exit zero.</summary>
    [Fact]
    public void Audit_Verify_NoLedger_ReportsZeroEvents()
    {
        string ledger = Path.Combine(_dir, "absent-ledger.jsonl");
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "verify", "--ledger", ledger); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(cap.Stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("events").GetInt32());
    }

    /// <summary>verify on an unparseable ledger reports the breakage and still exits zero non-strict.</summary>
    [Fact]
    public void Audit_Verify_BrokenLedger_ReportsBreakageNonStrict()
    {
        string ledger = Path.Combine(_dir, "broken-ledger.jsonl");
        File.WriteAllText(ledger, "not-json\n");
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "verify", "--ledger", ledger); }
        finally { ReleaseConsole(cap); }
        // Non-strict verify reports the breakage but still exits zero.
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(cap.Stdout.ToString());
        Assert.False(doc.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("events").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("breakages").GetArrayLength());
    }

    // ---- audit encrypt-file branch (#209 CodeRabbit: CLI-path coverage) -------------

    /// <summary>encrypt-file without --input/--output exits one with the required-args error.</summary>
    [Fact]
    public void Audit_EncryptFile_MissingArgs_RequiredError()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "encrypt-file"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Error: --input and --output required", cap.Stderr.ToString());
    }

    /// <summary>encrypt-file with a non-existent input file exits one with the not-found error.</summary>
    [Fact]
    public void Audit_EncryptFile_InputNotFound_FileNotFoundError()
    {
        string missing = Path.Combine(_dir, "no-such-file.txt");
        string output = Path.Combine(_dir, "out.enc");
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "encrypt-file", "--input", missing, "--output", output); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(1, exit);
        Assert.Contains("Error: input file not found: " + missing, cap.Stderr.ToString());
    }

    // ---- audit list branch (#209 CodeRabbit: CLI-path coverage) ----------------------

    /// <summary>list with no audit.json present outputs empty brackets and exits zero.</summary>
    [Fact]
    public void Audit_List_NoAuditFile_EmptyBrackets()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "list"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, exit);
        Assert.Equal("[]", cap.Stdout.ToString().Trim());
    }

    /// <summary>list --json with an audit.json file echoes the raw file content.</summary>
    [Fact]
    public void Audit_List_JsonFlag_EchoesRawContent()
    {
        string auditDir = Path.Combine(_dir, "EnvManager");
        Directory.CreateDirectory(auditDir);
        string auditPath = Path.Combine(auditDir, "audit.json");
        File.WriteAllText(auditPath, "[{\"command\":\"set\",\"timestamp\":\"2026-01-01T00:00:00Z\"}]");
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "list", "--json"); }
        finally { ReleaseConsole(cap); }
        Assert.Equal(0, exit);
        Assert.Contains("\"command\":\"set\"", cap.Stdout.ToString());
    }
}
