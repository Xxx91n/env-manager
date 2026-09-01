using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// Backup command domain (architecture-recovery issue 05): backup/restore/diff/merge/validate
/// implementation and the backup file path validator, moved verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
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
        WriteAtomicUtf8(outputPath, json);
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
                if (WriteVariableCore(Engine, IsProtectedVariable, v.Name, v.Value, v.Scope))
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

        WriteAtomicUtf8(outFull, JsonSerializer.Serialize(result, JsonOptsIndented));
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
}
