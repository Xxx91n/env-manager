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

// --- Phase 1: SecretProviderManager (routes to active provider) ---




// --- Phase 7: Azure Key Vault Provider ---

internal sealed class AzureKeyVaultProvider : ISecretProvider
{
    public string Name => "azure-keyvault";

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
            ?? throw new InvalidOperationException(
                "AZURE_KEYVAULT_URI environment variable not set (e.g. https://myvault.vault.azure.net)");

        // Enforce TLS: Azure Key Vault is always HTTPS
        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure Key Vault requires HTTPS (TLS mandatory)");

        string secretName = context != null
            ? SanitizeSecretName(context)
            : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        string token = GetBearerToken();

        // Build PUT request body
        string payload = "{\"value\":\"" + JsonEscape(plaintext) + "\"}";
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = Timeout;

        var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
        var response = client.PutAsync(apiUrl, content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Azure Key Vault write failed ({response.StatusCode}): {err}");
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
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName))
            throw new InvalidOperationException("Missing targetName in envelope");

        int pipeIdx = parsed.TargetName.IndexOf('|');
        if (pipeIdx < 0)
            throw new InvalidOperationException("Invalid targetName format, expected vaultUri|secretName");

        string vaultUri = parsed.TargetName.Substring(0, pipeIdx);
        string secretName = parsed.TargetName.Substring(pipeIdx + 1);

        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure Key Vault requires HTTPS (TLS mandatory)");

        string token = GetBearerToken();
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = Timeout;

        var response = client.GetAsync(apiUrl).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Azure Key Vault secret {secretName} not found");
            throw new InvalidOperationException($"Azure Key Vault read failed ({response.StatusCode}): {err}");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("value", out var val))
            return val.GetString() ?? "";
        throw new InvalidOperationException("Azure Key Vault response does not contain value field");
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

                string token = GetBearerToken();
                string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
                client.Timeout = Timeout;
                client.DeleteAsync(apiUrl).GetAwaiter().GetResult();
            }
            catch { }
        }
    }

    public bool CanRotate => true;

    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        return Encrypt(plaintext, context);
    }

    private string GetBearerToken()
    {
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedToken;

        string? token = TryGetManagedIdentityToken() ?? TryGetServicePrincipalToken();

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Failed to obtain Azure access token. Either run on an Azure VM with managed identity, " +
                "or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.");

        _cachedToken = token;
        return token;
    }

    private string? TryGetManagedIdentityToken()
    {
        try
        {
            string imdsUrl = "http://169.254.169.254/metadata/identity/oauth2/token" +
                "?api-version=2018-02-01&resource=https://vault.azure.net";

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("Metadata", "true");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = client.GetAsync(imdsUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

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
        catch
        {
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
        catch
        {
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

// --- Phase 8: 1Password CLI Provider ---

internal sealed class OnePasswordProvider : ISecretProvider
{
    public string Name => "1password";

    // Envelope: { provider, version, createdAt, targetName (vault|itemId|field) }
    // The actual secret value is fetched via the 1Password CLI (op) at launch time.

    private static readonly string OP_BINARY = FindOpBinary();

    private static string FindOpBinary()
    {
        string envPath = Environment.GetEnvironmentVariable("OP_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        string[] searchDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string dir in searchDirs)
        {
            string candidate = Path.Combine(dir.Trim('"'), "op.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.Trim('"'), "op");
            if (File.Exists(candidate)) return candidate;
        }

        string[] commonPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "1Password CLI", "op.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "1Password CLI", "op.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "1Password CLI", "op.exe")
        };
        foreach (string p in commonPaths)
            if (File.Exists(p)) return p;

        return "op";
    }

    private static void EnsureOpAvailable()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OP_BINARY,
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("1Password CLI (op) binary not found");
            proc.WaitForExit(5000);
            if (!proc.HasExited || proc.ExitCode != 0)
                throw new InvalidOperationException("1Password CLI (op) binary not functional");
            // v0.9.13 Phase 4F: record provider binary hash for tamper detection
            try { Program.RecordProviderHash("op", OP_BINARY); } catch { }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "1Password CLI (op) not found. Install op and ensure it is on PATH, or set OP_PATH env var.");
        }
    }

    public string Encrypt(string plaintext, string? context = null)
    {
        EnsureOpAvailable();
        string vaultName = Environment.GetEnvironmentVariable("OP_VAULT") ?? "EnvManager";
        string itemName = context != null
            ? context.Split('/')[0]
            : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OP_BINARY,
            Arguments = "item create --category=Password --title=" + ShellQuote(itemName) + " --vault=" + ShellQuote(vaultName) + " password=" + ShellQuote(plaintext ?? "") + " --format=json",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        string[] opEnvVars = { "OP_ACCOUNT", "OP_SERVICE_ACCOUNT_TOKEN", "OP_ACCESS_TOKEN" };
        foreach (var envVar in opEnvVars)
        {
            var v = Environment.GetEnvironmentVariable(envVar);
            if (v != null) psi.EnvironmentVariables[envVar] = v;
        }

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start op process");
        proc.WaitForExit(30000);
        if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("1Password CLI timed out"); }
        if (proc.ExitCode != 0)
        {
            string stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException("1Password CLI create failed (exit " + proc.ExitCode + "): " + stderr);
        }

        string json = proc.StandardOutput.ReadToEnd();
        string itemId = "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var id)) itemId = id.GetString() ?? "";
        }
        catch { }

        var env = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = vaultName + "|" + itemId + "|password"
        };
        return env.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name) throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName)) throw new InvalidOperationException("Missing targetName in envelope");

        EnsureOpAvailable();
        var parts = parsed.TargetName.Split('|');
        if (parts.Length < 2) throw new InvalidOperationException("Invalid targetName format, expected vault|itemId|field");

        string itemId = parts[1];
        string fieldName = parts.Length > 2 ? parts[2] : "password";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OP_BINARY,
            Arguments = "item get " + ShellQuote(itemId) + " --field " + ShellQuote(fieldName) + " --reveal",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        string[] opEnvVars = { "OP_ACCOUNT", "OP_SERVICE_ACCOUNT_TOKEN", "OP_ACCESS_TOKEN" };
        foreach (var envVar in opEnvVars)
        {
            var v = Environment.GetEnvironmentVariable(envVar);
            if (v != null) psi.EnvironmentVariables[envVar] = v;
        }

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start op process");
        proc.WaitForExit(30000);
        if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("1Password CLI timed out"); }
        if (proc.ExitCode != 0)
        {
            string stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException("1Password CLI get failed (exit " + proc.ExitCode + "): " + stderr);
        }
        return proc.StandardOutput.ReadToEnd().TrimEnd();
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed == null || string.IsNullOrEmpty(parsed.TargetName)) return;
        try
        {
            EnsureOpAvailable();
            var parts = parsed.TargetName.Split('|');
            if (parts.Length < 2) return;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OP_BINARY,
                Arguments = "item delete " + ShellQuote(parts[1]) + " --archive",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null) proc.WaitForExit(15000);
        }
        catch { }
    }

    public bool CanRotate => true;
    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        Delete(oldEnvelope, context);
        return Encrypt(plaintext, context);
    }

    private static string ShellQuote(string s)
    {
        // Use double-quote wrapping with internal quote doubling
        return "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
    }
}

