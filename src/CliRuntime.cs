using Microsoft.Win32;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// Shared CLI runtime infrastructure for the partial Program dispatch surface
/// (architecture-recovery issue 21): members moved verbatim from Program.cs, which stays
/// the thin Main dispatcher. Type name unchanged so all call sites compile without
/// modification.
/// </summary>
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
    internal static string BuildHelpText()
    {
        var asm = System.Reflection.Assembly.GetExecutingAssembly().GetName();
        var ver = asm.Version ?? new Version(0, 0, 0);
        return $@"Env Manager v{ver.Major}.{ver.Minor}.{ver.Build}

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
  --debug                    Enable verbose stderr logging";
    }

    static int ShowHelp()
    {
        Console.WriteLine(BuildHelpText());
        return 0;
    }

    // v0.9.13 Phase 4F: Provider binary hash verification (best-effort)
    // Computes SHA256 of sops/op binary on first use, warns on subsequent mismatch.
    private static readonly string ProviderHashPath = Path.Combine(
        LocalAppDataRoot, "EnvManager", "provider-hash.json");

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

    // --- shared runtime infrastructure (architecture-recovery issue 06, moved verbatim from EnvFeatures.cs) ---

    // CI user-state isolation (architecture-recovery issue 24): when
    // ENVMANAGER_LOCALAPPDATA is set, it replaces the per-user LocalApplicationData
    // root for ALL user-state file resolution (profiles.json, audit, secret mounts,
    // provider config/hash, protection stores). GetFolderPath does not honor a
    // process-level LOCALAPPDATA override (shell folders expand from the registry),
    // so CI integration tests redirect user-state writes through this variable
    // instead. Unset in production: GetFolderPath applies unchanged.
    internal static string LocalAppDataRoot
    {
        get
        {
            string? redirect = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
            return string.IsNullOrEmpty(redirect)
                ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
                : redirect;
        }
    }

    static string AppDataDirectory
    {
        get
        {
            if (_appDataDirectoryForTests != null) return _appDataDirectoryForTests;
            string path = Path.Combine(LocalAppDataRoot, "EnvManager");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    // Ticket 18 test seam: when non-null, AppDataDirectory returns this path so the
    // xUnit lane can redirect the protection JSON stores (protected-vars.json,
    // builtin-protected-vars.json) to a temp dir. Production never sets it.
    static string? _appDataDirectoryForTests;

    internal static void SetAppDataDirectoryForTests(string? path)
    {
        _appDataDirectoryForTests = path;
    }

    static bool IsWriteInvocation(string[] args)
    {
        if (args.Length == 0) return false;
        string command = args[0].ToLowerInvariant();
        if (command is "list" or "get" or "backup" or "diff" or "validate" or "agents" or "help" or "expand") return false;
        if (command == "history") return args.Length > 1 && args[1].Equals("undo", StringComparison.OrdinalIgnoreCase);
        if (command == "bulk") return args.Length > 1 && args[1].Equals("import", StringComparison.OrdinalIgnoreCase) && !args.Contains("--dry-run");
        if (command == "profile") return args.Length < 2 || !new[] { "list", "show", "status", "preview", "export", "help" }.Contains(args[1], StringComparer.OrdinalIgnoreCase);
        if (command == "path") return args.Length < 2 || !new[] { "list", "help" }.Contains(args[1], StringComparer.OrdinalIgnoreCase);
        return true;
    }

    // Issue 25 fuzz seam: argv arrays arriving from the fuzzer are attacker-shaped;
    // null elements must be treated as malformed input (return false), not a crash.
    internal static bool IsWriteInvocationForFuzz(params string?[]? args)
    {
        if (args == null || args.Any(a => a == null)) return false;
        return IsWriteInvocation(args!);
    }

    static Mutex? AcquireMutationLock(string[] args)
    {
        if (!IsWriteInvocation(args)) return null;
        var mutex = new Mutex(false, "Local\\EnvManager.RegistryMutation");
        try
        {
            if (!mutex.WaitOne(TimeSpan.FromSeconds(30)))
            {
                mutex.Dispose();
                throw new TimeoutException("Another Env Manager write operation is still running");
            }
        }
        catch (AbandonedMutexException)
        {
            DebugLog("Recovered abandoned mutation lock");
        }
        return mutex;
    }

    static Dictionary<string, string?> CaptureEnvironmentSnapshot()
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        CaptureScope(Registry.CurrentUser, UserEnvPath, "user", snapshot);
        CaptureScope(Registry.LocalMachine, SystemEnvPath, "system", snapshot);
        return snapshot;
    }

    static void CaptureScope(RegistryKey hive, string path, string scope, Dictionary<string, string?> target)
    {
        using var key = hive?.OpenSubKey(path, false);
        if (key == null) return;
        foreach (string name in key.GetValueNames())
        {
            if (name.Contains("_PowerToys_", StringComparison.OrdinalIgnoreCase)) continue;
            target[scope + "\0" + name] = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString();
        }
    }

static void AtomicWriteJson<T>(string path, T value)
{
    string temp = path + ".tmp." + Environment.ProcessId;
    // v0.8.0 A3: fsync before rename to match Rust write_atomic.
    using (var fs = File.Create(temp))
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptsIndented);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true); // fsync
    }
    File.Move(temp, path, true);
}

    /// <summary>
    /// Atomically write a UTF-8 string to a file: temp + fsync + rename.
    /// Same pattern as AtomicWriteJson but accepts pre-serialized text.
    /// </summary>
    static void WriteAtomicUtf8(string path, string content)
    {
        string temp = path + ".tmp." + Environment.ProcessId;
        using (var fs = File.Create(temp))
        {
            byte[] bytes = new UTF8Encoding(false).GetBytes(content);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true); // fsync
        }
        File.Move(temp, path, true);
    }


}
