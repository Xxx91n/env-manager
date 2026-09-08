using EnvManager.Secrets.Core;
// AzureKeyVaultProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

// ticket 40: adapter-boundary typed error family. HTTP failures map through
// SecretProviderErrors; stderr diagnostics from the identity-token path are
// scrubbed (ADR 0005); best-effort cleanup reports through the scrubbed channel.

using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager.Secrets.Providers;

// --- Phase 7: Azure Key Vault Provider ---

internal sealed class AzureKeyVaultProvider : ISecretProvider
{
    public string Name => "azure-keyvault";

    // Capability descriptors (ticket 41, spec Phase 6): declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => true;   // service-principal certificate auth model
    public bool RequiresNetwork => true;

    // Cheap gate mirroring Encrypt's fail-fast precondition (no network I/O).
    public bool Available => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URI"));

    // Envelope: { provider, version, createdAt, targetName (vaultUri|secretName) }
    // The profile stores only the vault URI and secret name.
    // The actual secret value lives in Azure Key Vault and is fetched via REST API.

    private const string API_VERSION = "7.4";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";

        string vaultUri = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URI")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpEncrypt,
                "AZURE_KEYVAULT_URI environment variable not set (e.g. https://myvault.vault.azure.net)");

        // Enforce TLS: Azure Key Vault is always HTTPS
        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpEncrypt, "Azure Key Vault requires HTTPS (TLS mandatory)");

        string secretName = context != null
            ? SanitizeSecretName(context)
            : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        string token = GetBearerToken(SecretProviderErrors.OpEncrypt);

        // Build PUT request body
        string payload = "{\"value\":\"" + JsonEscape(plaintext) + "\"}";
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = Timeout;

        var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpEncrypt,
            () => client.PutAsync(apiUrl, content).GetAwaiter().GetResult());
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
            TargetName = vaultUri.TrimEnd('/') + "|" + secretName
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
            throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Missing targetName in envelope", parsed.TargetName);

        int pipeIdx = parsed.TargetName.IndexOf('|');
        if (pipeIdx < 0)
            throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid targetName format, expected vaultUri|secretName", parsed.TargetName);

        string vaultUri = parsed.TargetName.Substring(0, pipeIdx);
        string secretName = parsed.TargetName.Substring(pipeIdx + 1);

        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpDecrypt, "Azure Key Vault requires HTTPS (TLS mandatory)");

        string token = GetBearerToken(SecretProviderErrors.OpDecrypt);
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = Timeout;

        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpDecrypt,
            () => client.GetAsync(apiUrl).GetAwaiter().GetResult());
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new SecretProviderNotFoundException(Name, SecretProviderErrors.OpDecrypt, $"Azure Key Vault secret {secretName} not found", parsed.TargetName);
            throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpDecrypt, response.StatusCode, "read", err, parsed.TargetName);
        }

        return SecretProviderErrors.Send(Name, SecretProviderErrors.OpDecrypt, () =>
        {
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("value", out var val))
                return val.GetString() ?? "";
            throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpDecrypt, "Azure Key Vault response does not contain value field", parsed.TargetName);
        });
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            try
            {
                int pipeIdx = parsed.TargetName.IndexOf('|');
                if (pipeIdx < 0) return;

                string vaultUri = parsed.TargetName.Substring(0, pipeIdx);
                string secretName = parsed.TargetName.Substring(pipeIdx + 1);

                if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return;

                string token = GetBearerToken(SecretProviderErrors.OpDelete);
                string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
                client.Timeout = Timeout;
                SecretProviderErrors.Send(Name, SecretProviderErrors.OpDelete,
                    () => client.DeleteAsync(apiUrl).GetAwaiter().GetResult());
            }
            catch (Exception ex) { SecretProviderErrors.SwallowBestEffort(Name, SecretProviderErrors.OpDelete, ex); }
        }
    }

    public bool CanRotate => true;

    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        return Encrypt(plaintext, context);
    }

    private string GetBearerToken(string operation)
    {
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedToken;

        string? token = TryGetManagedIdentityToken() ?? TryGetServicePrincipalToken();

        if (string.IsNullOrEmpty(token))
            throw new SecretProviderAuthFailedException(Name, operation,
                "Failed to obtain Azure access token. Either run on an Azure VM with managed identity, " +
                "or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.");

        _cachedToken = token;
        return token;
    }

    private string? TryGetManagedIdentityToken()
    {
        try
        {
            // App Service / emulator convention (issue 15): IDENTITY_ENDPOINT overrides
            // the hardcoded IMDS URL, IDENTITY_HEADER names the auth header. Production
            // never sets these; the L1 Lowkey Vault emulator does.
            string? identityEndpoint = Environment.GetEnvironmentVariable("IDENTITY_ENDPOINT");
            string imdsUrl;
            if (!string.IsNullOrEmpty(identityEndpoint))
            {
                string joiner = identityEndpoint.Contains('?') ? "&" : "?";
                imdsUrl = identityEndpoint + joiner + "api-version=2018-02-01&resource=https://vault.azure.net";
            }
            else
            {
                imdsUrl = "http://169.254.169.254/metadata/identity/oauth2/token" +
                    "?api-version=2018-02-01&resource=https://vault.azure.net";
            }

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("Metadata", "true");
            string? identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            if (!string.IsNullOrEmpty(identityEndpoint) && !string.IsNullOrEmpty(identityHeader))
            {
                client.DefaultRequestHeaders.Add("X-IDENT-HEADER", identityHeader);
            }
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = client.GetAsync(imdsUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                // issue 15 diagnostics: surface the emulator/IMDS rejection reason; silent
                // null made the L1 lane failure undiagnosable (CI run 33853880605).
                string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                Console.Error.WriteLine("[azure-keyvault] identity token request failed: " +
                    (int)response.StatusCode + " " + Program.ScrubExceptionMessage(body[..Math.Min(300, body.Length)]));
                return null;
            }

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
            {
                if (doc.RootElement.TryGetProperty("expires_on", out var exp))
                {
                    if (long.TryParse(exp.GetString(), out long expUnix))
                        _tokenExpiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                }
                return token.GetString();
            }
            return null;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("[azure-keyvault] identity token request threw: " + ex.GetType().Name + ": " + Program.ScrubExceptionMessage(ex.Message));
            return null;
        }
    }

    private string? TryGetServicePrincipalToken()
    {
        string? clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        string? tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
            return null;

        try
        {
            string tokenUrl = "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/token";

            string formData = "client_id=" + Uri.EscapeDataString(clientId) +
                "&client_secret=" + Uri.EscapeDataString(clientSecret) +
                "&scope=" + Uri.EscapeDataString("https://vault.azure.net/.default") +
                "&grant_type=client_credentials";

            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var content = new System.Net.Http.StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = client.PostAsync(tokenUrl, content).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
            {
                if (doc.RootElement.TryGetProperty("expires_in", out var exp))
                {
                    if (int.TryParse(exp.GetString(), out int expSeconds))
                        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expSeconds);
                }
                return token.GetString();
            }
            return null;
        }
        catch (Exception ex)
        {
            SecretProviderErrors.SwallowBestEffort(Name, "identity-token", ex);
            return null;
        }
    }

    private static string SanitizeSecretName(string s)
    {
        // Azure Key Vault secret names: alphanumeric and hyphens only, max 127 chars
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '-')
                sb.Append(c);
            else
                sb.Append('-');
        }
        string result = sb.ToString().Trim('-');
        if (result.Length > 127) result = result.Substring(0, 127);
        return result;
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
}
