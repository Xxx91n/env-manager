namespace EnvManager;

partial class Program
{
    static int RunRename(string[] args)
    {
        string oldName = args[1];
        string newName = args[2];
        string? scope = ParseScope(args, 3, "user");
        if (scope == null) return 1;

        // Refuse to rename a protected variable. Without this entry guard,
        // SetVariableWithoutNotify(newName) could succeed while
        // DeleteVariableWithoutNotify(oldName) is blocked by the internal
        // protected-variable guard, leaving the variable duplicated and the
        // registry in an inconsistent state.
        if (IsProtectedVariable(oldName, scope))
            return ArgError("Error: Cannot rename protected variable (source protected): " + oldName);
        if (IsProtectedVariable(newName, scope))
            return ArgError("Error: Cannot rename into a protected slot: " + newName + " is a protected variable");

        ValidateVariableInput(newName, "", scope);
        string? oldValue = GetVariableValue(oldName, scope);
        if (oldValue == null) return ArgError("Error: Source variable not found");
        string? targetValue = GetVariableValue(newName, scope);
        if (targetValue != null && !args.Contains("--overwrite"))
            return ArgError("Error: Target variable already exists; use --overwrite");

        SetVariableWithoutNotify(newName, oldValue, scope);
        if (GetVariableValue(newName, scope) != oldValue)
            return ArgError("Error: Failed to verify renamed variable; source preserved");
        DeleteVariableWithoutNotify(oldName, scope);
        BroadcastSettingChange();
        Console.WriteLine($"Renamed variable '{oldName}' to '{newName}'");
        return 0;
    }
}
