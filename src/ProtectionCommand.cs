using System.Text.Json;
namespace EnvManager;

partial class Program
{
    static int RunProtectionCommand(string[] args)
    {
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";

        if (sub == "list")
        {
            // Output: { protectedVars: { builtIn: [...], custom: [...] },
            //           protectedPaths: { builtIn: [...], custom: [...] } }
            var result = new
            {
                protectedVars = new
                {
                    builtIn = ProtectedSystemVars.OrderBy(x => x).ToList(),
                    custom = CustomProtectedVars,
                },
                protectedPaths = new
                {
                    builtIn = ProtectedPathEntries.ToList(),
                    custom = CustomProtectedPathEntries,
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
            return 0;
        }

        // --- Custom protected PATH entries ---

        if (sub == "add-path" && args.Length > 2)
        {
            string entry = args[2];
            if (string.IsNullOrWhiteSpace(entry) || entry.Contains('\0') || entry.Contains(';') || entry.Contains('\r') || entry.Contains('\n'))
                return ArgError("Error: Invalid PATH entry (must not contain semicolons or control characters)");
            var custom = CustomProtectedPathEntries;
            if (!custom.Any(c => c.TrimEnd('\\', '/').Equals(entry.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
            {
                custom.Add(entry);
                SaveCustomProtectedPathEntries(custom);
            }
            Console.WriteLine($"Added protected PATH entry: {entry}");
            return 0;
        }

        if (sub == "remove-path" && args.Length > 2)
        {
            string entry = args[2];
            // Cannot remove built-in protected entries
            if (ProtectedPathEntries.Any(p => p.TrimEnd('\\', '/').Equals(entry.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                return ArgError("Error: Cannot remove built-in protected PATH entry");

            var custom = CustomProtectedPathEntries;
            int removed = custom.RemoveAll(c => c.TrimEnd('\\', '/').Equals(entry.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveCustomProtectedPathEntries(custom);
                Console.WriteLine($"Removed protected PATH entry: {entry}");
            }
            else
            {
                Console.Error.WriteLine($"Warning: '{entry}' not found in custom protected PATH list");
            }
            return 0;
        }

        // --- Custom protected variables (user-lockable) ---

        if (sub == "add-var" && args.Length > 2)
        {
            string varName = args[2];
            if (string.IsNullOrWhiteSpace(varName) || varName.Length > 255 || varName.Contains('\0') || varName.Contains('=') || varName.Contains('\r') || varName.Contains('\n') || varName.Contains('\t'))
                return ArgError("Error: Invalid variable name (must not contain =, null, or control characters)");

            // Built-in protected variables cannot be added via custom list
            // (they are already protected; adding would conflict with removable logic)
            var custom = CustomProtectedVars;
            if (!custom.Any(v => v.Equals(varName, StringComparison.OrdinalIgnoreCase)))
            {
                custom.Add(varName);
                SaveCustomProtectedVars(custom);
            }
            Console.WriteLine($"Added protected variable: {varName}");
            return 0;
        }

        if (sub == "remove-var" && args.Length > 2)
        {
            string varName = args[2];
            // Cannot remove built-in protected variables
            if (ProtectedSystemVars.Contains(varName))
                return ArgError("Error: Cannot remove built-in protected variable. Only custom protected variables can be unlocked.");

            // Cannot remove built-in protected PATH entries
            if (ProtectedPathEntries.Any(p => p.TrimEnd('\\', '/').Equals(varName.TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase)))
                return ArgError("Error: Cannot remove built-in protected PATH entry");

            var custom = CustomProtectedVars;
            int removed = custom.RemoveAll(v => v.Equals(varName, StringComparison.OrdinalIgnoreCase));
            if (removed > 0)
            {
                SaveCustomProtectedVars(custom);
                Console.WriteLine($"Removed protected variable: {varName}");
            }
            else
            {
                Console.Error.WriteLine($"Warning: '{varName}' not found in custom protected variable list");
            }
            return 0;
        }

        return ArgError("Usage: env-manager protection list | add-path <dir> | remove-path <dir> | add-var <name> | remove-var <name>");
    }
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
            catch (Exception ex) { DebugLog("Warning: corrupt protection config, using defaults: " + ex.GetType().Name); return new(); }
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

}
