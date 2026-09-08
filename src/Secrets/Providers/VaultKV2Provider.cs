using EnvManager.Secrets.Core;
// VaultKV2Provider.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

// ticket 40: adapter-boundary typed error family. HTTP failures map through
// SecretProviderErrors (family message scrubbed once by the base constructor);
// best-effort cleanup keeps its bare-catch semantics but reports through the
// scrubbed family channel.

using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager.Secrets.Providers;

// --- Phase 5: HashiCorp Vault KV v2 Adapter ---

internal sealed class VaultKV2Provider : ISecretProvider
{
    public string Name => "vault-kv2";

    // Capability descriptors (ticket 41, spec Phase 6): declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => true;   // AppRole / TLS client-cert auth model
    public bool RequiresNetwork => true;

    // Cheap gate mirroring Encrypt's fail-fast preconditions (no network I/O).
    public bool Available =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAULT_ADDR")) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAULT_TOKEN"));

    // Envelope: { provider, version, mountPath, secretPath, secretKey }
    // The profile stores only the mount path, secret path, and key name
    // The actual secret value is fetched from Vault's KV v2 secret engine via HTTP API

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";

        // Vault KV v2 stores secrets as key-value maps
        // The context string provides the secret path; we use a fixed key "value"
        string secretPath = context != null
            ? "env-manager/" + SanitizePath(context)
            : "env-manager/" + Guid.NewGuid().ToString("N");

        // Write to Vault
        string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpEncrypt, "VAULT_ADDR environment variable not set");
        string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN")
            ?? throw new SecretProviderAuthFailedException(Name, SecretProviderErrors.OpEncrypt, "VAULT_TOKEN environment variable not set");

        // Enforce TLS (refuse http:// unless explicitly localhost)
        if (!vaultAddr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLocalhost(vaultAddr))
                throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpEncrypt, "TLS mandatory: VAULT_ADDR must use https:// for non-localhost addresses");
        }

        // Build JSON payload: { "data": { "value": "<plaintext>" } }
        string payload = "{\"data\":{\"value\":\"" + JsonEscape(plaintext) + "\"}}";

        string apiUrl = vaultAddr.TrimEnd('/') + "/v1/secret/data/" + secretPath;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
        client.Timeout = TimeSpan.FromSeconds(10);

        var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpEncrypt,
            () => client.PostAsync(apiUrl, content).GetAwaiter().GetResult());
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpEncrypt, response.StatusCode, "write", err);
        }

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = "secret/" + secretPath + ":value"
        };
        return envelope.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, $"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName))
            throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Missing targetName in envelope");

        // TargetName format: "secret/<path>:value"
        // Extract mount (secret), path, and key (value)
        int colonIdx = parsed.TargetName.IndexOf(':');
        string secretKey = colonIdx >= 0 ? parsed.TargetName.Substring(colonIdx + 1) : "value";
        string mountAndPath = colonIdx >= 0 ? parsed.TargetName.Substring(0, colonIdx) : parsed.TargetName;
        int slashIdx = mountAndPath.IndexOf('/');
        string mount = slashIdx >= 0 ? mountAndPath.Substring(0, slashIdx) : "secret";
        string secretPath = slashIdx >= 0 ? mountAndPath.Substring(slashIdx + 1) : "";

        string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpDecrypt, "VAULT_ADDR environment variable not set");
        string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN")
            ?? throw new SecretProviderAuthFailedException(Name, SecretProviderErrors.OpDecrypt, "VAULT_TOKEN environment variable not set");

        if (!vaultAddr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLocalhost(vaultAddr))
                throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpDecrypt, "TLS mandatory: VAULT_ADDR must use https:// for non-localhost addresses");
        }

        string apiUrl = vaultAddr.TrimEnd('/') + "/v1/" + mount + "/data/" + secretPath;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
        client.Timeout = TimeSpan.FromSeconds(10);

        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpDecrypt,
            () => client.GetAsync(apiUrl).GetAwaiter().GetResult());
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpDecrypt, response.StatusCode, "read", err);
        }

        return SecretProviderErrors.Send(Name, SecretProviderErrors.OpDecrypt, () =>
        {
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            // Parse the Vault KV v2 response: { "data": { "data": { "value": "<plaintext>" } } }
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var data = doc.RootElement.GetProperty("data").GetProperty("data");
            if (data.TryGetProperty(secretKey, out var val))
            {
                return val.GetString() ?? "";
            }
            throw new SecretProviderNotFoundException(Name, SecretProviderErrors.OpDecrypt, $"Key '{secretKey}' not found in Vault secret at path '{mount}/{secretPath}'", parsed.TargetName);
        });
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            try
            {
                string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR");
                string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN");
                if (vaultAddr == null || vaultToken == null) return;

                int colonIdx = parsed.TargetName.IndexOf(':');
                string mountAndPath = colonIdx >= 0 ? parsed.TargetName.Substring(0, colonIdx) : parsed.TargetName;
                int slashIdx = mountAndPath.IndexOf('/');
                string mount = slashIdx >= 0 ? mountAndPath.Substring(0, slashIdx) : "secret";
                string secretPath = slashIdx >= 0 ? mountAndPath.Substring(slashIdx + 1) : "";

                string apiUrl = vaultAddr.TrimEnd('/') + "/v1/" + mount + "/metadata/" + secretPath;

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
                client.Timeout = TimeSpan.FromSeconds(10);
                SecretProviderErrors.Send(Name, SecretProviderErrors.OpDelete,
                    () => client.DeleteAsync(apiUrl).GetAwaiter().GetResult());
            }
            catch (Exception ex) { SecretProviderErrors.SwallowBestEffort(Name, SecretProviderErrors.OpDelete, ex); }
        }
    }

    private static string SanitizePath(string s)
    {
        return s.Replace("\\", "/").Replace(":", "_").Replace(" ", "_");
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static bool IsLocalhost(string addr)
    {
        return addr.Contains("127.0.0.1") || addr.Contains("localhost") || addr.Contains("[::1]");
    }
}
