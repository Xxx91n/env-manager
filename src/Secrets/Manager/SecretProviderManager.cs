using EnvManager.Secrets.Core;
using EnvManager.Secrets.Providers;
// SecretProviderManager.cs - secret provider architecture
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager.Secrets.Manager;

// --- Phase 1: SecretProviderManager (routes to active provider) ---
// Now also the ISecretStore implementation - the domain facade above the
// ISecretProvider transport adapters (spec Phase 6 story 19). Call sites consume the
// five domain verbs (Mount/Reveal/Rotate/Export/Import) via ISecretStore; provider
// routing (GetActiveProvider/Delete/ListProviders/SetActiveProvider) stays here.

internal sealed class SecretProviderManager : ISecretStore
{
    // Process-wide store instance.
    internal static readonly SecretProviderManager Instance = new SecretProviderManager();

    // --- ISecretStore domain facade: thin delegates over the routing core ---
    string ISecretStore.Mount(string plaintext, string? context) => Encrypt(plaintext, context);
    string ISecretStore.Reveal(string envelope, string? context) => Decrypt(envelope, context);
    (int total, int rotated, int failed) ISecretStore.Rotate(List<ProfileData> profiles, ISecretEnvelopeHolder envelopeHolder) => RotateAll(profiles, null, envelopeHolder);
    string ISecretStore.Export(ProfileData profile) => ExportSecrets(profile);
    List<(string name, bool success)> ISecretStore.Import(ProfileData profile, string encryptedBackup) => ImportSecrets(profile, encryptedBackup);

    private static readonly Dictionary<string, ISecretProvider> _providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dpapi-current-user"] = new DpapiCurrentUserProvider(),
        ["credential-manager"] = new CredentialManagerProvider(),
        ["powershell-secretmanagement"] = new PowerShellSecretManagementProvider(),
        ["vault-kv2"] = new VaultKV2Provider(),
        ["sops"] = new SopsProvider(),
        ["azure-keyvault"] = new AzureKeyVaultProvider(),
        ["1password"] = new OnePasswordProvider(),
        ["aws-secretsmanager"] = new AwsSecretsManagerProvider()
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
            Program.LocalAppDataRoot,
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

    // List providers with REAL availability: the provider's cheap
    // declared Available gate first (env config / binary presence, no I/O), then
    // an end-to-end sentinel probe (same Encrypt/Decrypt/Delete round-trip
    // SetActiveProvider has used since v0.7.5) for network providers only.
    // Local providers never probe, so a scrubbed environment stays deterministic
    // with zero network I/O.
    public static List<(string Name, bool Available)> ListProviders()
    {
        var result = new List<(string, bool)>();
        foreach (var kvp in _providers)
        {
            result.Add((kvp.Key, IsProviderAvailable(kvp.Value)));
        }
        return result;
    }

    // Declared capability descriptors for one provider (machine-readable surface
    // consumed by the `secret-provider list` output and the GUI parser).
    public static (bool RefreshCapable, bool CertAuthRequired, bool RequiresNetwork) GetCapabilities(string name)
    {
        if (!_providers.TryGetValue(name, out var provider))
            throw new InvalidOperationException($"Unknown secret provider: {name}");
        return (provider.RefreshCapable, provider.CertAuthRequired, provider.RequiresNetwork);
    }

    // Availability computation: declared Available gate, then the
    // sentinel round-trip for network providers. Internal so capability tests
    // can pin the composition with fake providers (no network, no cloud).
    internal static bool IsProviderAvailable(ISecretProvider provider)
    {
        try
        {
            if (!provider.Available) return false;
            if (!provider.RequiresNetwork) return true;
            const string probeContext = "__env_manager_compat_probe__";
            string envelope = provider.Encrypt("__probe_value__", probeContext);
            try { provider.Decrypt(envelope, probeContext); } catch { /* async/network providers may not round-trip immediately */ }
            try { provider.Delete(envelope, probeContext); } catch { /* best-effort cleanup */ }
            return true;
        }
        catch
        {
            return false;
        }
    }

    // Get the active provider name from config
    public static string GetActiveProviderName()
    {
        return LoadConfig().ActiveProvider;
    }

