using Microsoft.Win32;
using Spectre.Console;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

class EnvVariable
{
    [JsonPropertyName("name")] public string Name { get; set; }
    [JsonPropertyName("value")] public string Value { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; }
}

class BackupData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; }
    [JsonPropertyName("version")] public string Version { get; set; }
    [JsonPropertyName("variables")] public List<EnvVariable> Variables { get; set; }
}

class Program
{
    static void Main(string[] args)
    {
        if (args.Length == 0) { ShowHelp(); return; }
        
        switch (args[0])
        {
            case "list": ListEnvironment(); break;
            case "get": if (args.Length < 2) { Console.WriteLine("Usage: env-manager get <name>"); return; } GetVariable(args[1]); break;
            case "set": if (args.Length < 3) { Console.WriteLine("Usage: env-manager set <name> <value> [--scope]"); return; } 
                SetVariable(args[1], args[2], args.Length > 3 && args[3] == "--scope" && args.Length > 4 ? args[4] : "user"); break;
            case "delete": if (args.Length < 2) { Console.WriteLine("Usage: env-manager delete <name> [--scope]"); return; }
                DeleteVariable(args[1], args.Length > 2 && args[2] == "--scope" && args.Length > 3 ? args[3] : "user"); break;
            case "backup": CreateBackup(args.Length > 1 && args[1] == "--output" && args.Length > 2 ? args[2] : "env_backup_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json"); break;
            case "restore": if (args.Length < 2) { Console.WriteLine("Usage: env-manager restore <file> [--scope]"); return; }
                RestoreBackup(args[1], args.Length > 2 && args[2] == "--scope" && args.Length > 3 ? args[3] : null); break;
            case "diff": if (args.Length < 3) { Console.WriteLine("Usage: env-manager diff <old> <new>"); return; } DiffBackups(args[1], args[2]); break;
            case "merge": if (args.Length < 5 || args[3] != "--output") { Console.WriteLine("Usage: env-manager merge <old> <new> --output <file>"); return; }
                MergeBackups(args[1], args[2], args[4]); break;
            case "validate": if (args.Length < 2) { Console.WriteLine("Usage: env-manager validate <file>"); return; } ValidateBackup(args[1]); break;
            case "help": ShowHelp(); break;
            default: Console.WriteLine($"Unknown command: {args[0]}"); ShowHelp(); break;
        }
    }
    
    static void ListEnvironment()
    {
        var items = new List<(string, string, string)>();
        using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
            if (key != null)
                foreach (var name in key.GetValueNames())
                    items.Add((name, "user", key.GetValue(name)?.ToString() ?? ""));
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                if (key != null)
                    foreach (var name in key.GetValueNames())
                        items.Add((name, "system", key.GetValue(name)?.ToString() ?? ""));
        }
        catch (UnauthorizedAccessException) { }
        
