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

class ProfileData
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString();
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("isEnabled")] public bool IsEnabled { get; set; } = false;
    [JsonPropertyName("appliedAt")] public long? AppliedAt { get; set; }
    [JsonPropertyName("inherits")] public List<string> Inherits { get; set; } = new();
    [JsonPropertyName("pathEntries")] public List<string> PathEntries { get; set; } = new();
    // Per-entry scope mirror: PathScopes[i] is "user" (default) or "system" for PathEntries[i].
    // Older profiles.json files written before this field existed load as empty list;
    // ProfileApply treats a missing entry as "user" so existing behaviour is unchanged.
    [JsonPropertyName("pathScopes")] public List<string> PathScopes { get; set; } = new();
    [JsonPropertyName("variables")] public List<ProfileVariable> Variables { get; set; } = new();

    // Launch profile type: "global" (default, applies to user registry) or "launch" (launcher template, never writes registry).
    // v0.7.0 introduces per-app launch profiles and DPAPI-encrypted secrets to avoid polluting the global user environment.
    [JsonPropertyName("profileType")] public string ProfileType { get; set; } = "global";
    [JsonPropertyName("targetExecutable")] public string? TargetExecutable { get; set; }
    [JsonPropertyName("launchArguments")] public string? LaunchArguments { get; set; }
    [JsonPropertyName("workingDirectory")] public string? WorkingDirectory { get; set; }
    // Secret variable names in a launch profile are DPAPI-encrypted on disk; plaintext lives only in process memory.
    [JsonPropertyName("secretVariables")] public List<string> SecretVariables { get; set; } = new();
    // v0.9.16: Resolved PATH entries (including inherited) with scope + sourceProfile.
    [JsonPropertyName("resolvedPaths")] public List<ResolvedPathEntry>? ResolvedPaths { get; set; }
    // v0.9.9: Schema version for migration framework. 0 = pre-v0.9.9 (inferred on load).
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 0;
}
