using Microsoft.Win32;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// bulk import/export command domain (architecture-recovery issue 06): members moved
/// verbatim from EnvFeatures.cs. Behavior unchanged.
/// </summary>
class BulkVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
}

partial class Program
{
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
}
