// SecretProvider.cs - Phase 1-2 Secret Provider Architecture
// v0.8: ISecretProvider interface, versioned envelopes, Windows Credential Manager adapter
// License: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

// --- Phase 1: Versioned Envelope ---

internal sealed class SecretEnvelope
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "dpapi-current-user";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    // For dpapi-current-user: base64 DPAPI blob
    // For credential-manager: CRED target name (e.g. "EnvManager\\<profile>\\<var>")
    [JsonPropertyName("ciphertext")]
    public string? Ciphertext { get; set; }

    // For credential-manager: the actual encrypted credential blob is in CredMan
    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, SecretEnvelopeJsonContext.Default.SecretEnvelope);
    }

    public static SecretEnvelope? TryParse(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        // Backwards compat: bare base64 DPAPI blob (no leading {)
        string trimmed = stored.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return null;
        try
        {
            return JsonSerializer.Deserialize(stored, SecretEnvelopeJsonContext.Default.SecretEnvelope);
        }
        catch
        {
            return null;
        }
    }

    // Backwards compat: detect if a stored value is a bare DPAPI base64 blob
    // (pre-v0.8 format, no envelope wrapper)
    public static bool IsBareBase64Blob(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        string trimmed = stored.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == '{') return false;
        try
        {
            // Must be valid base64
            Convert.FromBase64String(stored);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

[JsonSerializable(typeof(SecretEnvelope))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
internal sealed partial class SecretEnvelopeJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(SecretProviderManager.ProviderConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
internal sealed partial class ProviderConfigJsonContext : JsonSerializerContext
{
}

// --- Phase 1: ISecretProvider Interface ---

internal interface ISecretProvider
{
    string Name { get; }

    // Encrypt plaintext into an envelope string (JSON)
    string Encrypt(string plaintext, string? context = null);

    // Decrypt an envelope string back to plaintext
    string Decrypt(string envelope, string? context = null);

    // Whether this provider supports key rotation
    bool CanRotate => false;

    // Rotate: re-encrypt with a new key (optional, default no-op)
    string Rotate(string oldEnvelope, string? context = null)
    {
        return oldEnvelope;
    }

    // Delete any provider-side state (e.g. CredMan entry) for a given envelope
    void Delete(string envelope, string? context = null) { }
}

// --- Phase 1: DpapiCurrentUserProvider (wraps existing DpapiHelper) ---

internal sealed class DpapiCurrentUserProvider : ISecretProvider
{
    public string Name => "dpapi-current-user";

    public string Encrypt(string plaintext, string? context = null)
    {
        string cipherBase64 = DpapiHelper.EncryptSecret(plaintext);
        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Ciphertext = cipherBase64
        };
        return envelope.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        // Backwards compat: bare base64 DPAPI blob from pre-v0.8
        if (SecretEnvelope.IsBareBase64Blob(envelope))
        {
            return DpapiHelper.DecryptSecret(envelope);
        }

        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.Ciphertext))
            throw new InvalidOperationException("Missing ciphertext in envelope");

        return DpapiHelper.DecryptSecret(parsed.Ciphertext);
    }

    public bool CanRotate => false;
}

// --- Phase 2: CredentialManagerProvider (advapi32.dll P/Invoke) ---

internal sealed class CredentialManagerProvider : ISecretProvider
{
    public string Name => "credential-manager";

    // CRED_TYPE_GENERIC = 1
    private const int CRED_TYPE_GENERIC = 1;

    // CRED_PERSIST_ENTERPRISE = 3 (roams with user profile)
    private const int CRED_PERSIST_ENTERPRISE = 3;

    // Maximum credential blob size (512 bytes for Generic, per MS docs)
    private const int MAX_CRED_BLOB = 512;

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        if (plainBytes.Length > MAX_CRED_BLOB)
            throw new InvalidOperationException(
                $"Credential Manager blob too large ({plainBytes.Length} bytes, max {MAX_CRED_BLOB}). " +
                "Use dpapi-current-user provider for larger secrets.");

        // Target name: EnvManager\<context> or EnvManager\<generated-uuid>
        string targetName = context != null
            ? "EnvManager\\" + SanitizeTargetName(context)
            : "EnvManager\\" + Guid.NewGuid().ToString("N");

        // DPAPI-encrypt the plaintext before storing in CredMan
        // so even if CredMan is dumped, the blob is still encrypted
        string dpapiCipher = DpapiHelper.EncryptSecret(plaintext);

        byte[] credBlob = Encoding.UTF8.GetBytes(dpapiCipher);

