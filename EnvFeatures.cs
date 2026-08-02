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

class ProtectionDefaults
{
    [JsonPropertyName("variables")] public List<string> Variables { get; set; } = new();
    [JsonPropertyName("paths")] public List<string> Paths { get; set; } = new();
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
    /// Created on first run from <c>protection.defaults.json</c> if missing.
    /// Users / admins can edit this file to customize protection without recompiling.
    /// </summary>
    static string BuiltinProtectedVarsFile => Path.Combine(AppDataDirectory, "builtin-protected-vars.json");

    /// <summary>
    /// Path to the externally-editable built-in protected PATH entries list.
    /// Created on first run from <c>protection.defaults.json</c> if missing.
    /// </summary>
    static string BuiltinProtectedPathsFile => Path.Combine(AppDataDirectory, "builtin-protected-paths.json");

    static ProtectionDefaults LoadProtectionDefaults()
    {
        const string resourceName = "EnvManager.protection.defaults.json";
        try
        {
            using var stream = typeof(Program).Assembly.GetManifestResourceStream(resourceName);
            if (stream == null) throw new InvalidDataException("Embedded protection defaults are unavailable");
            var defaults = JsonSerializer.Deserialize<ProtectionDefaults>(stream, JsonOpts);
            if (defaults == null || defaults.Variables.Count == 0 || defaults.Paths.Count == 0)
                throw new InvalidDataException("Embedded protection defaults are invalid");
            return defaults;
        }
        catch (Exception error)
        {
            throw new InvalidOperationException("Cannot load embedded protection defaults", error);
        }
    }

    static List<string> LoadBuiltinProtectedVars()
    {
        var defaults = LoadProtectionDefaults().Variables;
        try
        {
            if (!File.Exists(BuiltinProtectedVarsFile))
                AtomicWriteJson(BuiltinProtectedVarsFile, defaults);
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BuiltinProtectedVarsFile), JsonOpts)
                ?? defaults;
        }
        catch
        {
            return defaults;
        }
    }

    static List<string> LoadBuiltinProtectedPaths()
    {
        var defaults = LoadProtectionDefaults().Paths;
        try
        {
            if (!File.Exists(BuiltinProtectedPathsFile))
                AtomicWriteJson(BuiltinProtectedPathsFile, defaults);
            return JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BuiltinProtectedPathsFile), JsonOpts)
                ?? defaults;
        }
        catch
        {
            return defaults;
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
    // v0.8.0 A3: fsync before rename to match Rust write_atomic.
    using (var fs = File.Create(temp))
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, JsonOptsIndented);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true); // fsync
    }
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
        var paths = ResolveProfilePaths(profile, profiles).Select(path => new { path, expandedPath = Environment.ExpandEnvironmentVariables(path), exists = FastDirectoryExists(Environment.ExpandEnvironmentVariables(path)) }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(new { profile = name, profile.Inherits, variables, pathEntries = paths }, JsonOptsIndented));
        return 0;
    }

    // v0.7.5: DFS to detect if adding parents would close a cycle. Walk
// every requested parent's existing Inherits chain; if any chain leads back
// to the target profile name there is a cycle.
static bool HasInheritanceCycle(string targetName, List<string> requestedParents, List<ProfileData> allProfiles)
{
    var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    var stack = new Stack<string>(requestedParents);
    while (stack.Count > 0)
    {
        var cur = stack.Pop();
        if (cur.Equals(targetName, StringComparison.OrdinalIgnoreCase)) return true;
        if (!visited.Add(cur)) continue;
        var p = allProfiles.FirstOrDefault(x => x.Name.Equals(cur, StringComparison.OrdinalIgnoreCase));
        if (p != null) foreach (var parent in p.Inherits) stack.Push(parent);
    }
    return false;
}

