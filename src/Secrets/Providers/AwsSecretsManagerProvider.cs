using EnvManager.Secrets.Core;
// AwsSecretsManagerProvider.cs - secret provider architecture
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

// Adapter-boundary typed error family. AWS is the audited provider
// (aws-sdk-java #2702): the Authorization header (SigV4 Credential=<accessKey>/...)
// is echoed into service error payloads, so this adapter DISCARDS the response body
// and any raw SDK/transport message - only the typed classification survives.

using System;
using System.Net.Http;
using System.Text.Json;

namespace EnvManager.Secrets.Providers;

// --- Phase 9: AWS Secrets Manager Provider ---

internal sealed class AwsSecretsManagerProvider : ISecretProvider
{
    public string Name => "aws-secretsmanager";

    // Capability descriptors: declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => false;  // IAM role / static keys, no cert
    public bool RequiresNetwork => true;

    // Cheap gate mirroring Encrypt's fail-fast preconditions (no network I/O).
    public bool Available =>
        (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_REGION")) ||
         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION"))) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));

    // Envelope: { provider, version, createdAt, targetName (region|secretId) }
    // Uses AWS SigV4 signed REST API calls. TLS mandatory (HTTPS only).

    private const string SERVICE = "secretsmanager";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        string region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpEncrypt, "AWS_REGION or AWS_DEFAULT_REGION not set");
        var target = new AwsSecretTarget(region,
            context != null ? AwsSecretTarget.SanitizeSecretId(context) : AwsSecretTarget.NewSecretId());

        // A per-call UUID is the documented idempotency pattern and is safe in production
        // (retries dedupe).
        string body = AwsSecretsManagerPayload.CreateSecret(target.SecretId, plaintext, Guid.NewGuid().ToString("N"));
        var response = SecretProviderErrors.Send(Name, SecretProviderErrors.OpEncrypt,
            () => CallAwsApi(region, "secretsmanager.CreateSecret", body), discardRawMessage: true);
        if (!response.IsSuccessStatusCode) throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpEncrypt, response.StatusCode, "create");

        var env = new SecretEnvelope { Provider = Name, Version = 1, CreatedAt = DateTimeOffset.UtcNow.ToString("O"), TargetName = target.EnvelopeValue };
        return env.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope) ?? throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid secret envelope format");
        if (parsed.Provider != Name) throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, $"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName)) throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Missing targetName", parsed.TargetName);
        var target = AwsSecretTarget.Parse(parsed.TargetName)
            ?? throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid targetName format", parsed.TargetName);

        string body = AwsSecretsManagerPayload.GetSecretValue(target.SecretId);
        var response = CallAwsApi(target.Region, "secretsmanager.GetSecretValue", body);
        if (!response.IsSuccessStatusCode) throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpDecrypt, response.StatusCode, "read", mountId: parsed.TargetName);

        return SecretProviderErrors.Send(Name, SecretProviderErrors.OpDecrypt, () =>
        {
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("SecretString", out var val)) return val.GetString() ?? "";
            throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpDecrypt, "AWS response does not contain SecretString", parsed.TargetName);
        }, parsed.TargetName, discardRawMessage: true);
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        var target = AwsSecretTarget.Parse(parsed?.TargetName);
        if (target == null) return;
        try
        {
            string body = AwsSecretsManagerPayload.DeleteSecret(target.Value.SecretId);
            SecretProviderErrors.Send(Name, SecretProviderErrors.OpDelete,
                () => CallAwsApi(target.Value.Region, "secretsmanager.DeleteSecret", body), discardRawMessage: true);
        }
        catch (Exception ex) { SecretProviderErrors.SwallowBestEffort(Name, SecretProviderErrors.OpDelete, ex); }
    }

    public bool CanRotate => true;
    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        var parsed = SecretEnvelope.TryParse(oldEnvelope);
        var target = AwsSecretTarget.Parse(parsed?.TargetName);
        if (target == null) return Encrypt(plaintext, context);
        string body = AwsSecretsManagerPayload.PutSecretValue(target.Value.SecretId, plaintext);
        var resp = SecretProviderErrors.Send(Name, SecretProviderErrors.OpRotate,
            () => CallAwsApi(target.Value.Region, "secretsmanager.PutSecretValue", body), parsed?.TargetName, discardRawMessage: true);
        if (!resp.IsSuccessStatusCode) throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpRotate, resp.StatusCode, "rotation (PutSecretValue)", mountId: parsed?.TargetName);
        return oldEnvelope;
    }

    private static System.Net.Http.HttpResponseMessage CallAwsApi(string region, string target, string body)
    {
        // Official AWS service-specific endpoint override convention:
        // AWS_ENDPOINT_URL_SECRETS_MANAGER redirects all Secrets Manager calls -
        // production deployments never set it; the L1 LocalStack emulator does.
        // TLS stays mandatory for the default endpoint; an explicit override may
        // use http:// (it is always an opt-in local/emulator endpoint by nature).
        string? endpointOverride = Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER");
        string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "";
        string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "";
        string sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN") ?? "";
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new SecretProviderAuthFailedException("aws-secretsmanager", "aws-api", "AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY required");

        // Signing, canonical request and transport live in AwsSigV4Client so the
        // Roles Anywhere provider signs with the same builders instead of a copy.
        return AwsSigV4Client.Call(
            "aws-secretsmanager", "aws-api", region, SERVICE, "/", target, body,
            "application/x-amz-json-1.1",
            new AwsCredentials(accessKey, secretKey, string.IsNullOrEmpty(sessionToken) ? null : sessionToken),
            string.IsNullOrEmpty(endpointOverride) ? null : endpointOverride,
            Timeout);
    }
}
