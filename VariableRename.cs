namespace EnvManager;

partial class Program
{
    internal static int RunRename(string[] args, IEnvironmentScope? env = null, Func<string, string, bool>? isProtectedVariable = null)
    {
        IEnvironmentScope engine = env ?? Engine;
        Func<string, string, bool> isProtected = isProtectedVariable ?? IsProtectedVariable;
        string oldName = args[1];
        string newName = args[2];
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;
        if (IsInternalToggleBackupName(oldName) || IsInternalToggleBackupName(newName))
            return ArgError("Error: Internal disabled-variable backup names cannot be renamed");

        // Refuse to rename a protected variable. Without this entry guard,
        // SetVariableWithoutNotify(newName) could succeed while
        // DeleteVariableWithoutNotify(oldName) is blocked by the internal
        // protected-variable guard, leaving the variable duplicated and the
        // registry in an inconsistent state.
        if (isProtected(oldName, scope))
            return ArgError("Error: Cannot rename protected variable (source protected): " + oldName);
        if (isProtected(newName, scope))
            return ArgError("Error: Cannot rename into a protected slot: " + newName + " is a protected variable");

        ValidateVariableInput(newName, "", scope);
        string? oldValue = engine.ReadValue(oldName, scope)?.Value;
        if (oldValue == null) return ArgError("Error: Source variable not found");
        string? targetValue = engine.ReadValue(newName, scope)?.Value;
        if (targetValue != null && !args.Contains("--overwrite"))
            return ArgError("Error: Target variable already exists; use --overwrite");

        // Write-verify-delete contract (hard boundary): write the target through the seam,
        // verify the exact raw value landed, and only then remove the source. Never
        // delete-then-write. The single broadcast fires only after the source is gone.
        engine.WriteValuePreservingKind(newName, oldValue, scope);
        if (engine.ReadValue(newName, scope)?.Value != oldValue)
            return ArgError("Error: Failed to verify renamed variable; source preserved");
        engine.DeleteValueWithoutNotify(oldName, scope);
        engine.BroadcastSettingChange();
        Console.WriteLine($"Renamed variable '{oldName}' to '{newName}'");
        return 0;
    }
}
