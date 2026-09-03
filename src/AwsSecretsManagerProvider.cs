// AwsSecretsManagerProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

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
        // Official AWS service-specific endpoint override convention (issue 15):
        // AWS_ENDPOINT_URL_SECRETS_MANAGER redirects all Secrets Manager calls -
        // production deployments never set it; the L1 LocalStack emulator does.
        // TLS stays mandatory for the default endpoint; an explicit override may
        // use http:// (it is always an opt-in local/emulator endpoint by nature).
        string? endpointOverride = Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER");
        string requestUrl;
        if (!string.IsNullOrEmpty(endpointOverride))
        {
            requestUrl = endpointOverride.TrimEnd('/') + "/";
        }
        else
        {
            string host = "secretsmanager." + region + ".amazonaws.com";
            requestUrl = "https://" + host + "/";
        }
        string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "";
        string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "";
        string sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN") ?? "";
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new InvalidOperationException("AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY required");

        string amzDate = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        string credentialScope = dateStamp + "/" + region + "/" + SERVICE + "/aws4_request";

        string signedHost = endpointOverride != null
            ? new Uri(requestUrl).Host
            : "secretsmanager." + region + ".amazonaws.com";
        string canonicalHeaders = "content-type:application/x-amz-json-1.1\nhost:" + signedHost + "\nx-amz-date:" + amzDate + "\n" +
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
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, requestUrl);
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
