using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace EnvManager;

/// <summary>
/// Production IEnvironmentScope: a pure move of the registry and P/Invoke mechanics that live in
/// Program.cs today (GetScopeTarget, AppendEnvironmentItems, GetVariableValue, SetVariable,
/// SetVariableWithoutNotify, DeleteVariable, RunToggle core, BroadcastSettingChange). Command-layer
/// concerns (argument validation, IsProtectedVariable, MaxLength, Console output, JSON emission)
/// remain in Program.cs untouched in this ticket; later extraction tickets rewire them onto this seam.
/// </summary>
internal sealed class RegistryScope : IEnvironmentScope
{
    const string SystemEnvPath = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    const string UserEnvPath = "Environment";

    /// <summary>Debug hook the command layer can wire to Program.DebugLog during extraction; mirrors the DebugLog calls in the moved mechanics.</summary>
    internal static Action<string>? DebugSink;

    static (RegistryKey? hive, string path) GetScopeTarget(string scope)
    {
        if (scope == "system")
            return (Registry.LocalMachine, SystemEnvPath);
        return (Registry.CurrentUser, UserEnvPath);
    }

    public IReadOnlyList<EnvVariable> ListVariables(string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        var items = new List<EnvVariable>();
        using (var key = hive?.OpenSubKey(path))
        {
            if (key != null)
                AppendEnvironmentItems(key, scope, items);
        }
        return items;
    }

    // Pure move of Program.AppendEnvironmentItems.
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

