using EnvManager.Secrets.Core;
// AzureKeyVaultProvider.cs - secret provider architecture
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

// Adapter-boundary typed error family. HTTP failures map through
// SecretProviderErrors; stderr diagnostics from the identity-token path are
// scrubbed by ScrubExceptionMessage; best-effort cleanup reports through the scrubbed channel.

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
    // Test seam only: production always uses the parameterless constructor, so no
    // environment switch can redirect a request. Same shape as AwsRolesAnywhereProvider.
    private readonly HttpMessageHandler? _handler;

    public AzureKeyVaultProvider() { }

    internal AzureKeyVaultProvider(HttpMessageHandler handler) { _handler = handler; }

    // One place that builds the client, so the seam above covers every request this
    // adapter sends and a test can assert the probe's own wire traffic.
    private HttpClient CreateClient()
    {
        return _handler == null
            ? new System.Net.Http.HttpClient()
            : new System.Net.Http.HttpClient(_handler, disposeHandler: false);
    }

    public string Name => "azure-keyvault";

    // Capability descriptors: declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => true;   // service-principal certificate auth model
    public bool RequiresNetwork => true;

    // Cheap gate mirroring Encrypt's fail-fast precondition (no network I/O).
    public bool Available => !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URI"));

    // The availability probe reads; it never writes. `list` runs on every
    // profile-editor mount, and the old sentinel round-trip created and then deleted a
    // secret in the customer's vault each time.
    public bool ProbeWritesData => false;

    // Budget for the read-only probe. Shorter than the real calls' 15s for the same
    // reason the Vault probe is 5s: it sits in front of a GUI page load.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    // Read-only probe: GET a secret name that cannot exist. `SecretNotFound` (404) proves
    // the identity may read secret values in this vault; 401 means the token could not be
    // obtained and 403 means the identity authenticated but lacks the data-plane role.
    // The roles matter here: Key Vault Reader can list metadata and still cannot read a
    // value, so "the request got an answer" is not enough - only 404 is.
    public void ProbeReadOnly()
    {
        string vaultUri = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URI")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpProbe,
                "AZURE_KEYVAULT_URI environment variable not set (e.g. https://myvault.vault.azure.net)");

        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpProbe, "Azure Key Vault requires HTTPS (TLS mandatory)");

        // One deadline for the whole probe, not 5s per stage: a managed-identity fetch
        // that fails, a service-principal fetch that succeeds late, and the data-plane GET
        // would otherwise add up to near 15s, and `list` probes providers one after
        // another in front of a page load. `list` runs on each profile-editor mount.
        var deadline = DateTimeOffset.UtcNow.Add(ProbeTimeout);
        string token = GetBearerToken(SecretProviderErrors.OpProbe, RemainingProbeBudget(deadline));
        string probeName = "env-manager-probe-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + probeName + "?api-version=" + API_VERSION;

        using var client = CreateClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = RemainingProbeBudget(deadline);

        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpProbe,
            () => client.GetAsync(apiUrl).GetAwaiter().GetResult());
        string body = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        if ((int)response.StatusCode == 404 &&
            body.Contains("SecretNotFound", StringComparison.OrdinalIgnoreCase))
            return;

        // A 404 without SecretNotFound is not the answer the probe expects; everything
        // else maps onto the typed family (401 -> auth, 403 -> permission).
        throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpProbe, response.StatusCode, "probe", body);
    }

    // What is left of the probe budget. A spent budget cannot be handed to HttpClient
    // (a zero Timeout means "no limit"), so the exhausted case gets a deliberately tiny
    // positive window: the next request fails fast instead of stalling the page load.
    private static TimeSpan RemainingProbeBudget(DateTimeOffset deadline)
    {
        TimeSpan left = deadline - DateTimeOffset.UtcNow;
        return left > TimeSpan.FromMilliseconds(100) ? left : TimeSpan.FromMilliseconds(100);
    }

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

        using var client = CreateClient();
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

        using var client = CreateClient();
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

                using var client = CreateClient();
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

    // `timeout` is the budget for the token acquisition, shared between the two attempts
    // (the probe passes its remaining deadline so the whole probe stays inside one window;
    // the real calls keep the 10s default).
    private string GetBearerToken(string operation, TimeSpan? timeout = null)
    {
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedToken;

        TimeSpan budget = timeout ?? TimeSpan.FromSeconds(10);
        // The managed-identity attempt usually has nothing to talk to off Azure, and it is
        // tried first. Giving it the whole budget means a service-principal fallback starts
        // with nothing left, so it gets at most half.
        TimeSpan firstAttempt = budget > TimeSpan.FromMilliseconds(200)
            ? TimeSpan.FromMilliseconds(Math.Max(100, budget.TotalMilliseconds / 2))
            : budget;
        string? token = TryGetManagedIdentityToken(firstAttempt) ?? TryGetServicePrincipalToken(budget);
        if (string.IsNullOrEmpty(token))
            throw new SecretProviderAuthFailedException(Name, operation,
                "Failed to obtain Azure access token. Either run on an Azure VM with managed identity, " +
                "or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.");

        _cachedToken = token;
        return token;
    }

    private string? TryGetManagedIdentityToken(TimeSpan timeout)
    {
        try
        {
            // App Service / emulator convention: IDENTITY_ENDPOINT overrides
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

            using var client = CreateClient();
            client.DefaultRequestHeaders.Add("Metadata", "true");
            string? identityHeader = Environment.GetEnvironmentVariable("IDENTITY_HEADER");
            if (!string.IsNullOrEmpty(identityEndpoint) && !string.IsNullOrEmpty(identityHeader))
            {
                client.DefaultRequestHeaders.Add("X-IDENT-HEADER", identityHeader);
            }
            client.Timeout = timeout;

            var response = client.GetAsync(imdsUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
            {
                // diagnostics: surface the emulator/IMDS rejection reason; silent
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

    private string? TryGetServicePrincipalToken(TimeSpan timeout)
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

            using var client = CreateClient();
            client.Timeout = timeout;

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
