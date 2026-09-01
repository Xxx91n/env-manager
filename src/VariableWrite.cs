using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Write-path command cores for set/delete/toggle and the PATH list helpers
/// (architecture-recovery issue 03). Every core receives the IEnvironmentScope seam and the
/// protection predicates explicitly, so xUnit tests drive them against InMemoryScope with
/// synthetic protection lists while production wiring resolves to RegistryScope and the real
/// protection files. Error messages reproduce the pre-seam CLI output verbatim.
/// </summary>
partial class Program
{
    // Production seam instance behind every write-path command. Tests never touch this
    // field: they construct their own IEnvironmentScope and pass it into the cores.
    static readonly RegistryScope EngineRegistry = new();

    static IEnvironmentScope Engine => EngineRegistry;

    internal static bool WriteVariableCore(IEnvironmentScope env, Func<string, string, bool> isProtectedVariable, string name, string? value, string scope)
    {
        DebugLog("SetVariable scope=" + scope);
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Variable name cannot be empty");
            return false;
        }
        if (IsInternalToggleBackupName(name))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not writable");
            return false;
        }

        // PowerToys: user env var names limited to 255 chars in registry
        int maxNameLength = scope == "user" ? 255 : MaxLength;
        if (name.Length > maxNameLength)
        {
            Console.Error.WriteLine($"Error: Variable name exceeds {maxNameLength} characters");
            return false;
        }

        // Reject names containing '=' (invalid in Windows environment)
        if (name.Contains('='))
        {
            Console.Error.WriteLine("Error: Variable name cannot contain '='");
            return false;
        }

        // Protect critical system variables from being overwritten
        if (isProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot modify protected system variable '{name}'");
            return false;
        }

        value ??= "";
        if (value.Length > MaxLength)
        {
            Console.Error.WriteLine("Error: Value exceeds maximum length");
            return false;
        }

        // The seam owns kind policy ('%' -> ExpandString), write verification, automatic
        // rollback, and the broadcast on the verified path; this switch reproduces the exact
        // stderr branches the pre-seam SetVariable emitted for each terminal state.
        WriteOutcome outcome = env.WriteValue(name, value, scope);
        switch (outcome)
        {
            case WriteOutcome.Verified:
                return true;
            case WriteOutcome.RolledBack:
                Console.Error.WriteLine("Error: Variable write could not be verified; original value restored");
                return false;
            case WriteOutcome.RollbackFailed:
                Console.Error.WriteLine("Error: Variable write could not be verified and automatic rollback failed; restore from a backup before retrying");
                return false;
            default: // ScopeUnavailable
                Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
                return false;
        }
    }

    internal static void DeleteVariableCore(IEnvironmentScope env, Func<string, string, bool> isProtectedVariable, string name, string scope)
    {
        DebugLog("DeleteVariable scope=" + scope);
        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Invalid variable name");
            return;
        }

        // Protect critical system variables from being deleted
        if (isProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot delete protected system variable '{name}'");
            return;
        }

        // The seam owns the delete mechanics including toggle-backup/_PowerToys_ cleanup
        // and the trailing broadcast; false means the scope key could not be opened.
        if (!env.DeleteValue(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot open registry key for scope '{scope}'");
        }
    }

    internal static int ToggleVariableCore(IEnvironmentScope env, Func<string, string, bool> isProtectedVariable, string name, string scope)
    {
        DebugLog($"Toggle scope={scope}");

        if (string.IsNullOrEmpty(name))
        {
            Console.Error.WriteLine("Error: Variable name cannot be empty");
            return 1;
        }
        if (IsInternalToggleBackupName(name))
        {
            Console.Error.WriteLine("Error: Cannot toggle a variable whose name ends with '_EnvManager_disabled'");
            return 1;
        }
        if (isProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot toggle protected variable '{name}'");
            return 1;
        }

        // The seam owns the full disable/restore mechanics; failure strings arrive without
        // the "Error: " prefix and are printed here exactly where RunToggle printed them.
        ToggleResult result = env.Toggle(name, scope);
        if (!result.Success)
        {
            Console.Error.WriteLine("Error: " + result.Error);
            return 1;
        }

        Console.WriteLine(JsonSerializer.Serialize(new { name, scope, isDisabled = result.IsDisabled }, JsonOpts));
        return 0;
    }

    internal static List<string> GetPathEntriesCore(IEnvironmentScope env, string scope)
    {
        string? pathValue = env.ReadValue("PATH", scope)?.Value;
        if (string.IsNullOrEmpty(pathValue))
            return new List<string>();

        return pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    internal static bool SetPathEntriesCore(IEnvironmentScope env, Func<string, string, bool> isProtectedVariable, Func<string, bool> isProtectedPathEntry, List<string> entries, string scope)
    {
        string joined = string.Join(";", entries);
        if (joined.Length > MaxLength)
        {
            Console.Error.WriteLine($"Error: PATH value exceeds maximum length of {MaxLength} characters (current: {joined.Length})");
            return false;
        }

        // Validate: don't allow removing protected PATH entries.
        // Compare current entries vs new entries to find what's being removed.
        var currentEntries = GetPathEntriesCore(env, scope);
        var removed = currentEntries.Where(e => !entries.Any(x => NormalizePathEntry(x).Equals(NormalizePathEntry(e), StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var r in removed)
        {
            if (isProtectedPathEntry(r))
            {
                Console.Error.WriteLine($"Error: Cannot remove protected PATH entry: {r}");
                return false;
            }
        }

        // PATH is intentionally editable. WriteVariableCore verifies the exact raw
        // registry value and restores the previous value if verification fails.
        // PATH callers therefore never report success after an unverified write.
        return WriteVariableCore(env, isProtectedVariable, "PATH", joined, scope);
    }
    internal static int RunSet(string[] args, IEnvironmentScope? env = null, Func<string, string, bool>? isProtectedVariable = null)
    {
        IEnvironmentScope engine = env ?? Engine;
        Func<string, string, bool> isProtected = isProtectedVariable ?? IsProtectedVariable;
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        // SAFETY (hard boundary): reject protected-system-variable writes BEFORE
        // any value comparison -- otherwise the "already exists" overwrite
        // prompt can leak past the protection guard for non-interactive flows.
        if (IsInternalToggleBackupName(args[1]))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not writable");
            return 1;
        }
        if (isProtected(args[1], scope))
        {
            Console.Error.WriteLine($"Error: Cannot modify protected system variable '{args[1]}'");
            return 1;
        }

        string? existing = engine.ReadValue(args[1], scope)?.Value;
        if (existing != null && existing != args[2] && !args.Contains("--overwrite"))
            return ArgError("Error: Variable already exists with a different value; use --overwrite");
        return WriteVariableCore(engine, isProtected, args[1], args[2], scope) ? 0 : 1;
    }

    internal static int RunDelete(string[] args, IEnvironmentScope? env = null, Func<string, string, bool>? isProtectedVariable = null)
    {
        IEnvironmentScope engine = env ?? Engine;
        Func<string, string, bool> isProtected = isProtectedVariable ?? IsProtectedVariable;
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;

        // SAFETY (hard boundary): reject protected-system-variable deletes BEFORE
        // delegating to DeleteVariable, so non-interactive callers see a clear
        // non-zero exit and a "protected" error message rather than silent success.
        if (IsInternalToggleBackupName(args[1]))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not deletable");
            return 1;
        }
        if (isProtected(args[1], scope))
        {
            Console.Error.WriteLine($"Error: Cannot delete protected system variable '{args[1]}'");
            return 1;
        }
        DeleteVariableCore(engine, isProtected, args[1], scope);
        return 0;
    }

    static string GetToggleBackupName(string varName)
    {
        return varName + "_EnvManager_disabled";
    }

    static bool IsInternalToggleBackupName(string name)
    {
        return name.EndsWith("_EnvManager_disabled", StringComparison.OrdinalIgnoreCase);
    }

    internal static int RunToggle(string[] args, IEnvironmentScope? env = null, Func<string, string, bool>? isProtectedVariable = null)
    {
        string name = args[1];
        string? scope = ParseScope(args, 2, "user");
        if (scope == null) return 1;
        return ToggleVariableCore(env ?? Engine, isProtectedVariable ?? IsProtectedVariable, name, scope);
    }


    // --- VariableWrite members (architecture-recovery issue 06, moved verbatim from EnvFeatures.cs) ---

    static void ValidateVariableInput(string name, string value, string scope)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 255 || name.Contains('=') || name.Any(char.IsControl)) throw new ArgumentException("Invalid variable name");
        if (value.Length > MaxLength || value.Contains('\0')) throw new ArgumentException("Invalid variable value");
        if (IsProtectedVariable(name, scope)) throw new UnauthorizedAccessException("Protected system variable");
    }

}
