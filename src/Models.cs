using Microsoft.Win32;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

class EnvVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    [JsonPropertyName("isDisabled")] public bool IsDisabled { get; set; } = false;
    [JsonPropertyName("profileSource")] public string? ProfileSource { get; set; }
    [JsonPropertyName("isProtected")] public bool IsProtected { get; set; } = false;
    [JsonPropertyName("isBuiltinProtected")] public bool IsBuiltinProtected { get; set; } = false;
}

class BackupData
{
    [JsonPropertyName("timestamp")] public string Timestamp { get; set; } = "";
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("variables")] public List<EnvVariable> Variables { get; set; } = new();
}

class ProfileVariable
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("value")] public string Value { get; set; } = "";
    // Optional scope: "user" (default) or "system". Only meaningful for global profiles.
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
    // v0.9.16: Which profile contributed this variable (set by ResolveProfileVariablesWithSource).
    [JsonPropertyName("sourceProfile")] public string? SourceProfile { get; set; }
}

// v0.9.16: Resolved path entry with scope + source profile for profile show output.
class ResolvedPathEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
    [JsonPropertyName("sourceProfile")] public string? SourceProfile { get; set; }
}

// v0.11.0 aggregate-root encapsulation (architecture-recovery ticket 42).
// All setters are private; external code mutates a profile ONLY through the
// domain methods below. [JsonInclude] keeps each property serializable AND
// deserializable through its private setter (System.Text.Json honours the
// attribute for non-public accessors), so profiles.json round-trips unchanged.
// SecretMount remains a separate aggregate (stored in secretMount.json); this
// class holds only secret variable NAMES (SecretVariables) and a "mount:<id>"
// reference inside Variables[].Value -- it never holds SecretMount objects.
class ProfileData
{
    [JsonPropertyName("id")] [JsonInclude] public string Id { get; private set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] [JsonInclude] public string Name { get; private set; } = "";
    [JsonPropertyName("isEnabled")] [JsonInclude] public bool IsEnabled { get; private set; } = false;
    [JsonPropertyName("appliedAt")] [JsonInclude] public long? AppliedAt { get; private set; }
    [JsonPropertyName("inherits")] [JsonInclude] public List<string> Inherits { get; private set; } = new();
    [JsonPropertyName("pathEntries")] [JsonInclude] public List<string> PathEntries { get; private set; } = new();
    // Per-entry scope mirror: PathScopes[i] is "user" (default) or "system" for PathEntries[i].
    // Older profiles.json files written before this field existed load as empty list;
    // ProfileApply treats a missing entry as "user" so existing behaviour is unchanged.
    [JsonPropertyName("pathScopes")] [JsonInclude] public List<string> PathScopes { get; private set; } = new();
    [JsonPropertyName("variables")] [JsonInclude] public List<ProfileVariable> Variables { get; private set; } = new();

    // Launch profile type: "global" (default, applies to user registry) or "launch" (launcher template, never writes registry).
    // v0.7.0 introduces per-app launch profiles and DPAPI-encrypted secrets to avoid polluting the global user environment.
    [JsonPropertyName("profileType")] [JsonInclude] public string ProfileType { get; private set; } = "global";
    [JsonPropertyName("targetExecutable")] [JsonInclude] public string? TargetExecutable { get; private set; }
    [JsonPropertyName("launchArguments")] [JsonInclude] public string? LaunchArguments { get; private set; }
    [JsonPropertyName("workingDirectory")] [JsonInclude] public string? WorkingDirectory { get; private set; }
    // Secret variable names in a launch profile are DPAPI-encrypted on disk; plaintext lives only in process memory.
    [JsonPropertyName("secretVariables")] [JsonInclude] public List<string> SecretVariables { get; private set; } = new();
    // v0.9.16: Resolved PATH entries (including inherited) with scope + sourceProfile.
    [JsonPropertyName("resolvedPaths")] [JsonInclude] public List<ResolvedPathEntry>? ResolvedPaths { get; private set; }
    // v0.9.9: Schema version for migration framework. 0 = pre-v0.9.9 (inferred on load).
    [JsonPropertyName("schemaVersion")] [JsonInclude] public int SchemaVersion { get; private set; } = 0;