        var cred = new CREDENTIALW
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = targetName,
            Persist = CRED_PERSIST_ENTERPRISE,
            CredentialBlobSize = credBlob.Length,
            CredentialBlob = Marshal.AllocHGlobal(credBlob.Length),
            UserName = Environment.UserName
        };

        try
        {
            Marshal.Copy(credBlob, 0, cred.CredentialBlob, credBlob.Length);

            if (!CredWriteW(ref cred, 0))
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err,
                    $"CredWriteW failed (Win32 error {err})");
            }
        }
        finally
        {
            if (cred.CredentialBlob != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cred.CredentialBlob);
            }
            // Zero the DPAPI ciphertext bytes from managed memory
            for (int i = 0; i < credBlob.Length; i++) credBlob[i] = 0;
        }

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = targetName
        };
        return envelope.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName))
            throw new InvalidOperationException("Missing targetName in envelope");

        IntPtr credPtr = IntPtr.Zero;
        try
        {
            if (!CredReadW(parsed.TargetName, CRED_TYPE_GENERIC, 0, out credPtr))
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err,
                    $"CredReadW failed for target '{parsed.TargetName}' (Win32 error {err})");
            }

            var cred = (CREDENTIALW)Marshal.PtrToStructure(credPtr, typeof(CREDENTIALW))!;
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                throw new InvalidOperationException("Credential blob is empty");

            byte[] credBlob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, credBlob, 0, cred.CredentialBlobSize);
            try
            {
                string dpapiCipher = Encoding.UTF8.GetString(credBlob);
                return DpapiHelper.DecryptSecret(dpapiCipher);
            }
            finally
            {
                for (int i = 0; i < credBlob.Length; i++) credBlob[i] = 0;
            }
        }
        finally
        {
            if (credPtr != IntPtr.Zero) CredFree(credPtr);
        }
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            CredDeleteW(parsed.TargetName, CRED_TYPE_GENERIC, 0);
        }
    }

    private static string SanitizeTargetName(string s)
    {
        // Target name cannot contain backslash as separator conflict
        return s.Replace("\\", "_").Replace("/", "_");
    }

    // --- P/Invoke: advapi32.dll Credential Manager ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIALW
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref CREDENTIALW cred, int flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);
}

// --- Phase 1: SecretProviderManager (routes to active provider) ---

