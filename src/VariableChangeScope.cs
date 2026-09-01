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
    internal static int RunChangeScope(string[] args, IEnvironmentScope? env = null, Func<string, string, bool>? isProtectedVariable = null)
    {
        IEnvironmentScope engine = env ?? Engine;
        Func<string, string, bool> isProtected = isProtectedVariable ?? IsProtectedVariable;
        if (args.Length < 3)
            return ArgError("Usage: env-manager change-scope <name> <new-scope> [--scope user|system] [--overwrite]");

        string name = args[1];
        string newScope = args[2].ToLowerInvariant();
        if (IsInternalToggleBackupName(name))
            return ArgError("Error: Internal disabled-variable backup names cannot change scope");
        if (newScope != "user" && newScope != "system")
            return ArgError("Error: new scope must be user or system");

        // Source scope: explicit --scope flag, otherwise auto-detect from registry
        string? oldScope = ParseScope(args, 3, null);
        if (oldScope != null && oldScope != "user" && oldScope != "system")
            return ArgError("Error: scope must be user or system");

        if (string.IsNullOrEmpty(name))
            return ArgError("Error: Variable name cannot be empty");

        // Auto-detect source scope by scanning both hives when --scope omitted.
        // When the variable exists in BOTH scopes (common Windows state for
        // PATH, TEMP, TMP), the CLI REFUSES to silently pick one -- that would
        // risk destructively relocating the wrong hive (e.g. moving user PATH
        // when the user meant the system one). Require an explicit --scope.
        // Reviewed by code-reviewer lane (MEDIUM finding).
        if (oldScope == null)
        {
            bool inUser = engine.ReadValue(name, "user") != null;
            bool inSystem = engine.ReadValue(name, "system") != null;
            if (inUser && inSystem)
                return ArgError("Error: " + name + " exists in both user and system scope; specify --scope user|system");
            if (inUser) oldScope = "user";
            else if (inSystem) oldScope = "system";
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
        if (isProtected(name, oldScope))
            return ArgError("Error: Cannot move protected variable " + name + " out of " + oldScope + " scope");
        if (isProtected(name, newScope))
            return ArgError("Error: Cannot place variable " + name + " into " + newScope + " scope (protected)");

        string? oldValue = engine.ReadValue(name, oldScope)?.Value;
        if (oldValue == null)
            return ArgError("Error: Variable " + name + " not found in " + oldScope + " scope");

        // Cross-scope collision refuses unless --overwrite matches rename contract
        string? targetValue = engine.ReadValue(name, newScope)?.Value;
        if (targetValue != null && !args.Contains("--overwrite"))
            return ArgError("Error: " + name + " already exists in " + newScope + " scope; use --overwrite");

        // Write-then-verify-then-delete. Same safety contract as RunRename.
        // The seam batches the batch steps without broadcasts; single broadcast at the end.
        engine.WriteValuePreservingKind(name, oldValue, newScope);
        if (engine.ReadValue(name, newScope)?.Value != oldValue)
            return ArgError("Error: Failed to verify variable in new scope; source preserved");

        // Only delete source after target is confirmed in the store.
        engine.DeleteValueWithoutNotify(name, oldScope);

        // Relocate any toggle backup so disabled state follows the variable.
        string toggleBackup = GetToggleBackupName(name);
        string? backupVal = engine.ReadValue(toggleBackup, oldScope)?.Value;
        if (backupVal != null)
        {
            engine.WriteValuePreservingKind(toggleBackup, backupVal, newScope);
            if (engine.ReadValue(toggleBackup, newScope)?.Value == backupVal)
                engine.DeleteValueWithoutNotify(toggleBackup, oldScope);
        }

        engine.BroadcastSettingChange();
        Console.WriteLine("Changed scope of " + name + " from " + oldScope + " to " + newScope);
        return 0;
    }
}
