using EnvManager.Secrets.Core;
// AwsRolesAnywhereProvider.cs - Secrets Manager through IAM Roles Anywhere
// License: Apache-2.0

// Same remote store as aws-secretsmanager, different door: instead of a long-lived
// access key on disk, a certificate in Cert:\LocalMachine\My is exchanged for a
// short-lived session. The envelope payload is the same `region|secretId` shape, but
// the envelope records the provider that wrote it and a mismatch is rejected, so
// switching providers means re-adding the secret rather than just changing the
// dropdown. The remote secret itself never moves.

using System;
using System.Net.Http;
using System.Text.Json;

namespace EnvManager.Secrets.Providers;

internal sealed class AwsRolesAnywhereProvider : ISecretProvider
{
    public string Name => "aws-roles-anywhere";

    // Capability descriptors: declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => true;
    public bool CertAuthRequired => true;
    public bool RequiresNetwork => true;

    // Certificate exchange only: a probe must never create a secret in the customer
    // account. Profiles are opened often; writing on every open is not acceptable.
    public bool ProbeWritesData => false;

    // Cheap gate mirroring Encrypt's fail-fast preconditions (no network I/O).
    public bool Available => AwsRolesAnywhereSessionExchange.ReadOptions() != null;

    private const string SERVICE = "secretsmanager";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    // One session per process, refreshed ahead of expiry. The CLI is one process per
    // command, so this covers a rotation pass and repeated calls in one run; a future
    // service-side refresh needs its own cache across invocations.
    private readonly object _gate = new object();
    private string? _cachedKey;
    private RolesAnywhereSession? _cachedSession;

    // Test seam only: production always uses the parameterless constructor in
    // SecretProviderManager._providers, so no environment switch can redirect a
    // credential exchange.
    private readonly IAwsHttpSender? _sender;
    private readonly Func<DateTimeOffset>? _clock;
    private readonly System.Security.Cryptography.X509Certificates.X509Certificate2? _certificate;

    public AwsRolesAnywhereProvider() { }

    internal AwsRolesAnywhereProvider(
        IAwsHttpSender sender,
        System.Security.Cryptography.X509Certificates.X509Certificate2? certificate,
        Func<DateTimeOffset>? clock = null)
    {
        _sender = sender;
        _certificate = certificate;
        _clock = clock;
    }

    public void ProbeReadOnly()
    {
        var options = RequireOptions("probe");
        _ = Session(options, "probe");
    }

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) return "";
        var options = RequireOptions(SecretProviderErrors.OpEncrypt);
        var target = new AwsSecretTarget(options.Region,
            context != null ? AwsSecretTarget.SanitizeSecretId(context) : AwsSecretTarget.NewSecretId());

        string body = AwsSecretsManagerPayload.CreateSecret(target.SecretId, plaintext, Guid.NewGuid().ToString("N"));
        var response = CallSecretsManager(target.Region, SecretProviderErrors.OpEncrypt, "secretsmanager.CreateSecret", body);
        if (!response.IsSuccessStatusCode) throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpEncrypt, response.StatusCode, "create");

        var env = new SecretEnvelope { Provider = Name, Version = 1, CreatedAt = DateTimeOffset.UtcNow.ToString("O"), TargetName = target.EnvelopeValue };
        return env.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope) ?? throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid secret envelope format");
        if (parsed.Provider != Name) throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Provider mismatch: expected " + Name + ", got " + parsed.Provider);
        if (string.IsNullOrEmpty(parsed.TargetName)) throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Missing targetName", parsed.TargetName);
        var target = AwsSecretTarget.Parse(parsed.TargetName)
            ?? throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid targetName format", parsed.TargetName);

        string body = AwsSecretsManagerPayload.GetSecretValue(target.SecretId);
        var response = CallSecretsManager(target.Region, SecretProviderErrors.OpDecrypt, "secretsmanager.GetSecretValue", body);
        if (!response.IsSuccessStatusCode)
            throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpDecrypt, response.StatusCode, "read", mountId: parsed.TargetName);

        return SecretProviderErrors.Send(Name, SecretProviderErrors.OpDecrypt, () =>
        {
            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = JsonDocument.Parse(json);
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
            _ = CallSecretsManager(target.Value.Region, SecretProviderErrors.OpDelete,
                "secretsmanager.DeleteSecret", body);
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
        var response = CallSecretsManager(target.Value.Region, SecretProviderErrors.OpRotate,
            "secretsmanager.PutSecretValue", body);
        if (!response.IsSuccessStatusCode)
            throw SecretProviderErrors.FromStatus(Name, SecretProviderErrors.OpRotate, response.StatusCode, "rotation (PutSecretValue)", mountId: parsed?.TargetName);
        return oldEnvelope;
    }

    /// <summary>
    /// One Secrets Manager call with the cached session. The Secrets Manager region comes
    /// from the envelope; the session keeps using the configured region, because a trust
    /// anchor is a regional resource and exchanging it in the wrong region fails outright.
    /// </summary>
    private HttpResponseMessage CallSecretsManager(string secretsRegion, string operation, string target, string body)
    {
        HttpResponseMessage first = SendWithSession(secretsRegion, operation, target, body);
        if (first.IsSuccessStatusCode) return first;
        if ((int)first.StatusCode != 401 && (int)first.StatusCode != 403) return first;
        first.Dispose();
        Invalidate();
        return SendWithSession(secretsRegion, operation, target, body);
    }

    private HttpResponseMessage SendWithSession(string secretsRegion, string operation, string target, string body)
    {
        var options = RequireOptions(operation);
        RolesAnywhereSession session = Session(options, operation);
        return AwsSigV4Client.Call(Name, operation, secretsRegion, SERVICE, "/", target, body,
            "application/x-amz-json-1.1", session.Credentials, AwsEndpointOverride.SecretsManager,
            Timeout, _sender);
    }

    private RolesAnywhereSession Session(RolesAnywhereOptions options, string operation)
    {
        DateTimeOffset now = _clock != null ? _clock() : DateTimeOffset.UtcNow;
        lock (_gate)
        {
            if (_cachedSession != null
                && string.Equals(_cachedKey, options.CacheKey, StringComparison.Ordinal)
                && !_cachedSession.NeedsRefresh(now))
            {
                return _cachedSession;
            }
        }

        // Outside the lock: CreateSession is a network round trip, and the exchange is
        // idempotent, so a rare duplicate on a race costs one extra session and is
        // preferable to serialising every caller behind it.
        RolesAnywhereSession fresh = AwsRolesAnywhereSessionExchange.CreateSession(Name, operation, options, _sender, _clock, _certificate);
        lock (_gate)
        {
            _cachedKey = options.CacheKey;
            _cachedSession = fresh;
        }
        return fresh;
    }

    private void Invalidate()
    {
        lock (_gate)
        {
            _cachedKey = null;
            _cachedSession = null;
        }
    }

    private RolesAnywhereOptions RequireOptions(string operation)
    {
        return AwsRolesAnywhereSessionExchange.ReadOptions()
            ?? throw new SecretProviderUnavailableException(Name, operation,
                "IAM Roles Anywhere is not configured; missing " + AwsRolesAnywhereSessionExchange.DescribeMissing());
    }
}
