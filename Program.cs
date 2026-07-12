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
    [JsonPropertyName("variables")] public List<ProfileVariable> Variables { get; set; } = new();
}

class Program
{
    const string SystemEnvPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    const string UserEnvPath = "Environment";
    const int MaxLength = 32767;
    const long MaxBackupFileSize = 50 * 1024 * 1024; // 50 MB safety cap

    static bool DebugMode = false;

    static void DebugLog(string msg)
    {
        if (DebugMode)
            Console.Error.WriteLine($"[debug] {DateTime.Now:HH:mm:ss.fff} {msg}");
    }

    static readonly HashSet<string> ValidCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "get", "set", "delete", "toggle", "backup", "restore", "diff", "merge",
        "validate", "help", "profile", "path", "agents"
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

        DebugLog($"Args: {string.Join(" ", args)}");

        string command = args[0];
        if (!ValidCommands.Contains(command))
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            ShowHelp();
            return 1;
        }

        try
        {
            return command.ToLowerInvariant() switch
            {
                "list" => ListEnvironment(),
                "get" => args.Length < 2 ? ArgError("Usage: env-manager get <name>") : GetVariable(args[1]),
                "set" => args.Length < 3 ? ArgError("Usage: env-manager set <name> <value> [--scope user|system]") : RunSet(args),
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
                "help" => ShowHelp(),
                _ => 1
            };
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
        SetVariable(args[1], args[2], scope);
        return 0;
    }

    static int RunDelete(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
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
    static string? ParseScope(string[] args, int flagIndex, string defaultValue)
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

    static int ListEnvironment()
    {
        DebugLog("ListEnvironment: reading user + system variables");
        var items = new List<EnvVariable>();

        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    // Skip internal backup variables from toggle/profile features
                    if (name.Contains("_EnvManager_disabled") || name.Contains("_PowerToys_"))
                        continue;
                    string backupName = name + "_EnvManager_disabled";
                    bool isDisabled = key.GetValueNames().Contains(backupName);
                    items.Add(new EnvVariable
                    {
                        Name = name,
                        Value = isDisabled ? (key.GetValue(backupName)?.ToString() ?? "") : (key.GetValue(name)?.ToString() ?? ""),
                        Scope = "user",
                        IsDisabled = isDisabled
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
                        items.Add(new EnvVariable
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

        var ordered = items.OrderBy(x => x.Name).ThenBy(x => x.Scope).ToList();
        Console.WriteLine(JsonSerializer.Serialize(ordered, JsonOpts));
        return 0;
    }

    static int GetVariable(string name)
    {
        DebugLog("GetVariable: " + name);
        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (key != null)
            {
                var v = key.GetValue(name);
                if (v != null)
                {
                    string backupName = GetToggleBackupName(name);
                    bool isDisabled = key.GetValueNames().Contains(backupName);
                    string value = isDisabled ? (key.GetValue(backupName)?.ToString() ?? "") : v.ToString();
                    var result = new { name, value, scope = "user", isDisabled };
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

    static void SetVariable(string name, string? value, string scope)
    {
        DebugLog("SetVariable: " + name + " scope=" + scope);
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Variable name cannot be empty");
            return;
        }

        // PowerToys: user env var names limited to 255 chars in registry
        int maxNameLength = scope == "user" ? 255 : MaxLength;
        if (name.Length > maxNameLength)
        {
            Console.Error.WriteLine($"Error: Variable name exceeds {maxNameLength} characters");
            return;
        }

        // Reject names containing '=' (invalid in Windows environment)
        if (name.Contains('='))
        {
            Console.Error.WriteLine("Error: Variable name cannot contain '='");
            return;
        }

        value ??= "";

        if (value.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: Value exceeds maximum length");
            return;
        }

        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive.OpenSubKey(path, true))
        {
            if (key == null)
            {
                Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
                return;
            }

            // Preserve ExpandString kind for variables like PATH.
            // PowerToys: if value contains %, use ExpandString (same as Windows default editor)
            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                kind = key.GetValueKind(name);
            }
            catch (IOException)
            {
                // Variable doesn't exist yet; default to String is correct.
            }

            if (value.Contains('%'))
            {
                kind = RegistryValueKind.ExpandString;
            }

            key.SetValue(name, value, kind);
        }

        BroadcastSettingChange();
    }

    static void DeleteVariable(string name, string scope)
    {
        DebugLog("DeleteVariable: " + name + " scope=" + scope);
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return;
        }

        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive.OpenSubKey(path, true))
        {
            if (key == null)
            {
                Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
                return;
            }

            key.DeleteValue(name, false);
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
            "Environment", SMTO_ABORTIFHUNG, 1000, out _);
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
            "create" => args.Length < 3 ? ArgError("Usage: env-manager profile create <name>") : ProfileCreate(args[2]),
            "delete" => args.Length < 3 ? ArgError("Usage: env-manager profile delete <name>") : ProfileDelete(args[2]),
            "apply" => args.Length < 3 ? ArgError("Usage: env-manager profile apply <name>") : ProfileApply(args[2]),
            "unapply" => args.Length < 3 ? ArgError("Usage: env-manager profile unapply <name>") : ProfileUnapply(args[2]),
            "show" => args.Length < 3 ? ArgError("Usage: env-manager profile show <name>") : ProfileShow(args[2]),
            "add-var" => args.Length < 5 ? ArgError("Usage: env-manager profile add-var <profile> <name> <value>") : ProfileAddVar(args[2], args[3], args[4]),
            "remove-var" => args.Length < 4 ? ArgError("Usage: env-manager profile remove-var <profile> <name>") : ProfileRemoveVar(args[2], args[3]),
            "edit-var" => args.Length < 6 ? ArgError("Usage: env-manager profile edit-var <profile> <old-name> <new-name> <new-value>") : ProfileEditVar(args[2], args[3], args[4], args[5]),
            "status" => args.Length < 3 ? ArgError("Usage: env-manager profile status <name>") : ProfileStatus(args[2]),
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

    static int RunToggle(string[] args)
    {
        string name = args[1];
        string scope = ParseScope(args, 2, "user");
        DebugLog($"Toggle: {name} scope={scope}");

        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Variable name cannot be empty");
            return 1;
        }

        string backupName = GetToggleBackupName(name);
        var currentValue = GetVariableValue(name, scope);
        var backupValue = GetVariableValue(backupName, scope);

        if (backupValue != null)
        {
            // Re-enable: restore original value from backup, then delete backup.
            // Write-first order ensures data is not lost if delete fails.
            SetVariableWithoutNotify(name, backupValue, scope);
            // Verify the restore succeeded before removing backup
            var restoredCheck = GetVariableValue(name, scope);
            if (restoredCheck != null)
            {
                DeleteVariableWithoutNotify(backupName, scope);
                BroadcastSettingChange();
                Console.WriteLine(JsonSerializer.Serialize(new { name, scope, isDisabled = false }, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine($"Error: Failed to restore variable {name}, backup preserved");
                return 1;
            }
        }
        else if (currentValue != null)
        {
            // Disable: write backup first, then delete original.
            // Write-first order ensures data is not lost if delete fails.
            SetVariableWithoutNotify(backupName, currentValue, scope);
            // Verify backup was written before removing original
            var backupCheck = GetVariableValue(backupName, scope);
            if (backupCheck != null)
            {
                DeleteVariableWithoutNotify(name, scope);
                BroadcastSettingChange();
                Console.WriteLine(JsonSerializer.Serialize(new { name, scope, isDisabled = true }, JsonOpts));
            }
            else
            {
                Console.Error.WriteLine($"Error: Failed to create backup for {name}, variable not modified");
                return 1;
            }
        }
        else
        {
            Console.Error.WriteLine($"Error: Variable {name} not found in {scope} scope");
            return 1;
        }
        return 0;
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

        var.Name = newVarName;
        var.Value = newVarValue;
        SaveProfiles(profiles);

        if (profile.IsEnabled)
        {
            SetVariableWithoutNotify(newVarName, newVarValue, "user");
            BroadcastSettingChange();
        }

        Console.WriteLine($"Edited variable '{oldVarName}' -> '{newVarName}' in profile '{profileName}'");
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

    static int ShowProfileHelp()
    {
        Console.WriteLine(@"Profile commands:
  profile list                        List all profiles (JSON)
  profile create <name>               Create a new empty profile
  profile delete <name>               Delete a profile
  profile show <name>                 Show profile details (JSON)
  profile apply <name>                Apply a profile (backs up existing user vars)
  profile unapply <name>              Unapply a profile (restores backed-up user vars)
  profile add-var <profile> <name> <val>        Add a variable to a profile
  profile remove-var <profile> <name>           Remove a variable from a profile
  profile edit-var <profile> <old> <new> <val>  Edit a variable in a profile
  profile status <name>                         Check profile application status");
        return 0;
    }

    static List<ProfileData> LoadProfiles()
    {
        if (!File.Exists(ProfilesFilePath))
            return new List<ProfileData>();

        string json = File.ReadAllText(ProfilesFilePath);
        var profiles = JsonSerializer.Deserialize<List<ProfileData>>(json, JsonOpts);
        return profiles ?? new List<ProfileData>();
    }

    static void SaveProfiles(List<ProfileData> profiles)
    {
        string json = JsonSerializer.Serialize(profiles, JsonOptsIndented);
        File.WriteAllText(ProfilesFilePath, json);
    }

    static ProfileData? FindProfile(List<ProfileData> profiles, string name)
    {
        return profiles.FirstOrDefault(p =>
            p.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    static int ProfileList()
    {
        var profiles = LoadProfiles();
        Console.WriteLine(JsonSerializer.Serialize(profiles, JsonOptsIndented));
        return 0;
    }

    static int ProfileCreate(string name)
    {
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
            Variables = new List<ProfileVariable>()
        };
        profiles.Add(profile);
        SaveProfiles(profiles);
        Console.WriteLine($"Created profile: {name}");
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

        profiles.Remove(profile);
        SaveProfiles(profiles);
        Console.WriteLine($"Deleted profile: {name}");
        return 0;
    }

    static int ProfileShow(string name)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, name);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{name}' not found");
            return 1;
        }
        Console.WriteLine(JsonSerializer.Serialize(profile, JsonOptsIndented));
        return 0;
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
            Console.Error.WriteLine($"Error: Profile '{name}' contains invalid variables and cannot be applied");
            return 1;
        }

        // Unapply any currently enabled profile first
        foreach (var p in profiles)
        {
            if (p.IsEnabled && !p.Id.Equals(profile.Id))
            {
                UnapplyProfile(p);
                p.IsEnabled = false;
            }
        }

        ApplyProfile(profile);
        profile.IsEnabled = true;
        SaveProfiles(profiles);
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

        UnapplyProfile(profile);
        profile.IsEnabled = false;
        SaveProfiles(profiles);
        Console.WriteLine($"Unapplied profile: {name}");
        return 0;
    }

    static int ProfileAddVar(string profileName, string varName, string varValue)
    {
        var profiles = LoadProfiles();
        var profile = FindProfile(profiles, profileName);
        if (profile == null)
        {
            Console.Error.WriteLine($"Error: Profile '{profileName}' not found");
            return 1;
        }

        profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        profile.Variables.Add(new ProfileVariable { Name = varName, Value = varValue });
        SaveProfiles(profiles);

        // If profile is currently applied, propagate the change to the registry
        if (profile.IsEnabled)
        {
            SetVariableWithoutNotify(varName, varValue, "user");
            BroadcastSettingChange();
        }

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

        int removed = profile.Variables.RemoveAll(v => v.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
        if (removed == 0)
        {
            Console.Error.WriteLine($"Warning: Variable '{varName}' not found in profile '{profileName}'");
            return 0;
        }

        SaveProfiles(profiles);

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
        using (var key = hive.OpenSubKey(path, false))
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
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive.OpenSubKey(path, true))
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
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive.OpenSubKey(path, true))
        {
            if (key == null) return;
            key.DeleteValue(name, false);
        }
    }

    /// <summary>
    /// Applies a profile: for each variable, backs up the existing user variable
    /// (if it exists) by renaming it to name_PowerToys_profileName, then sets
    /// the profile variable value. Finally broadcasts the setting change.
    /// </summary>
    /// <summary>
    /// Checks if a profile's variables are all correctly applied in the registry.
    /// Mirrors PowerToys' IsCorrectlyApplied().
    /// </summary>
    static bool IsProfileCorrectlyApplied(ProfileData profile)
    {
        foreach (var var in profile.Variables)
        {
            var applied = GetVariableValue(var.Name, "user");
            if (applied == null || applied != var.Value)
                return false;
        }
        return true;
    }

    /// <summary>
    /// Validates that all variables in a profile can be applied.
    /// Mirrors PowerToys' IsApplicable().
    /// </summary>
    static bool IsProfileApplicable(ProfileData profile)
    {
        foreach (var var in profile.Variables)
        {
            if (string.IsNullOrWhiteSpace(var.Name) || var.Name.Length >= 255)
                return false;

            if (var.Name.Contains('='))
                return false;
        }
        return true;
    }

    static void ApplyProfile(ProfileData profile)
    {
        DebugLog("ApplyProfile: " + profile.Name);
        foreach (var var in profile.Variables)
        {
            string backupName = GetBackupVariableName(var.Name, profile.Name);

            // Back up existing user variable if it exists and no backup exists yet
            var existingValue = GetVariableValue(var.Name, "user");
            if (existingValue != null)
            {
                var existingBackup = GetVariableValue(backupName, "user");
                if (existingBackup == null)
                {
                    SetVariableWithoutNotify(backupName, existingValue, "user");
                }
            }

            SetVariableWithoutNotify(var.Name, var.Value, "user");
        }

        BroadcastSettingChange();
    }

    /// <summary>
    /// Unapplies a profile: for each variable, deletes the profile variable
    /// and restores the backup if it exists.
    /// </summary>
    static void UnapplyProfile(ProfileData profile)
    {
        DebugLog("UnapplyProfile: " + profile.Name);
        foreach (var var in profile.Variables)
        {
            string backupName = GetBackupVariableName(var.Name, profile.Name);

            DeleteVariableWithoutNotify(var.Name, "user");

            var backupValue = GetVariableValue(backupName, "user");
            if (backupValue != null)
            {
                SetVariableWithoutNotify(var.Name, backupValue, "user");
                DeleteVariableWithoutNotify(backupName, "user");
            }
        }

        BroadcastSettingChange();
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
        DebugLog("PathCommand: " + string.Join(" ", args.Skip(1)));
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
            "help" => ShowPathHelp(),
            _ => ArgError($"Unknown path subcommand: {sub}")
        };
    }

    static int ShowPathHelp()
    {
        Console.WriteLine(@"Path commands (edits PATH as a semicolon-separated list):
  path list [--scope user|system]              List PATH entries (JSON)
  path add <dir> [--scope user|system] [--index N]  Add directory to PATH
  path remove <dir> [--scope user|system]      Remove directory from PATH
  path move-up <index> [--scope user|system]   Move PATH entry up
  path move-down <index> [--scope user|system] Move PATH entry down");
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
    static void SetPathEntries(List<string> entries, string scope)
    {
        string joined = string.Join(";", entries);
        SetVariable("PATH", joined, scope);
    }

    static int PathList(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;

        var entries = GetPathEntries(scope);
        var result = entries.Select((e, i) => new { index = i, path = e }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
        return 0;
    }

    static int PathAdd(string[] args)
    {
        string dir = args[2];
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

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

        SetPathEntries(entries, scope);
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

        SetPathEntries(entries, scope);
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
        SetPathEntries(entries, scope);
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
        SetPathEntries(entries, scope);
        Console.WriteLine($"Moved PATH entry at index {index} down");
        return 0;
    }

    static void CreateBackup(string outputPath)
    {
        DebugLog("CreateBackup: " + outputPath);
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
        DebugLog("RestoreBackup: " + inputPath);
        string fullPath = ValidateFilePath(inputPath, mustExist: true);

        var backup = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(fullPath), JsonOpts);
        if (backup?.Variables == null)
        {
            Console.Error.WriteLine("Error: Invalid backup format");
            return;
        }

        int restored = 0;
        foreach (var v in backup.Variables)
        {
            if (scope == "all" || v.Scope == scope)
            {
                if (v.Scope != "user" && v.Scope != "system")
                {
                    Console.Error.WriteLine($"Skipping '{v.Name}': invalid scope '{v.Scope}'");
                    continue;
                }
                SetVariable(v.Name, v.Value, v.Scope);
                restored++;
            }
        }
        Console.WriteLine($"Restored {restored} variables");
    }

    static int DiffBackups(string oldPath, string newPath)
    {
        string oldFull = ValidateFilePath(oldPath, mustExist: true);
        string newFull = ValidateFilePath(newPath, mustExist: true);

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

        // Resolve AGENTS.md path: adjacent to the CLI executable, then fallback to AppContext.BaseDirectory
        string agentsPath = "";
        try
        {
            string exeDir = System.AppContext.BaseDirectory;
            agentsPath = Path.Combine(exeDir, "AGENTS.cli.md");
            if (!File.Exists(agentsPath))
            {
                // Try "AGENTS.md" as alternate name
                agentsPath = Path.Combine(exeDir, "AGENTS.md");
            }
        }
        catch { }

        if (pathOnly)
        {
            Console.WriteLine(agentsPath);
            return 0;
        }

        if (File.Exists(agentsPath))
        {
            Console.WriteLine(File.ReadAllText(agentsPath));
        }
        else
        {
            // Fallback: output minimal inline guide
            Console.WriteLine("# Env Manager CLI\n\nCommands: list, get, set, delete, toggle, backup, restore, diff, merge, validate, profile, path, agents, help\n\nUse --debug for verbose logging. Use --scope user|system for scope control.");
        }
        return 0;
    }

    static int ShowHelp()
    {
        Console.WriteLine(@"Env Manager v0.5.0

Commands:
  list                       List all variables (JSON)
  get <name>                 Get variable (JSON)
  set <name> <val> [--scope user|system] Set variable
  delete <name> [--scope user|system]    Delete variable
  toggle <name> [--scope user|system]    Enable/disable a variable (backs up value)
  backup [--output <file>]   Create backup
  restore <file> [--scope user|system]   Restore backup
  diff <old> <new>           Compare backups (JSON)
  merge <old> <new> --output <file>      Merge backups
  validate <file>            Validate backup
  profile <subcommand>       Manage variable profiles (see: profile help)
  path <subcommand>          Edit PATH variable as list (see: path help)
  agents [--path]            Output AGENTS.md (CLI spec for AI agents), --path for file path only
  help                       Show help
  --debug                    Enable verbose stderr logging");
        return 0;
    }
}
