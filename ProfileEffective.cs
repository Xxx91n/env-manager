namespace EnvManager;

partial class Program
{
    static bool CanUnapplySafely(ProfileData profile, List<ProfileData> profiles)
    {
        long appliedAt = profile.AppliedAt ?? 0;
        var names = GetEffectiveProfileVariables(profile).Select(variable => variable.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return profiles.Where(candidate => candidate.IsEnabled && candidate.Id != profile.Id &&
                (candidate.AppliedAt ?? 0) > appliedAt)
            .All(candidate => !GetEffectiveProfileVariables(candidate).Any(variable => names.Contains(variable.Name)));
    }
    static List<ProfileVariable> GetEffectiveProfileVariables(ProfileData profile)
    {
        var variables = ResolveProfileVariables(profile);
        var pathEntries = ResolveProfilePaths(profile);
        if (pathEntries.Count == 0) return variables;

        var currentPath = GetPathEntries("user");
        foreach (string entry in pathEntries)
        {
            if (!currentPath.Any(item => NormalizePathEntry(item).Equals(NormalizePathEntry(entry), StringComparison.OrdinalIgnoreCase)))
                currentPath.Add(entry);
        }
        variables.RemoveAll(variable => variable.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        variables.Add(new ProfileVariable { Name = "PATH", Value = string.Join(';', currentPath) });
        return variables;
    }

    static bool IsProfileCorrectlyApplied(ProfileData profile) => GetEffectiveProfileVariables(profile).All(variable =>
        GetVariableValue(variable.Name, "user") == variable.Value);

    static bool IsProfileApplicable(ProfileData profile)
    {
        try
        {
            foreach (var variable in ResolveProfileVariables(profile))
            {
                if (string.IsNullOrWhiteSpace(variable.Name) || variable.Name.Length >= 255 ||
                    variable.Name.Contains('=') || ProtectedSystemVars.Contains(variable.Name)) return false;
                // v0.7: secrets are DPAPI-CurrentUser ciphertext. Applying them to the
                // user registry would write ciphertext garbage, violating the 'plaintext
                // never persisted to the registry' hard boundary. Secrets are meaningful
                // only for Launch profiles (env_clear + inject + decrypt-in-process).
                if (profile.SecretVariables.Any(sv => sv.Equals(variable.Name, StringComparison.OrdinalIgnoreCase)))
                    return false;
            }
            foreach (string path in ResolveProfilePaths(profile)) ValidatePathFragment(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    static void ApplyProfile(ProfileData profile)
    {
        foreach (var variable in GetEffectiveProfileVariables(profile))
        {
            string backupName = GetBackupVariableName(variable.Name, profile.Name);
            string? existingValue = GetVariableValue(variable.Name, "user");
            if (existingValue != null && GetVariableValue(backupName, "user") == null)
                SetVariableWithoutNotify(backupName, existingValue, "user");
            SetVariableWithoutNotify(variable.Name, variable.Value, "user");
        }
        BroadcastSettingChange();
    }

    static void UnapplyProfile(ProfileData profile)
    {
        foreach (var variable in GetEffectiveProfileVariables(profile))
        {
            string backupName = GetBackupVariableName(variable.Name, profile.Name);
            DeleteVariableWithoutNotify(variable.Name, "user");
            string? backupValue = GetVariableValue(backupName, "user");
            if (backupValue == null) continue;
            SetVariableWithoutNotify(variable.Name, backupValue, "user");
            DeleteVariableWithoutNotify(backupName, "user");
        }
        BroadcastSettingChange();
    }
}
