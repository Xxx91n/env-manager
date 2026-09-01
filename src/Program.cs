using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

partial class Program
{
    const string SystemEnvPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    const string UserEnvPath = "Environment";
    const int MaxLength = 32767;
    const long MaxBackupFileSize = 50 * 1024 * 1024; // 50 MB safety cap

    static List<string> CustomProtectedPathEntries
    {
        get
        {
            try
            {
                string file = Path.Combine(AppDataDirectory, "protected-paths.json");
                if (!File.Exists(file)) return new();
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file), JsonOpts) ?? new();
            }
            catch (Exception ex) { DebugLog("Warning: corrupt protection config, using defaults: " + ex.GetType().Name); return new(); }
        }
    }

    static void SaveCustomProtectedPathEntries(List<string> entries)
    {
        string file = Path.Combine(AppDataDirectory, "protected-paths.json");
        AtomicWriteJson(file, entries.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    static bool IsProtectedPathEntry(string entry)
    {
        string normalized = entry.TrimEnd('\\', '/').Trim();
        if (ProtectedPathEntries.Any(p => p.TrimEnd('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase)))
            return true;
        foreach (var custom in CustomProtectedPathEntries)
        {
            if (custom.TrimEnd('\\', '/').Equals(normalized, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    static bool DebugMode = false;

    static void DebugLog(string msg)
    {
        if (DebugMode)
            Console.Error.WriteLine($"[debug] {DateTime.Now:HH:mm:ss.fff} {msg}");
    }

    static readonly HashSet<string> ValidCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "get", "set", "rename", "change-scope", "delete", "toggle", "backup", "restore", "diff", "merge",
        "validate", "help", "profile", "path", "agents", "history", "bulk", "expand", "protection", "update", "service", "audit", "export-state", "import-state"
    };

    static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    static readonly JsonSerializerOptions JsonOptsIndented = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    static int Main(string[] args)
    {
        // v0.9.13 Phase 2D/4A: Disable WER crash dialogs + SEM_NOGPFAULTERRORBOX (best-effort)
        DisableCrashDialogs();

        // Route seam debug output (RegistryScope write/toggle failure logs) into the CLI
        // --debug channel; before the seam migration these lines were DebugLog calls inside
        // SetVariable/Toggle. No output change without --debug.
        RegistryScope.DebugSink = DebugLog;

        // v0.7.1 fix: recover from the classic Windows "trailing backslash + quote"
        // tokenizer hazard, where values like "C:\Program Files\PowerShell\7\"
        // merge with following --scope/--overwrite flags. Main(args) follows
        // CommandLineToArgvW, treating an odd backslash count before a quote as
        // an escaped literal quote. We re-tokenize Environment.CommandLine with
        // a lenient rule (quote is always a terminator, backslashes are literal)
        // so trailing-backslash PATH values survive.
        if (LenientArgs.WasArgsCorruptedByTrailingBackslashQuote(args))
        {
            string[] recovered = LenientArgs.Tokenize();
            if (recovered.Length == 0)
            {
                Console.Error.WriteLine("Error: Command-line recovery failed");
                return 1;
            }

            // The runtime argv is already known to be corrupted. Prefer the
            // deterministic recovery even when it has fewer elements: the
            // malformed runtime split can introduce spurious empty fragments.
            args = recovered;
        }

        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        // Check for --debug flag anywhere in args
        var argList = args.ToList();
        if (argList.Remove("--debug") || argList.Remove("-d"))
        {
            DebugMode = true;
        }
        args = argList.ToArray();

        DebugLog($"Command: {args.FirstOrDefault() ?? "none"}; argumentCount={Math.Max(0, args.Length - 1)}");

        string command = args[0];
        if (!ValidCommands.Contains(command))
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            ShowHelp();
            return 1;
        }

        Mutex? mutationLock = null;
        Dictionary<string, string?>? beforeSnapshot = null;
        try
        {
            mutationLock = AcquireMutationLock(args);
            if (mutationLock != null) beforeSnapshot = CaptureEnvironmentSnapshot();

            int exitCode = command.ToLowerInvariant() switch
            {
                "list" => ListEnvironment(),
                "get" => args.Length < 2 ? ArgError("Usage: env-manager get <name>") : GetVariable(args[1]),
                "set" => args.Length < 3 ? ArgError("Usage: env-manager set <name> <value> [--scope user|system] [--overwrite]") : RunSet(args),
                "rename" => args.Length < 3 ? ArgError("Usage: env-manager rename <old> <new> [--scope user|system] [--overwrite]") : RunRename(args),
                "change-scope" => args.Length < 3 ? ArgError("Usage: env-manager change-scope <name> <new-scope> [--scope user|system] [--overwrite]") : RunChangeScope(args),
                "delete" => args.Length < 2 ? ArgError("Usage: env-manager delete <name> [--scope user|system]") : RunDelete(args),
                "toggle" => args.Length < 2 ? ArgError("Usage: env-manager toggle <name> [--scope user|system]") : RunToggle(args),
                "backup" => RunBackup(args),
                "restore" => args.Length < 2 ? ArgError("Usage: env-manager restore <file> [--scope user|system]") : RunRestore(args),
                "diff" => args.Length < 3 ? ArgError("Usage: env-manager diff <old> <new>") : DiffBackups(args[1], args[2]),
                "merge" => args.Length < 5 || args[3] != "--output" ? ArgError("Usage: env-manager merge <old> <new> --output <file>") : MergeBackups(args[1], args[2], args[4]),
                "validate" => args.Length < 2 ? ArgError("Usage: env-manager validate <file>") : ValidateBackup(args[1]),
                "profile" => RunProfileCommand(args),
                "path" => RunPathCommand(args),
                "agents" => RunAgents(args),
                "history" => RunHistoryCommand(args),
                "bulk" => RunBulkCommand(args),
                "expand" => args.Length < 2 ? ArgError("Usage: env-manager expand <value>") : RunExpand(args[1]),
                "protection" => RunProtectionCommand(args),
                "update" => RunUpdate(args),
                "service" => RunServiceCommand(args),
                "audit" => RunAuditCommand(args),
                "export-state" => RunExportState(args),
                "import-state" => RunImportState(args),
                "help" => ShowHelp(),
                _ => 1
            };

            if (exitCode == 0 && beforeSnapshot != null)
            {
                try
                {
                    RecordSnapshotDiff(args[0] + (args.Length > 1 && args[0] is "profile" or "path" or "history" or "bulk" ? " " + args[1] : ""), beforeSnapshot, CaptureEnvironmentSnapshot());
                }
                catch (Exception auditError)
                {
                    DebugLog("Audit recording failed: " + auditError.GetType().Name);
                }
            }
            return exitCode;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Error: Access denied (requires elevation)");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ScrubExceptionMessage(ex.Message)}");
            return 1;
        }
        finally
        {
            if (mutationLock != null)
            {
                mutationLock.ReleaseMutex();
                mutationLock.Dispose();
            }
        }
    }

    /// <summary>
    /// v0.9.12: Scrubs potentially sensitive data from exception messages before
    /// writing to stderr or logs. Masks common secret-bearing patterns so provider
    /// error messages are traceable without leaking credentials. Bounded + best-effort.
    /// </summary>
    internal static string ScrubExceptionMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return msg;
        var result = msg.Length > 512 ? msg[..512] : msg;
        string[] patterns = {
            "Bearer ", "token=", "Token=", "password=", "Password=",
            "setx ", "OP_SERVICE_ACCOUNT_TOKEN=", "VAULT_TOKEN=",
            "AWS_SECRET_ACCESS_KEY=", "AWS_SESSION_TOKEN=",
            "client_secret", "connection_string", "subscription_key",
            "api_key", "apikey", "client_id=", "tenant_id=",
            "access_token", "refresh_token", "Authorization:",
            "X-Vault-Token", "x-api-key"
        };
        foreach (var pat in patterns)
        {
            int searchFrom = 0;
            while (searchFrom < result.Length)
            {
                int i = result.IndexOf(pat, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (i < 0) break;
                int start = i + pat.Length;
                int tailLen = Math.Min(8, result.Length - start);
                if (tailLen > 0)
                {
                    result = result[..start] + "<redacted>" + result[(start + tailLen)..];
                    searchFrom = start + "<redacted>".Length;
                }
                else
                {
                    searchFrom = start;
                }
            }
        }
        return result;
    }

    /// <summary>
    /// v0.9.12: SecretString wraps a decrypted secret value to prevent accidental
    /// logging or serialization. Zeroes the underlying char[] on Dispose.
    /// Ponytail: minimal struct, no interface, no factory. The caller is responsible
    /// for using the value before Dispose; we do not add lifetime tracking.
    /// </summary>
    public ref struct SecretString
    {
        private char[] _buffer;
        private bool _disposed;
        public SecretString(string value) { _buffer = value.ToCharArray(); _disposed = false; }
        public ReadOnlySpan<char> AsSpan() => _disposed ? ReadOnlySpan<char>.Empty : _buffer.AsSpan();
        public override string ToString() => _disposed ? "<redacted>" : new string(_buffer);
        public void Dispose() { if (!_disposed && _buffer != null) Array.Clear(_buffer); _disposed = true; }
    }

    static int ArgError(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }

    // v0.9.0 Phase B+C: CLI service subcommand - thin IPC gateway to env-manager-service.exe.
    // Sends a JSON request to the named pipe and prints the JSON response.
    // See docs/adr/0001-secret-architecture-revision.md decision A7.
    /// <summary>
    /// v1.0.0 Phase E: Audit ledger commands.
    /// "audit encrypt-file --input <path> --output <path>": DPAPI-encrypts a file
    /// (used by the service's export_survival_kit to create machine+user-bound archives).
    /// "audit list": lists audit ledger events (read-only).
    /// </summary>
    static int ShowHelp()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName();
        var ver = asm.Version ?? new Version(0, 0, 0);
        Console.WriteLine($@"Env Manager v{ver.Major}.{ver.Minor}.{ver.Build}

Commands:
  list                       List all variables (JSON)
  get <name>                 Get variable (JSON)
  set <name> <val> [--scope user|system] [--overwrite] Set variable
  rename <old> <new> [--scope user|system] [--overwrite] Rename variable atomically
  change-scope <name> <new-scope> [--scope user|system] [--overwrite] Move variable to another scope atomically
  delete <name> [--scope user|system]    Delete variable
  toggle <name> [--scope user|system]    Enable/disable a variable (backs up value)
  backup [--output <file>]   Create backup
  restore <file> [--scope user|system]   Restore backup
  diff <old> <new>           Compare backups (JSON)
  merge <old> <new> --output <file>      Merge backups
  validate <file>            Validate backup
  profile <subcommand>       Manage variable profiles (see: profile help)
  path <subcommand>          Edit PATH variable as list (see: path help)
  history list|undo           View or undo audited changes
  bulk import|export          Import/export .json, .env, or .csv
  expand <value>              Expand nested %VARIABLE% references
  protection list|add-path|remove-path|add-var|remove-var  View or manage protected variables and PATH entries
  agents [--path|--json|--summary] Output CLI spec. --path: file only. --json: machine-readable. --summary: brief
  help                       Show help
  update check                Check for latest version
  service status|health|refresh <id>|rotate <id>  Interact with the secret lifecycle service
  --debug                    Enable verbose stderr logging");
       return 0;
    }

    // v0.9.13 Phase 4F: Provider binary hash verification (best-effort)
    // Computes SHA256 of sops/op binary on first use, warns on subsequent mismatch.
    private static readonly string ProviderHashPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "EnvManager", "provider-hash.json");

    internal static void RecordProviderHash(string binaryName, string binaryPath)
    {
        try
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            var hashBytes = sha.ComputeHash(File.ReadAllBytes(binaryPath));
            var hashHex = Convert.ToHexString(hashBytes);

            var existing = File.Exists(ProviderHashPath)
                ? File.ReadAllText(ProviderHashPath)
                : "{}";
            using var doc = JsonDocument.Parse(existing);
            using var stream = new MemoryStream();
            using var writer = new System.Text.Json.Utf8JsonWriter(stream);
            writer.WriteStartObject();
            foreach (var prop in doc.RootElement.EnumerateObject())
            {
                writer.WritePropertyName(prop.Name);
                writer.WriteStringValue(prop.Name == binaryName ? hashHex : prop.Value.GetString());
                if (prop.Name == binaryName && prop.Value.GetString() != hashHex)
                    DebugLog($"Provider {binaryName} hash mismatch (expected={prop.Value.GetString()[..16]}, actual={hashHex[..16]})");
            }
            if (!doc.RootElement.EnumerateObject().Any(p => p.Name == binaryName))
            {
                writer.WritePropertyName(binaryName);
                writer.WriteStringValue(hashHex);
                DebugLog($"Provider {binaryName} hash recorded: {hashHex[..16]}");
            }
            writer.WriteEndObject();
            writer.Flush();
            var jsonText = System.Text.Encoding.UTF8.GetString(stream.ToArray());
            WriteAtomicUtf8(ProviderHashPath, jsonText);
        }
        catch (Exception ex) { DebugLog($"Provider hash recording failed: {ex.GetType().Name}"); }
    }
}
