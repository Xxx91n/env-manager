using Microsoft.Win32;
using System.Linq;
using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Agents command domain (architecture-recovery issue 05): the `agents` CLI spec emitter, moved verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
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
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            string version = $"{ver.Major}.{ver.Minor}.{ver.Build}";
            Console.WriteLine($"env-manager-cli v{version} | Commands: list,get,set,delete,toggle,backup,restore,diff,merge,validate,profile,path,agents,help | Scopes: user,system | Safe: no-credentials,injection-protected,write-serialized | Agents: env-manager-cli agents --json for full spec");
            return 0;
        }

        // --json: structured machine-readable spec for AI agent integration
        if (jsonOutput)
        {
            var specVer = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            var spec = new
            {
                name = "env-manager-cli",
                version = $"{specVer.Major}.{specVer.Minor}.{specVer.Build}",
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
                    new { cmd = "rename", desc = "Atomically rename variable (verify before delete)", args = "<old> <new>", scope = true, @async = false },
                    new { cmd = "change-scope", desc = "Move variable user<->system atomically", args = "<name> <new-scope>", scope = true, @async = false },
                    new { cmd = "history", desc = "Audit log + guarded undo", args = "list|undo|delete|clear", scope = false, @async = true },
                    new { cmd = "bulk", desc = "Import/export JSON/.env/CSV with --dry-run", args = "import|export <file>", scope = true, @async = false },
                    new { cmd = "expand", desc = "Resolve nested %VAR% references", args = "<value>", scope = false, @async = true },
                    new { cmd = "protection", desc = "List/add/remove protected vars and PATH entries", args = "list|add-var|remove-var|add-path|remove-path", scope = false, @async = true },
                    new { cmd = "update", desc = "Check GitHub releases for newer version", args = "check", scope = false, @async = true },
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
}
