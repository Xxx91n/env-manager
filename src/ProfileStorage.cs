using System.Text;
using System.Text.Json;

namespace EnvManager;

partial class Program
{
    static string ProfilesBackupPath => ProfilesFilePath + ".bak";

    internal static List<ProfileData> LoadProfiles()
    {
        if (!File.Exists(ProfilesFilePath)) return new();
        try
        {
            return ReadProfilesFile(ProfilesFilePath);
        }
        catch (JsonException) when (File.Exists(ProfilesBackupPath))
        {
            var recovered = ReadProfilesFile(ProfilesBackupPath);
            AtomicWriteProfiles(recovered, createBackup: false);
        return recovered;
        }
    }

    static List<ProfileData> ReadProfilesFile(string path)
    {
        var info = new FileInfo(path);
        if (info.Length > MaxBackupFileSize) throw new InvalidDataException("Profiles file exceeds 50 MB");
        var profiles = JsonSerializer.Deserialize<List<ProfileData>>(File.ReadAllText(path), JsonOpts) ?? new();
        foreach (var profile in profiles)
        {
            profile.Inherits ??= new();
            profile.PathEntries ??= new();
            profile.Variables ??= new();
        }
        // v0.9.9: Versioned schema migration (replaces ad-hoc MigrateSecretsToMounts call).
        // The registry detects schema version, runs pending steps sequentially, and persists.
        MigrateProfiles(profiles);
        return profiles;
    }

    internal static void SaveProfiles(List<ProfileData> profiles)
    {
        ValidateProfiles(profiles);
        AtomicWriteProfiles(profiles, createBackup: true);
    }

static void AtomicWriteProfiles(List<ProfileData> profiles, bool createBackup)
{
    string temp = ProfilesFilePath + ".tmp." + Environment.ProcessId;
    // v0.8.0 A3: fsync before rename to match Rust write_atomic (sync_all before rename).
    // Without this a crash after rename could lose temp content and resurrect
    // stale state - the same bug class that motivated the Rust-side guard.
    using (var fs = File.Create(temp))
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(profiles, JsonOptsIndented);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(flushToDisk: true); // fsync
    }
    if (createBackup && File.Exists(ProfilesFilePath)) File.Copy(ProfilesFilePath, ProfilesBackupPath, true);
    File.Move(temp, ProfilesFilePath, true);
}

    static void ValidateProfiles(List<ProfileData> profiles)
    {
        // CLI commands address profiles by name. Keep one namespace so a command
        // can never target a Global profile when the caller intended Launch.
        var profileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name))
                throw new InvalidDataException("Profile names must be non-empty");

            bool isLaunch = profile.ProfileType.Equals("launch", StringComparison.OrdinalIgnoreCase);
            if (!profileNames.Add(profile.Name))
                throw new InvalidDataException($"Profile names must be unique (duplicate: {profile.Name})");

            if (isLaunch)
            {
                if (string.IsNullOrWhiteSpace(profile.TargetExecutable))
                    throw new InvalidDataException($"Launch profile '{profile.Name}' must specify targetExecutable");
                ValidateLaunchTarget(profile.TargetExecutable);
            }
            else
            {
                // Global profile: cannot reference a target executable (reserved for Launch type).
                if (!string.IsNullOrWhiteSpace(profile.TargetExecutable))
                    throw new InvalidDataException($"Global profile '{profile.Name}' must not set targetExecutable");
            }

            // Per-profile variable uniqueness within the profile itself.
            var varNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var variable in profile.Variables)
            {
                ValidateVariableInput(variable.Name, variable.Value, "user");
                if (!varNames.Add(variable.Name))
                    throw new InvalidDataException($"Variable names must be unique within profile '{profile.Name}' (duplicate: {variable.Name})");
                if (ProtectedSystemVars.Contains(variable.Name)) throw new InvalidDataException("Protected variables cannot be stored in profiles");
            }
            foreach (string path in profile.PathEntries) ValidatePathFragment(path);
            ResolveProfileVariables(profile, profiles);
            ResolveProfilePaths(profile, profiles);
        }
    }
    /// <summary>
    /// Returns the path to the profiles JSON file in LocalAppData.
    /// Mirrors PowerToys' approach of storing profiles in a per-user app data folder.
    /// </summary>
    static string ProfilesFilePath
    {
        get
        {
            if (_profilesFilePathOverride != null) return _profilesFilePathOverride; // Ticket 04 test redirect
            string dir = Path.Combine(
                LocalAppDataRoot,
                "EnvManager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "profiles.json");
        }
    }

    // Ticket 04 test seam: when non-null, ProfilesFilePath returns this path so the
    // xUnit lane can redirect profiles.json to a temp dir. Production never sets it.
    static string? _profilesFilePathOverride;

    internal static void SetProfilesFilePathForTests(string? path)
    {
        _profilesFilePathOverride = path;
    }

    // Ticket 04 test seam: persist profiles WITHOUT ValidateProfiles, simulating a
    // hand-edited profiles.json so the ApplyProfile protection guard (defense in depth
    // behind pre-flight) can be exercised against poisoned data.
    internal static void SaveProfilesRawForTests(List<ProfileData> profiles)
    {
        AtomicWriteProfiles(profiles, createBackup: false);
    }

}
