using Microsoft.Win32;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace EnvManager;

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

class BulkVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
}

partial class Program
{
    const int MaxAuditEntries = 2000;
    static readonly Regex ExpandPattern = new("%([^%]+)%", RegexOptions.Compiled);

    static string AppDataDirectory
    {
        get
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnvManager");
            Directory.CreateDirectory(path);
            return path;
        }
    }

    static string AuditFilePath => Path.Combine(AppDataDirectory, "audit.json");

    /// <summary>
    /// Path to the externally-editable built-in protected variables list.
    /// Created on first run from <see cref="DefaultBuiltinProtectedVars"/> if missing.
    /// Users / admins can edit this file to customize protection without recompiling.
    /// </summary>
    static string BuiltinProtectedVarsFile => Path.Combine(AppDataDirectory, "builtin-protected-vars.json");

    /// <summary>
    /// Path to the externally-editable built-in protected PATH entries list.
    /// Created on first run from <see cref="DefaultBuiltinProtectedPaths"/> if missing.
    /// </summary>
    static string BuiltinProtectedPathsFile => Path.Combine(AppDataDirectory, "builtin-protected-paths.json");

    static readonly string[] DefaultBuiltinProtectedVars =
    {
        "PATHEXT", "PSMODULEPATH", "SystemRoot", "windir", "ComSpec",
        "TEMP", "TMP", "USERPROFILE", "SystemDrive", "ProgramFiles",
        "ProgramFiles(x86)", "ProgramData", "HOMEDRIVE", "HOMEPATH",
        "NUMBER_OF_PROCESSORS", "OS", "PROCESSOR_ARCHITECTURE",
        "PROCESSOR_IDENTIFIER", "PROCESSOR_LEVEL", "PROCESSOR_REVISION",
        "ALLUSERSPROFILE", "APPDATA", "COMMONPROGRAMFILES", "COMMONPROGRAMFILES(x86)",
        "COMPUTERNAME", "LOCALAPPDATA", "LOGONSERVER", "OneDrive", "OneDriveConsumer",
        "PUBLIC", "SESSIONNAME", "USERDOMAIN", "USERNAME",
    };

    static readonly string[] DefaultBuiltinProtectedPaths =
    {
        @"C:\Windows\System32",
        @"C:\Windows",
        @"C:\Windows\System32\Wbem",
        @"C:\Windows\System32\WindowsPowerShell\v1.0\",
    };

    static List<string> LoadBuiltinProtectedVars()
    {
        try
        {
            if (!File.Exists(BuiltinProtectedVarsFile))
            {
                AtomicWriteJson(BuiltinProtectedVarsFile, DefaultBuiltinProtectedVars.ToList());
            }
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BuiltinProtectedVarsFile), JsonOpts)
                ?? DefaultBuiltinProtectedVars.ToList();
        }
        catch
        {
            return DefaultBuiltinProtectedVars.ToList();
        }
    }

    static List<string> LoadBuiltinProtectedPaths()
    {
        try
        {
            if (!File.Exists(BuiltinProtectedPathsFile))
            {
                AtomicWriteJson(BuiltinProtectedPathsFile, DefaultBuiltinProtectedPaths.ToList());
            }
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BuiltinProtectedPathsFile), JsonOpts)
                ?? DefaultBuiltinProtectedPaths.ToList();
        }
        catch
        {
            return DefaultBuiltinProtectedPaths.ToList();
        }
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

    static List<AuditEntry> LoadAuditHistory()
    {
        if (!File.Exists(AuditFilePath)) return new();
        var info = new FileInfo(AuditFilePath);
        if (info.Length > MaxBackupFileSize) throw new InvalidDataException("Audit history exceeds the safety limit");
        return JsonSerializer.Deserialize<List<AuditEntry>>(File.ReadAllText(AuditFilePath), JsonOpts) ?? new();
    }

    static void AtomicWriteJson<T>(string path, T value)
    {
        string temp = path + ".tmp." + Environment.ProcessId;
        File.WriteAllText(temp, JsonSerializer.Serialize(value, JsonOptsIndented), new UTF8Encoding(false));
        File.Move(temp, path, true);
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

    static int RunExpand(string value)
    {
        string expanded = value;
        for (int depth = 0; depth < 8; depth++)
        {
            string next = ExpandPattern.Replace(expanded, match =>
            {
                string name = match.Groups[1].Value;
                return GetVariableValue(name, "user") ?? GetVariableValue(name, "system") ??
                    Environment.GetEnvironmentVariable(name) ?? match.Value;
            });
            if (next == expanded) break;
            expanded = next;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { value, expanded }, JsonOpts));
        return 0;
    }

    static int RunBulkCommand(string[] args)
    {
        if (args.Length < 3) return ArgError("Usage: env-manager bulk import|export <file> [--scope user|system] [--overwrite] [--dry-run]");
        string sub = args[1].ToLowerInvariant();
        string file = ValidateInterchangePath(args[2], sub == "import");
        string scope = ParseScope(args, 3, "user") ?? "user";
        if (sub == "export")
        {
            var data = ReadScopeVariables(scope).Select(p => new BulkVariable { Name = p.Key, Value = p.Value, Scope = scope }).ToList();
            WriteBulkFile(file, data);
            Console.WriteLine(JsonSerializer.Serialize(new { exported = data.Count, file }, JsonOpts));
            return 0;
        }
        if (sub != "import") return ArgError("Unknown bulk subcommand");
        var imported = ReadBulkFile(file, scope);
        var duplicateNames = imported.GroupBy(v => v.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicateNames.Count > 0) return ArgError("Error: Import contains duplicate variable names: " + string.Join(", ", duplicateNames));
        var conflicts = imported.Where(v => GetVariableValue(v.Name, v.Scope) is not null)
            .Select(v => new { v.Name, v.Scope, existingValue = GetVariableValue(v.Name, v.Scope), newValue = v.Value }).ToList();
        bool overwrite = args.Contains("--overwrite");
        bool dryRun = args.Contains("--dry-run");
        if (dryRun || (conflicts.Count > 0 && !overwrite))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { dryRun = true, count = imported.Count, conflicts }, JsonOptsIndented));
            return conflicts.Count > 0 && !dryRun ? 2 : 0;
        }
        foreach (var item in imported) ValidateVariableInput(item.Name, item.Value, item.Scope);
        var originals = imported.ToDictionary(
            item => item.Scope + "\0" + item.Name,
            item => GetVariableValue(item.Name, item.Scope),
            StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var item in imported)
            {
                SetVariableWithoutNotify(item.Name, item.Value, item.Scope);
                if (GetVariableValue(item.Name, item.Scope) != item.Value)
                    throw new IOException("Failed to verify imported variable: " + item.Name);
            }
        }
        catch
        {
            foreach (var item in imported.AsEnumerable().Reverse())
            {
                string? original = originals[item.Scope + "\0" + item.Name];
                if (original == null) DeleteVariableWithoutNotify(item.Name, item.Scope);
                else SetVariableWithoutNotify(item.Name, original, item.Scope);
            }
            BroadcastSettingChange();
            throw;
        }
        BroadcastSettingChange();
        Console.WriteLine(JsonSerializer.Serialize(new { imported = imported.Count, conflictsOverwritten = conflicts.Count }, JsonOpts));
        return 0;
    }

    static Dictionary<string, string> ReadScopeVariables(string scope)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var (hive, path) = GetScopeTarget(scope);
        using var key = hive?.OpenSubKey(path, false);
        if (key == null) return result;
        foreach (string name in key.GetValueNames())
        {
            if (name.Contains("_PowerToys_", StringComparison.OrdinalIgnoreCase) || name.EndsWith("_EnvManager_disabled", StringComparison.OrdinalIgnoreCase)) continue;
            result[name] = key.GetValue(name, "", RegistryValueOptions.DoNotExpandEnvironmentNames)?.ToString() ?? "";
        }
        return result;
    }

    static string ValidateInterchangePath(string path, bool mustExist)
    {
        string full = Path.GetFullPath(path);
        if (!new[] { ".json", ".env", ".csv" }.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Supported formats are .json, .env, and .csv");
        if (mustExist && !File.Exists(full)) throw new FileNotFoundException("File not found", full);
        if (mustExist && new FileInfo(full).Length > MaxBackupFileSize) throw new InvalidDataException("Import file exceeds 50 MB");
        return full;
    }

    static List<BulkVariable> ReadBulkFile(string path, string defaultScope)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".json") return JsonSerializer.Deserialize<List<BulkVariable>>(File.ReadAllText(path), JsonOpts) ?? new();
        if (extension == ".env") return File.ReadLines(path).Select(ParseEnvLine).Where(v => v != null).Select(v => { v!.Scope = defaultScope; return v; }).ToList()!;
        return File.ReadLines(path).Skip(1).Select(ParseCsvLine).Where(v => v != null).Select(v => { if (string.IsNullOrEmpty(v!.Scope)) v.Scope = defaultScope; return v; }).ToList()!;
    }

    static BulkVariable? ParseEnvLine(string line)
    {
        string trimmed = line.Trim();
        if (trimmed.Length == 0 || trimmed.StartsWith('#')) return null;
        if (trimmed.StartsWith("export ", StringComparison.OrdinalIgnoreCase)) trimmed = trimmed[7..].TrimStart();
        int split = trimmed.IndexOf('=');
        if (split <= 0) throw new InvalidDataException("Invalid .env line");
        string value = trimmed[(split + 1)..].Trim();
        if (value.Length >= 2 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''))) value = value[1..^1];
        return new BulkVariable { Name = trimmed[..split].Trim(), Value = value };
    }

    static BulkVariable? ParseCsvLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        var fields = ParseCsvFields(line);
        if (fields.Count < 2) throw new InvalidDataException("CSV requires name,value[,scope]");
        return new BulkVariable { Name = fields[0], Value = fields[1], Scope = fields.Count > 2 ? fields[2] : "" };
    }

    static List<string> ParseCsvFields(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch == '"' && quoted && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
            else if (ch == '"') quoted = !quoted;
            else if (ch == ',' && !quoted) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(ch);
        }
        if (quoted) throw new InvalidDataException("Unterminated CSV quote");
        fields.Add(current.ToString());
        return fields;
    }

    static void WriteBulkFile(string path, List<BulkVariable> data)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".json") { File.WriteAllText(path, JsonSerializer.Serialize(data, JsonOptsIndented), new UTF8Encoding(false)); return; }
        if (extension == ".env") { File.WriteAllLines(path, data.Select(v => v.Name + "=" + QuoteEnv(v.Value)), new UTF8Encoding(false)); return; }
        File.WriteAllLines(path, new[] { "name,value,scope" }.Concat(data.Select(v => Csv(v.Name) + "," + Csv(v.Value) + "," + Csv(v.Scope))), new UTF8Encoding(false));
    }

    static string QuoteEnv(string value) => value.Any(char.IsWhiteSpace) || value.Contains('#') ? "\"" + value.Replace("\"", "\\\"") + "\"" : value;
    static string Csv(string value) => value.IndexOfAny([',', '"', '\r', '\n']) >= 0 ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;

    static void ValidateVariableInput(string name, string value, string scope)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Contains('=') || name.Any(char.IsControl)) throw new ArgumentException("Invalid variable name");
        if (value.Length > MaxLength || value.Contains('\0')) throw new ArgumentException("Invalid variable value");
        if (IsProtectedVariable(name, scope)) throw new UnauthorizedAccessException("Protected system variable");
    }

    static string NormalizePathEntry(string path) => Environment.ExpandEnvironmentVariables(path).Trim().TrimEnd('\\', '/');

    /// <summary>
    /// Removes the Windows \\?\ verbatim prefix that `Path.GetFullPath` can append.
    /// We always expose normalized paths to the user, the registry, profiles, and PATH entries
    /// to avoid leaking the prefix (regression: previously GUI "Add CLI to PATH" produced
    /// \\?\D:\... in user PATH which broke child invocations).
    /// </summary>
    static string StripVerbatimPrefix(string? path)
    {
        if (string.IsNullOrEmpty(path)) return path ?? string.Empty;
        if (path.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)) return @"\\" + path.Substring(8);
        if (path.StartsWith(@"\\?\", StringComparison.OrdinalIgnoreCase)) return path.Substring(4);
        return path;
    }

    /// <summary>
    /// Validates that a Launch profile target executable exists, has a known executable
    /// extension, and is NOT inside \\Windows\\System32 (hard refusal: system32 hijacking).
    /// </summary>
    static void ValidateLaunchTarget(string target)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidDataException("Launch target is empty");
        string cwd = Environment.CurrentDirectory;
        string full = Path.IsPathRooted(target) ? target : Path.GetFullPath(Path.Combine(cwd, target));
        string ext = Path.GetExtension(full).ToLowerInvariant();
        if (ext is not (".exe" or ".bat" or ".cmd" or ".ps1"))
            throw new InvalidDataException($"Launch target must be an .exe/.bat/.cmd/.ps1 file (got: {ext})");
        if (!File.Exists(full)) throw new InvalidDataException($"Launch target does not exist: {full}");
        string lower = full.ToLowerInvariant();
        if (lower.StartsWith(@"c:\\windows\\system32\\"))
            throw new InvalidDataException("Launch targets inside \\Windows\\System32 are rejected to prevent system32 hijacking");
    }

    static List<ProfileVariable> ResolveProfileVariables(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new Dictionary<string, ProfileVariable>(StringComparer.OrdinalIgnoreCase);
        ResolveProfile(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        return result.Values.ToList();
    }

    static void ResolveProfile(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, Dictionary<string, ProfileVariable> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits)
        {
            var parent = FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName);
            ResolveProfile(parent, profiles, stack, result);
        }
        foreach (var variable in profile.Variables) result[variable.Name] = new ProfileVariable { Name = variable.Name, Value = variable.Value };
        stack.Remove(profile.Name);
    }

    static List<string> ResolveProfilePaths(ProfileData profile, List<ProfileData>? profiles = null)
    {
        profiles ??= LoadProfiles();
        var result = new List<string>();
        ResolvePaths(profile, profiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase), result);
        return result.DistinctBy(NormalizePathEntry, StringComparer.OrdinalIgnoreCase).ToList();
    }

    static void ResolvePaths(ProfileData profile, List<ProfileData> profiles, HashSet<string> stack, List<string> result)
    {
        if (!stack.Add(profile.Name)) throw new InvalidDataException("Profile inheritance cycle detected at " + profile.Name);
        foreach (string parentName in profile.Inherits) ResolvePaths(FindProfile(profiles, parentName) ?? throw new InvalidDataException("Inherited profile not found: " + parentName), profiles, stack, result);
        result.AddRange(profile.PathEntries);
        stack.Remove(profile.Name);
    }

    static int ProfilePreview(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null) return ArgError("Error: Profile not found");
        var variables = ResolveProfileVariables(profile, profiles).Select(v => new { v.Name, v.Value, currentValue = GetVariableValue(v.Name, "user"), conflict = GetVariableValue(v.Name, "user") != null }).ToList();
        var paths = ResolveProfilePaths(profile, profiles).Select(path => new { path, expandedPath = Environment.ExpandEnvironmentVariables(path), exists = Directory.Exists(Environment.ExpandEnvironmentVariables(path)) }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(new { profile = name, profile.Inherits, variables, pathEntries = paths }, JsonOptsIndented));
        return 0;
    }

    static int ProfileSetInherits(string[] args)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, args[2]);
        if (profile == null) return ArgError("Error: Profile not found");
        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled) UnapplyProfile(profile);
        profile.Inherits = args.Skip(3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ResolveProfileVariables(profile, profiles);
        ResolveProfilePaths(profile, profiles);
        SaveProfiles(profiles);
        // If the profile was active, re-apply it with the new inheritance chain
        if (wasEnabled) { ApplyProfile(profile); profile.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveProfiles(profiles); }
        Console.WriteLine(JsonSerializer.Serialize(new { profile = profile.Name, profile.Inherits }, JsonOpts));
        return 0;
    }

    static int ProfileAddPath(string profileName, string path)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) return ArgError("Error: Profile not found");
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing PATH entries");
        ValidatePathFragment(path);
        if (!profile.PathEntries.Any(p => NormalizePathEntry(p).Equals(NormalizePathEntry(path), StringComparison.OrdinalIgnoreCase))) profile.PathEntries.Add(path);
        SaveProfiles(profiles);
        Console.WriteLine("Added PATH entry to profile: " + profileName);
        return 0;
    }

    static int ProfileRemovePath(string profileName, string path)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) return ArgError("Error: Profile not found");
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing PATH entries");
        profile.PathEntries.RemoveAll(p => NormalizePathEntry(p).Equals(NormalizePathEntry(path), StringComparison.OrdinalIgnoreCase));
        SaveProfiles(profiles);
        Console.WriteLine("Removed PATH entry from profile: " + profileName);
        return 0;
    }

    static void ValidatePathFragment(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength || path.Contains(';') || path.Any(ch => ch == '\0' || (char.IsControl(ch) && ch != '\t'))) throw new ArgumentException("Invalid PATH entry");
    }
}
