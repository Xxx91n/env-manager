using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

/// <summary>
/// Variable query/scoping helpers (architecture-recovery issue 05): --scope parsing, registry
/// hive targeting, list/get projection, raw unexpanded reads, and the WM_SETTINGCHANGE
/// broadcast, moved verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
    static string? ParseScope(string[] args, int flagIndex, string? defaultValue)
    {
        if (args.Length > flagIndex && args[flagIndex] == "--scope" && args.Length > flagIndex + 1)
        {
            string s = args[flagIndex + 1];
            if (s != "user" && s != "system")
            {
                Console.Error.WriteLine("Error: scope must be 'user' or 'system'");
                return null;
            }
            return s;
        }
        return defaultValue;
    }

    /// <summary>
    /// Parses optional --scope flag. Returns null if not present (meaning "all scopes").
    /// </summary>
    static string? ParseScopeOptional(string[] args, int flagIndex)
    {
        if (args.Length > flagIndex && args[flagIndex] == "--scope" && args.Length > flagIndex + 1)
        {
            string s = args[flagIndex + 1];
            if (s != "user" && s != "system")
            {
                Console.Error.WriteLine("Error: scope must be 'user' or 'system'");
                return null;
            }
            return s;
        }
        return "all"; // default for restore: all scopes
    }

    static (RegistryKey? hive, string path) GetScopeTarget(string scope)
    {
        if (scope == "system")
            return (Registry.LocalMachine, SystemEnvPath);
        return (Registry.CurrentUser, UserEnvPath);
    }

    static void AppendEnvironmentItems(RegistryKey key, string scope, List<EnvVariable> items)
    {
        var allNames = key.GetValueNames();
        var nameSet = new HashSet<string>(allNames, StringComparer.OrdinalIgnoreCase);
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in allNames)
        {
            // PowerToys keeps separate internal backup values; Env Manager only
            // projects its own disabled backup when the original key is absent.
            if (name.Contains("_PowerToys_", StringComparison.OrdinalIgnoreCase))
                continue;

            if (name.EndsWith("_EnvManager_disabled", StringComparison.OrdinalIgnoreCase))
            {
                string originalName = name[..^"_EnvManager_disabled".Length];
                if (!nameSet.Contains(originalName) && processedNames.Add(originalName))
                {
                    items.Add(new EnvVariable
                    {
                        Name = originalName,
                        Value = key.GetValue(name)?.ToString() ?? "",
                        Scope = scope,
                        IsDisabled = true
                    });
                }
                continue;
            }

            if (processedNames.Add(name))
            {
                items.Add(new EnvVariable
                {
                    Name = name,
                    Value = key.GetValue(name)?.ToString() ?? "",
                    Scope = scope,
                    IsDisabled = false
                });
            }
        }
    }

    static int ListEnvironment()
    {
        DebugLog("ListEnvironment: reading user + system variables");
        var items = new List<EnvVariable>();

        using (var userKey = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (userKey != null)
                AppendEnvironmentItems(userKey, "user", items);
        }

        try
        {
            using (var systemKey = Registry.LocalMachine.OpenSubKey(SystemEnvPath))
            {
                if (systemKey != null)
                    AppendEnvironmentItems(systemKey, "system", items);
            }
        }
        catch (UnauthorizedAccessException) { }

        // Precompute profile values once. The previous item -> profile -> effective
        // variable loop recalculated profile state for every environment variable.
        try
        {
            var profileCandidates = new Dictionary<string, List<(string Source, string Value)>>(StringComparer.OrdinalIgnoreCase);
            foreach (var profile in LoadProfiles().Where(profile => profile.IsEnabled)
                         .OrderByDescending(profile => profile.AppliedAt ?? 0))
            {
                foreach (var variable in GetEffectiveProfileVariables(profile))
                {
                    if (!profileCandidates.TryGetValue(variable.Name, out var candidates))
                    {
                        candidates = new List<(string Source, string Value)>();
                        profileCandidates[variable.Name] = candidates;
                    }
                    candidates.Add((profile.Name, variable.Value));
                }
            }

            foreach (var item in items)
            {
                if (profileCandidates.TryGetValue(item.Name, out var candidates))
                {
                    var source = candidates.FirstOrDefault(candidate => candidate.Value == item.Value);
                    if (!string.IsNullOrEmpty(source.Source))
                        item.ProfileSource = source.Source;
                }
            }
        }
        catch (Exception error)
        {
            DebugLog("ListEnvironment: profile annotation failed: " + error.GetType().Name);
        }

        try
        {
            foreach (var item in items)
            {
                item.IsProtected = IsProtectedVariable(item.Name, item.Scope);
                item.IsBuiltinProtected = IsBuiltinProtectedVar(item.Name) && item.Scope == "system";
            }
        }
        catch (Exception error)
        {
            DebugLog("ListEnvironment: protection annotation failed: " + error.GetType().Name);
        }

        var ordered = items.OrderBy(item => item.Name).ThenBy(item => item.Scope).ToList();
        Console.WriteLine(JsonSerializer.Serialize(ordered, JsonOpts));
        return 0;
    }

    static int GetVariable(string name)
    {
        DebugLog("GetVariable");
        if (IsInternalToggleBackupName(name))
        {
            Console.Error.WriteLine("Error: Internal disabled-variable backup names are not addressable");
            return 1;
        }
        using (var key = Registry.CurrentUser.OpenSubKey(UserEnvPath))
        {
            if (key != null)
            {
                // First check if variable is disabled (backup exists but original deleted)
                string backupName = GetToggleBackupName(name);
                var backupVal = key.GetValue(backupName);
                if (backupVal != null && key.GetValue(name) == null)
                {
                    // Variable is disabled: original deleted, backup exists
                    var result = new { name, value = backupVal.ToString(), scope = "user", isDisabled = true };
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                    return 0;
                }

                // Normal active variable
                var v = key.GetValue(name);
                if (v != null)
                {
                    var result = new { name, value = v.ToString(), scope = "user", isDisabled = false };
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                    return 0;
                }
            }
        }

        try
        {
            using (var key = Registry.LocalMachine.OpenSubKey(SystemEnvPath))
            {
                var v = key?.GetValue(name);
                if (v != null)
                {
                    var result = new { name, value = v.ToString(), scope = "system", isDisabled = false };
                    Console.WriteLine(JsonSerializer.Serialize(result, JsonOpts));
                    return 0;
                }
            }
        }
        catch (UnauthorizedAccessException) { }

        Console.Error.WriteLine($"Not found: {name}");
        return 1;
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    static extern bool SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    static void BroadcastSettingChange()
    {
        const uint HWND_BROADCAST = 0xFFFF;
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
            "Environment", SMTO_ABORTIFHUNG, 500, out _);
    }

    /// Gets a variable value from registry without expanding environment variables.
    /// </summary>
    static string? GetVariableValue(string name, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, false))
        {
            if (key == null) return null;
            var v = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            return v?.ToString();
        }
    }

    /// <summary>
    /// Sets a variable in the registry without broadcasting WM_SETTINGCHANGE.
    /// Used by profile apply/unapply to batch changes efficiently.
    /// </summary>
    static void SetVariableWithoutNotify(string name, string value, string scope)
    {
        // Protect critical system variables even in profile/toggle operations
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot modify protected system variable '{name}'");
            return;
        }

        Engine.WriteValuePreservingKind(name, value, scope);
    }

    /// <summary>
    /// Deletes a variable from the registry without broadcasting WM_SETTINGCHANGE.
    /// </summary>
    static void DeleteVariableWithoutNotify(string name, string scope)
    {
        // Protect critical system variables from deletion even in internal paths
        // (rollback, toggle, profile cleanup). Without this guard, a rollback
        // after a failed bulk import could delete a protected variable whose
        // original value was null.
        if (IsProtectedVariable(name, scope))
        {
            Console.Error.WriteLine($"Error: Cannot delete protected system variable '{name}'");
            return;
        }

        Engine.DeleteValueWithoutNotify(name, scope);
    }
}