        var table = new Table();
        table.AddColumn("Name").AddColumn("Scope").AddColumn("Value");
        foreach (var (n, s, v) in items.OrderBy(x => x.Item1))
            table.AddRow(n, s, v.Length > 60 ? v.Substring(0, 57) + "..." : v);
        AnsiConsole.Write(table);
    }
    
    static void GetVariable(string name)
    {
        using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
        {
            var v = key?.GetValue(name);
            if (v != null) { Console.WriteLine($"{name} = {v}"); return; }
        }
        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
            {
                var v = key?.GetValue(name);
                if (v != null) { Console.WriteLine($"{name} = {v}"); return; }
            }
        }
        catch { }
        Console.WriteLine($"Not found: {name}");
    }
    
    static void SetVariable(string name, string value, string scope)
    {
        if (string.IsNullOrEmpty(name) || name.Length > 32767 || value?.Length > 32767) { Console.WriteLine("Error: Invalid name or value"); return; }
        var path = scope == "system" ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment" : "Environment";
        var hive = scope == "system" ? Registry.LocalMachine : Registry.CurrentUser;
        try
        {
            using (var key = hive.OpenSubKey(path, true))
                if (key != null) { key.SetValue(name, value); Console.WriteLine($"Set {name} = {(value.Length > 50 ? value.Substring(0, 47) + "..." : value)} [{scope}]"); }
        }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("Error: Access denied (requires elevation)"); }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void DeleteVariable(string name, string scope)
    {
        if (string.IsNullOrEmpty(name)) { Console.WriteLine("Error: Invalid name"); return; }
        var path = scope == "system" ? @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment" : "Environment";
        var hive = scope == "system" ? Registry.LocalMachine : Registry.CurrentUser;
        try
        {
            using (var key = hive.OpenSubKey(path, true))
                if (key != null) { key.DeleteValue(name, false); Console.WriteLine($"Deleted {name} [{scope}]"); }
        }
        catch (UnauthorizedAccessException) { Console.Error.WriteLine("Error: Access denied"); }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void CreateBackup(string outputPath)
    {
        try
        {
            var backup = new BackupData { Timestamp = DateTime.Now.ToString("O"), Version = "1.0.0", Variables = new List<EnvVariable>() };
            using (var key = Registry.CurrentUser.OpenSubKey("Environment"))
                if (key != null)
                    foreach (var name in key.GetValueNames())
                        backup.Variables.Add(new EnvVariable { Name = name, Value = key.GetValue(name)?.ToString() ?? "", Scope = "user" });
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Environment"))
                    if (key != null)
                        foreach (var name in key.GetValueNames())
                            backup.Variables.Add(new EnvVariable { Name = name, Value = key.GetValue(name)?.ToString() ?? "", Scope = "system" });
            }
            catch { }
            var json = JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(outputPath, json);
            Console.WriteLine($"Backup created: {outputPath}");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void RestoreBackup(string inputPath, string scope)
    {
        try
        {
            if (!File.Exists(inputPath)) { Console.Error.WriteLine("File not found"); return; }
            var backup = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(inputPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (backup?.Variables == null) { Console.Error.WriteLine("Invalid backup format"); return; }
            int restored = 0;
            foreach (var v in backup.Variables)
                if (scope == null || v.Scope == scope) { SetVariable(v.Name, v.Value, v.Scope); restored++; }
            Console.WriteLine($"Restored {restored} variables");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void DiffBackups(string oldPath, string newPath)
    {
        try
        {
            if (!File.Exists(oldPath) || !File.Exists(newPath)) { Console.Error.WriteLine("File not found"); return; }
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var old = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(oldPath), opts);
            var nu = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(newPath), opts);
            var oldMap = old.Variables.ToDictionary(v => (v.Name, v.Scope), v => v.Value);
            var newMap = nu.Variables.ToDictionary(v => (v.Name, v.Scope), v => v.Value);
            Console.WriteLine("Added:"); foreach (var ((n, s), v) in newMap.Where(x => !oldMap.ContainsKey(x.Key))) Console.WriteLine($"  {n} [{s}]");
            Console.WriteLine("Removed:"); foreach (var ((n, s), _) in oldMap.Where(x => !newMap.ContainsKey(x.Key))) Console.WriteLine($"  {n} [{s}]");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void MergeBackups(string oldPath, string newPath, string outputPath)
    {
        try
        {
            if (!File.Exists(oldPath) || !File.Exists(newPath)) { Console.Error.WriteLine("File not found"); return; }
            var opts = new JsonSerializerOptions { WriteIndented = true, PropertyNameCaseInsensitive = true };
            var old = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(oldPath), opts);
            var nu = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(newPath), opts);
            var merged = new Dictionary<(string, string), EnvVariable>();
            foreach (var v in old.Variables) merged[(v.Name, v.Scope)] = v;
            foreach (var v in nu.Variables) merged[(v.Name, v.Scope)] = v;
            var result = new BackupData { Timestamp = DateTime.Now.ToString("O"), Version = "1.0.0", Variables = merged.Values.ToList() };
            File.WriteAllText(outputPath, JsonSerializer.Serialize(result, opts));
            Console.WriteLine($"Merged: {outputPath}");
        }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void ValidateBackup(string inputPath)
    {
        try
        {
            if (!File.Exists(inputPath)) { Console.Error.WriteLine("File not found"); return; }
            var backup = JsonSerializer.Deserialize<BackupData>(File.ReadAllText(inputPath), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (backup?.Variables == null) { Console.WriteLine("Invalid: Bad format"); return; }
            Console.WriteLine($"Valid: {backup.Variables.Count} variables");
        }
        catch (JsonException) { Console.WriteLine("Invalid: JSON error"); }
        catch (Exception ex) { Console.Error.WriteLine($"Error: {ex.Message}"); }
    }
    
    static void ShowHelp()
    {
        Console.WriteLine(@"Env Manager v0.3.0

Commands:
  list                       List all variables
  get <name>                 Get variable
  set <name> <val> [--scope] Set variable
  delete <name> [--scope]    Delete variable
  backup [--output <file>]   Create backup
  restore <file> [--scope]   Restore backup
  diff <old> <new>           Compare backups
  merge <old> <new> --out    Merge backups
  validate <file>            Validate backup
  help                       Show help");
    }
}
