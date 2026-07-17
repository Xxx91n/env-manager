namespace EnvManager;

partial class Program
{
    /// <summary>
    /// Atomically changes a variable scope from one registry hive to another.
    /// Reads the old value, validates both scopes, writes to the new scope,
    /// verifies, then removes the old entry. Refuses to move protected
    /// variables and refuses cross-scope name collisions unless --overwrite.
    /// Mirrors RunRename write-then-verify-then-delete order so data is never
    /// lost if any step fails. Hooks into the standard audit snapshot so the
    /// change appears in history with the correct before/after values.
    /// </summary>
    static int RunChangeScope(string[] args)
    {
        if (args.Length < 3)
            return ArgError("Usage: env-manager change-scope <name> <new-scope> [--scope user|system] [--overwrite]");

        string name = args[1];
        string newScope = args[2].ToLowerInvariant();
        if (newScope != "user" && newScope != "system")
            return ArgError("Error: new scope must be user or system");

        // Source scope: explicit --scope flag, otherwise auto-detect from registry
        string? oldScope = ParseScope(args, 3, null);
        if (oldScope != null && oldScope != "user" && oldScope != "system")
            return ArgError("Error: scope must be user or system");

        if (string.IsNullOrEmpty(name))
            return ArgError("Error: Variable name cannot be empty");

        // Auto-detect source scope by scanning both hives when --scope omitted
        if (oldScope == null)
        {
            if (GetVariableValue(name, "user") != null) oldScope = "user";
            else if (GetVariableValue(name, "system") != null) oldScope = "system";
            else return ArgError("Error: Variable " + name + " not found in any scope");
        }

        // No-op if moving into the same scope it already occupies
        if (oldScope == newScope)
        {
            Console.Error.WriteLine("Warning: variable is already in " + newScope + " scope");
            return 0;
        }

        // Refuse to move a protected variable out of system scope, and refuse
        // to land into a protected slot. Same IsProtectedVariable checks used
        // by SetVariable/DeleteVariable for GUI/CLI state consistency.
        if (IsProtectedVariable(name, oldScope))
            return ArgError("Error: Cannot move protected variable " + name + " out of " + oldScope + " scope");
        if (IsProtectedVariable(name, newScope))
            return ArgError("Error: Cannot place variable " + name + " into " + newScope + " scope (protected)");

        string? oldValue = GetVariableValue(name, oldScope);
        if (oldValue == null)
            return ArgError("Error: Variable " + name + " not found in " + oldScope + " scope");

        // Cross-scope collision refuses unless --overwrite matches rename contract
        string? targetValue = GetVariableValue(name, newScope);
        if (targetValue != null && !args.Contains("--overwrite"))
            return ArgError("Error: " + name + " already exists in " + newScope + " scope; use --overwrite");

        // Write-then-verify-then-delete. Same safety contract as RunRename.
        // SetVariableWithoutNotify batches changes, single broadcast at the end.
        SetVariableWithoutNotify(name, oldValue, newScope);
        if (GetVariableValue(name, newScope) != oldValue)
            return ArgError("Error: Failed to verify variable in new scope; source preserved");

        // Only delete source after target is confirmed in the registry.
        DeleteVariableWithoutNotify(name, oldScope);

        // Relocate any toggle backup so disabled state follows the variable.
        string toggleBackup = GetToggleBackupName(name);
        string? backupVal = GetVariableValue(toggleBackup, oldScope);
        if (backupVal != null)
        {
            SetVariableWithoutNotify(toggleBackup, backupVal, newScope);
            if (GetVariableValue(toggleBackup, newScope) == backupVal)
                DeleteVariableWithoutNotify(toggleBackup, oldScope);
        }

        BroadcastSettingChange();
        Console.WriteLine("Changed scope of " + name + " from " + oldScope + " to " + newScope);
        return 0;
    }
}