internal static class SecretProviderManager
{
    private static readonly Dictionary<string, ISecretProvider> _providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dpapi-current-user"] = new DpapiCurrentUserProvider(),
        ["credential-manager"] = new CredentialManagerProvider()
    };

    private const string PROVIDER_CONFIG_FILE = "secret-providers.json";
    private const string DEFAULT_PROVIDER = "dpapi-current-user";

    // Config model
    internal sealed class ProviderConfig
    {
        [JsonPropertyName("activeProvider")]
        public string ActiveProvider { get; set; } = DEFAULT_PROVIDER;

        [JsonPropertyName("fallbackPolicy")]
        public string FallbackPolicy { get; set; } = "fail-closed"; // or "legacy-dpapi"
    }

    

    private static string GetConfigPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnvManager");
        return Path.Combine(dir, PROVIDER_CONFIG_FILE);
    }

    private static ProviderConfig LoadConfig()
    {
        try
        {
            string path = GetConfigPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize(json, ProviderConfigJsonContext.Default.ProviderConfig);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // Fall through to default
        }
        return new ProviderConfig();
    }

    public static ISecretProvider GetActiveProvider()
    {
        var config = LoadConfig();
        if (_providers.TryGetValue(config.ActiveProvider, out var provider))
            return provider;

        // Fail-closed: unknown provider = error, not silent fallback
        // Exception: if fallbackPolicy is legacy-dpapi and the stored envelope
        // is a bare DPAPI blob, DpapiCurrentUserProvider can still decrypt it
        if (config.FallbackPolicy == "legacy-dpapi")
            return _providers[DEFAULT_PROVIDER];

        throw new InvalidOperationException(
            $"Active secret provider '{config.ActiveProvider}' is not available. " +
            $"Install or configure the provider, or set fallbackPolicy to 'legacy-dpapi'.");
    }

    public static string Encrypt(string plaintext, string? context = null)
    {
        var provider = GetActiveProvider();
        return provider.Encrypt(plaintext, context);
    }

    public static string Decrypt(string envelope, string? context = null)
    {
        // First: check if it's a bare DPAPI blob (pre-v0.8 backwards compat)
        if (SecretEnvelope.IsBareBase64Blob(envelope))
        {
            return _providers[DEFAULT_PROVIDER].Decrypt(envelope, context);
        }

        // Parse envelope to find provider
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed == null)
            throw new InvalidOperationException("Invalid secret envelope: not a JSON envelope and not a valid base64 blob");

        if (_providers.TryGetValue(parsed.Provider, out var provider))
        {
            return provider.Decrypt(envelope, context);
        }

        // Unknown provider: fail-closed
        throw new InvalidOperationException(
            $"Secret provider '{parsed.Provider}' is not available. " +
            "The provider may not be installed or configured on this machine.");
    }

    public static void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && _providers.TryGetValue(parsed.Provider, out var provider))
        {
            provider.Delete(envelope, context);
        }
    }

    // List available providers and their status
    public static List<(string Name, bool Available)> ListProviders()
    {
        var result = new List<(string, bool)>();
        foreach (var kvp in _providers)
        {
            result.Add((kvp.Key, true));
        }
        return result;
    }

    // Get the active provider name from config
    public static string GetActiveProviderName()
    {
        return LoadConfig().ActiveProvider;
    }

    // Set the active provider (persists to config file)
    public static void SetActiveProvider(string name)
    {
        if (!_providers.ContainsKey(name))
            throw new InvalidOperationException($"Unknown secret provider: {name}");

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnvManager");
        Directory.CreateDirectory(dir);

        var config = LoadConfig();
        config.ActiveProvider = name;
        string json = JsonSerializer.Serialize(config, ProviderConfigJsonContext.Default.ProviderConfig);
        File.WriteAllText(GetConfigPath(), json);
    }

    // Phase 3: Key Rotation - re-encrypt all secrets in all profiles with the active provider
    // Returns (totalSecrets, rotatedCount, failedCount)
    public static (int total, int rotated, int failed) RotateAll(System.Collections.Generic.List<ProfileData> profiles)
    {
        var provider = GetActiveProvider();
        int total = 0, rotated = 0, failed = 0;

        foreach (var profile in profiles)
        {
            foreach (var v in profile.Variables)
            {
                if (!profile.SecretVariables.Any(s => s.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                total++;
                try
                {
                    // Decrypt with whatever provider encrypted it
                    string plaintext = Decrypt(v.Value, profile.Name + "\\" + v.Name);
                    // Re-encrypt with the active provider
                    v.Value = Encrypt(plaintext, profile.Name + "\\" + v.Name);
                    rotated++;
                }
                catch
                {
                    // Decryption failed (wrong provider, deleted CredMan entry, etc.)
                    failed++;
                }
            }
        }
        return (total, rotated, failed);
    }

    // Phase 3: Export secrets from a profile to an encrypted backup file
    // The backup is itself DPAPI-encrypted (CurrentUser scope) regardless of the provider,
    // so the backup is portable within the same user account.
    public static string ExportSecrets(ProfileData profile)
    {
        var secretsToExport = new System.Collections.Generic.List<(string name, string envelope)>();
        foreach (var v in profile.Variables)
        {
            if (profile.SecretVariables.Any(s => s.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
            {
                secretsToExport.Add((v.Name, v.Value));
            }
        }
        var exportData = new
        {
            profileName = profile.Name,
            exportedAt = DateTimeOffset.UtcNow.ToString("O"),
            secrets = secretsToExport.Select(s => new { name = s.name, envelope = s.envelope }).ToList()
        };
        string json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return DpapiHelper.EncryptSecret(json);
    }

    // Phase 3: Import secrets from an encrypted backup into a profile
    // Returns list of (name, success) tuples
    public static System.Collections.Generic.List<(string name, bool success)> ImportSecrets(ProfileData profile, string encryptedBackup)
    {
        var results = new System.Collections.Generic.List<(string, bool)>();
        // Decrypt the backup (DPAPI CurrentUser - same user that exported it)
        string json = DpapiHelper.DecryptSecret(encryptedBackup);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var secrets = doc.RootElement.GetProperty("secrets");
        foreach (var secret in secrets.EnumerateArray())
        {
            string name = secret.GetProperty("name").GetString() ?? "";
            string envelope = secret.GetProperty("envelope").GetString() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                results.Add((name, false));
                continue;
            }
            try
            {
                // Verify the envelope can be decrypted by trying to decrypt it
                _ = Decrypt(envelope, profile.Name + "\\" + name);

                // Remove existing variable with same name, then add the imported one
                profile.Variables.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                profile.Variables.Add(new ProfileVariable { Name = name, Value = envelope });
                if (!profile.SecretVariables.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    profile.SecretVariables.Add(name);

                results.Add((name, true));
            }
            catch
            {
                results.Add((name, false));
            }
        }
        return results;
    }
}
