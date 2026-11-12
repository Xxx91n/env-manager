using EnvManager.Secrets.Core;
// VaultKV2Provider.cs - secret provider architecture
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

// Adapter-boundary typed error family. HTTP failures map through
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
    // Test seam only: production always uses the parameterless constructor, so no
    // environment switch can redirect a request. Same shape as AwsRolesAnywhereProvider.
    private readonly HttpMessageHandler? _handler;

    public VaultKV2Provider() { }

    internal VaultKV2Provider(HttpMessageHandler handler) { _handler = handler; }

    // One place that builds the client, so the seam above covers every request this
    // adapter sends and a test can assert the probe's own wire traffic. The timeout
    // stays at each call site: it differs per request, and the timeout pins read it
    // there.
    private HttpClient CreateClient()
    {
        return _handler == null
            ? new System.Net.Http.HttpClient()
            : new System.Net.Http.HttpClient(_handler, disposeHandler: false);
    }

    public string Name => "vault-kv2";

    // Capability descriptors: declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => true;   // AppRole / TLS client-cert auth model
    public bool RequiresNetwork => true;

    // Cheap gate mirroring Encrypt's fail-fast preconditions (no network I/O).
    public bool Available =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAULT_ADDR")) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("VAULT_TOKEN"));

    // The availability probe reads; it never writes. `list` runs on every profile-editor
    // mount, and the old sentinel round-trip created and then deleted a key in the
    // customer's Vault each time.
    public bool ProbeWritesData => false;

    // Budget for the read-only probe, deliberately shorter than the real calls' 10s: it
    // waits in front of a GUI page load, and a Vault that cannot return a 404 in 5s is
    // down for practical purposes.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    // Read-only probe: GET a path that cannot exist, under the same mount and prefix
    // Encrypt writes into. A 404 with an empty error list means the token may read this
    // path - which is the capability a real secret round-trip needs. A 403 means the
    // policy does not grant it, or the token is dead; Vault answers 403 for both on
    // purpose, so one reason code must cover the pair. A 404 that names a missing route
    // is a different failure: the mount itself does not exist, so Encrypt could not
    // write there either.
    public void ProbeReadOnly()
    {
        string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpProbe, "VAULT_ADDR environment variable not set");
        string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN")
            ?? throw new SecretProviderAuthFailedException(Name, SecretProviderErrors.OpProbe, "VAULT_TOKEN environment variable not set");

        if (!vaultAddr.StartsWith("https://", StringComparison.OrdinalIgnoreCase) && !IsLocalhost(vaultAddr))
            throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpProbe, "TLS mandatory: VAULT_ADDR must use https:// for non-localhost addresses");

        string probePath = "env-manager/__env_manager_probe__" + Guid.NewGuid().ToString("N");
        string apiUrl = vaultAddr.TrimEnd('/') + "/v1/secret/data/" + probePath;

        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
        client.Timeout = ProbeTimeout;

        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpProbe,
            () => client.GetAsync(apiUrl).GetAwaiter().GetResult());
        if (response.IsSuccessStatusCode)
        {
            // Nothing should live at this path. Data coming back means either a name
            // collision with a real secret or a misbehaving mount; either way the probe
            // says nothing useful, so fail closed instead of claiming availability.
            throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpProbe,
                "Vault returned data for a probe path that must not exist (" + probePath + ")");
        }

        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if ((int)response.StatusCode == 404)
        {
            // Two 404s look alike and mean opposite things, and both arrive with the same
            // status: `{"errors":[]}` is "this path does not exist under a mount you may
            // read", while a named route failure is "there is no such mount". Anything
            // else - an empty body from a proxy, a WAF page, a truncated reply - is not
            // Vault saying "you may read here", so it must not pass.
            if (body.Contains("no handler for route", StringComparison.OrdinalIgnoreCase))
                throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpProbe,
                    "Vault has no secret mount at the path Encrypt writes to (" + body + ")");
            if (IsEmptyErrorList(body))
                return;
            throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpProbe,
                "Vault answered 404 without the expected empty error list; something other than Vault may be answering at VAULT_ADDR");
        }

        throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpProbe, response.StatusCode, "probe", body);
    }

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

        using var client = CreateClient();
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

        using var client = CreateClient();
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

    // Vault's "not found, and you may read here" answer: an errors array with nothing in
    // it. Parsed rather than substring-matched so `{"errors":[]}` passes and
    // `{"errors":["..."]}` does not, without caring about surrounding whitespace.
    private static bool IsEmptyErrorList(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return false;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(body);
            return doc.RootElement.TryGetProperty("errors", out var errors)
                && errors.ValueKind == System.Text.Json.JsonValueKind.Array
                && errors.GetArrayLength() == 0;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
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

                using var client = CreateClient();
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
