using EnvManager.Secrets.Core;
// AwsRolesAnywhereSession.cs - IAM Roles Anywhere credential exchange
// License: Apache-2.0

// CreateSession is not reachable through any AWS SDK, and the official helper only
// searches the CURRENT USER certificate store - which a service account does not
// have. So the exchange is implemented here over the .NET certificate APIs: the
// private key stays in the CNG key store and only ever signs, it is never exported.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;

namespace EnvManager.Secrets.Providers;

/// <summary>Where a Roles Anywhere identity comes from, plus the session length asked for.</summary>
internal sealed record RolesAnywhereOptions(
    string Region,
    string RoleArn,
    string ProfileArn,
    string TrustAnchorArn,
    string CertificateSha1,
    int DurationSeconds)
{
    /// <summary>Identity key for the session cache: a change to any part must refetch.</summary>
    internal string CacheKey =>
        Region + "|" + RoleArn + "|" + ProfileArn + "|" + TrustAnchorArn + "|" +
        CertificateSha1.Replace(" ", "", StringComparison.Ordinal).ToUpperInvariant() + "|" + DurationSeconds;
}

/// <summary>Temporary credentials plus the moment they stop being usable.</summary>
internal sealed record RolesAnywhereSession(AwsCredentials Credentials, DateTimeOffset ExpiresAt)
{
    // Refresh this far ahead of expiry so a long call cannot outlive its credentials.
    internal static readonly TimeSpan RefreshLead = TimeSpan.FromMinutes(5);

    internal bool NeedsRefresh(DateTimeOffset now) => now >= ExpiresAt - RefreshLead;
}

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
        string region = FirstNonEmpty("AWS_REGION", "AWS_DEFAULT_REGION");
        string role = FirstNonEmpty("AWS_ROLES_ANYWHERE_ROLE_ARN");
        string profile = FirstNonEmpty("AWS_ROLES_ANYWHERE_PROFILE_ARN");
        string trustAnchor = FirstNonEmpty("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN");
        string sha1 = FirstNonEmpty("AWS_ROLES_ANYWHERE_CERT_SHA1");
        if (region.Length == 0 || role.Length == 0 || profile.Length == 0 ||
            trustAnchor.Length == 0 || sha1.Length == 0)
            return null;

        int duration = DefaultDurationSeconds;
        string? raw = Environment.GetEnvironmentVariable("AWS_ROLES_ANYWHERE_DURATION_SECONDS");
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            duration = parsed;
        duration = Math.Clamp(duration, MinDurationSeconds, MaxDurationSeconds);

        return new RolesAnywhereOptions(region, role, profile, trustAnchor, sha1, duration);
    }

    /// <summary>Names every missing variable, so the CLI error is actionable in one read.</summary>
    internal static string DescribeMissing()
    {
        var missing = new List<string>();
        if (FirstNonEmpty("AWS_REGION", "AWS_DEFAULT_REGION").Length == 0) missing.Add("AWS_REGION or AWS_DEFAULT_REGION");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_ROLE_ARN").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_ROLE_ARN");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_PROFILE_ARN").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_PROFILE_ARN");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN");
        if (FirstNonEmpty("AWS_ROLES_ANYWHERE_CERT_SHA1").Length == 0) missing.Add("AWS_ROLES_ANYWHERE_CERT_SHA1");
        return string.Join(", ", missing);
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
            ",\"profileArn\":\"" + JsonEscape(options.ProfileArn) +
            "\",\"roleArn\":\"" + JsonEscape(options.RoleArn) +
            "\",\"trustAnchorArn\":\"" + JsonEscape(options.TrustAnchorArn) + "\"}";

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

    internal static string JsonEscape(string s)
    {
        var sb = new StringBuilder(s.Length);
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

/// <summary>Machine-store certificate lookup. A service account cannot see a user store.</summary>
internal static class CertificateStore
{
    /// <summary>
    /// The one certificate named by its SHA-1 fingerprint. Anything else - missing,
    /// expired, no key, failing the shape rules - is an authentication failure, never a
    /// silent pick of a different certificate.
    /// </summary>
    internal static X509Certificate2 SelectForRolesAnywhere(
        string provider, string operation, string sha1, DateTimeOffset now)
    {
        string wanted = AwsRolesAnywhereSessionExchange.Fingerprint(sha1);
        using var store = new X509Store(StoreName.My, StoreLocation.LocalMachine);
        try
        {
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        }
        catch (Exception ex) when (ex is CryptographicException or System.Security.SecurityException)
        {
            throw new SecretProviderAuthFailedException(provider, operation,
                "cannot open Cert:\\LocalMachine\\My (" + ex.GetType().Name + ")");
        }

        X509Certificate2Collection found = store.Certificates.Find(X509FindType.FindByThumbprint, wanted, validOnly: false);
        X509Certificate2? cert = null;
        for (int i = 0; i < found.Count; i++)
        {
            if (string.Equals(found[i].Thumbprint, wanted, StringComparison.OrdinalIgnoreCase))
            {
                cert = found[i];
                break;
            }
        }
        if (cert == null)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " is not in Cert:\\LocalMachine\\My");

        if (cert.NotAfter.ToUniversalTime() <= now.UtcDateTime)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " expired on " + cert.NotAfter.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
        if (!cert.HasPrivateKey)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " has no private key in the machine store");
        if (cert.Version != 3)
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " is X.509 v" + cert.Version + "; Roles Anywhere requires v3");
        if (!HasDigitalSignatureKeyUsage(cert))
            throw new SecretProviderAuthFailedException(provider, operation,
                "certificate " + wanted + " does not allow digital signature in its key usage");
        return cert;
    }

    /// <summary>
    /// Intermediate certificates from the built chain, leaf and self-signed root
    /// excluded, capped at the five the service accepts.
    /// </summary>
    internal static List<X509Certificate2> IntermediatesFor(X509Certificate2 leaf)
    {
        var result = new List<X509Certificate2>();
        using var chain = new X509Chain();
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        if (!chain.Build(leaf))
        {
            // A chain that will not build offline is not fatal here: the trust anchor may
            // hold what the local machine does not, and the service decides. Send nothing
            // rather than a guess.
            return result;
        }
        for (int i = 0; i < chain.ChainElements.Count; i++)
        {
            X509Certificate2 element = chain.ChainElements[i].Certificate;
            if (i == 0) continue;                                    // the leaf travels separately
            if (ElementIsSelfSigned(element)) continue;               // the trust anchor owns the root
            if (result.Count >= 5) break;                             // the service accepts at most five
            result.Add(new X509Certificate2(element.RawData));
        }
        return result;
    }

    private static bool ElementIsSelfSigned(X509Certificate2 cert) =>
        string.Equals(cert.Subject, cert.Issuer, StringComparison.OrdinalIgnoreCase);

    private static bool HasDigitalSignatureKeyUsage(X509Certificate2 cert)
    {
        foreach (X509KeyUsageExtension usage in cert.Extensions)
            return (usage.KeyUsages & X509KeyUsageFlags.DigitalSignature) != 0;
        // No key-usage extension means unrestricted, which satisfies the requirement.
        return true;
    }
}
