using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

// v0.9.9: Versioned schema migration framework.
// Replaces the ad-hoc MigrateSecretsToMounts call with a registry-based
// sequential migration system. Each config file (profiles.json, secretMount.json)
// carries a schemaVersion field; on load, migrations are applied sequentially
// until the current version is reached. Migrations are idempotent and atomic.
//
// Pattern: industry-standard migration registry (Entity Framework migrations,
// LiteDB, RavenDB). Each step is a (up) function that transforms the document
// from version N to N+1. Down/rollback is administrative-only (restore from
// .bak backup file). The registry never drops old steps so existing data at
// any version can always migrate forward.
//
// Hard boundary: migrations run inside the existing cross-process mutex and
// atomic write path. A failed migration throws and does NOT persist partial
// state (the .bak file is the rollback path).

partial class Program
{
    // Current schema versions. Bump when a new migration step is added.
    const int CurrentProfilesSchemaVersion = 2;
    const int CurrentSecretMountSchemaVersion = 2;

    // --- Profiles migration registry ---
    // Step 0->1: v0.7.1 add PathScopes field (default empty list already handled by ReadProfilesFile).
    // Step 1->2: v0.8.0 extract secret envelopes to secretMount.json (delegates to MigrateSecretsToMounts).
    delegate void ProfilesMigrationStep(List<ProfileData> profiles);

    static readonly Dictionary<int, ProfilesMigrationStep> ProfilesMigrations = new()
    {
        { 0, MigrateProfilesV0ToV1 },
        { 1, MigrateProfilesV1ToV2 },
    };

    /// <summary>
    /// Run all pending profile schema migrations. Called from ReadProfilesFile
    /// after deserialization, before any profile validation or apply.
    /// Writes the migrated profiles back to disk atomically if any step ran.
    /// </summary>
    static void MigrateProfiles(List<ProfileData> profiles)
    {
        int version = DetectProfilesSchemaVersion(profiles);

        if (version >= CurrentProfilesSchemaVersion)
            return; // already current

        bool changed = false;
        while (version < CurrentProfilesSchemaVersion)
        {
            if (!ProfilesMigrations.TryGetValue(version, out var step))
                throw new InvalidDataException(
                    $"No migration path from profiles schema version {version}. " +
                    $"Expected <= {CurrentProfilesSchemaVersion}. File may be from a newer build.");

            DebugLog($"Migrating profiles schema v{version} -> v{version + 1}");
            step(profiles);
            version++;
            changed = true;
        }

        if (changed)
        {
            // Stamp all profiles with current version and persist.
            foreach (var p in profiles) p.SchemaVersion = CurrentProfilesSchemaVersion;
            AtomicWriteProfiles(profiles, createBackup: true);
            DebugLog($"Profiles migrated to schema v{CurrentProfilesSchemaVersion}");
        }
    }

    /// <summary>
    /// Detect schema version from a loaded profiles list. Pre-v0.9.9 profiles.json
    /// has no schemaVersion field; we infer from content:
    ///   - If any profile still has inline secret envelopes (bare DPAPI base64 or
    ///     JSON envelope starting with '{') -> version 0 (pre-mount migration).
    ///   - If all secrets are mount references ("mount:...") but no schemaVersion -> version 1.
    ///   - If schemaVersion field is present -> use it directly.
    /// </summary>
    static int DetectProfilesSchemaVersion(List<ProfileData> profiles)
    {
        if (profiles.Count == 0)
            return CurrentProfilesSchemaVersion; // empty = current

        // If any profile has schemaVersion > 0, use the minimum (most unmigrated).
        int minStamped = profiles.Min(p => p.SchemaVersion);
        if (minStamped > 0)
            return minStamped;

        // No schemaVersion field (all are 0/default). Infer from content.
        bool hasInlineSecret = false;
        bool hasMountRef = false;

        foreach (var profile in profiles)
        {
            foreach (var sv in profile.SecretVariables)
            {
                var pv = profile.Variables.FirstOrDefault(x => x.Name.Equals(sv, StringComparison.OrdinalIgnoreCase));
                if (pv == null) continue;
                if (pv.Value != null && pv.Value.StartsWith("mount:"))
                    hasMountRef = true;
                else if (pv.Value != null && (pv.Value.StartsWith("{") || IsLikelyDpapiBase64(pv.Value)))
                    hasInlineSecret = true;
            }
        }

        if (hasInlineSecret)
            return 0; // needs v0->v1->v2 migration
        if (hasMountRef)
            return 1; // already mount-refactored, just needs version stamp
        // No secrets at all — existing content is already at v2 shape.
        return CurrentProfilesSchemaVersion;
    }

    static bool IsLikelyDpapiBase64(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length < 20) return false;
        // DPAPI base64 blobs are long and contain only base64 chars.
        return value.All(c => char.IsLetterOrDigit(c) || c == '+' || c == '/' || c == '=');
    }

    // v0->v1: ensure PathScopes list is initialized (no-op, handled by ReadProfilesFile).
    static void MigrateProfilesV0ToV1(List<ProfileData> profiles)
    {
        foreach (var p in profiles)
        {
            p.PathScopes ??= new();
            p.Inherits ??= new();
            p.Variables ??= new();
            p.PathEntries ??= new();
            p.SecretVariables ??= new();
        }
    }

    // v1->v2: extract secret envelopes from profiles to secretMount.json.
    static void MigrateProfilesV1ToV2(List<ProfileData> profiles)
    {
        MigrateSecretsToMounts(profiles);
    }

    // --- SecretMount schema version detection ---
    // secretMount.json entries each carry schemaVersion (default 2). Since all
    // entries are created at v2, there is currently no mount migration step.
    // If a future v3 is needed, add to SecretMountMigrations and bump CurrentSecretMountSchemaVersion.
    static readonly Dictionary<int, Action<List<SecretMount>>> SecretMountMigrations = new();

    static void MigrateSecretMounts(List<SecretMount> mounts)
    {
        if (mounts.Count == 0)
            return;

        int version = mounts.Min(m => m.SchemaVersion);
        while (version < CurrentSecretMountSchemaVersion)
        {
            if (!SecretMountMigrations.TryGetValue(version, out var step))
                throw new InvalidDataException(
                    $"No migration path from secretMount schema version {version}.");

            step(mounts);
            version++;
        }

        foreach (var m in mounts) m.SchemaVersion = CurrentSecretMountSchemaVersion;
    }
}