// --- Phase 9: AWS Secrets Manager Provider ---

internal sealed class AwsSecretsManagerProvider : ISecretProvider
{
    public string Name => "aws-secretsmanager";

    // Envelope: { provider, version, createdAt, targetName (region|secretId) }
    // Uses AWS SigV4 signed REST API calls. TLS mandatory (HTTPS only).

    private const string SERVICE = "secretsmanager";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        string region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? throw new InvalidOperationException("AWS_REGION or AWS_DEFAULT_REGION not set");
        string secretId = context != null ? SanitizeSecretId(context) : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        string body = "{\"Name\":\"" + JsonEscape(secretId) + "\",\"SecretString\":\"" + JsonEscape(plaintext) + "\"}";
        var response = CallAwsApi(region, "secretsmanager.CreateSecret", body);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AWS create failed ({response.StatusCode}): {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

        var env = new SecretEnvelope { Provider = Name, Version = 1, CreatedAt = DateTimeOffset.UtcNow.ToString("O"), TargetName = region + "|" + secretId };
        return env.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope) ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name) throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName)) throw new InvalidOperationException("Missing targetName");

        var parts = parsed.TargetName.Split('|');
        if (parts.Length < 2) throw new InvalidOperationException("Invalid targetName format");
        string region = parts[0];
        string secretId = parts[1];

        string body = "{\"SecretId\":\"" + JsonEscape(secretId) + "\"}";
        var response = CallAwsApi(region, "secretsmanager.GetSecretValue", body);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AWS read failed ({response.StatusCode}): {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("SecretString", out var val)) return val.GetString() ?? "";
        throw new InvalidOperationException("AWS response does not contain SecretString");
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed == null || string.IsNullOrEmpty(parsed.TargetName)) return;
        try
        {
            var parts = parsed.TargetName.Split('|');
            if (parts.Length < 2) return;
            string body = "{\"SecretId\":\"" + JsonEscape(parts[1]) + "\",\"ForceDeleteWithoutRecovery\":true}";
            CallAwsApi(parts[0], "secretsmanager.DeleteSecret", body);
        }
        catch { }
    }

    public bool CanRotate => true;
    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        var parsed = SecretEnvelope.TryParse(oldEnvelope);
        if (parsed == null || string.IsNullOrEmpty(parsed.TargetName)) return Encrypt(plaintext, context);
        var parts = parsed.TargetName.Split('|');
        if (parts.Length < 2) return Encrypt(plaintext, context);
        string body = "{\"SecretId\":\"" + JsonEscape(parts[1]) + "\",\"SecretString\":\"" + JsonEscape(plaintext) + "\"}";
        var resp = CallAwsApi(parts[0], "secretsmanager.PutSecretValue", body);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("AWS rotation (PutSecretValue) failed");
        return oldEnvelope;
    }

    private static System.Net.Http.HttpResponseMessage CallAwsApi(string region, string target, string body)
    {
        string host = "secretsmanager." + region + ".amazonaws.com";
        string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "";
        string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "";
        string sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN") ?? "";
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new InvalidOperationException("AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY required");

        string amzDate = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        string credentialScope = dateStamp + "/" + region + "/" + SERVICE + "/aws4_request";

        string canonicalHeaders = "content-type:application/x-amz-json-1.1\nhost:" + host + "\nx-amz-date:" + amzDate + "\n" +
            (!string.IsNullOrEmpty(sessionToken) ? "x-amz-security-token:" + sessionToken + "\n" : "");
        string signedHeaders = "content-type;host;x-amz-date" + (!string.IsNullOrEmpty(sessionToken) ? ";x-amz-security-token" : "");
        string payloadHash = HexSHA256(body);
        string canonicalRequest = "POST\n/\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
        string stringToSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + credentialScope + "\n" + HexSHA256(canonicalRequest);

        byte[] kDate = HmacSHA256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        byte[] kRegion = HmacSHA256(kDate, region);
        byte[] kService = HmacSHA256(kRegion, SERVICE);
        byte[] kSigning = HmacSHA256(kService, "aws4_request");
        byte[] signature = HmacSHA256(kSigning, stringToSign);
        string auth = "AWS4-HMAC-SHA256 Credential=" + accessKey + "/" + credentialScope + ", SignedHeaders=" + signedHeaders + ", Signature=" + BytesToHex(signature);

        using var client = new System.Net.Http.HttpClient();
        client.Timeout = Timeout;
        var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/x-amz-json-1.1");
        // Per AWS SigV4: Authorization, X-Amz-Target, X-Amz-Date, X-Amz-Security-Token
        // are REQUEST headers, not content headers. Adding "Authorization" to
        // HttpContent throws "Misused header name, 'Authorization'" because the
        // .NET dispatcher treats it as a content header.
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://" + host + "/");
        request.Content = content;
        request.Headers.Add("X-Amz-Target", target);
        request.Headers.Add("X-Amz-Date", amzDate);
        request.Headers.Add("Authorization", auth);
        if (!string.IsNullOrEmpty(sessionToken)) request.Headers.Add("X-Amz-Security-Token", sessionToken);
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static string HexSHA256(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return BytesToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
    }

    private static byte[] HmacSHA256(byte[] key, string data) => new System.Security.Cryptography.HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));
    private static byte[] HmacSHA256(byte[] key, byte[] data) => new System.Security.Cryptography.HMACSHA256(key).ComputeHash(data);

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string SanitizeSecretId(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) { if (char.IsLetterOrDigit(c) || "/_+=.@-".IndexOf(c) >= 0) sb.Append(c); else sb.Append('-'); }
        string r = sb.ToString();
        return r.Length > 512 ? r.Substring(0, 512) : r;
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) {
            switch (c) {
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

internal static class SecretProviderManager
{
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
        if (!_providers.TryGetValue(name, out var provider))
            throw new InvalidOperationException($"Unknown secret provider: {name}");

        // v0.7.5: probe the provider with a sentinel Encrypt/Decrypt round-trip
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
