using Microsoft.Win32;
using System.Linq;
using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Path command domain (architecture-recovery issue 05): semicolon-separated list variable
/// handling and the path list/add/remove/move/rename/dedupe/health subcommands, moved
/// verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
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
        DebugLog($"PathCommand: subcommand={args.ElementAtOrDefault(1) ?? "none"}; argumentCount={Math.Max(0, args.Length - 2)}");
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
            "rename" => args.Length < 5 ? ArgError("Usage: env-manager path rename <old-name> <new-name> [--scope user|system]") : PathRename(args),
            "dedupe" => PathDedupe(args),
            "health" => PathHealth(args),
            "help" => ShowPathHelp(),
            _ => ArgError($"Unknown path subcommand: {sub}")
        };
    }

    /// <summary>
    /// Removes duplicate PATH entries (case-insensitive), preserving the
    /// first occurrence. Protected PATH entries are never removed even if
    /// they appear duplicated -- the CLI treats protection as an absolute
    /// lock that dedupe cannot bypass (mirrors PathRename/PathRemove).
    /// Supports --dry-run to preview the removal without modifying PATH.
    /// Output is JSON so the GUI can show a precise before/after list.
    /// </summary>

    /// <summary>
    /// PATH health check: detect duplicates AND dead (non-existent) entries.
    /// Protected entries are NEVER reported as duplicates (defense-in-depth: HashSet isolation),
    /// and --fix NEVER removes a protected entry, even if it appears dead.
    /// Output: JSON array of entries with status, deadCount, duplicateCount, healthyCount.
    /// --fix: write a cleaned PATH, preserving order, removing ONLY non-protected duplicates and
    ///       non-protected dead entries. Protected entries are always preserved.
    /// </summary>
    static int PathHealth(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
        bool fix = args.Contains("--fix");
        bool dryRun = args.Contains("--dry-run");

        var entries = GetPathEntries(scope);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<object>();
        var keptClean = new List<string>();
        int deadCount = 0, dupCount = 0, healthyCount = 0;
        foreach (var entry in entries)
        {
            bool isProtected = IsProtectedPathEntry(entry);
            bool isDup = false;
            if (!isProtected)
            {
                isDup = !seen.Add(NormalizePathEntry(entry));
            }
            bool isDead = false;
            string expandedPath = Environment.ExpandEnvironmentVariables(entry);
            string fullPath = StripVerbatimPrefix(Path.IsPathRooted(expandedPath) ? expandedPath : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, expandedPath)));
            try { isDead = !FastDirectoryExists(fullPath); }
            catch { isDead = true; }

            string status = isDup || isDead
                ? (isDup && isDead ? "duplicate+dead" : isDup ? "duplicate" : "dead")
                : "healthy";
            if (isDup) dupCount++;
            if (isDead) deadCount++;
            if (status == "healthy") healthyCount++;

            bool keep = (!isDead || isProtected) && !isDup;
            if (keep) keptClean.Add(entry);

            results.Add(new
            {
                entry,
                status,
                isProtected,
                isDead,
                isDuplicate = isDup,
                fullPath
            });
        }

        if (!fix || dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                dryRun = dryRun || !fix,
                totalEntries = entries.Count,
                healthyCount,
                duplicateCount = dupCount,
                deadCount,
                wouldFix = fix && !dryRun,
                results
            }, JsonOpts));
            return 0;
        }

        if (keptClean.Count == entries.Count)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                dryRun = false,
                totalEntries = entries.Count,
                healthyCount,
                duplicateCount = dupCount,
                deadCount,
                wouldFix = false,
                results
            }, JsonOpts));
            return 0;
        }

        if (!SetPathEntries(keptClean, scope)) return 1;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scope,
            dryRun = false,
            totalEntries = entries.Count,
            cleanedEntries = keptClean.Count,
            removedDuplicate = dupCount,
            removedDead = deadCount,
            healthyCount,
            results
        }, JsonOpts));
        return 0;
    }

    static int PathDedupe(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
        bool dryRun = args.Contains("--dry-run");

        var entries = GetPathEntries(scope);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var removed = new List<string>();
        var kept = new List<string>();
        foreach (var entry in entries)
        {
            bool isProtected = IsProtectedPathEntry(entry);
            if (!isProtected && seen.Contains(entry))
            {
                removed.Add(entry);
                continue;
            }
            // Only non-protected entries populate the dedupe set. This keeps
            // the HashSet as a precise "have we already kept a NON-PROTECTED
            // entry like this?" index, so a future maintainer extending dedupe
            // cannot accidentally drop a protected duplicate by reusing seen
            // without re-checking isProtected. Defense-in-depth: SetPathEntries
            // also independently rejects removing protected entries, so even
            // drift on this side is caught downstream. (code-reviewer MEDIUM)
            if (!isProtected) seen.Add(entry);
            kept.Add(entry);
        }

        if (dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                dryRun = true,
                removedCount = removed.Count,
                keptCount = kept.Count,
                removed,
                kept
            }, JsonOpts));
            return 0;
        }

        if (removed.Count == 0)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                scope,
                removedCount = 0,
                keptCount = kept.Count,
                removed,
                kept
            }, JsonOpts));
            return 0;
        }

        if (!SetPathEntries(kept, scope)) return 1;
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            scope,
            removedCount = removed.Count,
            keptCount = kept.Count,
            removed,
            kept
        }, JsonOpts));
        return 0;
    }



    /// <summary>
    /// Renames a PATH entry: replaces the old directory string with a new one
    /// at the same position. Validates the new name for injection safety.
    /// </summary>
    static int PathRename(string[] args)
    {
        string oldDir = args[2];
        string newDir = args[3];
        string? scope = ParseScope(args, 4, "user");
        if (scope == null) return 1;

        // Validate new directory name (injection prevention)
        if (string.IsNullOrEmpty(newDir))
        {
            Console.Error.WriteLine("Error: New directory path cannot be empty");
            return 1;
        }
        if (newDir.Contains('\0'))
        {
            Console.Error.WriteLine("Error: Invalid characters in new directory path");
            return 1;
        }
        if (newDir.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: New directory path exceeds maximum length");
            return 1;
        }

        var entries = GetPathEntries(scope);
        int index = entries.FindIndex(e => e.Equals(oldDir, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            Console.Error.WriteLine($"Error: '{oldDir}' not found in PATH ({scope})");
            return 1;
        }

        // Check for duplicates (if new name matches an existing entry that isn't the one being renamed)
        bool dupFound = false;
        for (int i = 0; i < entries.Count; i++)
        {
            if (i != index && entries[i].Equals(newDir, StringComparison.OrdinalIgnoreCase))
            {
                dupFound = true;
                break;
            }
        }
        if (dupFound)
        {
            Console.Error.WriteLine($"Error: '{newDir}' already exists in PATH ({scope})");
            return 1;
        }

        entries[index] = newDir;
        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Renamed PATH entry from '{oldDir}' to '{newDir}' ({scope})");
        return 0;
    }

    static int ShowPathHelp()
    {
        Console.WriteLine(@"Path commands (edits PATH as a semicolon-separated list):
  path list [--scope user|system]              List PATH entries (JSON)
  path add <dir> [--scope user|system] [--index N]  Add directory to PATH
  path remove <dir> [--scope user|system]      Remove directory from PATH
  path move-up <index> [--scope user|system]   Move PATH entry up
  path move-down <index> [--scope user|system] Move PATH entry down
  path rename <old> <new> [--scope user|system] Rename a PATH entry
  path dedupe [--scope user|system] [--dry-run]  Remove duplicate PATH entries (preserves first, respects protected)
  path health [--scope user|system] [--fix] [--dry-run]  Detect + optionally remove duplicates and dead (non-existent) PATH entries");
        return 0;
    }

    /// <summary>
    /// Parses the PATH variable for a given scope, returns entries as a list.
    /// </summary>
    static List<string> GetPathEntries(string scope)
    {
        return GetPathEntriesCore(Engine, scope);
    }

    /// <summary>
    /// Writes PATH entries back to the registry for a given scope.
    /// </summary>
    static bool SetPathEntries(List<string> entries, string scope)
    {
        return SetPathEntriesCore(Engine, IsProtectedVariable, IsProtectedPathEntry, entries, scope);
    }

    /// <summary>
    /// Checks directory existence in parallel with a per-entry timeout to avoid
    /// hanging on slow UNC/network/non-existent paths. Mirrors PowerToys PATH
    /// health-check resilience: stale/slow entries resolve to exists=false
    /// instead of blocking the entire PathList response.
    /// </summary>
    static bool FastDirectoryExists(string expandedPath)
    {
        if (string.IsNullOrEmpty(expandedPath)) return false;
        // UNC paths and drive roots that do not resolve quickly are treated as
        // non-existent rather than blocking. 200ms is enough for local NTFS.
        try
        {
            var task = Task.Run(() => Directory.Exists(expandedPath));
            if (task.Wait(200)) return task.Result;
            return false;
        }
        catch
        {
            return false;
        }
    }
    static int PathList(string[] args)
    {
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;

        var entries = GetPathEntries(scope);
        var normalizedCounts = entries.GroupBy(NormalizePathEntry, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        var result = entries.Select((e, i) => new
        {
            index = i,
            path = e,
            expandedPath = Environment.ExpandEnvironmentVariables(e),
            isDuplicate = normalizedCounts.GetValueOrDefault(NormalizePathEntry(e)) > 1,
            exists = FastDirectoryExists(Environment.ExpandEnvironmentVariables(e)),
            isProtected = IsProtectedPathEntry(e),
            isBuiltinProtected = ProtectedPathEntries.Any(p => p.TrimEnd('\\', '/').Equals(e.TrimEnd('\\', '/').Trim(), StringComparison.OrdinalIgnoreCase))
        }).ToList();
        Console.WriteLine(JsonSerializer.Serialize(result, JsonOptsIndented));
        return 0;
    }

    static int PathAdd(string[] args)
    {
        string dir = args[2];
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        // Validate directory path (injection prevention for direct CLI usage)
        if (string.IsNullOrWhiteSpace(dir))
        {
            Console.Error.WriteLine("Error: Directory path cannot be empty");
            return 1;
        }
        if (dir.Contains('\0'))
        {
            Console.Error.WriteLine("Error: Directory path contains invalid characters");
            return 1;
        }
        if (dir.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: Directory path exceeds maximum length");
            return 1;
        }

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

        if (!SetPathEntries(entries, scope)) return 1;
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

        if (!SetPathEntries(entries, scope)) return 1;
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
        if (!SetPathEntries(entries, scope)) return 1;
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
        if (!SetPathEntries(entries, scope)) return 1;
        Console.WriteLine($"Moved PATH entry at index {index} down");
        return 0;
    }

    // --- PathCommand members (architecture-recovery issue 06, moved verbatim from EnvFeatures.cs) ---

    internal static string NormalizePathEntry(string path) => Environment.ExpandEnvironmentVariables(path).Trim().TrimEnd('\\', '/');

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

}
