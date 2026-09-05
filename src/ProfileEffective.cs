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

        variables.RemoveAll(variable => variable.Name.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        var allPairs = ResolveProfilePathsWithScopes(profile);
        foreach (var scopeGroup in allPairs.GroupBy(p => p.scope ?? "user"))
        {
            string scope = scopeGroup.Key;
            var currentPath = GetPathEntries(scope);
            foreach (string entry in scopeGroup.Select(p => p.path))
            {
                if (!currentPath.Any(item => NormalizePathEntry(item).Equals(NormalizePathEntry(entry), StringComparison.OrdinalIgnoreCase)))
                    currentPath.Add(entry);
            }
            variables.Add(new ProfileVariable { Name = "PATH", Value = string.Join(';', currentPath), Scope = scope });
        }
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

    /// <summary>
    /// Ticket 04 seam extraction: profile apply/launch pre-flight validation core.
    /// Runs every entry-point invariant that gates ProfileApply and ProfileLaunch against
    /// an explicitly supplied profile list (hermetic: no LoadProfiles side effects), so
    /// xUnit tests drive it without the registry or the real profiles.json.
    /// Order matters: the topology guard (Global inheriting Launch) fires first, then
    /// the inherited-secret union, then per-variable name checks, then PATH fragments.
    /// The v0.7.7 inherited-secret tests (ProfileSeamValidationTests) fail against any
    /// implementation that only walks profile.SecretVariables - keep them green.
    /// </summary>
    internal static bool RunProfilePreflight(ProfileData profile, List<ProfileData> allProfiles)
    {
        try
        {
            if (profile.ProfileType.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string parentName in profile.Inherits)
                {
                    var parent = FindProfile(allProfiles, parentName);
                    if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
                        return false;
                }
            }
            var allSecretNames = CollectInheritedSecretsFrom(profile, allProfiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            // T04: an inherited secret (present in the chain union but not declared on this
            // profile itself) has no decrypt path here - reject, mirroring the v0.7.7
            // ProfileSetInherits rejections that a hand-edited profiles.json can bypass.
            var ownSecrets = new HashSet<string>(profile.SecretVariables, StringComparer.OrdinalIgnoreCase);
            if (allSecretNames.Any(name => !ownSecrets.Contains(name))) return false;
            foreach (var variable in ResolveProfileVariables(profile, allProfiles))
            {
                if (string.IsNullOrWhiteSpace(variable.Name) || variable.Name.Length >= 255 ||
                    variable.Name.Contains('=') || ProtectedSystemVars.Contains(variable.Name)) return false;
                if (allSecretNames.Contains(variable.Name)) return false;
            }
            foreach (string path in ResolveProfilePaths(profile, allProfiles)) ValidatePathFragment(path);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>Production adapter: pre-flight against the live profiles.json store.</summary>

    /// <summary>
    /// Ticket 19: two-tier pre-flight validation result. Errors = data-destroying/
    /// half-write conditions (keep hard-blocking); warnings = suspicious-but-safe
    /// conditions (downgraded per spec Phase 4 / MongoDB validationAction pattern).
    /// The structured warning list is the telemetry basis for future tightening:
    /// once warnings prove rare in practice, promote one to the error tier with a
    /// --strict escape hatch retained.
    /// </summary>
    internal sealed class PreflightResult
    {
        internal List<string> Errors = new();
        internal List<string> Warnings = new();
        internal bool HasErrors => Errors.Count > 0;
        internal bool HasWarnings => Warnings.Count > 0;
    }

    /// <summary>
    /// Ticket 19: warn-tier checks. Suspicious but safe to execute:
    /// (1) variable values containing undefined %VAR% references (registry EXPAND
    /// semantics simply leave them literal at read time - no data loss),
    /// (2) PATH entries whose expanded target does not exist today (stale entry),
    /// (3) launch profiles whose targetExecutable file is missing (dangling target).
    /// Each check is self-contained so the error tier stays the sole gate of the
    /// data-destroying conditions pinned by the v0.7.7 / T04 hard boundaries.
    /// </summary>
    internal static void CollectPreflightWarnings(ProfileData profile, List<ProfileData> allProfiles, PreflightResult result)
    {
        foreach (var variable in ResolveProfileVariables(profile, allProfiles))
        {
            if (string.IsNullOrEmpty(variable.Value)) continue;
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(variable.Value, "%([^%]+)%"))
            {
                string refName = m.Groups[1].Value;
                if (refName.Length == 0) continue;
                // Ticket 19 fix2: the expansion-resolvability surface includes the CLI
                // process environment (it merges system+user at spawn time), not just the
                // two registry hives - %SystemRoot% is kernel-provided and absent from both
                // hives, so the registry-only check misreported defined vars as undefined
                // (CI run 33953937157 red). Semantics: resolvable expansion => no warning.
                bool defined = Environment.GetEnvironmentVariable(refName) != null
                    || GetVariableValue(refName, "user") != null || GetVariableValue(refName, "system") != null
                    || ResolveProfileVariables(profile, allProfiles).Any(v => v.Name.Equals(refName, StringComparison.OrdinalIgnoreCase));
                if (!defined)
                    result.Warnings.Add($"Variable '{variable.Name}' references undefined %VAR%: %{refName}% (expands literally)");
            }
        }
        foreach (string path in ResolveProfilePaths(profile, allProfiles))
        {
            string expanded = Environment.ExpandEnvironmentVariables(path);
            if (!FastDirectoryExists(expanded))
                result.Warnings.Add($"PATH entry does not exist: {path} (stale entry)");
        }
        if (profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(profile.TargetExecutable))
        {
            string full = Path.IsPathRooted(profile.TargetExecutable)
                ? profile.TargetExecutable
                : Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, profile.TargetExecutable));
            if (!File.Exists(full))
                result.Warnings.Add($"Launch target does not exist: {profile.TargetExecutable} (dangling launch target)");
        }
    }

    /// <summary>
    /// Ticket 19: the full two-tier pre-flight core. Runs the same error-tier invariants
    /// as <see cref="RunProfilePreflight"/> (identical order, identical messages) and then
    /// the warn tier. Never throws for warn-tier findings; InvalidDataException from the
    /// resolution walk still lands in Errors (cycle / missing parent = data-integrity).
    /// </summary>
    internal static PreflightResult RunProfilePreflightDetailed(ProfileData profile, List<ProfileData> allProfiles, bool strict)
    {
        var result = new PreflightResult();
        // Error tier: verbatim port of the RunProfilePreflight body, message-shaped.
        try
        {
            if (profile.ProfileType.Equals("global", StringComparison.OrdinalIgnoreCase))
            {
                foreach (string parentName in profile.Inherits)
                {
                    var parent = FindProfile(allProfiles, parentName);
                    if (parent != null && parent.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase))
                        result.Errors.Add($"Global profile '{profile.Name}' cannot inherit from a Launch profile (secret ciphertext leak)");
                }
            }
            var allSecretNames = CollectInheritedSecretsFrom(profile, allProfiles, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var ownSecrets = new HashSet<string>(profile.SecretVariables, StringComparer.OrdinalIgnoreCase);
            if (allSecretNames.Any(name => !ownSecrets.Contains(name)))
                result.Errors.Add($"Profile '{profile.Name}' resolves inherited secret variables that it does not declare (no decrypt path)");
            foreach (var variable in ResolveProfileVariables(profile, allProfiles))
            {
                if (string.IsNullOrWhiteSpace(variable.Name) || variable.Name.Length >= 255)
                    result.Errors.Add($"Variable name is empty or exceeds 255 characters: '{variable.Name}'");
                if (variable.Name.Contains('='))
                    result.Errors.Add($"Variable name contains '=': '{variable.Name}'");
                if (ProtectedSystemVars.Contains(variable.Name))
                    result.Errors.Add($"Variable '{variable.Name}' is protected and cannot be stored in profiles");
                if (allSecretNames.Contains(variable.Name))
                    result.Errors.Add($"Variable '{variable.Name}' collides with an inherited secret name");
            }
            foreach (string path in ResolveProfilePaths(profile, allProfiles))
            {
                try { ValidatePathFragment(path); }
                catch (ArgumentException) { result.Errors.Add($"Invalid PATH entry: '{path}'"); }
            }
        }
        catch (InvalidDataException ex)
        {
            result.Errors.Add(ex.Message);
        }
        if (result.HasErrors) return result;
        // Warn tier only runs when the profile is otherwise applicable - a rejected
        // profile must not bury its hard rejection under advisory noise.
        CollectPreflightWarnings(profile, allProfiles, result);
        return result;
    }

    /// <summary>
    /// Ticket 19: emit the machine-parseable warn report. One JSON object on stdout
    /// (parseable), the human line on stderr. Kept shape-stable: consumers key on
    /// "preflight" / "warnings" / "strict" field names, not on prose.
    /// </summary>
    internal static void EmitPreflightWarnReport(string command, string profileName, PreflightResult result, bool strict)
    {
        var report = new
        {
            preflight = "warn",
            command,
            profile = profileName,
            strict,
            warnings = result.Warnings
        };
        Console.Error.WriteLine("Warning: Profile pre-flight warnings (" + (strict ? "strict mode: refusing" : "continuing") + "):");
        Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(report, JsonOptsIndented));
    }

    internal static bool RunProfilePreflight(ProfileData profile) => RunProfilePreflight(profile, LoadProfiles());

    /// <summary>
    /// Ticket 04 seam extraction: CollectInheritedSecrets over an explicit profile list.
    /// Walks the ENTIRE inheritance chain (visited-set keyed by profile Name, so a
    /// poisoned profiles.json with an undetected cycle cannot infinite-loop) and unions
    /// every SecretVariables entry - the authoritative inherited-secret membership source
    /// per the v0.7.7 hard boundary. Do not add ad-hoc walk functions.
    /// </summary>
    internal static HashSet<string> CollectInheritedSecretsFrom(ProfileData profile, List<ProfileData> allProfiles, HashSet<string> visited)
    {
        if (visited.Contains(profile.Name)) return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        visited.Add(profile.Name);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var sv in profile.SecretVariables)
            result.Add(sv);
        foreach (string parentName in profile.Inherits)
        {
            var parent = FindProfile(allProfiles, parentName);
            if (parent != null)
                foreach (var name in CollectInheritedSecretsFrom(parent, allProfiles, visited))
                    result.Add(name);
        }
        return result;
    }

    static HashSet<string> CollectInheritedSecrets(ProfileData profile, HashSet<string> visited)
        => CollectInheritedSecretsFrom(profile, LoadProfiles(), visited);

    /// <summary>
    /// Ticket 04 seam extraction: the apply write path. Every registry touch goes
    /// through IEnvironmentScope: backup preservation via WriteValuePreservingKind,
    /// the value write via WriteValuePreservingKind, teardown via DeleteValueWithoutNotify,
    /// and exactly ONE BroadcastSettingChange per apply/unapply. The protection guard
    /// (SetVariableWithoutNotify's IsProtectedVariable check) is preserved so a poisoned
    /// profile can never write a protected entry even if it slips past pre-flight.
    /// </summary>
    internal static void ApplyProfile(ProfileData profile, IEnvironmentScope env)
    {
        bool wrote = false;
        foreach (var variable in GetEffectiveProfileVariables(profile))
        {
            string backupName = GetBackupVariableName(variable.Name, profile.Name);
            string scope = variable.Scope ?? "user";
            if (IsProtectedVariable(variable.Name, scope)) continue;
            string? existingValue = env.ReadValue(variable.Name, scope)?.Value;
            if (existingValue != null && env.ReadValue(backupName, scope) == null)
                env.WriteValuePreservingKind(backupName, existingValue, scope);
            env.WriteValuePreservingKind(variable.Name, variable.Value, scope);
            wrote = true;
        }
        // T04: broadcast only when the batch actually changed something (protected-only
        // or empty profiles changed nothing, so WM_SETTINGCHANGE would be noise).
        if (wrote) env.BroadcastSettingChange();
    }

    /// <summary>Production adapter: apply against the real registry seam.</summary>
    static void ApplyProfile(ProfileData profile) => ApplyProfile(profile, Engine);

    /// <summary>
    /// Ticket 04 seam extraction: the unapply write path. Deletes through the seam's
    /// DeleteValueWithoutNotify (no per-delete broadcast), restores the backup value,
    /// removes the backup, and broadcasts exactly once at the end.
    /// </summary>
    internal static void UnapplyProfile(ProfileData profile, IEnvironmentScope env)
    {
        foreach (var variable in GetEffectiveProfileVariables(profile))
        {
            string backupName = GetBackupVariableName(variable.Name, profile.Name);
            string scope = variable.Scope ?? "user";
            if (IsProtectedVariable(variable.Name, scope)) continue;
            env.DeleteValueWithoutNotify(variable.Name, scope);
            string? backupValue = env.ReadValue(backupName, scope)?.Value;
            if (backupValue == null) continue;
            env.WriteValuePreservingKind(variable.Name, backupValue, scope);
            env.DeleteValueWithoutNotify(backupName, scope);
        }
        env.BroadcastSettingChange();
    }

    /// <summary>Production adapter: unapply against the real registry seam.</summary>
    static void UnapplyProfile(ProfileData profile) => UnapplyProfile(profile, Engine);
}
