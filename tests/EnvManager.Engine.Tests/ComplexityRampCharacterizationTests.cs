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
        Program.SetAppDataDirectoryForTests(_dir);
        Program.SetProfilesFilePathForTests(Path.Combine(_dir, "profiles.json"));
        Program.SetAuditFilePathForTests(Path.Combine(_dir, "audit.json"));
        Program.SetAuditKeyPathForTests(Path.Combine(_dir, "audit.key"));
    }

    public void Dispose()
    {
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

    static List<AuditEntry> LoadHistory()
    {
        var mi = typeof(Program).GetMethod("LoadAuditHistory",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("missing LoadAuditHistory");
        return (List<AuditEntry>)mi.Invoke(null, null)!;
    }

    static void RecordProfileAudit(string command, string profileName)
    {
        var mi = typeof(Program).GetMethod("RecordProfileAudit",
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException("missing RecordProfileAudit");
        mi.Invoke(null, new object?[] { command, profileName, null, null });
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

    static void ReleaseConsole()
    {
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());
    }

    // ---- history undo: the profile branch (TryUndoProfileAudit) ------------------

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
        finally { ReleaseConsole(); }
        Assert.Equal(0, exit);
        Assert.Contains("\"undone\"", cap.Stdout.ToString());
        Assert.Contains("em_ramp_a", cap.Stdout.ToString());
        Assert.DoesNotContain(Program.LoadProfiles(), p => p.Name == "em_ramp_a");
    }

    [Fact]
    public void Undo_ProfileDelete_RestoresProfileFromCapturedState()
    {
        Assert.Equal(0, Program.RunProfileCommand(new[] { "profile", "create", "em_ramp_b" }));
        Assert.Equal(0, Program.RunProfileCommand(new[] { "profile", "delete", "em_ramp_b" }));
        var history = LoadHistory();
        Assert.Equal(2, history.Count);
        AuditEntry? deleteEntry = null;
        foreach (var e in history) if (e.Command == "profile delete") deleteEntry = e;
        Assert.NotNull(deleteEntry);
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", deleteEntry!.Id); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, exit);
        Assert.Contains(Program.LoadProfiles(), p => p.Name == "em_ramp_b");
    }

    [Fact]
    public void Undo_NonUndoableProfileCommand_RefusesWithClearError()
    {
        RecordProfileAudit("profile add-secret", "em_ramp_c");
        var history = LoadHistory();
        Assert.Single(history);
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", history[0].Id); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("has no undo path", cap.Stderr.ToString());
        Assert.Contains("cannot be undone", cap.Stderr.ToString());
    }

    [Fact]
    public void Undo_UnknownEntry_ExitsOneWithNotFoundCopy()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunHistoryCommand", "history", "undo", "no-such-entry"); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("Error: Audit entry not found", cap.Stderr.ToString());
    }

    // ---- protection dispatcher ----------------------------------------------------

    [Fact]
    public void Protection_List_FreshStore_EmitsBuiltInsAndEmptyCustom()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "list"); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(cap.Stdout.ToString());
        var vars = doc.RootElement.GetProperty("protectedVars");
        Assert.Equal(0, vars.GetProperty("custom").GetArrayLength());
        Assert.Contains(vars.GetProperty("builtIn").EnumerateArray(), v => v.GetString() == "windir");
        var paths = doc.RootElement.GetProperty("protectedPaths");
        Assert.Equal(0, paths.GetProperty("custom").GetArrayLength());
    }

    [Fact]
    public void Protection_AddVar_ThenRemoveVar_RoundTripsCustomStore()
    {
        var cap = CaptureConsole();
        int add;
        try { add = Invoke("RunProtectionCommand", "protection", "add-var", "EM_RAMP_X"); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, add);
        Assert.Contains("Added protected variable: EM_RAMP_X", cap.Stdout.ToString());
        Assert.True(File.Exists(Path.Combine(_dir, "protected-vars.json")));

        cap = CaptureConsole();
        int remove;
        try { remove = Invoke("RunProtectionCommand", "protection", "remove-var", "EM_RAMP_X"); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, remove);
        Assert.Contains("Removed protected variable: EM_RAMP_X", cap.Stdout.ToString());
    }

    [Fact]
    public void Protection_AddPath_ThenRemovePath_RoundTripsCustomStore()
    {
        var cap = CaptureConsole();
        int add;
        try { add = Invoke("RunProtectionCommand", "protection", "add-path", "C:\\em-ramp\\dir"); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, add);
        Assert.Contains("Added protected PATH entry: C:\\em-ramp\\dir", cap.Stdout.ToString());

        cap = CaptureConsole();
        int remove;
        try { remove = Invoke("RunProtectionCommand", "protection", "remove-path", "C:\\em-ramp\\dir"); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, remove);
        Assert.Contains("Removed protected PATH entry: C:\\em-ramp\\dir", cap.Stdout.ToString());
    }

    [Fact]
    public void Protection_RemoveVar_BuiltIn_Rejected()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "remove-var", "windir"); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("Cannot remove built-in protected variable", cap.Stderr.ToString());
    }

    [Fact]
    public void Protection_AddPath_InvalidEntry_Rejected()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "add-path", "C:\\a;b"); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("Invalid PATH entry", cap.Stderr.ToString());
    }

    [Fact]
    public void Protection_UnknownSub_UsageError()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunProtectionCommand", "protection", "bogus"); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("Usage: env-manager protection", cap.Stderr.ToString());
    }

    // ---- audit dispatcher ----------------------------------------------------------

    [Fact]
    public void Audit_NoSubcommand_UsageError()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit"); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("Usage: env-manager audit", cap.Stderr.ToString());
    }

    [Fact]
    public void Audit_UnknownSubcommand_Rejected()
    {
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "bogus"); }
        finally { ReleaseConsole(); }
        Assert.Equal(1, exit);
        Assert.Contains("Unknown audit subcommand: bogus", cap.Stderr.ToString());
    }

    [Fact]
    public void Audit_Verify_NoLedger_ReportsZeroEvents()
    {
        string ledger = Path.Combine(_dir, "absent-ledger.jsonl");
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "verify", "--ledger", ledger); }
        finally { ReleaseConsole(); }
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(cap.Stdout.ToString());
        Assert.True(doc.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("events").GetInt32());
    }

    [Fact]
    public void Audit_Verify_BrokenLedger_ReportsBreakageNonStrict()
    {
        string ledger = Path.Combine(_dir, "broken-ledger.jsonl");
        File.WriteAllText(ledger, "not-json\n");
        var cap = CaptureConsole();
        int exit;
        try { exit = Invoke("RunAuditCommand", "audit", "verify", "--ledger", ledger); }
        finally { ReleaseConsole(); }
        // Non-strict verify reports the breakage but still exits zero.
        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(cap.Stdout.ToString());
        Assert.False(doc.RootElement.GetProperty("verified").GetBoolean());
        Assert.Equal(0, doc.RootElement.GetProperty("events").GetInt32());
        Assert.Equal(1, doc.RootElement.GetProperty("breakages").GetArrayLength());
    }
}