    // --- Aggregate-root domain methods (ticket 42) -------------------------------
    // External code must change profile state only via these; the raw setters are
    // private so invariants (normalized PATH entries, secret-name bookkeeping,
    // parallel PathScopes array) are always enforced.

    public void SetId(string id) => Id = id;
    public void SetName(string name) => Name = name;
    public void SetEnabled(bool enabled) => IsEnabled = enabled;
    public void SetAppliedAt(long? appliedAt) => AppliedAt = appliedAt;
    public void SetProfileType(string profileType) => ProfileType = profileType;
    public void SetLaunchTarget(string? target) => TargetExecutable = target;
    public void SetLaunchArguments(string? args) => LaunchArguments = args;
    public void SetWorkingDirectory(string? cwd) => WorkingDirectory = cwd;
    public void SetSchemaVersion(int version) => SchemaVersion = version;

    public void SetInherits(IEnumerable<string> parents)
        => Inherits = parents.Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public void SetPathEntries(IEnumerable<string> entries)
        => PathEntries = entries.ToList();

    public void SetPathScopes(IEnumerable<string> scopes)
        => PathScopes = scopes.ToList();

    public void SetSecretVariables(IEnumerable<string> names)
        => SecretVariables = names.ToList();

    public void SetResolvedPaths(List<ResolvedPathEntry>? paths) => ResolvedPaths = paths;

    public void AddVariable(string name, string value, string scope = "user", string? sourceProfile = null)
    {
        Variables.RemoveAll(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        Variables.Add(new ProfileVariable { Name = name, Value = value, Scope = scope, SourceProfile = sourceProfile });
    }

    public bool RemoveVariable(string name)
        => Variables.RemoveAll(v => v.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) > 0;

    public void AddSecretVariable(string name)
    {
        if (!SecretVariables.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
            SecretVariables.Add(name);
    }

    public void RemoveSecretVariable(string name)
        => SecretVariables.RemoveAll(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));

    public bool IsSecretVariable(string name)
        => SecretVariables.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase));

    // PATH entries carry the NormalizePathEntry invariant: add/remove go through these
    // domain methods (never the raw setter or a bare list mutation) so the PathScopes
    // parallel array stays in lockstep and duplicate normalized entries are rejected.
    public void AddPathEntry(string path, string scope = "user")
    {
        if (!PathEntries.Any(p => NormalizePathEntryLocal(p).Equals(NormalizePathEntryLocal(path), StringComparison.OrdinalIgnoreCase)))
        {
            PathEntries.Add(path);
            while (PathScopes.Count < PathEntries.Count - 1) PathScopes.Add("user");
            PathScopes.Add(scope);
        }
    }

    public bool RemovePathEntry(string path)
    {
        int idx = PathEntries.FindIndex(p => NormalizePathEntryLocal(p).Equals(NormalizePathEntryLocal(path), StringComparison.OrdinalIgnoreCase));
        if (idx < 0) return false;
        PathEntries.RemoveAt(idx);
        if (idx < PathScopes.Count) PathScopes.RemoveAt(idx);
        return true;
    }

    // Local copy of the PATH normalization used as the add/remove invariant. Inlined so the
    // aggregate root has no dependency on the CLI runtime's PathCommand.NormalizePathEntry.
    private static string NormalizePathEntryLocal(string path)
        => Environment.ExpandEnvironmentVariables(path).Trim().TrimEnd('\\', '/');

    // Initialize any collection that a hand-built or deserialized profile is missing.
    public void EnsureCollections()
    {
        Inherits ??= new();
        PathEntries ??= new();
        Variables ??= new();
        PathScopes ??= new();
        SecretVariables ??= new();
    }
}
