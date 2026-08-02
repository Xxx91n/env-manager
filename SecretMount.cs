using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

// v0.8.0 Phase A: SecretMount schema v2.
// A SecretMount decouples the secret envelope (provider + ciphertext/targetName)
// from the profile's variable list. The profile stores only the mount ID and
// variable name; the actual envelope lives in secretMount.json.
// See docs/adr/0001-secret-architecture-revision.md decision A3.
class SecretMount
{
    [JsonPropertyName("id")] public string Id { get; set; } = Guid.NewGuid().ToString("N");
    [JsonPropertyName("provider")] public string Provider { get; set; } = "dpapi-current-user";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("targetName")] public string? TargetName { get; set; }
    [JsonPropertyName("scope")] public string Scope { get; set; } = "user";
    // v0.8.0: defaults CreatedOnce + null. Activated in v0.9.0 by the service.
    [JsonPropertyName("refreshPolicy")] public string RefreshPolicy { get; set; } = "CreatedOnly";
    [JsonPropertyName("refreshIntervalSeconds")] public int? RefreshIntervalSeconds { get; set; }
    [JsonPropertyName("lastRotatedAt")] public string? LastRotatedAt { get; set; }
    [JsonPropertyName("lastFetchedAt")] public string? LastFetchedAt { get; set; }
    [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("O");
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 2;
    [JsonPropertyName("bootstrapCertThumbprint")] public string? BootstrapCertThumbprint { get; set; }
    // The encrypted envelope string (JSON from SecretEnvelope.Serialize()).
    // For CreatedOnly mounts this is the full envelope; for Periodic mounts
    // the value lives in the provider backend and this field may be null.
    [JsonPropertyName("envelope")] public string? Envelope { get; set; }
    // The profile name that owns this mount. Used for migration and cleanup.
    [JsonPropertyName("profileName")] public string? ProfileName { get; set; }
}

partial class Program
{
    static string SecretMountFilePath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "EnvManager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "secretMount.json");
        }
    }

    static List<SecretMount> LoadSecretMounts()
    {
        if (!File.Exists(SecretMountFilePath)) return new();
        try
 {
            return JsonSerializer.Deserialize<List<SecretMount>>(File.ReadAllText(SecretMountFilePath), JsonOpts) ?? new();
        }
        catch (JsonException) when (File.Exists(SecretMountFilePath + ".bak"))
        {
            return JsonSerializer.Deserialize<List<SecretMount>>(File.ReadAllText(SecretMountFilePath + ".bak"), JsonOpts) ?? new();
        }
    }

    // v0.8.0 A3: write order is mount-first, profile-second (forward reference safety).
    // Atomic write with fsync (aligned with Rust write_atomic).
    static void SaveSecretMounts(List<SecretMount> mounts)
    {
        string temp = SecretMountFilePath + ".tmp." + Environment.ProcessId;
        using (var fs = File.Create(temp))
        {
            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(mounts, JsonOptsIndented);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(flushToDisk: true); // fsync
        }
        if (File.Exists(SecretMountFilePath)) File.Copy(SecretMountFilePath, SecretMountFilePath + ".bak", true);
        File.Move(temp, SecretMountFilePath, true);
    }

    // v0.8.0 A2: one-shot migration. For each profile, extract secret variable
    // envelopes from ProfileData.Variables into SecretMount objects in
    // secretMount.json. The profile's SecretVariables list stays as variable
    // names (unchanged); the ProfileVariable.Value envelope is replaced with
    // the mount ID prefixed with "mount:". On next load, "mount:" prefixed
    // values are resolved from secretMount.json.
    // No dual-write: migration runs once on load if secretMount.json does not
    // exist or is empty AND profiles contain secret variables.
    static void MigrateSecretsToMounts(List<ProfileData> profiles)
    {
        // Skip if already migrated (secretMount.json exists and non-empty)
        var existing = LoadSecretMounts();
        if (existing.Count > 0) return;

        var mounts = new List<SecretMount>();
        bool changed = false;

        foreach (var profile in profiles)
        {
            if (profile.SecretVariables.Count == 0) continue;

            foreach (var varName in profile.SecretVariables)
            {
                var v = profile.Variables.FirstOrDefault(x => x.Name.Equals(varName, StringComparison.OrdinalIgnoreCase));
                if (v == null) continue;
                var envelope = SecretEnvelope.TryParse(v.Value);
                if (envelope == null) continue; // not an envelope (bare DPAPI or plain)

                var mount = new SecretMount
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Provider = envelope.Provider ?? "dpapi-current-user",
                    Name = varName,
                    TargetName = envelope.TargetName,
                    Scope = v.Scope,
                    CreatedAt = envelope.CreatedAt ?? DateTimeOffset.UtcNow.ToString("O"),
                    Envelope = v.Value, // keep the full envelope JSON
                    ProfileName = profile.Name,
                };
                mounts.Add(mount);
                v.Value = "mount:" + mount.Id; // replace inline envelope with mount reference
                changed = true;
            }
        }

        if (!changed) return;

        // A3: write order mount-first, profile-second.
        SaveSecretMounts(mounts);
        SaveProfiles(profiles);
        DebugLog("Migrated " + mounts.Count + " secret envelopes to secretMount.json");
    }

    // Resolve a secret value by mount ID. Returns the envelope string or null.
    static string? ResolveSecretMount(string value)
    {
        if (value == null || !value.StartsWith("mount:")) return null;
        string mountId = value.Substring(6);
        var mounts = LoadSecretMounts();
        var mount = mounts.FirstOrDefault(m => m.Id.Equals(mountId, StringComparison.OrdinalIgnoreCase));
        return mount?.Envelope;
    }
}
