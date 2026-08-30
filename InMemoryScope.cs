using Microsoft.Win32;

namespace EnvManager;

/// <summary>
/// Dictionary-backed IEnvironmentScope test double preserving user/system scope semantics: the two
/// scopes are isolated stores, so a user write never affects a system read and vice versa.
/// Broadcasts are counted (BroadcastCount) instead of hitting user32. The mechanics mirror
/// RegistryScope operation-for-operation so the ticket-02 test net can run the same assertions
/// against both implementations.
/// </summary>
internal sealed class InMemoryScope : IEnvironmentScope
{
    // Mirrors RegistryScope.GetScopeTarget: scope "system" -> system store, anything else -> user.
    readonly Dictionary<string, EnvValueSnapshot> _user = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, EnvValueSnapshot> _system = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Number of BroadcastSettingChange calls; asserted by tests instead of WM_SETTINGCHANGE.</summary>
    public int BroadcastCount { get; private set; }

    Dictionary<string, EnvValueSnapshot> Store(string scope) => scope == "system" ? _system : _user;

    public IReadOnlyList<EnvVariable> ListVariables(string scope)
    {
        var store = Store(scope);
        var items = new List<EnvVariable>();
        var nameSet = new HashSet<string>(store.Keys, StringComparer.OrdinalIgnoreCase);
        var processedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string name in store.Keys)
        {
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
                        Value = store[name].Value,
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
                    Value = store[name].Value,
                    Scope = scope,
                    IsDisabled = false
                });
            }
        }
        return items;
    }

    public EnvValueSnapshot? ReadValue(string name, string scope) =>
        Store(scope).TryGetValue(name, out var snapshot) ? snapshot : null;

    public bool Exists(string name, string scope) => Store(scope).ContainsKey(name);

    public WriteOutcome WriteValue(string name, string? value, string scope)
    {
        value ??= "";
        var store = Store(scope);

        bool existed = store.ContainsKey(name);
        EnvValueSnapshot? original = existed ? store[name] : null;
        RegistryValueKind originalKind = original?.Kind ?? RegistryValueKind.String;

        RegistryValueKind writeKind = originalKind;
        if (!existed || value.Contains('%'))
        {
            writeKind = value.Contains('%') ? RegistryValueKind.ExpandString : RegistryValueKind.String;
        }

        void RestoreOriginal()
        {
            if (existed) store[name] = original!;
            else store.Remove(name);
        }

        store[name] = new EnvValueSnapshot(value, writeKind);
        EnvValueSnapshot persisted = store[name];
        bool verified = string.Equals(persisted.Value, value, StringComparison.Ordinal) && persisted.Kind == writeKind;
        if (verified)
        {
            BroadcastSettingChange();
            return WriteOutcome.Verified;
        }

        RestoreOriginal();
        BroadcastSettingChange();
        return WriteOutcome.RolledBack;
    }

    public void WriteValuePreservingKind(string name, string value, string scope)
    {
        var store = Store(scope);
        RegistryValueKind kind = store.TryGetValue(name, out var existing) ? existing.Kind : RegistryValueKind.String;
        if (value.Contains('%'))
        {
            kind = RegistryValueKind.ExpandString;
        }
        store[name] = new EnvValueSnapshot(value, kind);
    }

    public bool DeleteValue(string name, string scope)
    {
        var store = Store(scope);
        store.Remove(name);

        string toggleBackupName = name + "_EnvManager_disabled";
        store.Remove(toggleBackupName);

        // Snapshot the keys first: mirrors GetValueNames returning a point-in-time array.
        string profilePrefix = name + "_PowerToys_";
        foreach (var valName in store.Keys.ToList())
        {
            if (valName.StartsWith(profilePrefix) && !valName.EndsWith("_EnvManager_disabled"))
            {
                store.Remove(valName);
            }
        }

        BroadcastSettingChange();
        return true;
    }

    public ToggleResult Toggle(string name, string scope)
    {
        var store = Store(scope);
        string backupName = name + "_EnvManager_disabled";

        bool originalExists = store.ContainsKey(name);
        bool backupExists = store.ContainsKey(backupName);
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
                EnvValueSnapshot backup = store[backupName];
                store[name] = backup;
                EnvValueSnapshot restoredValue = store[name];
                bool restored = string.Equals(restoredValue.Value, backup.Value, StringComparison.Ordinal) && restoredValue.Kind == backup.Kind;
                if (!restored)
                {
                    store.Remove(name);
                    return ToggleResult.Fail($"Failed to restore '{name}' exactly; disabled backup was preserved");
                }
                store.Remove(backupName);
                if (store.ContainsKey(backupName))
                {
                    return ToggleResult.Fail($"Restored '{name}', but could not remove its disabled backup");
                }
                BroadcastSettingChange();
                return ToggleResult.Ok(ToggleOutcome.Restored);
            }

            EnvValueSnapshot originalValue = store[name];
            store[backupName] = originalValue;
            EnvValueSnapshot persistedBackup = store[backupName];
            bool backupVerified = string.Equals(persistedBackup.Value, originalValue.Value, StringComparison.Ordinal) && persistedBackup.Kind == originalValue.Kind;
            if (!backupVerified)
            {
                store.Remove(backupName);
                return ToggleResult.Fail($"Failed to preserve '{name}' exactly; variable was not disabled");
            }
            store.Remove(name);
            if (store.ContainsKey(name))
            {
                return ToggleResult.Fail($"Failed to disable '{name}'; its backup was preserved for recovery");
            }
            BroadcastSettingChange();
            return ToggleResult.Ok(ToggleOutcome.Disabled);
        }
        catch (Exception error) when (error is UnauthorizedAccessException or IOException or InvalidDataException)
        {
            return ToggleResult.Fail($"Could not toggle '{name}'; no destructive recovery was attempted");
        }
    }

    public void BroadcastSettingChange()
    {
        BroadcastCount++;
    }
}
