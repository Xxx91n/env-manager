using System.Text.Json;
namespace EnvManager;

partial class Program
{
    static int RunProtectionCommand(string[] args)
    {
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "list";

        if (sub == "list")
        {
            // Output: { protectedVars: ["PATHEXT", ...], protectedPaths: { builtIn: [...], custom: [...] } }
            var builtIn = ProtectedPathEntries.ToList();
            var custom = CustomProtectedPathEntries;
            var result = new
            {
                protectedVars = ProtectedSystemVars.OrderBy(x => x).ToList(),
                protectedPaths = new
                {
                    builtIn = builtIn,
                    custom = custom,
                },
            };
            Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
            return 0;
        }

        if (sub == "add-path" && args.Length > 2)
        {
            string entry = args[2];
            if (string.IsNullOrWhiteSpace(entry) || entry.Contains('\0'))
                return ArgError("Error: Invalid PATH entry");
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

        if (sub == "remove-var" && args.Length > 2)
        {
            // This only removes from the *displayed* protection list.
            // Built-in ProtectedSystemVars is hardcoded and cannot be removed.
            return ArgError("Error: Built-in protected variables cannot be removed. Only custom protected PATH entries can be managed.");
        }

        return ArgError("Usage: env-manager protection list | protection add-path <dir> | protection remove-path <dir>");
    }
}
