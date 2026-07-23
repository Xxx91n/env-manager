using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

class EnvVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    [JsonPropertyName("isDisabled")] public bool IsDisabled { get; set; } = false;
    [JsonPropertyName("profileSource")] public string? ProfileSource { get; set; }
    [JsonPropertyName("isProtected")] public bool IsProtected { get; set; } = false;
    [JsonPropertyName("isBuiltinProtected")] public bool IsBuiltinProtected { get; set; } = false;
}

class BackupData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("variables")] public List<EnvVariable> Variables { get; set; } = new();
}

class ProfileVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
}

class ProfileData
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; } = false;
    [JsonPropertyName("appliedAt")] public long? AppliedAt { get; set; }
    [JsonPropertyName("inherits")] public List<string> Inherits { get; set; } = new();
    [JsonPropertyName("pathEntries")] public List<string> PathEntries { get; set; } = new();
    [JsonPropertyName("variables")] public List<ProfileVariable> Variables { get; set; } = new();

    // Launch profile type: "global" (default, applies to user registry) or "launch" (launcher template, never writes registry).
    // v0.7.0 introduces per-app launch profiles and DPAPI-encrypted secrets to avoid polluting the global user environment.
    [JsonPropertyName("profileType")] public string ProfileType { get; set; } = "global";
    [JsonPropertyName("targetExecutable")] public string? TargetExecutable { get; set; }
    [JsonPropertyName("launchArguments")] public string? LaunchArguments { get; set; }
    [JsonPropertyName("workingDirectory")] public string? WorkingDirectory { get; set; }
    // Secret variable names in a launch profile are DPAPI-encrypted on disk; plaintext lives only in process memory.
    [JsonPropertyName("secretVariables")] public List<string> SecretVariables { get; set; } = new();
}

partial class Program
{
    const string SystemEnvPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    const string UserEnvPath = "Environment";
    const int MaxLength = 32767;
    const long MaxBackupFileSize = 50 * 1024 * 1024; // 50 MB safety cap

    // Built-in protected system variables and PATH entries are loaded from external
    // JSON config files in %LOCALAPPDATA%\EnvManager (see EnvFeatures.cs).
    // They are seeded from embedded protection.defaults.json on first run and can
    // be edited without recompiling. The HashSet wrapping is for O(1) case-insensitive lookups.
    static HashSet<string> ProtectedSystemVars => new(
        LoadBuiltinProtectedVars(),
        StringComparer.OrdinalIgnoreCase);

    static HashSet<string> ProtectedPathEntries => new(
        LoadBuiltinProtectedPaths(),
        StringComparer.OrdinalIgnoreCase);

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
            catch { return new(); }
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

