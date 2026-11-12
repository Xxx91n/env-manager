using EnvManager.Secrets.Core;
// AwsSigV4Client.cs - shared AWS request signing and transport
// License: Apache-2.0

// One signing implementation, two credential kinds. The HMAC path is the ordinary
// AWS SigV4 used by aws-secretsmanager; the X.509 path is the IAM Roles Anywhere
// variant (certificate serial in the credential scope, the certificate in a signed
// header, and the private key in place of the derived HMAC key). Both share the
// canonical request and string-to-sign builders below, so a fix to one shape cannot
// drift away from the other.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;

namespace EnvManager.Secrets.Providers;

internal static class AwsSigV4Client
{
    /// <summary>
    /// Sends one SigV4-signed request with the given credentials. Raw transport and
    /// status failures map onto the typed provider family at the boundary; the response
    /// body is never folded into an error message.
    /// </summary>
    internal static HttpResponseMessage Call(
        string provider,
        string operation,
        string region,
        string service,
        string path,
        string target,
        string body,
        string contentType,
        AwsCredentials credentials,
        string? endpointOverride,
        TimeSpan timeout,
        IAwsHttpSender? sender = null)
    {
        // Checked here, the one place every AWS request is built from, so both providers'
        // read and write paths are covered by the same rule. aws-roles-anywhere calls this
        // check earlier as well, to fail before spending a session exchange.
        string? safeOverride = AwsEndpointOverride.RequireSafeEndpoint(provider, operation, endpointOverride);
        string requestUrl = RequestUrl(region, service, path, safeOverride);
        string signedHost = new Uri(requestUrl).Host;

        // One clock read: two separate UtcNow calls can straddle UTC midnight, which
        // leaves x-amz-date on one day and the credential scope on the next.
        DateTime utcNow = DateTime.UtcNow;
        string amzDate = utcNow.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        string dateStamp = utcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        string scope = credentials.Scope(dateStamp, region, service);

        var headers = new List<(string Name, string Value)>
        {
            ("content-type", contentType),
            ("host", signedHost),
            ("x-amz-date", amzDate),
        };
        if (!string.IsNullOrEmpty(credentials.SessionToken))
            headers.Add(("x-amz-security-token", credentials.SessionToken!));

        string payloadHash = HexSHA256(body);
        string canonicalRequest = BuildCanonicalRequest("POST", path, headers, payloadHash);
        string stringToSign = BuildStringToSign("AWS4-HMAC-SHA256", amzDate, scope, canonicalRequest);
        string signature = BytesToHex(SignHmac(credentials.SecretAccessKey, dateStamp, region, service, stringToSign));

        string auth = "AWS4-HMAC-SHA256 Credential=" + credentials.AccessKeyId + "/" + scope +
            ", SignedHeaders=" + SignedHeaders(headers) + ", Signature=" + signature;

        using var content = new StringContent(body, Encoding.UTF8, contentType);
        // StringContent appends "; charset=utf-8", but the canonical request above signed
        // the bare media type. AWS recomputes the canonical headers from what arrived, so
        // the transmitted value has to match the signed one byte for byte.
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var request = new HttpRequestMessage(HttpMethod.Post, requestUrl)
        {
            Content = content,
        };
        request.Headers.Add("X-Amz-Target", target);
        request.Headers.Add("X-Amz-Date", amzDate);
        // TryAddWithoutValidation: the SigV4 value's base64-ish segments can trip the
        // strict Authorization header parser on some .NET platforms (CI Linux lane,
        // live-verified); the SigV4 string is well-formed by construction.
        request.Headers.TryAddWithoutValidation("Authorization", auth);
        if (!string.IsNullOrEmpty(credentials.SessionToken))
            request.Headers.TryAddWithoutValidation("X-Amz-Security-Token", credentials.SessionToken);

        var send = sender ?? HttpClientSender.Instance;
        return SecretProviderErrors.Send(provider, operation,
            () => send.Send(request, timeout), discardRawMessage: true);
    }

    /// <summary>
    /// Endpoint for a service, honouring the official override variable when one is set.
    /// The region is checked in both branches: without an override it becomes part of the
    /// host name, and with one it still labels the credential scope.
    /// </summary>
    internal static string RequestUrl(string region, string service, string path, string? endpointOverride)
    {
        if (!string.IsNullOrEmpty(endpointOverride))
        {
            if (!AwsRegion.IsSafeScopeLabel(region))
                throw new System.ArgumentException("Refusing to sign a credential scope for region '" + region + "'", nameof(region));
            return endpointOverride!.TrimEnd('/') + path;
        }
        // A region travels inside the host name. Anything outside the AWS alphabet could
        // redirect the request (and its session token) to another host.
        if (!AwsRegion.IsName(region))
            throw new System.ArgumentException("Refusing to build a request URL for region '" + region + "'", nameof(region));
        return "https://" + service + "." + region + ".amazonaws.com" + path;
    }

    internal static string BuildCanonicalRequest(
        string method, string path, IReadOnlyList<(string Name, string Value)> headers, string payloadHash)
    {
        var sb = new StringBuilder();
        sb.Append(method).Append('\n');
        sb.Append(path).Append('\n');
        sb.Append('\n'); // no query string
        foreach (var h in headers) sb.Append(h.Name).Append(':').Append(h.Value).Append('\n');
        sb.Append('\n');
        sb.Append(SignedHeaders(headers)).Append('\n');
        sb.Append(payloadHash);
        return sb.ToString();
    }

    internal static string BuildStringToSign(string algorithm, string amzDate, string scope, string canonicalRequest) =>
        algorithm + "\n" + amzDate + "\n" + scope + "\n" + HexSHA256(canonicalRequest);

    internal static string SignedHeaders(IReadOnlyList<(string Name, string Value)> headers)
    {
        var names = new List<string>(headers.Count);
        foreach (var h in headers) names.Add(h.Name);
        return string.Join(";", names);
    }

    internal static byte[] SignHmac(string secretKey, string dateStamp, string region, string service, string stringToSign)
    {
        byte[] kDate = HmacSHA256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        byte[] kRegion = HmacSHA256(kDate, region);
        byte[] kService = HmacSHA256(kRegion, service);
        byte[] kSigning = HmacSHA256(kService, "aws4_request");
        return HmacSHA256(kSigning, stringToSign);
    }

    internal static string HexSHA256(string s)
    {
        using var sha = SHA256.Create();
        return BytesToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
    }

    internal static byte[] HmacSHA256(byte[] key, string data) =>
        new HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));

    internal static byte[] HmacSHA256(byte[] key, byte[] data) =>
        new HMACSHA256(key).ComputeHash(data);

    internal static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        return sb.ToString();
    }

    /// <summary>
    /// The certificate serial number AWS wants in the Credential scope: its decimal
    /// value. Windows presents serial bytes most-significant first, so they are reversed
    /// for <see cref="System.Numerics.BigInteger"/> and padded to stay unsigned.
    /// </summary>
    internal static string SerialNumberDecimal(byte[] bigEndian)
    {
        var normalised = bigEndian.Length == 0 ? new byte[] { 0 } : bigEndian;
        var littleEndian = new byte[normalised.Length + 1];
        for (int i = 0; i < normalised.Length; i++)
            littleEndian[i] = normalised[normalised.Length - 1 - i];
        littleEndian[normalised.Length] = 0;
        return new System.Numerics.BigInteger(littleEndian).ToString(CultureInfo.InvariantCulture);
    }
}
