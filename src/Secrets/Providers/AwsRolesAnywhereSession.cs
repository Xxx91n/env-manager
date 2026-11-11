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
        using RSA? rsa = certificate.GetRSAPrivateKey();
        using ECDsa? ecdsa = rsa == null ? certificate.GetECDsaPrivateKey() : null;
        if (rsa == null && ecdsa == null)
        {
            owned?.Dispose();
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + Fingerprint(options.CertificateSha1) + " has no RSA or ECDSA private key");
        }

        string algorithm = rsa != null ? "AWS4-X509-RSA-SHA256" : "AWS4-X509-ECDSA-SHA256";

        string body = "{\"durationSeconds\":" + options.DurationSeconds.ToString(CultureInfo.InvariantCulture) +
            ",\"profileArn\":\"" + AwsJson.Escape(options.ProfileArn) +
            "\",\"roleArn\":\"" + AwsJson.Escape(options.RoleArn) +
            "\",\"trustAnchorArn\":\"" + AwsJson.Escape(options.TrustAnchorArn) + "\"}";

        // No environment seam here: a variable that redirects the credential exchange
        // would let a planted setting hand the process forged temporary credentials.
        // Tests inject IAwsHttpSender instead.
        string requestUrl = AwsSigV4Client.RequestUrl(options.Region, Service, SessionPath, null);
        string signedHost = new Uri(requestUrl).Host;

        string amzDate = now.UtcDateTime.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string dateStamp = now.UtcDateTime.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string scope = dateStamp + "/" + options.Region + "/" + Service + "/aws4_request";

        string leafBase64 = Convert.ToBase64String(certificate.RawData);
        var headers = new List<(string Name, string Value)>
        {
            ("content-type", "application/json"),
            ("host", signedHost),
            ("x-amz-date", amzDate),
            ("x-amz-x509", leafBase64),
        };
        // Intermediate certificates only: the trust anchor owns the root, and sending it
        // again is not what the service asks for.
        List<X509Certificate2> chain = CertificateStore.IntermediatesFor(certificate);
        try
        {
            if (chain.Count > 0)
            {
                var parts = new List<string>(chain.Count);
                foreach (var c in chain) parts.Add(Convert.ToBase64String(c.RawData));
                headers.Add(("x-amz-x509-chain", string.Join(",", parts)));
            }

            string payloadHash = AwsSigV4Client.HexSHA256(body);
            string canonicalRequest = AwsSigV4Client.BuildCanonicalRequest("POST", SessionPath, headers, payloadHash);
            string stringToSign = AwsSigV4Client.BuildStringToSign(algorithm, amzDate, scope, canonicalRequest);

            byte[] signature = rsa != null
                ? rsa.SignData(Encoding.UTF8.GetBytes(stringToSign), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1)
                // Explicit DER: the default here is IEEE P1363 (raw r||s), which AWS rejects.
                : ecdsa!.SignData(Encoding.UTF8.GetBytes(stringToSign), HashAlgorithmName.SHA256,
                    DSASignatureFormat.Rfc3279DerSequence);

            string serial = AwsSigV4Client.SerialNumberDecimal(certificate.SerialNumberBytes.ToArray());
            string auth = algorithm + " Credential=" + serial + "/" + scope +
                ", SignedHeaders=" + AwsSigV4Client.SignedHeaders(headers) +
                ", Signature=" + AwsSigV4Client.BytesToHex(signature);

            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            // The canonical request signs the bare media type; StringContent would send
            // "application/json; charset=utf-8" and AWS would recompute a different signature.
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
            var request = new HttpRequestMessage(HttpMethod.Post, requestUrl) { Content = content };
            request.Headers.Add("X-Amz-Date", amzDate);
            request.Headers.Add("X-Amz-X509", leafBase64);
            if (chain.Count > 0)
            {
                var chainBase64 = new List<string>(chain.Count);
                foreach (var c in chain) chainBase64.Add(Convert.ToBase64String(c.RawData));
                request.Headers.Add("X-Amz-X509-Chain", string.Join(",", chainBase64));
            }
            request.Headers.TryAddWithoutValidation("Authorization", auth);

            var send = sender ?? HttpClientSender.Instance;
            var response = SecretProviderErrors.Send(provider, operation,
                () => send.Send(request, Timeout), discardRawMessage: true);

            if (!response.IsSuccessStatusCode)
            {
                // 401/403 here means the certificate chain or the trust anchor is not
                // accepted; the body is dropped because it can echo request material.
                int status = (int)response.StatusCode;
                throw status == 401
                    ? new SecretProviderAuthFailedException(provider, operation,
                        "CreateSession rejected certificate " + Fingerprint(options.CertificateSha1) +
                        " (401): check the trust anchor contains the issuing CA")
                    : SecretProviderErrors.FromStatus(provider, operation, response.StatusCode, "create-session");
            }

            return SecretProviderErrors.Send(provider, operation, () =>
            {
                string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("credentials", out var creds))
                    throw new SecretProviderUnavailableException(provider, operation,
                        "CreateSession response does not contain credentials");
                string? accessKey = creds.GetProperty("accessKeyId").GetString();
                string? secretKey = creds.GetProperty("secretAccessKey").GetString();
                string? sessionToken = creds.GetProperty("sessionToken").GetString();
                string? expiration = creds.TryGetProperty("expiration", out var e) ? e.GetString() : null;
                if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
                    throw new SecretProviderUnavailableException(provider, operation,
                        "CreateSession response is missing the access key or secret key");
                DateTimeOffset expires = DateTimeOffset.TryParse(expiration, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset parsed)
                    ? parsed
                    : now.AddSeconds(options.DurationSeconds);
                return new RolesAnywhereSession(new AwsCredentials(accessKey!, secretKey!, sessionToken), expires);
            }, discardRawMessage: true);
        }
        finally
        {
            foreach (var c in chain) c.Dispose();
            owned?.Dispose();
        }
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
