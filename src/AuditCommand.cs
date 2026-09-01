using Microsoft.Win32;
using System.Linq;
using System.Text.Json;

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
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
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
}
