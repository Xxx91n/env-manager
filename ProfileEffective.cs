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
            // v0.7.7 hard boundary: a Global profile MUST NOT inherit from a Launch
            // profile. A Launch profile may carry DPAPI secrets that, once pulled
            // through ResolveProfileVariables into the Global chain, would be
            // written to the user registry as DPAPI ciphertext garbage. The prior
            // check only walked profile.SecretVariables (the Global profile's own
            // list); inherited secrets were never seen, so a Global profile that
            // inherited a Launch profile with SecretVariables silently passed apply
            // validation and leaked ciphertext to HKCU\Environment. This block also
            // forbids the Global-inherits-Launch topology outright so a later change
            // to the Launch parent cannot start leaking after the Global is already
            // applied.
            if (profile.ProfileType.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                var all = LoadProfiles();
                foreach (string parentName in profile.Inherits)
                {
                    var parent = FindProfile(all, parentName);
                    if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            var allSecretNames = CollectInheritedSecrets(profile, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            foreach (var variable in ResolveProfileVariables(profile))
            {
                if (string.IsNullOrWhiteSpace(variable.Name) || variable.Name.Length >= 255 ||
                    variable.Name.Contains('=') || ProtectedSystemVars.Contains(variable.Name)) return false;
                if (allSecretNames.Contains(variable.Name)) return false;
            }
            foreach (string path in ResolveProfilePaths(profile)) ValidatePathFragment(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    static HashSet<string> CollectInheritedSecrets(ProfileData profile, HashSet<string> visited)
    {
        if (visited.Contains(profile.Name)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        visited.Add(profile.Name);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sv in profile.SecretVariables)
            result.Add(sv);
        var all = LoadProfiles();
        foreach (string parentName in profile.Inherits)
        {
            var parent = FindProfile(all, parentName);
            if (parent != null)
                foreach (var name in CollectInheritedSecrets(parent, visited))
                    result.Add(name);
        }
        return result;
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
