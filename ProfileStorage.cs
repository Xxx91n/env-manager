using System.Text;
using System.Text.Json;

namespace EnvManager;

partial class Program
{
    static string ProfilesBackupPath => ProfilesFilePath + ".bak";

    static List<ProfileData> LoadProfiles()
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
        return profiles;
    }

    static void SaveProfiles(List<ProfileData> profiles)
    {
        ValidateProfiles(profiles);
        AtomicWriteProfiles(profiles, createBackup: true);
    }

    static void AtomicWriteProfiles(List<ProfileData> profiles, bool createBackup)
    {
        string temp = ProfilesFilePath + ".tmp." + Environment.ProcessId;
        File.WriteAllText(temp, JsonSerializer.Serialize(profiles, JsonOptsIndented), new UTF8Encoding(false));
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
}
