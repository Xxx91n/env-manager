using EnvManager.Secrets.Core;
// AwsRolesAnywhereSession.cs - IAM Roles Anywhere credential exchange
// License: Apache-2.0

// CreateSession is not reachable through any AWS SDK, and the official helper only
// searches the CURRENT USER certificate store - which a service account does not
// have. So the exchange is implemented here over the .NET certificate APIs: the
// private key stays in the CNG key store and only ever signs, it is never exported.
// The identity it reads, the session it returns, and the certificate lookup it uses
// are their own files.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace EnvManager.Secrets.Providers;

/// <summary>
/// One CreateSession exchange: reads the identity from the machine environment, signs
/// with the named certificate, and returns temporary credentials.
/// </summary>
internal static class AwsRolesAnywhereSessionExchange
{
    private const string Service = "rolesanywhere";
    private const string SessionPath = "/sessions";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(20);

    /// <summary>DEFAULT 1 hour; AWS accepts 900..43200 seconds.</summary>
    internal const int DefaultDurationSeconds = 3600;
    internal const int MinDurationSeconds = 900;
    internal const int MaxDurationSeconds = 43200;

    /// <summary>
    /// Reads the identity from the machine environment. Null when any required value is
    /// missing, which is what the provider's cheap availability gate reports.
    /// </summary>
    internal static RolesAnywhereOptions? ReadOptions()
    {
        var missing = MissingVariables();
        if (missing.Count > 0) return null;

        string region = FirstNonEmpty("AWS_REGION", "AWS_DEFAULT_REGION");
        int duration = DefaultDurationSeconds;
        string? raw = Environment.GetEnvironmentVariable("AWS_ROLES_ANYWHERE_DURATION_SECONDS");
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            duration = parsed;
        duration = Math.Clamp(duration, MinDurationSeconds, MaxDurationSeconds);

        return new RolesAnywhereOptions(
            region,
            FirstNonEmpty("AWS_ROLES_ANYWHERE_ROLE_ARN"),
            FirstNonEmpty("AWS_ROLES_ANYWHERE_PROFILE_ARN"),
            FirstNonEmpty("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN"),
            FirstNonEmpty("AWS_ROLES_ANYWHERE_CERT_SHA1"),
            duration);
    }

    /// <summary>Names every missing variable, so the CLI error is actionable in one read.</summary>
    internal static string DescribeMissing() => string.Join(", ", MissingVariables());

