using Microsoft.Win32;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

class EnvVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
}

class BackupData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("variables")] public List<EnvVariable> Variables { get; set; } = new();
}

class Program
{
    const string SystemEnvPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    const string UserEnvPath = "Environment";
    const int MaxLength = 32767;
    const long MaxBackupFileSize = 50 * 1024 * 1024; // 50 MB safety cap

    static readonly HashSet<string> ValidCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "list", "get", "set", "delete", "backup", "restore", "diff", "merge", "validate", "help"
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
        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

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
                "backup" => RunBackup(args),
                "restore" => args.Length < 2 ? ArgError("Usage: env-manager restore <file> [--scope user|system]") : RunRestore(args),
                "diff" => args.Length < 3 ? ArgError("Usage: env-manager diff <old> <new>") : DiffBackups(args[1], args[2]),
                "merge" => args.Length < 5 || args[3] != "--output" ? ArgError("Usage: env-manager merge <old> <new> --output <file>") : MergeBackups(args[1], args[2], args[4]),
                "validate" => args.Length < 2 ? ArgError("Usage: env-manager validate <file>") : ValidateBackup(args[1]),
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
        var items = new List<EnvVariable>();

        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (key != null)
            {
                foreach (var name in key.GetValueNames())
                {
                    items.Add(new EnvVariable
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
        // Output as JSON for reliable parsing by the GUI.
        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            var v = key?.GetValue(name);
            if (v != null)
            {
                var result = new { name, value = v.ToString(), scope = "user" };
                Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                return 0;
            }
        }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(SystemEnvPath))
            {
                var v = key?.GetValue(name);
                if (v != null)
                {
                    var result = new { name, value = v.ToString(), scope = "system" };
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
        if (string.IsNullOrEmpty(name) || name.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: Invalid variable name");
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
            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                kind = key.GetValueKind(name);
            }
            catch (IOException)
            {
                // Variable doesn't exist yet; default to String is correct.
            }

            key.SetValue(name, value, kind);
        }

        // Broadcast WM_SETTINGCHANGE so new processes pick up the change.
        BroadcastSettingChange();
    }

    static void DeleteVariable(string name, string scope)
    {
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

    static void CreateBackup(string outputPath)
    {
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

    static int ShowHelp()
    {
        Console.WriteLine(@"Env Manager v0.3.0

Commands:
  list                       List all variables (JSON)
  get <name>                 Get variable (JSON)
  set <name> <val> [--scope user|system] Set variable
  delete <name> [--scope user|system]    Delete variable
  backup [--output <file>]   Create backup
  restore <file> [--scope user|system]   Restore backup
  diff <old> <new>           Compare backups (JSON)
  merge <old> <new> --output <file>      Merge backups
  validate <file>            Validate backup
  help                       Show help");
        return 0;
    }
}