    // Set the active provider (persists to config file)
    public static void SetActiveProvider(string name)
    {
        if (!_providers.TryGetValue(name, out var provider))
            throw new InvalidOperationException($"Unknown secret provider: {name}");

        // probe the provider with a sentinel Encrypt/Decrypt round-trip
        // before committing it as the active provider. A provider that cannot
        // complete the round-trip (pwsh missing module, Vault no VAULT_ADDR,
        // cloud credentials missing) is REJECTED here so the user gets an
        // actionable error at config time instead of a CLIXML catastrophe at
        // add-secret time. This matches the PowerToys pattern of validating
        // extension dependencies at config time, not at use time.
        try
        {
            // Use a truly off-name sentinel so a real profile variable named
            // "__compat_probe__" never collides. Delete is best-effort because
            // some providers are pure-local and others have side effects.
            const string probeContext = "__env_manager_compat_probe__";
            string envelope = provider.Encrypt("__probe_value__", probeContext);
            try { provider.Decrypt(envelope, probeContext); } catch { /* async/network providers may not round-trip immediately */ }
            try { provider.Delete(envelope, probeContext); } catch { /* best-effort cleanup */ }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot activate provider '" + name + "': " + ex.Message +
                ". Fix the provider environment first (e.g. install pwsh modules, " +
                "set VAULT_ADDR, or configure cloud credentials).");
        }

        string dir = Path.Combine(
            Program.LocalAppDataRoot,
            "EnvManager");
        Directory.CreateDirectory(dir);

        var config = LoadConfig();
        config.ActiveProvider = name;
        string json = JsonSerializer.Serialize(config, ProviderConfigJsonContext.Default.ProviderConfig);
        File.WriteAllText(GetConfigPath(), json);
    }

    // Phase 3: Key Rotation - re-encrypt all secrets in all profiles with the active provider
    // Returns (totalSecrets, rotatedCount, failedCount).
    // Failures are also classified by the typed SecretProviderException family
    // (failureCounts: authFailed/notFound/permissionDenied/unavailable/timeout/invalidEnvelope/
    // unknown). The (total, rotated, failed) signature and the failed++ skip-and-count
    // semantics are pinned by the frontend source gate (secret-regression.test.ts) and
    // must not change - a failed secret is never deleted, only counted.
    //
    // Both stored forms are rotated: a profile holds either the envelope itself or a
    // "mount:<id>" reference into secretMount.json. The reference form is resolved through
    // the supplied holder and the rotated envelope is written back to the mount, because the
    // mount - not the profile - owns that envelope (GLOSSARY: Secret Mount). Rotation used to
    // hand the bare "mount:<id>" string to the provider, which cannot parse it as an
    // envelope, so every mount-referenced secret failed the pass and was never re-encrypted.
    // The value stayed readable, which is why nobody noticed: it just never rotated.
    public static (int total, int rotated, int failed) RotateAll(System.Collections.Generic.List<ProfileData> profiles, System.Collections.Generic.Dictionary<string, int>? failureCounts, ISecretEnvelopeHolder envelopeHolder)
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
                    RotateStoredValue(profile, v, envelopeHolder);
                    rotated++;
                }
                catch (Exception ex)
                {
                    // Decryption failed (wrong provider, deleted CredMan entry, dangling mount
                    // reference, etc.): skip + count, never delete.
                    failed++;
                    RecordFailure(failureCounts, ex);
                }
            }
        }
        return (total, rotated, failed);
    }

    // Re-encrypt one stored secret value and put it back where it came from: a profile holds
    // either the envelope itself (inline form) or a "mount:<id>" reference whose envelope
    // lives in the SecretMount aggregate. Only the reference needs the holder, and only the
    // holder owns that write-back.
    static void RotateStoredValue(ProfileData profile, ProfileVariable variable, ISecretEnvelopeHolder envelopeHolder)
    {
        string context = profile.Name + "\\" + variable.Name;
        // Mount-referenced secrets are decryptable through the mount that owns the envelope;
        // the reference itself is not an envelope.
        bool byReference = envelopeHolder.IsReference(variable.Value);
        string storedEnvelope = byReference ? envelopeHolder.ResolveReference(variable.Value) : variable.Value;
        // Decrypt with whatever provider encrypted it, re-encrypt with the active provider
        string plaintext = Decrypt(storedEnvelope, context);
        string rotatedEnvelope = Encrypt(plaintext, context);
        if (byReference) envelopeHolder.CommitReference(variable.Value, rotatedEnvelope);
        else variable.Value = rotatedEnvelope;
    }

    // Classified failure bucket behind the failed counter; a null dictionary means the
    // caller only wants the three counts.
    static void RecordFailure(System.Collections.Generic.Dictionary<string, int>? failureCounts, Exception ex)
    {
        if (failureCounts == null) return;
        string cat = SecretProviderErrors.Classify(ex);
        failureCounts[cat] = failureCounts.TryGetValue(cat, out int n) ? n + 1 : 1;
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