static int ProfileSetInherits(string[] args)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, args[2]);
        if (profile == null) return ArgError("Error: Profile not found");
        // v0.7.5: reject self-inheritance and cycles. A cycle (A inherits B
        // which inherits A) or a self-loop makes ResolveProfileVariables
        // infinite-loop and the profile un-recoverable.
        var requestedParents = args.Skip(3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (requestedParents.Any(p => p.Equals(args[2], StringComparison.OrdinalIgnoreCase)))
            return ArgError("Error: A profile cannot inherit itself");
        if (HasInheritanceCycle(args[2], requestedParents, profiles))
            return ArgError("Error: Inheritance cycle detected. One of the requested parents already inherits (transitively) from '" + args[2] + "'.");
        // v0.7.7 hard boundary: a Global profile MUST NOT inherit from a Launch profile.
        // The Launch profile type carries DPAPI secrets that cannot be put in the user
        // registry as plaintext, and a Launch-targeted apply would never act on them
        // in the right scope. This is the same guard as IsProfileApplicable, applied
        // at set-inherits time so the user sees the rejection immediately instead of
        // only at apply time. We also forbid a Launch profile from inheriting another
        // Launch profile that carries secrets -- the inherited secret has no in-process
        // decrypt path in this profile.
        bool targetIsGlobal = profile.ProfileType.Equals("global", StringComparison.OrdinalIgnoreCase);
        bool targetIsLaunch = profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase);
        if (targetIsGlobal)
        {
            foreach (string parentName in requestedParents)
            {
                var parent = FindProfile(profiles, parentName);
                if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
                    return ArgError("Error: A Global profile cannot inherit from a Launch profile. Launch profiles may carry DPAPI secrets that would leak ciphertext to the user registry if inherited.");
            }
        }
        if (targetIsLaunch)
        {
            foreach (string parentName in requestedParents)
            {
                var parent = FindProfile(profiles, parentName);
                if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase)
                    && parent.SecretVariables.Count > 0)
                    return ArgError("Error: A Launch profile cannot inherit from another Launch profile that already carries secrets. The inherited secret has no in-process decrypt path in this profile's launch target.");
            }
        }
        bool wasEnabled = profile.IsEnabled;
        if (wasEnabled) UnapplyProfile(profile);
        profile.Inherits = args.Skip(3).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        // v0.7.7: if the inheritance chain is somehow already poisoned (e.g. a
        // hand-edited profiles.json that bypassed CLI validation), ResolveProfile*
        // throws InvalidDataException. Wrap so set-inherits itself does not brick.
        try
        {
            ResolveProfileVariables(profile, profiles);
            ResolveProfilePaths(profile, profiles);
        }
        catch (InvalidDataException ex)
        {
            Console.Error.WriteLine("Error: Resolving the new inheritance chain failed: " + ex.Message + " -- the profiles.json file may have a pre-existing inheritance cycle. Aborting set-inherits without persisting.");
            return 1;
        }
        SaveProfiles(profiles);
        if (wasEnabled)
        {
            if (IsProfileApplicable(profile)) { ApplyProfile(profile); profile.AppliedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(); SaveProfiles(profiles); }
            else
            {
                profile.IsEnabled = false;
                SaveProfiles(profiles);
                Console.Error.WriteLine("Warning: Profile '" + profile.Name + "' is no longer applicable after the inheritance change (e.g. it now pulls in a secret variable). It has been disabled; fix the inheritance chain before re-applying.");
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(new { profile = profile.Name, profile.Inherits }, JsonOpts));
        return 0;
    }

    static int ProfileAddPath(string profileName, string path, string scope = "user")
    {
        if (scope != "user" && scope != "system")
            return ArgError("Error: Invalid scope. Must be 'user' or 'system'");
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null) return ArgError("Error: Profile not found");
        if (profile.IsEnabled) return ArgError("Error: Unapply the profile before changing PATH entries");
        ValidatePathFragment(path);
        if (!profile.PathEntries.Any(p => NormalizePathEntry(p).Equals(NormalizePathEntry(path), StringComparison.OrdinalIgnoreCase)))
        {
            profile.PathEntries.Add(path);
            // Track the scope the user chose for this entry. The list is parallel to
            // PathEntries; older profiles.json files without PathScopes are treated
            // as "user" by ProfileApply (index-based lookup with out-of-range guard).
            while (profile.PathScopes.Count < profile.PathEntries.Count - 1) profile.PathScopes.Add("user");
            profile.PathScopes.Add(scope);
        }
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
        int idx = profile.PathEntries.FindIndex(p => NormalizePathEntry(p).Equals(NormalizePathEntry(path), StringComparison.OrdinalIgnoreCase));
        if (idx >= 0)
        {
            profile.PathEntries.RemoveAt(idx);
            // Keep PathScopes in lockstep with PathEntries by index. If PathScopes
            // was shorter (legacy profile), simply drop the matching tail entry.
            if (idx < profile.PathScopes.Count) profile.PathScopes.RemoveAt(idx);
        }
        SaveProfiles(profiles);
        Console.WriteLine("Removed PATH entry from profile: " + profileName);
        return 0;
    }

    static void ValidatePathFragment(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaxLength || path.Contains(';') || path.Any(ch => ch == '\0' || (char.IsControl(ch) && ch != '\t'))) throw new ArgumentException("Invalid PATH entry");
    }
}


