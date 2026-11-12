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
    // Test seam only: production always uses the parameterless constructor, so no
    // environment switch can redirect a signed request. Same shape and same reasoning as
    // AwsRolesAnywhereProvider's seam.
    private readonly IAwsHttpSender? _sender;

    public AwsSecretsManagerProvider() { }

    internal AwsSecretsManagerProvider(IAwsHttpSender sender) { _sender = sender; }

    public string Name => "aws-secretsmanager";

    // Capability descriptors: declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => false;  // static access keys, no certificate
    public bool RequiresNetwork => true;

    // Cheap gate mirroring Encrypt's fail-fast preconditions (no network I/O).
    public bool Available =>
        (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_REGION")) ||
         !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION"))) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")) &&
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY"));

    // The availability probe reads; it never writes. `list` runs on every profile-editor
    // mount, and the old sentinel round-trip created and then deleted a secret in the
    // customer's account each time.
    public bool ProbeWritesData => false;

    // Budget for the read-only probe. Shorter than the real calls' 15s: it sits in front
    // of a GUI page load.
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(5);

    // Read-only probe: signed GetSecretValue for a name that cannot exist. Asking for a
    // missing secret is the cheapest way to prove the credentials may read secrets here,
    // because it needs exactly the permission a real read needs - `secretsmanager:GetSecretValue`
    // - and none of the extra ones a ListSecrets call would demand. AWS answers "not found"
    // as HTTP 400 with `ResourceNotFoundException`, not 404, so the body decides.
    public void ProbeReadOnly()
    {
        string region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? throw new SecretProviderUnavailableException(Name, SecretProviderErrors.OpProbe, "AWS_REGION or AWS_DEFAULT_REGION not set");

        string probeId = "env-manager-probe-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        string body = AwsSecretsManagerPayload.GetSecretValue(probeId);

        var response = CallAwsApi(region, SecretProviderErrors.OpProbe, "secretsmanager.GetSecretValue", body,
            () => ProbeTimeout);
        // The body is read here on purpose: AWS answers "not found" as HTTP 400 with
        // `ResourceNotFoundException`, not 404, and a refusal as 400/403 with
        // `AccessDeniedException`, so the exception name is what separates them. The
        // status still gates the match - a 200 or 500 carrying that name is not the
        // answer this probe is looking for.
        string payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        string? errorType = ProbeErrorType(payload);
        if (response.StatusCode == System.Net.HttpStatusCode.BadRequest &&
            string.Equals(errorType, "ResourceNotFoundException", StringComparison.Ordinal))
            return;

        // AccessDenied arrives as 403 for the plain case and as 400 when the KMS key is
        // the missing grant; both are a refusal, and FromStatus would call the 400 form
        // "unavailable", which reads as "network" in the GUI.
        if (string.Equals(errorType, "AccessDeniedException", StringComparison.Ordinal) ||
            string.Equals(errorType, "AccessDenied", StringComparison.Ordinal))
            throw new SecretProviderPermissionDeniedException(Name, SecretProviderErrors.OpProbe,
                Name + " probe refused (permission denied: " + errorType + ")");

        throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpProbe, response.StatusCode, "probe", errorType);
    }

    // The exception name only. AWS error bodies carry the caller ARN and the resource
    // name, never the secret value, but the adapter's standing rule is to keep raw
    // service text out of surfaced errors (the service otherwise echoes
    // `arn:aws:iam::<account>:user/...` into the message). Classification needs nothing
    // else, so nothing else is read out.
    private static string? ProbeErrorType(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload)) return null;
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(payload);
            // AWS JSON 1.1 spells the discriminator `__type`; some error shapes use `Code`.
            foreach (string key in new[] { "__type", "Code" })
            {
                if (!doc.RootElement.TryGetProperty(key, out var value)) continue;
                if (value.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                string exceptionName = value.GetString() ?? "";
                // Some names arrive namespace-qualified (`com.amazon...:AccessDeniedException`);
                // the tail is the useful part.
                int lastColon = exceptionName.LastIndexOf(':');
                if (lastColon >= 0) exceptionName = exceptionName.Substring(lastColon + 1);
                if (exceptionName.Length > 64) exceptionName = exceptionName.Substring(0, 64);
                return exceptionName;
            }
            return null;
        }
        catch (System.Text.Json.JsonException)
        {
            return null;
        }
    }

    // Envelope: { provider, version, createdAt, targetName (region|secretId) }
    // Uses AWS SigV4 signed REST API calls. The default endpoint is https only; an
    // endpoint override over http:// has to point at this machine.

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
            () => CallAwsApi(region, SecretProviderErrors.OpEncrypt, "secretsmanager.CreateSecret", body), discardRawMessage: true);
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
        var response = CallAwsApi(target.Region, SecretProviderErrors.OpDecrypt, "secretsmanager.GetSecretValue", body);
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
                () => CallAwsApi(target.Value.Region, SecretProviderErrors.OpDelete, "secretsmanager.DeleteSecret", body), discardRawMessage: true);
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
            () => CallAwsApi(target.Value.Region, SecretProviderErrors.OpRotate, "secretsmanager.PutSecretValue", body), parsed?.TargetName, discardRawMessage: true);
        if (!resp.IsSuccessStatusCode) throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpRotate, resp.StatusCode, "rotation (PutSecretValue)", mountId: parsed?.TargetName);
        return oldEnvelope;
    }

    private System.Net.Http.HttpResponseMessage CallAwsApi(string region, string operation, string target, string body, Func<TimeSpan>? timeout = null)
    {
        // Official AWS service-specific endpoint override convention:
        // AWS_ENDPOINT_URL_SECRETS_MANAGER redirects all Secrets Manager calls -
        // production deployments never set it; the L1 LocalStack emulator does.
        // https:// works for any host; http:// is accepted only for a host on this
        // machine, because a remote one would receive the signed request and the
        // session token in clear text.
        string? endpointOverride = AwsEndpointOverride.SecretsManager;
        string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "";
        string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "";
        string sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN") ?? "";
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new SecretProviderAuthFailedException("aws-secretsmanager", operation, "AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY required");

        // Signing, canonical request and transport live in AwsSigV4Client so the
        // Roles Anywhere provider signs with the same builders instead of a copy.
        return AwsSigV4Client.Call(
            "aws-secretsmanager", operation, region, SERVICE, "/", target, body,
            "application/x-amz-json-1.1",
            new AwsCredentials(accessKey, secretKey, string.IsNullOrEmpty(sessionToken) ? null : sessionToken),
            string.IsNullOrEmpty(endpointOverride) ? null : endpointOverride,
            timeout?.Invoke() ?? Timeout,
            _sender);
    }
}