    // --- Custom protected variables (user-lockable) ---
    // Users can lock any variable via the GUI lock button or CLI 'protection add-var'.
    // Locked variables cannot be toggled, edited, or deleted.
    static List<string> CustomProtectedVars
    {
        get
        {
            try
            {
                string file = Path.Combine(AppDataDirectory, "protected-vars.json");
                if (!File.Exists(file)) return new();
                return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(file), JsonOpts) ?? new();
            }
            catch { return new(); }
        }
    }

    static void SaveCustomProtectedVars(List<string> vars)
    {
        string file = Path.Combine(AppDataDirectory, "protected-vars.json");
        AtomicWriteJson(file, vars.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
    }

    static bool IsCustomProtectedVar(string name)
    {
        return CustomProtectedVars.Any(v => v.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true if the variable is protected from system-scope modification.
    /// PATH is no longer wholesale-protected; individual PATH entries are checked
    /// via IsProtectedPathEntry when they are about to be removed.
    /// For user scope, these are NOT protected (user can modify their own PATH).
    /// </summary>
    static bool IsProtectedVariable(string name, string scope)
    {
        // Built-in system protection (system scope only)
        if (scope == "system" && ProtectedSystemVars.Contains(name))
            return true;
        // User-locked variables (any scope)
        if (IsCustomProtectedVar(name))
            return true;
        return false;
    }

    /// <summary>
    /// Returns true if a variable is protected by built-in rules (cannot be unlocked).
    /// </summary>
    static bool IsBuiltinProtectedVar(string name)
    {
        return ProtectedSystemVars.Contains(name);
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
        "validate", "help", "profile", "path", "agents", "history", "bulk", "expand", "protection", "update"
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
    /// Returns the path to the profiles JSON file in LocalAppData.
    /// Mirrors PowerToys' approach of storing profiles in a per-user app data folder.
    /// </summary>
    static string ProfilesFilePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EnvManager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "profiles.json");
        }
    }

    static int Main(string[] args)
    {
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
            Console.Error.WriteLine($"Error: {ex.Message}");
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

    static int ArgError(string msg)
    {
        Console.Error.WriteLine(msg);
        return 1;
    }

    /// <summary>
    /// Validates a backup file path. Rejects paths that are not .json or that
    /// point to system directories, preventing path traversal attacks.
    /// </summary>
    static string ValidateFilePath(string path, bool mustExist)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("File path cannot be empty");

        string fullPath = Path.GetFullPath(path);
        string ext = Path.GetExtension(fullPath);

        if (!ext.Equals(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Backup file must have .json extension");

        // Block writes to system directories
        string root = Path.GetPathRoot(fullPath) ?? "";
        string[] blockedDirs = { @"Windows", @"Program Files", @"Program Files (x86)" };
        foreach (var blocked in blockedDirs)
        {
            string blockedPath = Path.Combine(root, blocked);
            if (fullPath.StartsWith(blockedPath, StringComparison.OrdinalIgnoreCase))
                throw new UnauthorizedAccessException($"Cannot write to system directory: {blockedPath}");
        }

        if (mustExist && !File.Exists(fullPath))
            throw new FileNotFoundException("File not found", fullPath);

        if (mustExist)
        {
            var fi = new FileInfo(fullPath);
            if (fi.Length > MaxBackupFileSize)
                throw new ArgumentException($"Backup file exceeds maximum size of {MaxBackupFileSize / 1024 / 1024} MB");
        }

        return fullPath;
    }

    static int RunSet(string[] args)
    {
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        // SAFETY (hard boundary): reject protected-system-variable writes BEFORE
        // any value comparison -- otherwise the "already exists" overwrite
        // prompt can leak past the protection guard for non-interactive flows.
        if (IsInternalToggleBackupName(args[1]))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not writable");
            return 1;
        }
        if (IsProtectedVariable(args[1], scope))
        {
            Console.Error.WriteLine($"Error: Cannot modify protected system variable '{args[1]}'");
            return 1;
        }

        string? existing = GetVariableValue(args[1], scope);
        if (existing != null && existing != args[2] && !args.Contains("--overwrite"))
            return ArgError("Error: Variable already exists with a different value; use --overwrite");
        return SetVariable(args[1], args[2], scope) ? 0 : 1;
    }

    static int RunDelete(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;

        // SAFETY (hard boundary): reject protected-system-variable deletes BEFORE
        // delegating to DeleteVariable, so non-interactive callers see a clear
        // non-zero exit and a "protected" error message rather than silent success.
        if (IsInternalToggleBackupName(args[1]))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not deletable");
            return 1;
        }
        if (IsProtectedVariable(args[1], scope))
        {
            Console.Error.WriteLine($"Error: Cannot delete protected system variable '{args[1]}'");
            return 1;
        }
        DeleteVariable(args[1], scope);
        return 0;
    }
    static int RunBackup(string[] args)
    {
        string outputPath = "env_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
        if (args.Length > 1 && args[1] == "--output" && args.Length > 2)
            outputPath = args[2];

        outputPath = ValidateFilePath(outputPath, mustExist: false);
        CreateBackup(outputPath);
        return 0;
    }

    static int RunRestore(string[] args)
    {
        string? scope = ParseScopeOptional(args, 2);
        if (scope == null) return 1;
        RestoreBackup(args[1], scope);
        return 0;
    }

    /// <summary>
    /// Parses --scope flag from args starting at the given index.
    /// Returns null and prints an error if the scope value is invalid.
    /// </summary>
    static string? ParseScope(string[] args, int flagIndex, string? defaultValue)
    {
        if (args.Length > flagIndex && args[flagIndex] == "--scope" && args.Length > flagIndex + 1)
        {
            string s = args[flagIndex + 1];
            if (s != "user" && s != "system")
            {
                Console.Error.WriteLine("Error: scope must be 'user' or 'system'");
                return null;
            }
            return s;
        }
        return defaultValue;
    }

    /// <summary>
    /// Parses optional --scope flag. Returns null if not present (meaning "all scopes").
    /// </summary>
    static string? ParseScopeOptional(string[] args, int flagIndex)
    {
        if (args.Length > flagIndex && args[flagIndex] == "--scope" && args.Length > flagIndex + 1)
        {
            string s = args[flagIndex + 1];
            if (s != "user" && s != "system")
            {
                Console.Error.WriteLine("Error: scope must be 'user' or 'system'");
                return null;
            }
            return s;
        }
        return "all"; // default for restore: all scopes
    }

    static (RegistryKey? hive, string path) GetScopeTarget(string scope)
    {
        if (scope == "system")
            return (Registry.LocalMachine, SystemEnvPath);
        return (Registry.CurrentUser, UserEnvPath);
    }

    static void AppendEnvironmentItems(RegistryKey key, string scope, List<EnvVariable> items)
    {
        var allNames = key.GetValueNames();
        var nameSet = new HashSet<string>(allNames, StringComparer.OrdinalIgnoreCase);
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in allNames)
        {
            // PowerToys keeps separate internal backup values; Env Manager only
            // projects its own disabled backup when the original key is absent.
            if (name.Contains("_PowerToys_", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.EndsWith("_EnvManager_disabled", StringComparison.OrdinalIgnoreCase))
            {
                string originalName = name[..^"_EnvManager_disabled".Length];
                if (!nameSet.Contains(originalName) && processedNames.Add(originalName))
                {
                    items.Add(new EnvVariable
                    {
                        Name = originalName,
                        Value = key.GetValue(name)?.ToString() ?? "",
                        Scope = scope,
                        IsDisabled = true
                    });
                }
                continue;
            }

            if (processedNames.Add(name))
            {
                items.Add(new EnvVariable
                {
                    Name = name,
                    Value = key.GetValue(name)?.ToString() ?? "",
                    Scope = scope,
                    IsDisabled = false
                });
            }
        }
    }

    static int ListEnvironment()
    {
        DebugLog("ListEnvironment: reading user + system variables");
        var items = new List<EnvVariable>();

        using (var userKey = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (userKey != null)
                AppendEnvironmentItems(userKey, "user", items);
        }

        try
        {
            using (var systemKey = Registry.LocalMachine.OpenSubKey(SystemEnvPath))
            {
                if (systemKey != null)
                    AppendEnvironmentItems(systemKey, "system", items);
            }
        }
        catch (UnauthorizedAccessException) { }

        // Precompute profile values once. The previous item -> profile -> effective
        // variable loop recalculated profile state for every environment variable.
        try
        {
            var profileCandidates = new Dictionary<string, List<(string Source, string Value)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in LoadProfiles().Where(profile => profile.IsEnabled)
                         .OrderByDescending(profile => profile.AppliedAt ?? 0))
            {
                foreach (var variable in GetEffectiveProfileVariables(profile))
                {
                    if (!profileCandidates.TryGetValue(variable.Name, out var candidates))
                    {
                        candidates = new List<(string Source, string Value)>();
                        profileCandidates[variable.Name] = candidates;
                    }
                    candidates.Add((profile.Name, variable.Value));
                }
            }

            foreach (var item in items)
            {
                if (profileCandidates.TryGetValue(item.Name, out var candidates))
                {
                    var source = candidates.FirstOrDefault(candidate => candidate.Value == item.Value);
                    if (!string.IsNullOrEmpty(source.Source))
                        item.ProfileSource = source.Source;
                }
            }
        }
        catch (Exception error)
        {
            DebugLog("ListEnvironment: profile annotation failed: " + error.GetType().Name);
        }

        try
        {
            foreach (var item in items)
            {
                item.IsProtected = IsProtectedVariable(item.Name, item.Scope);
                item.IsBuiltinProtected = IsBuiltinProtectedVar(item.Name) && item.Scope == "system";
            }
        }
        catch (Exception error)
        {
            DebugLog("ListEnvironment: protection annotation failed: " + error.GetType().Name);
        }

        var ordered = items.OrderBy(item => item.Name).ThenBy(item => item.Scope).ToList();
        Console.WriteLine(JsonSerializer.Serialize(ordered, JsonOpts));
        return 0;
    }

    static int GetVariable(string name)
    {
        DebugLog("GetVariable");
        if (IsInternalToggleBackupName(name))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not addressable");
            return 1;
        }
        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (key != null)
            {
                // First check if variable is disabled (backup exists but original deleted)
                string backupName = GetToggleBackupName(name);
                var backupVal = key.GetValue(backupName);
                if (backupVal != null && key.GetValue(name) == null)
                {
                    // Variable is disabled: original deleted, backup exists
                    var result = new { name, value = backupVal.ToString(), scope = "user", isDisabled = true };
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                    return 0;
                }

                // Normal active variable
                var v = key.GetValue(name);
                if (v != null)
                {
                    var result = new { name, value = v.ToString(), scope = "user", isDisabled = false };
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                    return 0;
                }
            }
        }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(SystemEnvPath))
            {
                var v = key?.GetValue(name);
                if (v != null)
                {
                    var result = new { name, value = v.ToString(), scope = "system", isDisabled = false };
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                    return 0;
                }
            }
        }
        catch (UnauthorizedAccessException) { }

        Console.Error.WriteLine($"Not found: {name}");
        return 1;
    }

    static bool SetVariable(string name, string? value, string scope)
    {
        DebugLog("SetVariable scope=" + scope);
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Variable name cannot be empty");
            return false;
        }
        if (IsInternalToggleBackupName(name))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not writable");
            return false;
        }

        // PowerToys: user env var names limited to 255 chars in registry
        int maxNameLength = scope == "user" ? 255 : MaxLength;
        if (name.Length > maxNameLength)
        {
            Console.Error.WriteLine($"Error: Variable name exceeds {maxNameLength} characters");
            return false;
        }

        // Reject names containing '=' (invalid in Windows environment)
        if (name.Contains('='))
        {
            Console.Error.WriteLine("Error: Variable name cannot contain '='");
            return false;
        }

        // Protect critical system variables from being overwritten
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot modify protected system variable '{name}'");
            return false;
        }

        value ??= "";
        if (value.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: Value exceeds maximum length");
            return false;
        }

        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null)
            {
                Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
                return false;
            }

            bool existed = key.GetValueNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            object? originalValue = existed
                ? key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                : null;
            RegistryValueKind originalKind = RegistryValueKind.String;
            if (existed)
            {
                try
                {
                    originalKind = key.GetValueKind(name);
                }
                catch (IOException)
                {
                    existed = false;
                    originalValue = null;
                }
            }

            RegistryValueKind writeKind = originalKind;
            if (!existed || value.Contains('%'))
            {
                writeKind = value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String;
            }

            bool RestoreOriginal()
            {
                try
                {
                    if (existed)
                    {
                        key.SetValue(name, originalValue!, originalKind);
                    }
                    else
                    {
                        key.DeleteValue(name, false);
                    }

                    bool restoredExists = key.GetValueNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (restoredExists != existed) return false;
                    if (!existed) return true;

                    object? restoredValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    return Equals(restoredValue, originalValue) && key.GetValueKind(name) == originalKind;
                }
                catch (Exception restoreError)
                {
                    DebugLog("SetVariable rollback failure=" + restoreError.GetType().Name);
                    return false;
                }
            }

            try
            {
                key.SetValue(name, value, writeKind);
                object? persisted = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                bool verified = persisted is string persistedString &&
                    string.Equals(persistedString, value, StringComparison.Ordinal) &&
                    key.GetValueKind(name) == writeKind;
                if (verified)
                {
                    BroadcastSettingChange();
                    return true;
                }
            }
            catch (Exception writeError)
            {
                DebugLog("SetVariable write failure=" + writeError.GetType().Name);
            }

            bool rolledBack = RestoreOriginal();
            if (rolledBack)
            {
                BroadcastSettingChange();
                Console.Error.WriteLine("Error: Variable write could not be verified; original value restored");
            }
            else
            {
                Console.Error.WriteLine("Error: Variable write could not be verified and automatic rollback failed; restore from a backup before retrying");
            }
            return false;
        }
    }

    static void DeleteVariable(string name, string scope)
    {
        DebugLog("DeleteVariable scope=" + scope);
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return;
        }

        // Protect critical system variables from being deleted
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot delete protected system variable '{name}'");
            return;
        }

        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null)
            {
                Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
                return;
            }

            key.DeleteValue(name, false);

            // Also clean up toggle backup key if the variable was disabled.
            // This prevents orphaned _EnvManager_disabled keys from accumulating.
            string toggleBackupName = GetToggleBackupName(name);
            if (key.GetValue(toggleBackupName) != null)
            {
                key.DeleteValue(toggleBackupName, false);
            }

            // Also clean up profile backup keys for this variable name.
            // Profile backups use the format: <varName>_PowerToys_<profileName>
            // We scan for _PowerToys_ backup keys that start with the variable name.
            foreach (var valName in key.GetValueNames())
            {
                // Match: <varName>_PowerToys_<anything> (the applied profile backup)
                // Also match: <varName>_EnvManager_disabled (the toggle backup)
                if (valName.StartsWith(name + "_PowerToys_") && !valName.EndsWith("_EnvManager_disabled"))
                {
                    key.DeleteValue(valName, false);
                }
            }
        }

        BroadcastSettingChange();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    static extern bool SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    static void BroadcastSettingChange()
    {
        const uint HWND_BROADCAST = 0xFFFF;
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
            "Environment", SMTO_ABORTIFHUNG, 500, out _);
    }

    // --- Profile commands ---
    // Mirrors PowerToys profile logic: profiles override user variables,
    // original values are backed up as name_PowerToys_<profileName> before apply,
    // and restored on unapply. Profiles only affect user-scope variables.

    static int RunProfileCommand(string[] args)
    {
        if (args.Length < 2)
        {
            ShowProfileHelp();
            return 0;
        }

        string sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "list" => ProfileList(),
            "create" => ProfileCreate(args),
            "delete" => args.Length < 3 ? ArgError("Usage: env-manager profile delete <name>") : ProfileDelete(args[2]),
            "apply" => args.Length < 3 ? ArgError("Usage: env-manager profile apply <name>") : ProfileApply(args[2]),
            "unapply" => args.Length < 3 ? ArgError("Usage: env-manager profile unapply <name>") : ProfileUnapply(args[2]),
            "show" => args.Length < 3 ? ArgError("Usage: env-manager profile show <name> [--reveal]") : ProfileShow(args[2], args.Any(a => a.Equals("--reveal", StringComparison.OrdinalIgnoreCase))),
            "preview" => args.Length < 3 ? ArgError("Usage: env-manager profile preview <name>") : ProfilePreview(args[2]),
            "set-inherits" => args.Length < 3 ? ArgError("Usage: env-manager profile set-inherits <name> [parent ...]") : ProfileSetInherits(args),
            "add-path" => args.Length < 4 ? ArgError("Usage: env-manager profile add-path <name> <directory>") : ProfileAddPath(args[2], args[3]),
            "remove-path" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-path <name> <directory>") : ProfileRemovePath(args[2], args[3]),
            "add-var" => args.Length < 5 ? ArgError("Usage: env-manager profile add-var <profile> <name> <value>") : ProfileAddVar(args[2], args[3], args[4]),
            "remove-var" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-var <profile> <name>") : ProfileRemoveVar(args[2], args[3]),
            "edit-var" => args.Length < 6 ? ArgError("Usage: env-manager profile edit-var <profile> <old-name> <new-name> <new-value>") : ProfileEditVar(args[2], args[3], args[4], args[5]),
            "status" => args.Length < 3 ? ArgError("Usage: env-manager profile status <name>") : ProfileStatus(args[2]),
            "export" => ProfileExport(args),
            "import" => ProfileImport(args),
            "rename" => args.Length < 4 ? ArgError("Usage: env-manager profile rename <old> <new>") : ProfileRename(args[2], args[3]),
            // v0.6.0 Launch profile commands. Configure target executable + isolation settings
            // without writing anything to the registry. Spawn a child process with env_clear + inject.
            "set-launch" => ProfileSetLaunch(args),
            "launch" => args.Length < 3 ? ArgError("Usage: env-manager profile launch <name> [-- <extra-args ...>]") : ProfileLaunch(args),
            // v0.7.0 DPAO secrets subcommands.
            "add-secret" => args.Length < 5 ? ArgError("Usage: env-manager profile add-secret <profile> <name> <value>") : ProfileAddSecret(args[2], args[3], args[4]),
            "edit-secret" => args.Length < 6 ? ArgError("Usage: env-manager profile edit-secret <profile> <old> <new> <value>") : ProfileEditSecret(args[2], args[3], args[4], args[5]),
            "remove-secret" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-secret <profile> <name>") : ProfileRemoveSecret(args[2], args[3]),
            "reveal-secret" => args.Length < 4 ? ArgError("Usage: env-manager profile reveal-secret <profile> <name>") : ProfileRevealSecret(args[2], args[3]),
            // v0.8 secret export/import subcommands
            "export-secrets" => ProfileExportSecrets(args),
            "import-secrets" => ProfileImportSecrets(args),
            // v0.8 secret provider management subcommands
            "secret-provider" => RunSecretProviderCommand(args),
            "help" => ShowProfileHelp(),
            _ => ArgError($"Unknown profile subcommand: {sub}")
        };
    }

    /// <summary>
    /// Returns the backup variable name for a toggled (disabled) variable.
    /// Mirrors PowerToys: name + "_EnvManager_disabled"
    /// </summary>
    static string GetToggleBackupName(string varName)
    {
        return varName + "_EnvManager_disabled";
    }

    static bool IsInternalToggleBackupName(string name)
    {
        return name.EndsWith("_EnvManager_disabled", StringComparison.OrdinalIgnoreCase);
    }

    static int RunToggle(string[] args)
    {
        string name = args[1];
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
        DebugLog($"Toggle scope={scope}");

        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Variable name cannot be empty");
            return 1;
        }
        if (IsInternalToggleBackupName(name))
        {
            Console.Error.WriteLine("Error: Cannot toggle a variable whose name ends with '_EnvManager_disabled'");
            return 1;
        }
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot toggle protected variable '{name}'");
            return 1;
        }

        string backupName = GetToggleBackupName(name);
        var (hive, registryPath) = GetScopeTarget(scope);
        using var key = hive?.OpenSubKey(registryPath, true);
        if (key == null)
        {
            Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
            return 1;
        }

        bool originalExists = key.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool backupExists = key.GetValueNames().Any(valueName => valueName.Equals(backupName, StringComparison.OrdinalIgnoreCase));
        if (originalExists && backupExists)
        {
            Console.Error.WriteLine($"Error: Toggle recovery conflict for '{name}'. Both the variable and its disabled backup exist; no values were changed.");
            return 1;
        }
        if (!originalExists && !backupExists)
        {
            Console.Error.WriteLine($"Error: Variable '{name}' not found");
            return 1;
        }

        try
        {
            if (backupExists)
            {
                object backupValue = key.GetValue(backupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                    ?? throw new InvalidDataException("Disabled backup has no value");
                RegistryValueKind backupKind = key.GetValueKind(backupName);
                key.SetValue(name, backupValue, backupKind);
                object? restoredValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                bool restored = Equals(restoredValue, backupValue) && key.GetValueKind(name) == backupKind;
                if (!restored)
                {
                    key.DeleteValue(name, false);
                    Console.Error.WriteLine($"Error: Failed to restore '{name}' exactly; disabled backup was preserved");
                    return 1;
                }
                key.DeleteValue(backupName, false);
                if (key.GetValueNames().Any(valueName => valueName.Equals(backupName, StringComparison.OrdinalIgnoreCase)))
                {
                    Console.Error.WriteLine($"Error: Restored '{name}', but could not remove its disabled backup");
                    return 1;
                }
                BroadcastSettingChange();
                Console.WriteLine(JsonSerializer.Serialize(new { name, scope, isDisabled = false }, JsonOpts));
                return 0;
            }

            object originalValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                ?? throw new InvalidDataException("Variable has no value");
            RegistryValueKind originalKind = key.GetValueKind(name);
            key.SetValue(backupName, originalValue, originalKind);
            object? persistedBackup = key.GetValue(backupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            bool backupVerified = Equals(persistedBackup, originalValue) && key.GetValueKind(backupName) == originalKind;
            if (!backupVerified)
            {
                key.DeleteValue(backupName, false);
                Console.Error.WriteLine($"Error: Failed to preserve '{name}' exactly; variable was not disabled");
                return 1;
            }
            key.DeleteValue(name, false);
            if (key.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                Console.Error.WriteLine($"Error: Failed to disable '{name}'; its backup was preserved for recovery");
                return 1;
            }
            BroadcastSettingChange();
            Console.WriteLine(JsonSerializer.Serialize(new { name, scope, isDisabled = true }, JsonOpts));
            return 0;
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidDataException)
        {
            DebugLog("Toggle failure=" + error.GetType().Name);
            Console.Error.WriteLine($"Error: Could not toggle '{name}'; no destructive recovery was attempted");
            return 1;
        }
    }

    static int ProfileEditVar(string profileName, string oldVarName, string newVarName, string newVarValue)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        if (profile.IsEnabled)
            return ArgError("Error: Unapply the profile before changing its variables");

        var var = profile.Variables.FirstOrDefault(v => v.Name.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
        if (var == null)
        {
            Console.Error.WriteLine($"Error: Variable '{oldVarName}' not found in profile '{profileName}'");
            return 1;
        }

        // If name changed and profile is applied, handle backup rename
        if (!oldVarName.Equals(newVarName, StringComparison.OrdinalIgnoreCase) && profile.IsEnabled)
        {
            string oldBackupName = GetBackupVariableName(oldVarName, profileName);
            string newBackupName = GetBackupVariableName(newVarName, profileName);

            var oldBackup = GetVariableValue(oldBackupName, "user");
            if (oldBackup != null)
            {
                SetVariableWithoutNotify(newBackupName, oldBackup, "user");
                DeleteVariableWithoutNotify(oldBackupName, "user");
            }

            DeleteVariableWithoutNotify(oldVarName, "user");
        }

        var preEditVar = new ProfileVariable { Name = var.Name, Value = var.Value };
        var.Name = newVarName;
        var.Value = newVarValue;
        SaveProfiles(profiles);

        if (profile.IsEnabled)
        {
            SetVariableWithoutNotify(newVarName, newVarValue, "user");
            BroadcastSettingChange();
        }

        var postEditVar = new ProfileVariable { Name = newVarName, Value = newVarValue };
        RecordProfileAudit("profile edit-var", profileName, JsonSerializer.Serialize(preEditVar, JsonOpts), JsonSerializer.Serialize(postEditVar, JsonOpts));
        Console.WriteLine($"Edited variable '{oldVarName}' -> '{newVarName}' in profile '{profileName}'");
        return 0;
    }

    // v0.7 DPAPI-encrypted secret variables for launch profiles.
    // Plaintext lives only in transient CLI process memory; the profiles.json stores
    // base64(DPAPI-Protect(CurrentUser, plaintext)). AGENTS.md hard boundary: audit
    // records the variable NAME only, never its plaintext or ciphertext value.
    static int ProfileAddSecret(string profileName, string varName, string varValue)
    {
        if (string.IsNullOrWhiteSpace(varName) || varName.Length > 255 || varName.Contains('=') || ProtectedSystemVars.Contains(varName))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return 1;
        }
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing its variables");

        profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        string encrypted = SecretProviderManager.Encrypt(varValue, profileName + "\\" + varName);
        profile.Variables.Add(new ProfileVariable { Name = varName, Value = encrypted });
        if (!profile.SecretVariables.Any(s => s.Equals(varName, StringComparison.OrdinalIgnoreCase)))
            profile.SecretVariables.Add(varName);
        SaveProfiles(profiles);

        RecordProfileAudit("profile add-secret", profileName,
            JsonSerializer.Serialize(new { name = varName, value = "<redacted>" }),
            JsonSerializer.Serialize(new { name = varName, value = "<encrypted>" }));
        Console.WriteLine($"Added secret variable '{varName}' ({SecretProviderManager.GetActiveProviderName()}) to profile '{profileName}'");
        return 0;
    }

    static int ProfileEditSecret(string profileName, string oldVarName, string newVarName, string newVarValue)
    {
        // v0.7: reject rename into a protected system variable name (same invariant as add-secret / AGENTS.md hard boundary).
        if (string.IsNullOrWhiteSpace(newVarName) || newVarName.Length > 255 || newVarName.Contains('=') || newVarName.Contains('\x00') || newVarName.Contains('\n') || newVarName.Contains('\r') || ProtectedSystemVars.Contains(newVarName))
        {
            Console.Error.WriteLine("Error: Invalid or protected variable name");
            return 1;
        }
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing its variables");

        var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
        if (v == null) { Console.Error.WriteLine($"Error: Secret variable '{oldVarName}' not found in profile '{profileName}'"); return 1; }

        bool wasMarkedSecret = profile.SecretVariables.Any(s => s.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
        string encryptedNew = SecretProviderManager.Encrypt(newVarValue, profileName + "\\" + newVarName);
        v.Name = newVarName;
        v.Value = encryptedNew;

        if (!newVarName.Equals(oldVarName, StringComparison.OrdinalIgnoreCase))
        {
            profile.SecretVariables.RemoveAll(s => s.Equals(oldVarName, StringComparison.OrdinalIgnoreCase));
            if (wasMarkedSecret && !profile.SecretVariables.Any(s => s.Equals(newVarName, StringComparison.OrdinalIgnoreCase)))
                profile.SecretVariables.Add(newVarName);
        }
        SaveProfiles(profiles);

        RecordProfileAudit("profile edit-secret", profileName,
            JsonSerializer.Serialize(new { name = oldVarName, value = "<redacted>" }),
            JsonSerializer.Serialize(new { name = newVarName, value = "<encrypted>" }));
        Console.WriteLine($"Edited secret '{oldVarName}' -> '{newVarName}' in profile '{profileName}'");
        return 0;
    }

    static int ProfileRemoveSecret(string profileName, string varName)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing its variables");

        var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (v == null) { Console.Error.WriteLine($"Error: Variable '{varName}' not found in profile '{profileName}'"); return 1; }

        // Delete provider-side state (e.g. CredMan entry) before removing from profile
        try { SecretProviderManager.Delete(v.Value, profileName + "\\" + varName); } catch { }

        profile.Variables.Remove(v);
        profile.SecretVariables.RemoveAll(s => s.Equals(varName, StringComparison.OrdinalIgnoreCase));
        SaveProfiles(profiles);

        RecordProfileAudit("profile remove-secret", profileName, JsonSerializer.Serialize(new { name = varName }), null);
        Console.WriteLine($"Removed secret variable '{varName}' from profile '{profileName}'");
        return 0;
    }

    // Reveal one secret's plaintext to stdout. Only succeeds for the same user account
    // that encrypted it (DPAPI CurrentUser). `profile launch` decrypts in-process; this
    // command exists only for the rare case where the agent actually needs the raw value
    // (e.g. piping into a credential helper). Use sparingly to limit plaintext exposure.
    static int ProfileRevealSecret(string profileName, string varName)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (v == null) { Console.Error.WriteLine($"Error: Variable '{varName}' not found in profile '{profileName}'"); return 1; }
        if (!profile.SecretVariables.Any(s => s.Equals(varName, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Error: Variable '{varName}' is not a secret in profile '{profileName}'");
            return 1;
        }
        try
        {
            Console.Out.Write(SecretProviderManager.Decrypt(v.Value, profileName + "\\" + varName));
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to decrypt secret '{varName}': {ex.Message}");
            return 1;
        }
    }


    // v0.8 Secret provider management
    static int RunSecretProviderCommand(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: env-manager profile secret-provider <list|set> [options]");
            Console.Error.WriteLine("  secret-provider list              List available providers and active selection");
            Console.Error.WriteLine("  secret-provider set <name>        Set the active secret provider");
            return 1;
        }
        string sub = args[2];
        switch (sub)
        {
            case "list":
                var providers = SecretProviderManager.ListProviders();
                string active = SecretProviderManager.GetActiveProviderName();
                Console.WriteLine($"Active provider: {active}");
                Console.WriteLine("Available providers:");
                foreach (var (name, available) in providers)
                {
                    string marker = name.Equals(active, StringComparison.OrdinalIgnoreCase) ? " (active)" : "";
                    Console.WriteLine($"  {name}{marker}{(available ? "" : " (unavailable)")}");
                }
                return 0;

            case "set":
                if (args.Length < 4)
                {
                    Console.Error.WriteLine("Usage: env-manager profile secret-provider set <name>");
                    return 1;
                }
                try
                {
                    SecretProviderManager.SetActiveProvider(args[3]);
                    Console.WriteLine($"Active secret provider set to: {args[3]}");
                    Console.WriteLine("Note: existing secrets encrypted with the previous provider will still decrypt correctly (fail-closed on unknown provider).");
                    return 0;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Error: {ex.Message}");
                    return 1;
                }

            case "rotate":
                var profilesToRotate = LoadProfiles();
                var (total, rotatedN, failedN) = SecretProviderManager.RotateAll(profilesToRotate);
                if (rotatedN > 0) SaveProfiles(profilesToRotate);
                Console.WriteLine($"Rotation complete: {rotatedN}/{total} secrets re-encrypted, {failedN} failed");
                if (rotatedN > 0)
                {
                    RecordProfileAudit("profile secret-provider rotate", "(all)",
                        JsonSerializer.Serialize(new { total, rotated = rotatedN, failed = failedN }), null);
                }
                return 0;

            default:
                Console.Error.WriteLine($"Unknown secret-provider subcommand: {sub}");
                return 1;
        }
    }

    // v0.8 Phase 3: Export secrets from a profile to an encrypted file
    static int ProfileExportSecrets(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: env-manager profile export-secrets <profile> <output-file>");
            return 1;
        }
        string profileName = args[2];
        string outputFile = args[3];
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }

        // Validate output path
        string? pathError = ValidateFilePath(outputFile, mustExist: false);
        if (pathError != null) { Console.Error.WriteLine($"Error: {pathError}"); return 1; }

        string encrypted = SecretProviderManager.ExportSecrets(profile);
        File.WriteAllText(outputFile, encrypted);
        int secretCount = profile.SecretVariables.Count;
        Console.WriteLine($"Exported {secretCount} secret(s) from profile '{profileName}' to '{outputFile}'");
        RecordProfileAudit("profile export-secrets", profileName,
            JsonSerializer.Serialize(new { file = outputFile, count = secretCount }), null);
        return 0;
    }

    // v0.8 Phase 3: Import secrets from an encrypted file into a profile
    static int ProfileImportSecrets(string[] args)
    {
        if (args.Length < 4)
        {
            Console.Error.WriteLine("Usage: env-manager profile import-secrets <profile> <input-file>");
            return 1;
        }
        string profileName = args[2];
        string inputFile = args[3];
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{profileName}' not found"); return 1; }
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before importing secrets");

        // Validate input path
        string? pathError = ValidateFilePath(inputFile, mustExist: true);
        if (pathError != null) { Console.Error.WriteLine($"Error: {pathError}"); return 1; }
        if (!File.Exists(inputFile)) { Console.Error.WriteLine($"Error: File '{inputFile}' not found"); return 1; }

        string encryptedBackup = File.ReadAllText(inputFile);
        var results = SecretProviderManager.ImportSecrets(profile, encryptedBackup);
        SaveProfiles(profiles);

        int succeeded = results.Count(r => r.success);
        int failedImp = results.Count(r => !r.success);
        Console.WriteLine($"Imported {succeeded} secret(s) into profile '{profileName}', {failedImp} failed");
        RecordProfileAudit("profile import-secrets", profileName,
            JsonSerializer.Serialize(new { file = inputFile, imported = succeeded, failed = failedImp }), null);
        return 0;
    }


    static int ProfileStatus(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        bool correctlyApplied = profile.IsEnabled && IsProfileCorrectlyApplied(profile);
        bool applicable = IsProfileApplicable(profile);

        var result = new
        {
            name = profile.Name,
            isEnabled = profile.IsEnabled,
            isCorrectlyApplied = correctlyApplied,
            isApplicable = applicable,
            variableCount = profile.Variables.Count
        };
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
        return 0;
    }

    static int ProfileExport(string[] args)
    {
        if (args.Length < 5 || args[3] != "--output")
        {
            Console.Error.WriteLine("Usage: env-manager profile export <name> --output <file>");
            return 1;
        }

        string profileName = args[2];
        string outputPath = ValidateFilePath(args[4], mustExist: false);

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        var exportData = new
        {
            name = profile.Name,
            inherits = profile.Inherits,
            pathEntries = profile.PathEntries,
            variables = profile.Variables.Select(v => new { name = v.Name, value = v.Value }).ToList()
        };

        string json = JsonSerializer.Serialize(exportData, JsonOptsIndented);
        File.WriteAllText(outputPath, json);
        Console.WriteLine($"Exported profile '{profileName}' to {outputPath}");
        return 0;
    }

    static int ProfileImport(string[] args)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: env-manager profile import <file>");
            return 1;
        }

        string inputPath = ValidateFilePath(args[2], mustExist: true);

        string json = File.ReadAllText(inputPath);
        using var doc = JsonDocument.Parse(json);

        string profileName = doc.RootElement.GetProperty("name").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(profileName))
        {
            Console.Error.WriteLine("Error: Profile name is empty in import file");
            return 1;
        }

        var profiles = LoadProfiles();
        var existing = FindProfile(profiles, profileName);
        if (existing != null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' already exists. Delete it first or rename in the import file.");
            return 1;
        }

        var newProfile = new ProfileData
        {
            Id = Guid.NewGuid().ToString(),
            Name = profileName,
            IsEnabled = false
        };

        if (doc.RootElement.TryGetProperty("inherits", out var inheritsElement))
            newProfile.Inherits = inheritsElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();
        if (doc.RootElement.TryGetProperty("pathEntries", out var pathsElement))
            newProfile.PathEntries = pathsElement.EnumerateArray().Select(item => item.GetString() ?? "").Where(item => item.Length > 0).ToList();

        foreach (var varElem in doc.RootElement.GetProperty("variables").EnumerateArray())
        {
            string varName = varElem.GetProperty("name").GetString() ?? "";
            string varValue = varElem.GetProperty("value").GetString() ?? "";
            if (!string.IsNullOrEmpty(varName))
            {
                newProfile.Variables.Add(new ProfileVariable { Name = varName, Value = varValue });
            }
        }

        profiles.Add(newProfile);
        SaveProfiles(profiles);
        Console.WriteLine($"Imported profile '{profileName}' with {newProfile.Variables.Count} variables");
        return 0;
    }

    static int ProfileRename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName))
        {
            Console.Error.WriteLine("Error: New profile name cannot be empty");
            return 1;
        }
        if (newName.Length > 255)
        {
            Console.Error.WriteLine("Error: Profile name exceeds 255 characters");
            return 1;
        }
        if (newName.Contains('\0') || newName.Contains('\n') || newName.Contains('\r'))
        {
            Console.Error.WriteLine("Error: Profile name contains invalid characters");
            return 1;
        }

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, oldName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{oldName}' not found");
            return 1;
        }

        // Check for name collision
        if (profiles.Any(p => p.Name.Equals(newName, StringComparison.OrdinalIgnoreCase) && p.Id != profile.Id))
        {
            Console.Error.WriteLine($"Error: Profile '{newName}' already exists");
            return 1;
        }

        // If profile is applied, we need to handle backup key renames
        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled)
        {
            UnapplyProfile(profile);
        }

        string oldProfileName = profile.Name;
        profile.Name = newName;
        SaveProfiles(profiles);

        if (wasEnabled)
        {
            ApplyProfile(profile);
        }

        RecordProfileAudit("profile rename", newName, oldProfileName, newName);
        Console.WriteLine($"Renamed profile '{oldProfileName}' -> '{newName}'");
        return 0;
    }

        // v0.6.0: Launch profile support --------------------------------------------------
    // Configure (or reset) a Launch profile's target executable / args / cwd without
    // writing anything to the registry. The profile's variables remain the trusted source;
    // when `profile launch` runs we spawn the target with env_clear + inject.
    static int ProfileSetLaunch(string[] args)
    {
        // Usage: profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]
        // If [--type launch] is passed on an existing Global profile, we convert it (apply status must be false).
        // If [--target """] clears target (only valid when [--type global]) the profile is converted back.
        if (args.Length < 3)
        {
            Console.Error.WriteLine("Usage: env-manager profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]");
            return 1;
        }
        string name = args[2];
        string? target = null, launchArgs = null, cwd = null, newType = null;
        for (int i = 3; i < args.Length; i++)
        {
            string a = args[i];
            switch (a)
            {
                case "--target": if (++i >= args.Length) goto Missing; target = args[i]; break;
                case "--args": if (++i >= args.Length) goto Missing; launchArgs = args[i]; break;
                case "--cwd": if (++i >= args.Length) goto Missing; cwd = args[i]; break;
                case "--type": if (++i >= args.Length) goto Missing; newType = args[i]; break;
                default: Console.Error.WriteLine($"Unknown flag: {a}"); return 1;
            }
        }
        Missing:
        if (target is null && newType is null)
        {
            Console.Error.WriteLine("Error: at least --target or --type must be specified");
            return 1;
        }
        if (newType is not null && !newType.Equals("global", StringComparison.OrdinalIgnoreCase) && !newType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --type must be 'global' or 'launch'");
            return 1;
        }

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled)
        {
            Console.Error.WriteLine($"Error: Cannot modify a launch configuration while profile '{name}' is applied. Unapply it first.");
            return 1;
        }

        if (newType is not null)
        {
            profile.ProfileType = newType.ToLowerInvariant();
            if (profile.ProfileType == "global") profile.TargetExecutable = null;
        }
        if (profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            if (target is not null) profile.TargetExecutable = StripVerbatimPrefix(target);
            if (launchArgs is not null) profile.LaunchArguments = launchArgs;
            if (cwd is not null) profile.WorkingDirectory = StripVerbatimPrefix(cwd);
            if (string.IsNullOrWhiteSpace(profile.TargetExecutable))
            {
                Console.Error.WriteLine("Error: Launch profile requires --target <exe>");
                return 1;
            }
        }
        else
        {
            // Global profile: --target is forbidden.
            if (target is not null)
            {
                Console.Error.WriteLine("Error: --target is only valid on Launch profiles");
                return 1;
            }
        }
        try
        {
            SaveProfiles(profiles);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 1;
        }
        Console.WriteLine($"Updated launch configuration for profile '{name}' (type={profile.ProfileType})");
        return 0;
    }

    /// <summary>
    /// Spawn the Launch profile's target executable with an isolated environment block.
    /// Critical safety properties:
    /// - Calls `Command(env_clear)` then `env(k,v)` for each profile variable + PATH entries.
    /// - NEVER calls `SetVariableWithoutNotify`, NEVER writes the registry, NEVER broadcasts WM_SETTINGCHANGE.
    /// - Secret names (in profile.SecretVariables) are read but their values are NOT echoed to stdout/stderr.
    /// - Logs only command names and arg counts (existing hard boundary: no env values in logs).
    /// </summary>
    static int ProfileLaunch(string[] args)
    {
        string name = args[2];
        // Collect any trailing args after "--" and pass to the child as additional app args.
        List<string> extraArgs = new();
        int dashIndex = Array.IndexOf(args, "--");
        if (dashIndex >= 0) extraArgs = args.Skip(dashIndex + 1).ToList();

        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null) { Console.Error.WriteLine($"Error: Profile '{name}' not found"); return 1; }
        if (!profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine($"Error: Profile '{name}' is a Global profile; only Launch profiles support `profile launch`");
            return 1;
        }
        if (string.IsNullOrWhiteSpace(profile.TargetExecutable))
        {
            Console.Error.WriteLine($"Error: Profile '{name}' has no targetExecutable. Use 'profile set-launch {name} --target <exe>' first.");
            return 1;
        }

        string exe = profile.TargetExecutable!;
        string cwd = profile.WorkingDirectory ?? Path.GetDirectoryName(exe)!;
        var effectiveVars = GetEffectiveProfileVariables(profile);
        var pathEntries = ResolveProfilePaths(profile);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = false,
            WorkingDirectory = cwd,
        };
        // Critical: do NOT inherit the parent's environment. The whole point of a Launch profile
        // is per-app isolation. Start from an empty env block, then inject only profile vars.
        psi.EnvironmentVariables.Clear();
        // Inject PATH first so subsequent variables that reference %PATH% resolve cleanly.
        var currentPath = pathEntries.Count > 0
            ? string.Join(';', pathEntries)
            : string.Empty;
        if (!string.IsNullOrEmpty(currentPath)) psi.EnvironmentVariables["PATH"] = currentPath;
        foreach (var v in effectiveVars)
        {
            if (v.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase)) continue;
            // v0.7: If this variable is in the profile's SecretVariables list, the stored
            // value is a base64-encoded DPAPI ciphertext (CurrentUser scope). Decrypt here so
            // the child process receives plaintext in its env block. Plaintext lives only in
            // this launcher process memory; never written to disk, the registry, or logs.
            string valueToInject = v.Value ?? string.Empty;
            if (profile.SecretVariables.Contains(v.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    valueToInject = SecretProviderManager.Decrypt(valueToInject, profile.Name + "\\" + v.Name);
                }
                catch (Exception)
                {
                    // Decryption failed: refuse to inject. Silent injection of garbage or
                    // ciphertext would be worse than failing the whole launch loudly.
                    Console.Error.WriteLine($"Error: Failed to decrypt secret '{v.Name}' for profile '{name}'");
                    return 1;
                }
            }
            psi.EnvironmentVariables[v.Name] = valueToInject;
        }
        foreach (string a in extraArgs) psi.ArgumentList.Add(a);
        if (!string.IsNullOrWhiteSpace(profile.LaunchArguments))
        {
            // Append the profile-defined static args after the user-supplied extras are already listed.
            // Order: user-supplied first, then profile default args. (Avoid surprising cross-shell parse.)
            psi.ArgumentList.Add(profile.LaunchArguments);
        }

        try
        {
            var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) { Console.Error.WriteLine("Error: Failed to start child process"); return 1; }
            Console.WriteLine($"Launched '{exe}' (PID={proc.Id}) with isolated environment from profile '{name}'. {(extraArgs.Count > 0 ? "Extra args: " + extraArgs.Count : "")}");
            return 0;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.Error.WriteLine($"Error: Failed to launch '{exe}': {ex.Message}");
            return 1;
        }
    }

    static int ShowProfileHelp()
    {
        Console.WriteLine(@"Profile commands:
  profile list                        List all profiles (JSON)
  profile create <name> [--type global|launch] [--target <exe>]
                                                    Create a Global or Launch profile atomically
  profile delete <name>               Delete a profile
  profile show <name>                 Show profile details (JSON)
  profile apply <name>                Apply a profile (backs up existing user vars)
  profile unapply <name>              Unapply a profile (restores backed-up user vars)
  profile add-var <profile> <name> <val>        Add a variable to a profile
  profile remove-var <profile> <name>           Remove a variable from a profile
  profile edit-var <profile> <old> <new> <val>  Edit a variable in a profile
  profile status <name>                         Check profile application status
  profile export <name> --output <file>          Export profile to JSON file
  profile import <file>                          Import profile from JSON file
  profile rename <old> <new>                     Rename a profile
  profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]
  profile launch <name> [-- <extra-args ...>]    Spawn target with isolated env from profile (no registry write)
  profile add-secret <profile> <name> <val>     Encrypt a value with DPAPI-CurrentUser; stored as ciphertext in profile json
  profile edit-secret <profile> <old> <new> <val>  Rename/re-encrypt a secret; plaintext lives only in memory
  profile remove-secret <profile> <name>          Remove a secret variable from a profile
  profile reveal-secret <profile> <name>          Print one secret's plaintext to stdout (DPAPI-bound to current user only)
  profile show <name> [--reveal]                  Show profile details (secret values masked unless --reveal; --reveal still only outputs decrypted value for the current user)
  profile export-secrets <profile> <file>          Export encrypted secrets from a profile to a file
  profile import-secrets <profile> <file>          Import encrypted secrets from a file into a profile
  profile secret-provider list                     List available secret providers and active selection
  profile secret-provider set <name>               Set the active secret provider
  profile secret-provider rotate                   Re-encrypt all secrets across all profiles with the active provider");
        return 0;
    }

    static ProfileData? FindProfile(List<ProfileData> profiles, string name)
    {
        var matches = profiles.Where(profile =>
            profile.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Take(2).ToList();
        if (matches.Count > 1)
            throw new InvalidDataException($"Ambiguous profile name '{name}'. Rename one profile before using name-only commands.");
        return matches.FirstOrDefault();
    }

    static int ProfileList()
    {
        var profiles = LoadProfiles();
        Console.WriteLine(JsonSerializer.Serialize(profiles, JsonOptsIndented));
        return 0;
    }

    static int ProfileCreate(string[] args)
    {
        if (args.Length < 3)
            return ArgError("Usage: env-manager profile create <name> [--type global|launch] [--target <exe>] [--args <args>] [--cwd <dir>]");

        string name = args[2];
        string profileType = "global";
        string? target = null;
        string? launchArgs = null;
        string? workingDirectory = null;
        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--type": if (++i >= args.Length) return ArgError("Missing value for --type"); profileType = args[i]; break;
                case "--target": if (++i >= args.Length) return ArgError("Missing value for --target"); target = args[i]; break;
                case "--args": if (++i >= args.Length) return ArgError("Missing value for --args"); launchArgs = args[i]; break;
                case "--cwd": if (++i >= args.Length) return ArgError("Missing value for --cwd"); workingDirectory = args[i]; break;
                default: return ArgError($"Unknown flag: {args[i]}");
            }
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 255)
        {
            Console.Error.WriteLine("Error: Profile name must be 1-255 characters");
            return 1;
        }
        if (name.Contains('\0') || name.Contains('\n') || name.Contains('\r'))
        {
            Console.Error.WriteLine("Error: Profile name contains invalid characters");
            return 1;
        }
        if (!profileType.Equals("global", StringComparison.OrdinalIgnoreCase) && !profileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.WriteLine("Error: --type must be 'global' or 'launch'");
            return 1;
        }
        bool isLaunch = profileType.Equals("launch", StringComparison.OrdinalIgnoreCase);
        if (isLaunch && string.IsNullOrWhiteSpace(target))
        {
            Console.Error.WriteLine("Error: Launch profile requires --target <exe>");
            return 1;
        }
        if (!isLaunch && (target != null || launchArgs != null || workingDirectory != null))
        {
            Console.Error.WriteLine("Error: --target, --args, and --cwd are only valid for Launch profiles");
            return 1;
        }

        var profiles = LoadProfiles();
        if (FindProfile(profiles, name) != null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' already exists");
            return 1;
        }

        var profile = new ProfileData
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            IsEnabled = false,
            Variables = new List<ProfileVariable>(),
            ProfileType = isLaunch ? "launch" : "global",
            TargetExecutable = isLaunch ? StripVerbatimPrefix(target) : null,
            LaunchArguments = isLaunch ? launchArgs : null,
            WorkingDirectory = isLaunch ? StripVerbatimPrefix(workingDirectory) : null,
        };
        try
        {
            SaveProfiles(profiles.Append(profile).ToList());
        }
        catch (InvalidDataException error)
        {
            Console.Error.WriteLine("Error: " + error.Message);
            return 1;
        }
        RecordProfileAudit("profile create", name, null, ProfileSummary(profile));
        Console.WriteLine($"Created {profile.ProfileType} profile: {name}");
        return 0;
    }

    static int ProfileDelete(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        if (profile.IsEnabled)
        {
            UnapplyProfile(profile);
        }

        var deletedSummary = ProfileSummary(profile);
        profiles.Remove(profile);
        SaveProfiles(profiles);
        RecordProfileAudit("profile delete", name, deletedSummary, null);
        Console.WriteLine($"Deleted profile: {name}");
        return 0;
    }

    static int ProfileShow(string name, bool revealSecrets = false)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }
        // v0.7: secret variable values are DPAPI-encrypted ciphertext on disk. By default
        // we emit "<encrypted>" in place of their values via stdout, so that an agent
        // calling 'profile show' for structural inspection never inadvertently receives or
        // records plaintext. Use 'profile show <name> --reveal' to surface decrypted values
        // (DPAPI-bound to the current user; decryption fails on any other user account).
        var masked = new ProfileData
        {
            Id = profile.Id,
            Name = profile.Name,
            IsEnabled = profile.IsEnabled,
            AppliedAt = profile.AppliedAt,
            Inherits = profile.Inherits,
            PathEntries = profile.PathEntries,
            ProfileType = profile.ProfileType,
            TargetExecutable = profile.TargetExecutable,
            LaunchArguments = profile.LaunchArguments,
            WorkingDirectory = profile.WorkingDirectory,
            SecretVariables = profile.SecretVariables,
        };
        foreach (var v in profile.Variables)
        {
            if (profile.SecretVariables.Contains(v.Name, StringComparer.OrdinalIgnoreCase))
            {
                masked.Variables.Add(new ProfileVariable
                {
                    Name = v.Name,
                    Value = revealSecrets ? TryDecryptSafe(v.Value) : "<encrypted>",
                });
            }
            else
            {
                masked.Variables.Add(new ProfileVariable { Name = v.Name, Value = v.Value });
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(masked, JsonOptsIndented));
        return 0;
    }

    static string TryDecryptSafe(string ciphertext)
    {
        try { return SecretProviderManager.Decrypt(ciphertext); }
        catch { return "<decryption-failed>"; }
    }

    static int ProfileApply(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        if (!IsProfileApplicable(profile))
        {
            Console.Error.WriteLine($"Error: Profile '{name}' contains invalid or protected variables and cannot be applied");
            return 1;
        }

        // Single active profile policy: unapply any currently-active profile before applying the new one.
        foreach (var other in profiles.Where(p => p.IsEnabled && p.Id != profile.Id).ToList())
        {
            UnapplyProfile(other);
            other.IsEnabled = false;
            other.AppliedAt = null;
            Console.WriteLine($"Unapplied profile: {other.Name} (single-profile policy)");
        }
        // If this profile is already applied, it's a no-op.
        if (profile.IsEnabled)
        {
            Console.WriteLine($"Profile '{name}' is already applied");
            return 0;
        }

        ApplyProfile(profile);
        profile.IsEnabled = true;
        profile.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        try
        {
            SaveProfiles(profiles);
        }
        catch
        {
            UnapplyProfile(profile);
            profile.IsEnabled = false;
            profile.AppliedAt = null;
            throw;
        }
        Console.WriteLine($"Applied profile: {name} ({profile.Variables.Count} variables)");
        return 0;
    }

    static int ProfileUnapply(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }

        if (!profile.IsEnabled)
        {
            Console.Error.WriteLine($"Warning: Profile '{name}' is not currently applied");
            return 0;
        }

        if (!CanUnapplySafely(profile, profiles))
            return ArgError("Error: A later-applied profile depends on overlapping variables; unapply it first");

        UnapplyProfile(profile);
        profile.IsEnabled = false;
        long? previousAppliedAt = profile.AppliedAt;
        profile.AppliedAt = null;
        try
        {
            SaveProfiles(profiles);
        }
        catch
        {
            ApplyProfile(profile);
            profile.IsEnabled = true;
            profile.AppliedAt = previousAppliedAt;
            throw;
        }
        Console.WriteLine($"Unapplied profile: {name}");
        return 0;
    }

    static int ProfileAddVar(string profileName, string varName, string varValue)
    {
        if (string.IsNullOrWhiteSpace(varName) || varName.Length > 255 || varName.Contains('=') || ProtectedSystemVars.Contains(varName))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return 1;
        }
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        if (profile.IsEnabled)
            return ArgError("Error: Unapply the profile before changing its variables");

        profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        var addedVar = new ProfileVariable { Name = varName, Value = varValue };
        profile.Variables.Add(addedVar);
        SaveProfiles(profiles);

        // If profile is currently applied, propagate the change to the registry
        if (profile.IsEnabled)
        {
            SetVariableWithoutNotify(varName, varValue, "user");
            BroadcastSettingChange();
        }

        RecordProfileAudit("profile add-var", profileName, null, JsonSerializer.Serialize(addedVar, JsonOpts));
        Console.WriteLine($"Added variable '{varName}' to profile '{profileName}'");
        return 0;
    }

    static int ProfileRemoveVar(string profileName, string varName)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        if (profile.IsEnabled)
            return ArgError("Error: Unapply the profile before changing its variables");

        var removedVar = profile.Variables.FirstOrDefault(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        int removed = profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            Console.Error.WriteLine($"Warning: Variable '{varName}' not found in profile '{profileName}'");
            return 0;
        }

        SaveProfiles(profiles);
        RecordProfileAudit("profile remove-var", profileName, JsonSerializer.Serialize(removedVar, JsonOpts), null);

        // If profile is currently applied, restore backup if it exists
        if (profile.IsEnabled)
        {
            string backupName = GetBackupVariableName(varName, profileName);
            var backupValue = GetVariableValue(backupName, "user");
            if (backupValue != null)
            {
                SetVariableWithoutNotify(varName, backupValue, "user");
                DeleteVariableWithoutNotify(backupName, "user");
            }
            else
            {
                DeleteVariableWithoutNotify(varName, "user");
            }
            BroadcastSettingChange();
        }

        Console.WriteLine($"Removed variable '{varName}' from profile '{profileName}'");
        return 0;
    }

    /// <summary>
    /// Returns the backup variable name for a given variable and profile.
    /// Mirrors PowerToys: name + "_PowerToys_" + profileName
    /// </summary>
    static string GetBackupVariableName(string varName, string profileName)
    {
        return varName + "_PowerToys_" + profileName;
    }

    /// <summary>
    /// Gets a variable value from registry without expanding environment variables.
    /// </summary>
    static string? GetVariableValue(string name, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, false))
        {
            if (key == null) return null;
            var v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return v?.ToString();
        }
    }

    /// <summary>
    /// Sets a variable in the registry without broadcasting WM_SETTINGCHANGE.
    /// Used by profile apply/unapply to batch changes efficiently.
    /// </summary>
    static void SetVariableWithoutNotify(string name, string value, string scope)
    {
        // Protect critical system variables even in profile/toggle operations
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot modify protected system variable '{name}'");
            return;
        }

        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null) return;

            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                kind = key.GetValueKind(name);
            }
            catch (IOException) { }

            // If value contains %, use ExpandString like Windows does
            if (value.Contains('%'))
            {
                kind = RegistryValueKind.ExpandString;
            }

            key.SetValue(name, value, kind);
        }
    }

    /// <summary>
    /// Deletes a variable from the registry without broadcasting WM_SETTINGCHANGE.
    /// </summary>
    static void DeleteVariableWithoutNotify(string name, string scope)
    {
        // Protect critical system variables from deletion even in internal paths
        // (rollback, toggle, profile cleanup). Without this guard, a rollback
        // after a failed bulk import could delete a protected variable whose
        // original value was null.
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot delete protected system variable '{name}'");
            return;
        }

        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null) return;
            key.DeleteValue(name, false);
        }
    }

    // Variables that should be edited as semicolon-separated lists.
    // Mirrors PowerToys' IsList() check.
    static readonly HashSet<string> ListVariables = new(StringComparer.OrdinalIgnoreCase)
    {
        "PATH", "PATHEXT", "PSMODULEPATH",
        "_NT_SYMBOL_PATH", "_NT_ALT_SYMBOL_PATH", "_NT_SYMCACHE_PATH"
    };

    static bool IsListVariable(string name) => ListVariables.Contains(name);

    // --- Path commands ---
    // Mirrors PowerToys list-style editing of PATH and similar semicolon-separated variables.

    static int RunPathCommand(string[] args)
    {
        DebugLog($"PathCommand: subcommand={args.ElementAtOrDefault(1) ?? "none"}; argumentCount={Math.Max(0, args.Length - 2)}");
        if (args.Length < 2)
        {
            ShowPathHelp();
            return 0;
        }

        string sub = args[1].ToLowerInvariant();
        return sub switch
        {
            "list" => PathList(args),
            "add" => args.Length < 3 ? ArgError("Usage: env-manager path add <dir> [--scope user|system] [--index N]") : PathAdd(args),
            "remove" => args.Length < 3 ? ArgError("Usage: env-manager path remove <dir> [--scope user|system]") : PathRemove(args),
            "move-up" => args.Length < 3 ? ArgError("Usage: env-manager path move-up <index> [--scope user|system]") : PathMoveUp(args),
            "move-down" => args.Length < 3 ? ArgError("Usage: env-manager path move-down <index> [--scope user|system]") : PathMoveDown(args),
            "rename" => args.Length < 5 ? ArgError("Usage: env-manager path rename <old-name> <new-name> [--scope user|system]") : PathRename(args),
            "dedupe" => PathDedupe(args),
            "health" => PathHealth(args),
            "help" => ShowPathHelp(),
            _ => ArgError($"Unknown path subcommand: {sub}")
        };
    }

    /// <summary>
    /// Removes duplicate PATH entries (case-insensitive), preserving the
    /// first occurrence. Protected PATH entries are never removed even if
    /// they appear duplicated -- the CLI treats protection as an absolute
    /// lock that dedupe cannot bypass (mirrors PathRename/PathRemove).
    /// Supports --dry-run to preview the removal without modifying PATH.
    /// Output is JSON so the GUI can show a precise before/after list.
    /// </summary>

    /// <summary>
    /// PATH health check: detect duplicates AND dead (non-existent) entries.
    /// Protected entries are NEVER reported as duplicates (defense-in-depth: HashSet isolation),
    /// and --fix NEVER removes a protected entry, even if it appears dead.
    /// Output: JSON array of entries with status, deadCount, duplicateCount, healthyCount.
    /// --fix: write a cleaned PATH, preserving order, removing ONLY non-protected duplicates and
    ///       non-protected dead entries. Protected entries are always preserved.
    /// </summary>
    static int PathHealth(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
        bool fix = args.Contains("--fix");
        bool dryRun = args.Contains("--dry-run");

        var entries = GetPathEntries(scope);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<object>();
        var keptClean = new List<string>();
        int deadCount = 0, dupCount = 0, healthyCount = 0;
        foreach (var entry in entries)
        {
            bool isProtected = IsProtectedPathEntry(entry);
            bool isDup = false;
            if (!isProtected)
            {
                isDup = !seen.Add(NormalizePathEntry(entry));
            }
            bool isDead = false;
            string expandedPath = Environment.ExpandEnvironmentVariables(entry);
            string fullPath = StripVerbatimPrefix(Path.IsPathRooted(expandedPath) ? expandedPath : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, expandedPath)));
            try { isDead = !Directory.Exists(fullPath); }
            catch { isDead = true; }

            string status = isDup || isDead
                ? (isDup && isDead ? "duplicate+dead" : isDup ? "duplicate" : "dead")
                : "healthy";
            if (isDup) dupCount++;
            if (isDead) deadCount++;
            if (status == "healthy") healthyCount++;

            bool keep = (!isDead || isProtected) && !isDup;
            if (keep) keptClean.Add(entry);

            results.Add(new
            {
                entry,
                status,
                isProtected,
                isDead,
                isDuplicate = isDup,
                fullPath
            });
        }

        if (!fix || dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                dryRun = dryRun || !fix,
                totalEntries = entries.Count,
                healthyCount,
                duplicateCount = dupCount,
                deadCount,
                wouldFix = fix && !dryRun,
                results
            }, JsonOpts));
            return 0;
        }

        if (keptClean.Count == entries.Count)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                dryRun = false,
                totalEntries = entries.Count,
                healthyCount,
                duplicateCount = dupCount,
                deadCount,
                wouldFix = false,
                results
            }, JsonOpts));
            return 0;
        }

        if (!SetPathEntries(keptClean, scope)) return 1;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scope,
            dryRun = false,
            totalEntries = entries.Count,
            cleanedEntries = keptClean.Count,
            removedDuplicate = dupCount,
            removedDead = deadCount,
            healthyCount,
            results
        }, JsonOpts));
        return 0;
    }

    static int PathDedupe(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
        bool dryRun = args.Contains("--dry-run");

        var entries = GetPathEntries(scope);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        var kept = new List<string>();
        foreach (var entry in entries)
        {
            bool isProtected = IsProtectedPathEntry(entry);
            if (!isProtected && seen.Contains(entry))
            {
                removed.Add(entry);
                continue;
            }
            // Only non-protected entries populate the dedupe set. This keeps
            // the HashSet as a precise "have we already kept a NON-PROTECTED
            // entry like this?" index, so a future maintainer extending dedupe
            // cannot accidentally drop a protected duplicate by reusing seen
            // without re-checking isProtected. Defense-in-depth: SetPathEntries
            // also independently rejects removing protected entries, so even
            // drift on this side is caught downstream. (code-reviewer MEDIUM)
            if (!isProtected) seen.Add(entry);
            kept.Add(entry);
        }

        if (dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                dryRun = true,
                removedCount = removed.Count,
                keptCount = kept.Count,
                removed,
                kept
            }, JsonOpts));
            return 0;
        }

        if (removed.Count == 0)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                removedCount = 0,
                keptCount = kept.Count,
                removed,
                kept
            }, JsonOpts));
            return 0;
        }

        if (!SetPathEntries(kept, scope)) return 1;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scope,
            removedCount = removed.Count,
            keptCount = kept.Count,
            removed,
            kept
        }, JsonOpts));
        return 0;
    }



    /// <summary>
    /// Renames a PATH entry: replaces the old directory string with a new one
    /// at the same position. Validates the new name for injection safety.
    /// </summary>
    static int PathRename(string[] args)
    {
        string oldDir = args[2];
        string newDir = args[3];
        string? scope = ParseScope(args, 4, "user");
        if (scope == null) return 1;

        // Validate new directory name (injection prevention)
        if (string.IsNullOrEmpty(newDir))
        {
            Console.Error.WriteLine("Error: New directory path cannot be empty");
            return 1;
        }
        if (newDir.Contains('\0'))
        {
            Console.Error.WriteLine("Error: Invalid characters in new directory path");
            return 1;
        }
        if (newDir.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: New directory path exceeds maximum length");
            return 1;
        }

        var entries = GetPathEntries(scope);
        int index = entries.FindIndex(e => e.Equals(oldDir, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Console.Error.WriteLine($"Error: '{oldDir}' not found in PATH ({scope})");
            return 1;
        }

        // Check for duplicates (if new name matches an existing entry that isn't the one being renamed)
        bool dupFound = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (i != index && entries[i].Equals(newDir, StringComparison.OrdinalIgnoreCase))
            {
                dupFound = true;
                break;
            }
        }
        if (dupFound)
        {
            Console.Error.WriteLine($"Error: '{newDir}' already exists in PATH ({scope})");
            return 1;
        }

        entries[index] = newDir;
        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Renamed PATH entry from '{oldDir}' to '{newDir}' ({scope})");
        return 0;
    }

    static int ShowPathHelp()
    {
        Console.WriteLine(@"Path commands (edits PATH as a semicolon-separated list):
  path list [--scope user|system]              List PATH entries (JSON)
  path add <dir> [--scope user|system] [--index N]  Add directory to PATH
  path remove <dir> [--scope user|system]      Remove directory from PATH
  path move-up <index> [--scope user|system]   Move PATH entry up
  path move-down <index> [--scope user|system] Move PATH entry down
  path rename <old> <new> [--scope user|system] Rename a PATH entry
  path dedupe [--scope user|system] [--dry-run]  Remove duplicate PATH entries (preserves first, respects protected)
  path health [--scope user|system] [--fix] [--dry-run]  Detect + optionally remove duplicates and dead (non-existent) PATH entries");
        return 0;
    }

    /// <summary>
    /// Parses the PATH variable for a given scope, returns entries as a list.
    /// </summary>
    static List<string> GetPathEntries(string scope)
    {
        string? pathValue = GetVariableValue("PATH", scope);
        if (string.IsNullOrEmpty(pathValue))
            return new List<string>();

        return pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    /// <summary>
    /// Writes PATH entries back to the registry for a given scope.
    /// </summary>
    static bool SetPathEntries(List<string> entries, string scope)
    {
        string joined = string.Join(";", entries);
        if (joined.Length > MaxLength)
        {
            Console.Error.WriteLine($"Error: PATH value exceeds maximum length of {MaxLength} characters (current: {joined.Length})");
            return false;
        }

        // Validate: don't allow removing protected PATH entries.
        // Compare current entries vs new entries to find what's being removed.
        var currentEntries = GetPathEntries(scope);
        var removed = currentEntries.Where(e => !entries.Any(x => NormalizePathEntry(x).Equals(NormalizePathEntry(e), StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var r in removed)
        {
            if (IsProtectedPathEntry(r))
            {
                Console.Error.WriteLine($"Error: Cannot remove protected PATH entry: {r}");
                return false;
            }
        }

        // PATH is intentionally editable. SetVariable verifies the exact raw
        // registry value and restores the previous value if verification fails.
        // PATH callers therefore never report success after an unverified write.
        return SetVariable("PATH", joined, scope);
    }

    static int PathList(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;

        var entries = GetPathEntries(scope);
        var normalizedCounts = entries.GroupBy(NormalizePathEntry, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var result = entries.Select((e, i) => new
        {
            index = i,
            path = e,
            expandedPath = Environment.ExpandEnvironmentVariables(e),
            isDuplicate = normalizedCounts.GetValueOrDefault(NormalizePathEntry(e)) > 1,
            exists = Directory.Exists(Environment.ExpandEnvironmentVariables(e)),
            isProtected = IsProtectedPathEntry(e),
            isBuiltinProtected = ProtectedPathEntries.Any(p => p.TrimEnd('\\', '/').Equals(e.TrimEnd('\\', '/').Trim(), StringComparison.OrdinalIgnoreCase))
        }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
        return 0;
    }

    static int PathAdd(string[] args)
    {
        string dir = args[2];
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        // Validate directory path (injection prevention for direct CLI usage)
        if (string.IsNullOrWhiteSpace(dir))
        {
            Console.Error.WriteLine("Error: Directory path cannot be empty");
            return 1;
        }
        if (dir.Contains('\0'))
        {
            Console.Error.WriteLine("Error: Directory path contains invalid characters");
            return 1;
        }
        if (dir.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: Directory path exceeds maximum length");
            return 1;
        }

        // Parse optional --index
        int? insertIndex = null;
        for (int i = 3; i < args.Length - 1; i++)
        {
            if (args[i] == "--index" && int.TryParse(args[i + 1], out int idx))
            {
                insertIndex = idx;
                break;
            }
        }

        var entries = GetPathEntries(scope);

        // Don't add duplicates
        if (entries.Any(e => e.Equals(dir, StringComparison.OrdinalIgnoreCase)))
        {
            Console.Error.WriteLine($"Warning: '{dir}' already exists in PATH ({scope})");
            return 0;
        }

        if (insertIndex.HasValue && insertIndex.Value >= 0 && insertIndex.Value <= entries.Count)
        {
            entries.Insert(insertIndex.Value, dir);
        }
        else
        {
            entries.Add(dir);
        }

        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Added '{dir}' to PATH ({scope}) at index {insertIndex ?? entries.Count - 1}");
        return 0;
    }

    static int PathRemove(string[] args)
    {
        string dir = args[2];
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        var entries = GetPathEntries(scope);
        int removed = entries.RemoveAll(e => e.Equals(dir, StringComparison.OrdinalIgnoreCase));

        if (removed == 0)
        {
            Console.Error.WriteLine($"Warning: '{dir}' not found in PATH ({scope})");
            return 0;
        }

        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Removed '{dir}' from PATH ({scope})");
        return 0;
    }

    static int PathMoveUp(string[] args)
    {
        if (!int.TryParse(args[2], out int index))
        {
            return ArgError("Error: index must be a number");
        }

        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        var entries = GetPathEntries(scope);
        if (index < 0 || index >= entries.Count || index == 0)
        {
            Console.Error.WriteLine("Error: Cannot move entry up (already at top or invalid index)");
            return 1;
        }

        (entries[index - 1], entries[index]) = (entries[index], entries[index - 1]);
        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Moved PATH entry at index {index} up");
        return 0;
    }

    static int PathMoveDown(string[] args)
    {
        if (!int.TryParse(args[2], out int index))
        {
            return ArgError("Error: index must be a number");
        }

        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        var entries = GetPathEntries(scope);
        if (index < 0 || index >= entries.Count - 1)
        {
            Console.Error.WriteLine("Error: Cannot move entry down (already at bottom or invalid index)");
            return 1;
        }

        (entries[index], entries[index + 1]) = (entries[index + 1], entries[index]);
        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Moved PATH entry at index {index} down");
        return 0;
    }

    static void CreateBackup(string outputPath)
    {
        DebugLog("CreateBackup");
        var backup = new BackupData
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            Version = "1.0.0"
        };

        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    backup.Variables.Add(new EnvVariable
                    {
                        Name = name,
                        Value = key.GetValue(name)?.ToString() ?? "",
                        Scope = "user"
                    });
                }
            }
        }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(SystemEnvPath))
            {
                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        backup.Variables.Add(new EnvVariable
                        {
                            Name = name,
                            Value = key.GetValue(name)?.ToString() ?? "",
                            Scope = "system"
                        });
                    }
                }
            }
        }
        catch (UnauthorizedAccessException) { }

        var json = JsonSerializer.Serialize(backup, JsonOptsIndented);
        File.WriteAllText(outputPath, json);
        Console.WriteLine($"Backup created: {outputPath} ({backup.Variables.Count} variables)");
    }

    static void RestoreBackup(string inputPath, string? scope)
    {
        DebugLog("RestoreBackup");
        string fullPath = ValidateFilePath(inputPath, mustExist: true);

        var backup = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(fullPath), JsonOpts);
        if (backup?.Variables == null)
        {
            Console.Error.WriteLine("Error: Invalid backup format");
            return;
        }

        int restored = 0;
        int skipped = 0;
        foreach (var v in backup.Variables)
        {
            if (scope == "all" || v.Scope == scope)
            {
                if (v.Scope != "user" && v.Scope != "system")
                {
                    Console.Error.WriteLine($"Skipping '{v.Name}': invalid scope '{v.Scope}'");
                    skipped++;
                    continue;
                }
                if (IsProtectedVariable(v.Name, v.Scope))
                {
                    Console.Error.WriteLine($"Skipping protected variable '{v.Name}' ({v.Scope})");
                    skipped++;
                    continue;
                }
                if (SetVariable(v.Name, v.Value, v.Scope))
                {
                    restored++;
                }
                else
                {
                    skipped++;
                }
            }
        }
        Console.WriteLine($"Restored {restored} variables" + (skipped > 0 ? $", skipped {skipped}" : ""));
    }

    static int DiffBackups(string oldPath, string newPath)
    {
        string oldFull = ValidateFilePath(oldPath, mustExist: true);
        string newFull = ValidateFilePath(newPath, mustExist: true);

        // Size validation (OOM prevention)
        foreach (var f in new[] { oldFull, newFull })
        {
            if (File.Exists(f) && new FileInfo(f).Length > MaxBackupFileSize)
            {
                Console.Error.WriteLine($"Error: File exceeds maximum size of {MaxBackupFileSize / 1024 / 1024} MB: {f}");
                return 1;
            }
        }

        var old = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(oldFull), JsonOpts);
        var nu = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(newFull), JsonOpts);

        if (old?.Variables == null || nu?.Variables == null)
        {
            Console.Error.WriteLine("Error: Invalid backup format");
            return 1;
        }

        var oldMap = old.Variables.ToDictionary(v => (v.Name, v.Scope), v => v.Value);
        var newMap = nu.Variables.ToDictionary(v => (v.Name, v.Scope), v => v.Value);

        var result = new
        {
            added = newMap.Where(x => !oldMap.ContainsKey(x.Key)).Select(x => new { name = x.Key.Name, scope = x.Key.Scope, value = x.Value }).ToList(),
            removed = oldMap.Where(x => !newMap.ContainsKey(x.Key)).Select(x => new { name = x.Key.Name, scope = x.Key.Scope }).ToList(),
            changed = newMap.Where(x => oldMap.ContainsKey(x.Key) && oldMap[x.Key] != x.Value).Select(x => new { name = x.Key.Name, scope = x.Key.Scope, oldValue = oldMap[x.Key], newValue = x.Value }).ToList()
        };

        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
        return 0;
    }

    static int MergeBackups(string oldPath, string newPath, string outputPath)
    {
        string oldFull = ValidateFilePath(oldPath, mustExist: true);
        string newFull = ValidateFilePath(newPath, mustExist: true);
        string outFull = ValidateFilePath(outputPath, mustExist: false);

        // Size validation (OOM prevention)
        foreach (var f in new[] { oldFull, newFull })
        {
            if (File.Exists(f) && new FileInfo(f).Length > MaxBackupFileSize)
            {
                Console.Error.WriteLine($"Error: File exceeds maximum size of {MaxBackupFileSize / 1024 / 1024} MB: {f}");
                return 1;
            }
        }

        var old = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(oldFull), JsonOpts);
        var nu = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(newFull), JsonOpts);

        if (old?.Variables == null || nu?.Variables == null)
        {
            Console.Error.WriteLine("Error: Invalid backup format");
            return 1;
        }

        var merged = new Dictionary<(string, string), EnvVariable>();
        foreach (var v in old.Variables) merged[(v.Name, v.Scope)] = v;
        foreach (var v in nu.Variables) merged[(v.Name, v.Scope)] = v;

        var result = new BackupData
        {
            Timestamp = DateTime.UtcNow.ToString("O"),
            Version = "1.0.0",
            Variables = merged.Values.ToList()
        };

        File.WriteAllText(outFull, JsonSerializer.Serialize(result, JsonOptsIndented));
        Console.WriteLine($"Merged: {outFull} ({result.Variables.Count} variables)");
        return 0;
    }

    static int ValidateBackup(string inputPath)
    {
        string fullPath = ValidateFilePath(inputPath, mustExist: true);

        try
        {
            var backup = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(fullPath), JsonOpts);
            if (backup?.Variables == null)
            {
                Console.Error.WriteLine("Invalid: Bad format");
                return 1;
            }
            Console.WriteLine($"Valid: {backup.Variables.Count} variables");
            return 0;
        }
        catch (JsonException)
        {
            Console.Error.WriteLine("Invalid: JSON error");
            return 1;
        }
    }

    /// <summary>
    /// Outputs the CLI-level AGENTS.md file content to stdout, or prints the
    /// file path when --path flag is used.
    ///
    /// This command allows AI agents and LLMs to discover the CLI's contract,
    /// safety boundaries, and integration patterns after invoking the CLI.
    ///
    /// Standard industry pattern: CLI tools expose an "agents" subcommand that
    /// outputs a machine-readable specification file. Agents call this command
    /// after first interaction to understand the tool's API.
    /// </summary>
    static int RunAgents(string[] args)
    {
        bool pathOnly = args.Length > 1 && args[1] == "--path";
        bool jsonOutput = args.Length > 1 && args[1] == "--json";
        bool summaryOnly = args.Length > 1 && args[1] == "--summary";

        // Resolve AGENTS.md path: adjacent to the CLI executable
        string agentsPath = "";
        try
        {
            string exeDir = System.AppContext.BaseDirectory;
            agentsPath = Path.Combine(exeDir, "AGENTS.cli.md");
            if (!File.Exists(agentsPath))
            {
                agentsPath = Path.Combine(exeDir, "AGENTS.md");
            }
        }
        catch { }

        if (pathOnly)
        {
            Console.WriteLine(agentsPath);
            return 0;
        }

        // --summary: brief machine-friendly overview (single line, easy to parse)
        if (summaryOnly)
        {
            string version = "0.5.0";
            Console.WriteLine($"env-manager-cli v{version} | Commands: list,get,set,delete,toggle,backup,restore,diff,merge,validate,profile,path,agents,help | Scopes: user,system | Safe: no-credentials,injection-protected,write-serialized | Agents: env-manager-cli agents --json for full spec");
            return 0;
        }

        // --json: structured machine-readable spec for AI agent integration
        if (jsonOutput)
        {
            var spec = new
            {
                name = "env-manager-cli",
                version = "0.5.0",
                description = "Windows environment variable manager CLI",
                commands = new[]
                {
                    new { cmd = "list", desc = "List all variables (JSON)", args = "", scope = false, @async = true },
                    new { cmd = "get", desc = "Get variable (JSON)", args = "<name>", scope = false, @async = true },
                    new { cmd = "set", desc = "Set variable", args = "<name> <value>", scope = true, @async = false },
                    new { cmd = "delete", desc = "Delete variable", args = "<name>", scope = true, @async = false },
                    new { cmd = "toggle", desc = "Enable/disable variable (backs up value)", args = "<name>", scope = true, @async = false },
                    new { cmd = "backup", desc = "Backup to JSON", args = "[--output <file>]", scope = false, @async = true },
                    new { cmd = "restore", desc = "Restore from JSON", args = "<file> [--scope]", scope = true, @async = false },
                    new { cmd = "diff", desc = "Compare backups (JSON)", args = "<old> <new>", scope = false, @async = true },
                    new { cmd = "merge", desc = "Merge backups", args = "<old> <new> --output <file>", scope = false, @async = false },
                    new { cmd = "validate", desc = "Validate backup", args = "<file>", scope = false, @async = true },
                    new { cmd = "profile", desc = "Manage profiles", args = "list|create|delete|apply|unapply|show|add-var|remove-var|edit-var|status", scope = true, @async = false },
                    new { cmd = "path", desc = "Edit PATH as list", args = "list|add|remove|move-up|move-down|rename", scope = true, @async = false },
                    new { cmd = "agents", desc = "Output AGENTS.md spec", args = "[--path|--json|--summary]", scope = false, @async = true },
                    new { cmd = "help", desc = "Show help", args = "", scope = false, @async = true },
                },
                scopes = new[] { "user", "system" },
                output = "stdout: JSON or text, stderr: errors/debug, exit 0=success 1=failure",
                safety = new
                {
                    noCredentials = true,
                    injectionProtected = true,
                    writeSerialized = true,
                    maxArgLen = 32767,
                    maxArgs = 64,
                    nullByteRejected = true,
                    controlCharRejected = true
                },
                integration = new
                {
                    pattern = "Call agents first to discover the contract, then use commands. Read operations are safe to batch. Write operations are serialized.",
                    tip = "Use --debug for verbose stderr logging. Pin to --scope user for non-interactive agent workflows (no elevation needed)."
                }
            };
            Console.WriteLine(JsonSerializer.Serialize(spec, JsonOptsIndented));
            return 0;
        }

        // Default: output AGENTS.cli.md content
        if (File.Exists(agentsPath))
        {
            Console.WriteLine(File.ReadAllText(agentsPath));
        }
        else
        {
            Console.WriteLine("# Env Manager CLI\n\nCommands: list, get, set, delete, toggle, backup, restore, diff, merge, validate, profile, path, agents, help\n\nUse --debug for verbose logging. Use --scope user|system for scope control.\nUse agents --json for machine-readable spec.");
        }
        return 0;
    }

    static int RunUpdate(string[] args)
    {
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "check";

        if (sub == "check")
        {
            // Query GitHub Releases API for latest version
            try
            {
                string url = "https://api.github.com/repos/Xxx91n/env-manager/releases/latest";
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("env-manager-cli");
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = client.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                tag = tag.TrimStart('v');
                string htmlUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "";

                string currentVersion = "0.5.0";
                bool isNewer = VersionIsNewer(tag, currentVersion);

                var result = new
                {
                    currentVersion = currentVersion,
                    latestVersion = tag,
                    isUpdateAvailable = isNewer,
                    releaseUrl = htmlUrl,
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, JsonOpts));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error checking for updates: " + ex.Message);
                return 1;
            }
        }

        return ArgError("Usage: env-manager update check");
    }

    static bool VersionIsNewer(string remote, string local)
    {
        var parse = (string s) => s.Split('.')
            .Select(p => int.TryParse(p.Trim(), out int n) ? n : 0)
            .ToArray();
        var r = parse(remote);
        var l = parse(local);
        int max = Math.Max(r.Length, l.Length);
        for (int i = 0; i < max; i++)
        {
            int rv = i < r.Length ? r[i] : 0;
            int lv = i < l.Length ? l[i] : 0;
            if (rv > lv) return true;
            if (rv < lv) return false;
        }
        return false;
    }

    static int ShowHelp()
    {
        Console.WriteLine(@"Env Manager v0.5.0

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
  --debug                    Enable verbose stderr logging");
        return 0;
    }
}