    public EnvValueSnapshot? ReadValue(string name, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, false))
        {
            if (key == null) return null;
            object? raw = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            if (raw == null) return null;
            RegistryValueKind kind;
            try
            {
                kind = key.GetValueKind(name);
            }
            catch (IOException)
            {
                return null;
            }
            return new EnvValueSnapshot(raw.ToString() ?? "", kind);
        }
    }

    public bool Exists(string name, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, false))
        {
            if (key == null) return false;
            return key.GetValueNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    public WriteOutcome WriteValue(string name, string? value, string scope)
    {
        value ??= "";
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null) return WriteOutcome.ScopeUnavailable;

            bool existed = key.GetValueNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            object? originalValue = existed
                ? key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                : null;
            RegistryValueKind originalKind = RegistryValueKind.String;
            if (existed)
            {
                try
                {
                    originalKind = key.GetValueKind(name);
                }
                catch (IOException)
                {
                    existed = false;
                    originalValue = null;
                }
            }

            RegistryValueKind writeKind = originalKind;
            if (!existed || value.Contains('%'))
            {
                writeKind = value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String;
            }

            bool RestoreOriginal()
            {
                try
                {
                    if (existed)
                    {
                        key.SetValue(name, originalValue!, originalKind);
                    }
                    else
                    {
                        key.DeleteValue(name, false);
                    }

                    bool restoredExists = key.GetValueNames().Any(n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                    if (restoredExists != existed) return false;
                    if (!existed) return true;

                    object? restoredValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                    return Equals(restoredValue, originalValue) && key.GetValueKind(name) == originalKind;
                }
                catch (Exception restoreError)
                {
                    DebugSink?.Invoke("SetVariable rollback failure=" + restoreError.GetType().Name);
                    return false;
                }
            }

            try
            {
                key.SetValue(name, value, writeKind);
                object? persisted = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                bool verified = persisted is string persistedString &&
                    string.Equals(persistedString, value, StringComparison.Ordinal) &&
                    key.GetValueKind(name) == writeKind;
                if (verified)
                {
                    BroadcastSettingChange();
                    return WriteOutcome.Verified;
                }
            }
            catch (Exception writeError)
            {
                DebugSink?.Invoke("SetVariable write failure=" + writeError.GetType().Name);
            }

            bool rolledBack = RestoreOriginal();
            if (rolledBack)
            {
                BroadcastSettingChange();
                return WriteOutcome.RolledBack;
            }
            return WriteOutcome.RollbackFailed;
        }
    }

    public void WriteValuePreservingKind(string name, string value, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null) return;

            RegistryValueKind kind = RegistryValueKind.String;
            try
            {
                kind = key.GetValueKind(name);
            }
            catch (IOException) { }

            // If value contains %, use ExpandString like Windows does
            if (value.Contains('%'))
            {
                kind = RegistryValueKind.ExpandString;
            }

            key.SetValue(name, value, kind);
        }
    }

    public bool DeleteValue(string name, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null) return false;

            key.DeleteValue(name, false);

            // Also clean up toggle backup key if the variable was disabled.
            // This prevents orphaned _EnvManager_disabled keys from accumulating.
            string toggleBackupName = name + "_EnvManager_disabled";
            if (key.GetValue(toggleBackupName) != null)
            {
                key.DeleteValue(toggleBackupName, false);
            }

            // Also clean up profile backup keys for this variable name.
            // Profile backups use the format: <varName>_PowerToys_<profileName>
            // We scan for _PowerToys_ backup keys that start with the variable name.
            foreach (var valName in key.GetValueNames())
            {
                // Match: <varName>_PowerToys_<anything> (the applied profile backup)
                // Also match: <varName>_EnvManager_disabled (the toggle backup)
                if (valName.StartsWith(name + "_PowerToys_") && !valName.EndsWith("_EnvManager_disabled"))
                {
                    key.DeleteValue(valName, false);
                }
            }
        }

        BroadcastSettingChange();
        return true;
    }

    /// <summary>Raw delete without backup cleanup or broadcast (moved DeleteVariableWithoutNotify mechanics).</summary>
    public void DeleteValueWithoutNotify(string name, string scope)
    {
        var (hive, path) = GetScopeTarget(scope);
        using (var key = hive?.OpenSubKey(path, true))
        {
            if (key == null) return;
            key.DeleteValue(name, false);
        }
    }

    public ToggleResult Toggle(string name, string scope)
    {
        string backupName = name + "_EnvManager_disabled";
        var (hive, registryPath) = GetScopeTarget(scope);
        using var key = hive?.OpenSubKey(registryPath, true);
        if (key == null)
        {
            return ToggleResult.Fail($"Cannot open registry key for scope '{scope}'");
        }

        bool originalExists = key.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase));
        bool backupExists = key.GetValueNames().Any(valueName => valueName.Equals(backupName, StringComparison.OrdinalIgnoreCase));
        if (originalExists && backupExists)
        {
            return ToggleResult.Fail($"Toggle recovery conflict for '{name}'. Both the variable and its disabled backup exist; no values were changed.");
        }
        if (!originalExists && !backupExists)
        {
            return ToggleResult.Fail($"Variable '{name}' not found");
        }

        try
        {
            if (backupExists)
            {
                object backupValue = key.GetValue(backupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                    ?? throw new InvalidDataException("Disabled backup has no value");
                RegistryValueKind backupKind = key.GetValueKind(backupName);
                key.SetValue(name, backupValue, backupKind);
                object? restoredValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                bool restored = Equals(restoredValue, backupValue) && key.GetValueKind(name) == backupKind;
                if (!restored)
                {
                    key.DeleteValue(name, false);
                    return ToggleResult.Fail($"Failed to restore '{name}' exactly; disabled backup was preserved");
                }
                key.DeleteValue(backupName, false);
                if (key.GetValueNames().Any(valueName => valueName.Equals(backupName, StringComparison.OrdinalIgnoreCase)))
                {
                    return ToggleResult.Fail($"Restored '{name}', but could not remove its disabled backup");
                }
                BroadcastSettingChange();
                return ToggleResult.Ok(ToggleOutcome.Restored);
            }

            object originalValue = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames)
                ?? throw new InvalidDataException("Variable has no value");
            RegistryValueKind originalKind = key.GetValueKind(name);
            key.SetValue(backupName, originalValue, originalKind);
            object? persistedBackup = key.GetValue(backupName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
            bool backupVerified = Equals(persistedBackup, originalValue) && key.GetValueKind(backupName) == originalKind;
            if (!backupVerified)
            {
                key.DeleteValue(backupName, false);
                return ToggleResult.Fail($"Failed to preserve '{name}' exactly; variable was not disabled");
            }
            key.DeleteValue(name, false);
            if (key.GetValueNames().Any(valueName => valueName.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                return ToggleResult.Fail($"Failed to disable '{name}'; its backup was preserved for recovery");
            }
            BroadcastSettingChange();
            return ToggleResult.Ok(ToggleOutcome.Disabled);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidDataException)
        {
            DebugSink?.Invoke("Toggle failure=" + error.GetType().Name);
            return ToggleResult.Fail($"Could not toggle '{name}'; no destructive recovery was attempted");
        }
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    static extern bool SendMessageTimeout(
        IntPtr hWnd, uint msg, IntPtr wParam, string lParam,
        uint fuFlags, uint uTimeout, out IntPtr lpdwResult);

    public void BroadcastSettingChange()
    {
        const uint HWND_BROADCAST = 0xFFFF;
        const uint WM_SETTINGCHANGE = 0x001A;
        const uint SMTO_ABORTIFHUNG = 0x0002;
        SendMessageTimeout((IntPtr)HWND_BROADCAST, WM_SETTINGCHANGE, IntPtr.Zero,
            "Environment", SMTO_ABORTIFHUNG, 500, out _);
    }
}