// v0.7 DPAPI-CurrentUser helper for encrypting secret variable values held in launch profiles.
// Implemented via P/Invoke on crypt32.dll to avoid any NuGet dependency, keeping the project
// build-compatible across MSVC and MinGW GNU toolchains. The secret value lives only transiently
// in process memory as a managed byte[]; copies are cleared after use to limit exposure.
internal static partial class DpapiHelper
{
    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataBlob);

    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, out string? ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataBlob);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    // Matches System.Security.Cryptography.ProtectedData with DataProtectionScope.CurrentUser
    // when called with no entropy: CryptProtectData writes CurrentUser-scope ciphertext that
    // only the same user + machine can decrypt.
    private const int CryptProtectUiForbidden = 0x01;

    public static string EncryptSecret(string plaintext)
    {
        if (plaintext == null) plaintext = "";
        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var inBlob = new DATA_BLOB();
            inBlob.cbData = plainBytes.Length;
            inBlob.pbData = System.Runtime.InteropServices.Marshal.AllocHGlobal(plainBytes.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(plainBytes, 0, inBlob.pbData, plainBytes.Length);
                if (!CryptProtectData(ref inBlob, "EnvManager.Secret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob))
                {
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    throw new System.ComponentModel.Win32Exception(err, "CryptProtectData failed (Win32 error " + err + ")");
                }
                try
                {
                    byte[] cipher = new byte[outBlob.cbData];
                    System.Runtime.InteropServices.Marshal.Copy(outBlob.pbData, cipher, 0, outBlob.cbData);
                    return Convert.ToBase64String(cipher);
                }
                finally
                {
                    if (outBlob.pbData != IntPtr.Zero) NativeMethods.LocalFree(outBlob.pbData);
                }
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(inBlob.pbData);
            }
        }
        finally
        {
            for (int i = 0; i < plainBytes.Length; i++) plainBytes[i] = 0;
        }
    }

    public static string DecryptSecret(string ciphertextBase64)
    {
        if (string.IsNullOrEmpty(ciphertextBase64)) return "";
        byte[] cipher = Convert.FromBase64String(ciphertextBase64);
        var inBlob = new DATA_BLOB();
        inBlob.cbData = cipher.Length;
        inBlob.pbData = System.Runtime.InteropServices.Marshal.AllocHGlobal(cipher.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(cipher, 0, inBlob.pbData, cipher.Length);
            if (!CryptUnprotectData(ref inBlob, out _, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob))
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err, "CryptUnprotectData failed (Win32 error " + err + ")");
            }
            try
            {
                byte[] plain = new byte[outBlob.cbData];
                System.Runtime.InteropServices.Marshal.Copy(outBlob.pbData, plain, 0, outBlob.cbData);
                try { return System.Text.Encoding.UTF8.GetString(plain); }
                finally { for (int i = 0; i < plain.Length; i++) plain[i] = 0; }
            }
            finally
            {
                if (outBlob.pbData != IntPtr.Zero) NativeMethods.LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(inBlob.pbData);
        }
    }
}

internal static partial class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    internal static extern IntPtr LocalFree(IntPtr hMem);
}