    // The single list both ReadOptions and DescribeMissing read, so a new required
    // variable cannot be enforced by one and silently absent from the other's message.
    private static List<string> MissingVariables()
    {
        var missing = new List<string>();
        if (FirstNonEmpty("AWS_REGION", "AWS_DEFAULT_REGION").Length == 0) missing.Add("AWS_REGION or AWS_DEFAULT_REGION");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_ROLE_ARN").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_ROLE_ARN");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_PROFILE_ARN").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_PROFILE_ARN");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_CERT_SHA1").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_CERT_SHA1");
        return missing;
    }

    /// <summary>
    /// Exchanges the certificate for temporary credentials. Throws the typed provider
    /// family; the message names the certificate fingerprint, never the response body.
    /// </summary>
    internal static RolesAnywhereSession CreateSession(
        string provider,
        string operation,
        RolesAnywhereOptions options,
        IAwsHttpSender? sender = null,
        Func<DateTimeOffset>? clock = null,
        X509Certificate2? certificateOverride = null)
    {
        DateTimeOffset now = (clock ?? (() => DateTimeOffset.UtcNow))();
        X509Certificate2? owned = certificateOverride == null
            ? CertificateStore.SelectForRolesAnywhere(provider, operation, options.CertificateSha1, now)
            : null;
        X509Certificate2 certificate = certificateOverride ?? owned!;
        try
        {
            using RSA? rsa = certificate.GetRSAPrivateKey();
            using ECDsa? ecdsa = rsa == null ? certificate.GetECDsaPrivateKey() : null;
            if (rsa == null && ecdsa == null)
                throw new SecretProviderAuthFailedException(provider, operation,
                    "certificate " + Fingerprint(options.CertificateSha1) + " has no RSA or ECDSA private key");

            string body = BuildSessionBody(options);
            // Intermediate certificates only: the trust anchor owns the root, and sending
            // it again is not what the service asks for. The leaf and its chain are
            // disposed here rather than by the caller, which never sees them.
            List<X509Certificate2> chain = CertificateStore.IntermediatesFor(certificate);
            try
            {
                var request = BuildSignedRequest(provider, operation, options, certificate,
                    rsa, ecdsa, chain, body, now);
                var response = SecretProviderErrors.Send(provider, operation,
                    () => (sender ?? HttpClientSender.Instance).Send(request, Timeout), discardRawMessage: true);
                if (!response.IsSuccessStatusCode)
                    throw RejectedResponse(provider, operation, options, response);

                string json = SecretProviderErrors.Send(provider, operation,
                    () => response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), discardRawMessage: true);
                return ParseSession(provider, operation, json, now, options);
            }
            finally
            {
                foreach (var c in chain) c.Dispose();
            }
        }
        finally
        {
            owned?.Dispose();
        }
    }

    private static string BuildSessionBody(RolesAnywhereOptions options) =>
        "{\"durationSeconds\":" + options.DurationSeconds.ToString(CultureInfo.InvariantCulture) +
        ",\"profileArn\":\"" + AwsJson.Escape(options.ProfileArn) +
        "\",\"roleArn\":\"" + AwsJson.Escape(options.RoleArn) +
        "\",\"trustAnchorArn\":\"" + AwsJson.Escape(options.TrustAnchorArn) + "\"}";

    /// <summary>
    /// The signed POST /sessions request: the X.509 SigV4 variant, where the credential
    /// scope carries the certificate serial and the certificate itself is a signed header.
    /// </summary>
    private static HttpRequestMessage BuildSignedRequest(
        string provider,
        string operation,
        RolesAnywhereOptions options,
        X509Certificate2 certificate,
        RSA rsa,
        ECDsa? ecdsa,
        IReadOnlyList<X509Certificate2> chain,
        string body,
        DateTimeOffset now)
    {
        string algorithm = ecdsa == null ? "AWS4-X509-RSA-SHA256" : "AWS4-X509-ECDSA-SHA256";

        // No environment seam here: a variable that redirects the credential exchange
        // would let a planted setting hand the process forged temporary credentials.
        // Tests inject IAwsHttpSender instead.
        string requestUrl = AwsSigV4Client.RequestUrl(options.Region, Service, SessionPath, null);
        string signedHost = new Uri(requestUrl).Host;

        string amzDate = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string dateStamp = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string scope = dateStamp + "/" + options.Region + "/" + Service + "/aws4_request";

        string leafBase64 = Convert.ToBase64String(certificate.RawData);
        string? chainHeader = ChainHeader(chain);
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "application/json"),
            ("host", signedHost),
            ("x-amz-date", amzDate),
            ("x-amz-x509", leafBase64),
        };
        if (chainHeader != null) headers.Add(("x-amz-x509-chain", chainHeader));

        string payloadHash = AwsSigV4Client.HexSHA256(body);
        string canonicalRequest = AwsSigV4Client.BuildCanonicalRequest("POST", SessionPath, headers, payloadHash);
        string stringToSign = AwsSigV4Client.BuildStringToSign(algorithm, amzDate, scope, canonicalRequest);

        byte[] signed = Encoding.UTF8.GetBytes(stringToSign);
        byte[] signature = ecdsa == null
            ? rsa.SignData(signed, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
            // Explicit DER: the default here is IEEE P1363 (raw r||s), which AWS rejects.
            : ecdsa.SignData(signed, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);

        string serial = AwsSigV4Client.SerialNumberDecimal(certificate.SerialNumberBytes.ToArray());
        string auth = algorithm + " Credential=" + serial + "/" + scope +
            ", SignedHeaders=" + AwsSigV4Client.SignedHeaders(headers) +
            ", Signature=" + AwsSigV4Client.BytesToHex(signature);

        // Not `using`: the HttpRequestMessage owns this content and disposes it together
        // with the response, which happens after the builder has returned.
        var content = new StringContent(body, Encoding.UTF8, "application/json");
        // The canonical request signs the bare media type; StringContent would send
        // "application/json; charset=utf-8" and AWS would recompute a different signature.
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
        request.Headers.Add("X-Amz-Date", amzDate);
        request.Headers.Add("X-Amz-X509", leafBase64);
        if (chainHeader != null) request.Headers.Add("X-Amz-X509-Chain", chainHeader);
        request.Headers.TryAddWithoutValidation("Authorization", auth);
        return request;
    }

    /// <summary>Comma-separated DER of the intermediates, or null when there are none.</summary>
    private static string? ChainHeader(IReadOnlyList<X509Certificate2> chain)
    {
        if (chain.Count == 0) return null;
        var parts = new List<string>(chain.Count);
        foreach (var c in chain) parts.Add(Convert.ToBase64String(c.RawData));
        return string.Join(",", parts);
    }

    /// <summary>
    /// 401 means the certificate chain or the trust anchor is not accepted. The body is
    /// dropped either way because it can echo request material.
    /// </summary>
    private static Exception RejectedResponse(
        string provider, string operation, RolesAnywhereOptions options, HttpResponseMessage response)
    {
        if ((int)response.StatusCode == 401)
            return new SecretProviderAuthFailedException(provider, operation,
                "CreateSession rejected certificate " + Fingerprint(options.CertificateSha1) +
                " (401): check the trust anchor contains the issuing CA");
        return SecretProviderErrors.FromStatus(provider, operation, response.StatusCode, "create-session");
    }

    private static RolesAnywhereSession ParseSession(
        string provider, string operation, string json, DateTimeOffset now, RolesAnywhereOptions options)
    {
        using var doc = JsonDocument.Parse(json);

        // AWS nests the credentials: credentialSet[] > credentials > accessKeyId.
        // Reading them off the root would parse cleanly and report "does not contain
        // credentials" against every real response.
        if (!doc.RootElement.TryGetProperty("credentialSet", out var set)
            || set.ValueKind != JsonValueKind.Array || set.GetArrayLength() == 0
            || !set[0].TryGetProperty("credentials", out var creds))
            throw new SecretProviderUnavailableException(provider, operation,
                "CreateSession response does not contain credentialSet[0].credentials");

        if (!creds.TryGetProperty("accessKeyId", out var accessKeyElement)
            || !creds.TryGetProperty("secretAccessKey", out var secretKeyElement))
            throw new SecretProviderUnavailableException(provider, operation,
                "CreateSession response is missing the access key or secret key");
        string? accessKey = accessKeyElement.GetString();
        string? secretKey = secretKeyElement.GetString();
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new SecretProviderUnavailableException(provider, operation,
                "CreateSession response is missing the access key or secret key");

        string? sessionToken = creds.TryGetProperty("sessionToken", out var token) ? token.GetString() : null;
        string? expiration = creds.TryGetProperty("expiration", out var e) ? e.GetString() : null;
        DateTimeOffset expires = DateTimeOffset.TryParse(expiration, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
            ? parsed
            : now.AddSeconds(options.DurationSeconds);
        return new RolesAnywhereSession(new AwsCredentials(accessKey!, secretKey!, sessionToken), expires);
    }

    internal static string Fingerprint(string sha1) =>
        sha1.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant();

    private static string FirstNonEmpty(params string[] names)
    {
        foreach (string name in names)
        {
            string? value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value)) return value!.Trim();
        }
        return "";
    }
}
