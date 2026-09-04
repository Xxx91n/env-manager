using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// Audit command domain (architecture-recovery issue 05): the `audit` subcommand (list, encrypt-file, ledger operation routing), moved verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
    static int RunAuditCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: env-manager audit <subcommand> [options]");
            Console.Error.WriteLine("Subcommands:");
            Console.Error.WriteLine("  encrypt-file --input <file> --output <file>  DPAPI-encrypt a file");
            Console.Error.WriteLine("  list [--mount <id>]                        List audit ledger events");
            Console.Error.WriteLine("  migrate-audit [--dry-run]                 Migrate audit.json to hash-chained ledger");
            Console.Error.WriteLine("  verify-ledger                              Verify audit ledger hash chain");
            Console.Error.WriteLine("  export-survival-kit [--mount <id>] [--output <file>]  Export DPAPI-encrypted survival kit");
            Console.Error.WriteLine("  recover-from-ledger                        Recover mounts from ledger replay");
            return 1;
        }

        string sub = args[1].ToLowerInvariant();

        switch (sub)
        {
            case "encrypt-file":
                {
                    string? inputPath = null;
                    string? outputPath = null;
                    for (int i = 2; i < args.Length - 1; i++)
                    {
                        if (args[i] == "--input" && i + 1 < args.Length) inputPath = args[++i];
                        else if (args[i] == "--output" && i + 1 < args.Length) outputPath = args[++i];
                    }
                    if (inputPath == null || outputPath == null)
                    {
                        Console.Error.WriteLine("Error: --input and --output required");
                        return 1;
                    }
                   if (!File.Exists(inputPath))
                   {
                       Console.Error.WriteLine("Error: input file not found: " + inputPath);
                       return 1;
                   }
                    // Path validation: reject system directories and enforce 50MB cap (same as backup validation)
                    string inputFull = Path.GetFullPath(inputPath);
                    string outputFull = Path.GetFullPath(outputPath);
                    string root = Path.GetPathRoot(outputFull) ?? "";
                    string[] blockedDirs = { "Windows", "Program Files", "Program Files (x86)" };
                    foreach (var blocked in blockedDirs)
                    {
                        string blockedPath = Path.Combine(root, blocked);
                        if (outputFull.StartsWith(blockedPath, StringComparison.OrdinalIgnoreCase))
                        {
                            Console.Error.WriteLine("Error: cannot write to system directory: " + blockedPath);
                            return 1;
                        }
                    }
                    var inputInfo = new FileInfo(inputFull);
                    if (inputInfo.Length > 50 * 1024 * 1024)
                    {
                        Console.Error.WriteLine("Error: input file exceeds 50 MB limit");
                        return 1;
                    }
                   using var plainSecret = new SecretString(File.ReadAllText(inputPath));
                   string cipherBase64 = SecretProviderManager.Encrypt(plainSecret.ToString(), "audit-survival-kit");
                   WriteAtomicUtf8(outputPath, cipherBase64);
                    Console.WriteLine("Encrypted: " + outputPath + " (" + cipherBase64.Length + " chars)");
                    return 0;
                }

            case "list":
                {
                    // List audit.json entries (existing audit system).
                    // The ledger migration from audit.json to audit-ledger.jsonl
                    // happens in v1.0.0; for now, list the existing audit entries.
                    string auditPath = Path.Combine(
                        LocalAppDataRoot,
                        "EnvManager", "audit.json");
                    if (!File.Exists(auditPath))
                    {
                        Console.WriteLine("[]");
                        return 0;
                    }
                    if (args.Contains("--json"))
                    {
                        Console.WriteLine(File.ReadAllText(auditPath));
                    }
                    else
                    {
                        var entries = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(auditPath)) ?? new();
                        foreach (var e in entries)
                        {
                            Console.WriteLine($"{e.GetProperty("timestamp")} | {e.GetProperty("command")} | {(e.TryGetProperty("scope", out var s) ? s.GetString() : "")}");
                        }
                    }
                    return 0;
                }

            case "migrate-audit":
                return RunAuditMigrate(args);
            case "verify-ledger":
                return RunAuditVerifyLedger();
            case "export-survival-kit":
                return RunAuditExportSurvivalKit(args);
            case "recover-from-ledger":
                return RunAuditRecoverFromLedger();
            default:
                Console.Error.WriteLine("Unknown audit subcommand: " + sub);
                return 1;
        }
    }

    // --- audit/history storage + `history` command (architecture-recovery issue 06, moved verbatim from EnvFeatures.cs) ---

    const int MaxAuditEntries = 2000;

    static string? _auditFilePathForTests;

    internal static void SetAuditFilePathForTests(string? path) => _auditFilePathForTests = path;

    static string AuditFilePath => _auditFilePathForTests ?? Path.Combine(AppDataDirectory, "audit.json");

    static List<AuditEntry> LoadAuditHistory()
    {
        if (!File.Exists(AuditFilePath)) return new();
        var info = new FileInfo(AuditFilePath);
        if (info.Length > MaxBackupFileSize) throw new InvalidDataException("Audit history exceeds the safety limit");
        // v0.9.13 Phase 3B: decrypt AES-GCM encrypted audit content at rest
        string rawContent = File.ReadAllText(AuditFilePath);
        string plainJson = DecryptAuditContent(rawContent);
        return JsonSerializer.Deserialize<List<AuditEntry>>(plainJson, JsonOpts) ?? new();
    }

    static int RunHistoryCommand(string[] args)
   {
       string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";
       if (sub == "list")
       {
           int limit = 200;
           int flag = Array.IndexOf(args, "--limit");
           if (flag >= 0 && flag + 1 < args.Length && int.TryParse(args[flag + 1], out int parsed)) limit = Math.Clamp(parsed, 1, 1000);
           Console.WriteLine(JsonSerializer.Serialize(LoadAuditHistory().TakeLast(limit).Reverse(), JsonOptsIndented));
           return 0;
       }
       if (sub == "undo" && args.Length > 2)
       {
           var entry = LoadAuditHistory().FirstOrDefault(e => e.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase));
           if (entry == null) return ArgError("Error: Audit entry not found");

           // Profile-level audit entries use Scope="profile" and are reverted
           // via TryUndoProfileAudit (restoring the profiles.json state). They
           // do not touch the registry, so no stale-value check applies.
           if (entry.Scope == "profile")
           {
               bool handled = TryUndoProfileAudit(entry);
               if (!handled) return ArgError("Error: This profile change cannot be undone");
               Console.WriteLine(JsonSerializer.Serialize(new { undone = entry.Id, entry.Name, entry.Scope }, JsonOpts));
               return 0;
           }

           // Registry-level entries: only undo if the live value still matches
           // the recorded newValue. Otherwise force the user to --force.
           string? current = GetVariableValue(entry.Name, entry.Scope);
           if (current != entry.NewValue && !args.Contains("--force"))
               return ArgError("Error: Variable changed since this audit entry; use --force to override");
           if (entry.OldValue == null) DeleteVariableWithoutNotify(entry.Name, entry.Scope);
           else SetVariableWithoutNotify(entry.Name, entry.OldValue, entry.Scope);
           BroadcastSettingChange();
           Console.WriteLine(JsonSerializer.Serialize(new { undone = entry.Id, entry.Name, entry.Scope }, JsonOpts));
           return 0;
       }
       if (sub == "delete")
       {
           if (args.Length < 3) return ArgError("Usage: env-manager history delete <id> | history delete --all [--scope user|system]");
           var history = LoadAuditHistory();
           if (args[2] == "--all")
           {
               string? scope = ParseScope(args, 3, "all");
               if (scope == null) return 1;
               if (scope == "all") history.Clear();
               else history.RemoveAll(e => e.Scope == scope);
               AtomicWriteJson(AuditFilePath, history);
               Console.WriteLine(JsonSerializer.Serialize(new { deleted = "all", scope }, JsonOpts));
               return 0;
           }
           var entry = history.FirstOrDefault(e => e.Id.Equals(args[2], StringComparison.OrdinalIgnoreCase));
           if (entry == null) return ArgError("Error: Audit entry not found");
           history.Remove(entry);
           AtomicWriteJson(AuditFilePath, history);
           Console.WriteLine(JsonSerializer.Serialize(new { deleted = entry.Id }, JsonOpts));
           return 0;
       }
       return ArgError("Usage: env-manager history list [--limit N] | history undo <id> [--force]\nNote: profile-scoped audit entries are undone via profile-state revert; --force has no effect. | history delete <id> | history delete --all [--scope user|system]");
    }


    static void RecordSnapshotDiff(string command, Dictionary<string, string?> before, Dictionary<string, string?> after)
    {
        var keys = before.Keys.Concat(after.Keys).Distinct(StringComparer.OrdinalIgnoreCase);
        var changes = new List<AuditEntry>();
        foreach (string key in keys)
        {
            before.TryGetValue(key, out string? oldValue);
            after.TryGetValue(key, out string? newValue);
            if (oldValue == newValue) continue;
            string[] parts = key.Split('\0', 2);
            changes.Add(new AuditEntry { Command = command, Scope = parts[0], Name = parts[1], OldValue = oldValue, NewValue = newValue });
        }
        if (changes.Count == 0) return;
        var history = LoadAuditHistory();
        history.AddRange(changes);
        if (history.Count > MaxAuditEntries) history = history[^MaxAuditEntries..];
        AtomicWriteJson(AuditFilePath, history);
    }

}

class AuditEntry
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    [JsonPropertyName("command")] public string Command { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
    [JsonPropertyName("oldValue")] public string? OldValue { get; set; }
    [JsonPropertyName("newValue")] public string? NewValue { get; set; }

}
