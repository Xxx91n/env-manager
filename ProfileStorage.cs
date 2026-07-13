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
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var profile in profiles)
        {
            if (string.IsNullOrWhiteSpace(profile.Name) || !names.Add(profile.Name))
                throw new InvalidDataException("Profile names must be non-empty and unique");
            foreach (var variable in profile.Variables)
            {
                ValidateVariableInput(variable.Name, variable.Value, "user");
                if (ProtectedSystemVars.Contains(variable.Name)) throw new InvalidDataException("Protected variables cannot be stored in profiles");
            }
            foreach (string path in profile.PathEntries) ValidatePathFragment(path);
            ResolveProfileVariables(profile, profiles);
            ResolveProfilePaths(profile, profiles);
        }
    }
}
