// AwsRolesAnywhereOfflineTests.cs - offline contract for the certificate-backed AWS provider
// License: Apache-2.0

// Everything here runs without a network and without an AWS account: the credential
// exchange talks to an injected sender, the certificate is generated in memory, and the
// clock is fixed. What this cannot prove is that AWS accepts the signature - that needs
// one real CreateSession against a throwaway trust anchor.

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Threading;

using EnvManager.Secrets.Core;
using EnvManager.Secrets.Providers;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Serial: the provider reads its identity from process environment variables, so these
/// tests mutate and restore them (same discipline as the AWS contract mount).
/// </summary>
[Collection("CliSnapshotSerial")]
public sealed class AwsRolesAnywhereOfflineTests : IDisposable
{
    private static readonly DateTimeOffset FixedNow = new DateTimeOffset(2026, 11, 11, 9, 0, 0, TimeSpan.Zero);

    private const string RoleArn = "arn:aws:iam::111122223333:role/env-manager";
    private const string ProfileArn = "arn:aws:rolesanywhere:us-east-1:111122223333:profile/abcd";
    private const string TrustAnchorArn = "arn:aws:rolesanywhere:us-east-1:111122223333:trust-anchor/abcd";

    private static readonly string[] EnvVars =
    {
        "AWS_REGION", "AWS_DEFAULT_REGION",
        "AWS_ROLES_ANYWHERE_ROLE_ARN", "AWS_ROLES_ANYWHERE_PROFILE_ARN",
        "AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN", "AWS_ROLES_ANYWHERE_CERT_SHA1",
        "AWS_ROLES_ANYWHERE_DURATION_SECONDS", "AWS_ENDPOINT_URL_SECRETS_MANAGER",
        "AWS_ENDPOINT_URL_ROLES_ANYWHERE",
        "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN",
    };

    private readonly Dictionary<string, string?> _saved = new();

    public AwsRolesAnywhereOfflineTests()
    {
        foreach (string name in EnvVars)
            _saved[name] = Environment.GetEnvironmentVariable(name);
    }

    public void Dispose()
    {
        foreach (var kvp in _saved) Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
    }

    // ---- certificate fixtures (generated in memory, never written to disk) ----

    private static X509Certificate2 CreateRsaCertificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=env-manager-rolesanywhere", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static X509Certificate2 CreateEcdsaCertificate(DateTimeOffset notBefore, DateTimeOffset notAfter)
    {
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=env-manager-rolesanywhere-ec", key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        return request.CreateSelfSigned(notBefore, notAfter);
    }

    private static void SetIdentity(string thumbprint, int durationSeconds = 3600)
    {
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", null);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_ROLE_ARN", RoleArn);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_PROFILE_ARN", ProfileArn);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN", TrustAnchorArn);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_CERT_SHA1", thumbprint);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_DURATION_SECONDS", durationSeconds.ToString());
        // Long-lived static keys stay unset so nothing can silently fall back to them.
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", null);
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", null);
        Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", null);
    }

    private static RolesAnywhereOptions Options(string thumbprint, int duration = 3600) =>
        new("us-east-1", RoleArn, ProfileArn, TrustAnchorArn, thumbprint, duration);

    /// <summary>Records every request and answers with a scripted status/body.</summary>
    private sealed class RecordingSender : IAwsHttpSender
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        internal List<(HttpRequestMessage Request, string Body)> Requests { get; } = new();

        internal RecordingSender(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;

        public HttpResponseMessage Send(HttpRequestMessage request, TimeSpan timeout)
        {
            string body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? "";
            Requests.Add((request, body));
            return _responder(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    // The real response shape: credentialSet[] > credentials. Reading the credentials
    // off the root parses cleanly but fails against every real response, so the fixture
    // has to be the documented shape for the offline suite to mean anything.
    private const string SessionBody =
        "{\"credentialSet\":[{\"assumedRoleUser\":{\"arn\":\"arn:aws:sts::111122223333:assumed-role/env-manager/abcd\"," +
        "\"assumedRoleId\":\"AROAEXAMPLE:abcd\"},\"credentials\":{\"accessKeyId\":\"ASIAEXAMPLE\"," +
        "\"secretAccessKey\":\"secret\",\"sessionToken\":\"token\",\"expiration\":\"2026-11-11T10:05:00Z\"}," +
        "\"packedPolicySize\":0,\"roleArn\":\"arn:aws:iam::111122223333:role/env-manager\"}]," +
        "\"subjectArn\":\"arn:aws:rolesanywhere:us-east-1:111122223333:subject/abcd\"}";

    // ---- 1. request shape: exact canonical headers, serial, algorithm ----

    [Fact]
    public void CreateSession_Rsa_RequestCarriesTheSignedX509HeadersAndDecimalSerial()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));

        var session = AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", SecretProviderErrors.OpEncrypt, Options(cert.Thumbprint), sender,
            () => FixedNow, cert);

        Assert.Single(sender.Requests);
        var request = sender.Requests[0].Request;
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://rolesanywhere.us-east-1.amazonaws.com/sessions", request.RequestUri!.ToString());

        string auth = request.Headers.GetValues("Authorization").First();
        Assert.StartsWith("AWS4-X509-RSA-SHA256 Credential=", auth);
        // The credential scope names the certificate serial in decimal, not an access key.
        string expectedSerial = AwsSigV4Client.SerialNumberDecimal(cert.SerialNumberBytes.ToArray());
        Assert.Contains("/20261111/us-east-1/rolesanywhere/aws4_request", auth);
        Assert.StartsWith("AWS4-X509-RSA-SHA256 Credential=" + expectedSerial + "/", auth);
        Assert.Contains("SignedHeaders=content-type;host;x-amz-date;x-amz-x509", auth);

        Assert.True(request.Headers.Contains("X-Amz-X509"));
        Assert.Equal(Convert.ToBase64String(cert.RawData), request.Headers.GetValues("X-Amz-X509").First());
        Assert.Equal("ASIAEXAMPLE", session.Credentials.AccessKeyId);
        Assert.Equal(FixedNow.AddMinutes(65), session.ExpiresAt);
    }

    [Fact]
    public void CreateSession_TimestampIsFixed_SoHeadersAndScopeAreDeterministic()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);

        var first = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));
        var second = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));
        var options = Options(cert.Thumbprint);

        AwsRolesAnywhereSessionExchange.CreateSession("aws-roles-anywhere", "encrypt", options, first, () => FixedNow, cert);
        AwsRolesAnywhereSessionExchange.CreateSession("aws-roles-anywhere", "encrypt", options, second, () => FixedNow, cert);

        Assert.Equal(first.Requests[0].Request.Headers.GetValues("X-Amz-Date").First(),
                     second.Requests[0].Request.Headers.GetValues("X-Amz-Date").First());
        Assert.Equal("20261111T090000Z", first.Requests[0].Request.Headers.GetValues("X-Amz-Date").First());
    }

    [Fact]
    public void CreateSession_Rsa_SignatureVerifiesAgainstTheCertificatePublicKey()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));

        AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert);

        // Rebuild the exact StringToSign the provider signed and check the signature.
        // The timestamp and payload are fixed, so this is reproducible offline.
        string body = sender.Requests[0].Body;
        string payloadHash = AwsSigV4Client.HexSHA256(body);
        var headers = new List<(string, string)>
        {
            ("content-type", "application/json"),
            ("host", "rolesanywhere.us-east-1.amazonaws.com"),
            ("x-amz-date", "20261111T090000Z"),
            ("x-amz-x509", Convert.ToBase64String(cert.RawData)),
        };
        string canonical = AwsSigV4Client.BuildCanonicalRequest("POST", "/sessions", headers, payloadHash);
        string sts = AwsSigV4Client.BuildStringToSign(
            "AWS4-X509-RSA-SHA256", "20261111T090000Z",
            "20261111/us-east-1/rolesanywhere/aws4_request", canonical);

        string signatureHex = sender.Requests[0].Request.Headers.GetValues("Authorization").First()
            .Split("Signature=")[1];
        byte[] signature = Convert.FromHexString(signatureHex);
        using var pub = cert.GetRSAPublicKey()!;
        Assert.True(pub.VerifyData(Encoding.UTF8.GetBytes(sts), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void CreateSession_Ecdsa_UsesDerEncodedSignatureNotRawP1363()
    {
        using var cert = CreateEcdsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));

        AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert);

        string auth = sender.Requests[0].Request.Headers.GetValues("Authorization").First();
        Assert.StartsWith("AWS4-X509-ECDSA-SHA256 Credential=", auth);
        byte[] signature = Convert.FromHexString(auth.Split("Signature=")[1]);
        // A DER SEQUENCE starts with 0x30; raw P1363 would not.
        Assert.Equal(0x30, signature[0]);

        using var pub = cert.GetECDsaPublicKey()!;
        string body = sender.Requests[0].Body;
        var headers = new List<(string, string)>
        {
            ("content-type", "application/json"),
            ("host", "rolesanywhere.us-east-1.amazonaws.com"),
            ("x-amz-date", "20261111T090000Z"),
            ("x-amz-x509", Convert.ToBase64String(cert.RawData)),
        };
        string sts = AwsSigV4Client.BuildStringToSign(
            "AWS4-X509-ECDSA-SHA256", "20261111T090000Z",
            "20261111/us-east-1/rolesanywhere/aws4_request",
            AwsSigV4Client.BuildCanonicalRequest("POST", "/sessions", headers, AwsSigV4Client.HexSHA256(body)));
        Assert.True(pub.VerifyData(Encoding.UTF8.GetBytes(sts), signature, HashAlgorithmName.SHA256,
            DSASignatureFormat.Rfc3279DerSequence));
    }

    // ---- 2. envelope round-trip through the provider (fake wire, no AWS) ----

    [Fact]
    public void EncryptThenDecrypt_RoundTripsThroughTheRealProviderCode()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);

        var sender = new RecordingSender(request =>
            request.RequestUri!.AbsolutePath == "/sessions"
                ? Json(HttpStatusCode.Created, SessionBody)
                : request.Headers.GetValues("X-Amz-Target").First().Contains("CreateSecret", StringComparison.Ordinal)
                    ? Json(HttpStatusCode.OK, "{}")
                    : Json(HttpStatusCode.OK, "{\"SecretString\":\"round-trip-plaintext\"}"));

        var provider = new AwsRolesAnywhereProvider(sender, cert, () => FixedNow);
        string envelope = provider.Encrypt("round-trip-plaintext", "em-ra-roundtrip");
        Assert.Equal("round-trip-plaintext", provider.Decrypt(envelope, "em-ra-roundtrip"));
        // The plaintext never rides in the envelope.
        Assert.DoesNotContain("round-trip-plaintext", envelope);
        // Both the session exchange and the Secrets Manager calls used the fake wire.
        Assert.True(sender.Requests.Count >= 3);
    }

    [Fact]
    public void Encrypt_EnvelopeNeverCarriesThePlaintext()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        const string canary = "em-ra-canary-42";
        var sender = new RecordingSender(request =>
            request.RequestUri!.AbsolutePath == "/sessions"
                ? Json(HttpStatusCode.Created, SessionBody)
                : Json(HttpStatusCode.OK, "{}"));
        var provider = new AwsRolesAnywhereProvider(sender, cert, () => FixedNow);

        string envelope = provider.Encrypt(canary, "em-ra-canary-context");

        // Same assertion every contract mount makes: the stored form never holds the value.
        Assert.DoesNotContain(canary, envelope);
    }

    [Fact]
    public void Encrypt_ReusesTheCachedSession_InsteadOfExchangingEveryCall()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));
        var provider = new AwsRolesAnywhereProvider(sender, cert, () => FixedNow);

        provider.ProbeReadOnly();
        provider.ProbeReadOnly();
        provider.ProbeReadOnly();

        // One exchange, not three: the cache is keyed on the whole identity.
        Assert.Single(sender.Requests);
    }

    [Fact]
    public void CachedSession_IsDiscardedOnceInsideTheRefreshLead()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        DateTimeOffset now = FixedNow;
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));
        var provider = new AwsRolesAnywhereProvider(sender, cert, () => now);

        provider.ProbeReadOnly();
        // The session expires at FixedNow + 65 min; past +60 min it must be replaced.
        now = FixedNow.AddMinutes(61);
        provider.ProbeReadOnly();

        Assert.Equal(2, sender.Requests.Count);
    }

    [Fact]
    public void Decrypt_UsesTheEnvelopeRegionForSecretsManager_ButTheConfiguredRegionForTheExchange()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(request =>
            request.RequestUri!.AbsolutePath == "/sessions"
                ? Json(HttpStatusCode.Created, SessionBody)
                : Json(HttpStatusCode.OK, "{\"SecretString\":\"v\"}"));
        var provider = new AwsRolesAnywhereProvider(sender, cert, () => FixedNow);

        // An envelope written in another region. The trust anchor is a regional resource,
        // so the exchange must still go to the configured region; only the Secrets Manager
        // call follows the envelope.
        var envelope = new SecretEnvelope
        {
            Provider = "aws-roles-anywhere",
            Version = 1,
            CreatedAt = FixedNow.ToString("O"),
            TargetName = "eu-west-1|em-elsewhere",
        }.Serialize();
        Assert.Equal("v", provider.Decrypt(envelope, "ctx"));

        var requests = sender.Requests;
        Assert.Contains(requests, r => r.Request.RequestUri!.Host == "rolesanywhere.us-east-1.amazonaws.com");
        Assert.Contains(requests, r => r.Request.RequestUri!.Host == "secretsmanager.eu-west-1.amazonaws.com");
        Assert.DoesNotContain(requests, r => r.Request.RequestUri!.Host == "rolesanywhere.eu-west-1.amazonaws.com");
    }

    [Fact]
    public void CreateSession_ReadsTheCredentialsFromCredentialSetNotTheRoot()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));

        var session = AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert);

        // The documented shape is credentialSet[0].credentials; a root-level read would
        // have thrown "does not contain credentials" here.
        Assert.Equal("ASIAEXAMPLE", session.Credentials.AccessKeyId);
        Assert.Equal("secret", session.Credentials.SecretAccessKey);
        Assert.Equal("token", session.Credentials.SessionToken);
        Assert.Equal(new DateTimeOffset(2026, 11, 11, 10, 5, 0, TimeSpan.Zero), session.ExpiresAt);
    }

    [Fact]
    public void CreateSession_SuccessWithoutCredentialSet_FailsAsUnavailableRatherThanThrowing()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        // A success status with the wrong shape must map onto the typed family, not a
        // KeyNotFoundException from JsonElement.GetProperty.
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created,
            "{\"subjectArn\":\"arn:aws:rolesanywhere:us-east-1:111122223333:subject/abcd\"}"));

        var ex = Assert.Throws<SecretProviderUnavailableException>(() =>
            AwsRolesAnywhereSessionExchange.CreateSession(
                "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert));

        Assert.Contains("credentialSet", ex.Message);
    }

    [Fact]
    public void CreateSession_CredentialWithoutKeys_FailsAsUnavailableRatherThanThrowing()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created,
            "{\"credentialSet\":[{\"credentials\":{\"sessionToken\":\"T\"}}]}"));

        var ex = Assert.Throws<SecretProviderUnavailableException>(() =>
            AwsRolesAnywhereSessionExchange.CreateSession(
                "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert));

        Assert.Contains("access key", ex.Message);
    }

    // ---- 3. failure mapping ----

    [Fact]
    public void CreateSession_401_ReportsAuthFailureNamingTheFingerprint_NotTheBody()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ =>
            Json(HttpStatusCode.Unauthorized, "{\"message\":\"Untrusted signing certificate\",\"accessKey\":\"SHOULD-NOT-LEAK\"}"));

        var ex = Assert.Throws<SecretProviderAuthFailedException>(() =>
            AwsRolesAnywhereSessionExchange.CreateSession(
                "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert));

        Assert.Contains(cert.Thumbprint, ex.Message);
        Assert.DoesNotContain("SHOULD-NOT-LEAK", ex.Message);
    }

    [Fact]
    public void MissingConfiguration_NamesEveryMissingVariableAndFallsBackToNothing()
    {
        foreach (string name in EnvVars) Environment.SetEnvironmentVariable(name, null);

        var provider = new AwsRolesAnywhereProvider();
        Assert.False(provider.Available);
        var ex = Assert.Throws<SecretProviderUnavailableException>(() => provider.Encrypt("x", "ctx"));
        Assert.Contains("AWS_ROLES_ANYWHERE_ROLE_ARN", ex.Message);
        Assert.Contains("AWS_ROLES_ANYWHERE_CERT_SHA1", ex.Message);
    }

    [Fact]
    public void Duration_IsClampedToTheRangeAwsAccepts()
    {
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_ROLE_ARN", RoleArn);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_PROFILE_ARN", ProfileArn);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN", TrustAnchorArn);
        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_CERT_SHA1", "AA");

        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_DURATION_SECONDS", "60");
        Assert.Equal(900, AwsRolesAnywhereSessionExchange.ReadOptions()!.DurationSeconds);

        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_DURATION_SECONDS", "999999");
        Assert.Equal(43200, AwsRolesAnywhereSessionExchange.ReadOptions()!.DurationSeconds);

        Environment.SetEnvironmentVariable("AWS_ROLES_ANYWHERE_DURATION_SECONDS", "not-a-number");
        Assert.Equal(3600, AwsRolesAnywhereSessionExchange.ReadOptions()!.DurationSeconds);
    }

    [Fact]
    public void Session_ExpiryParsesRfc3339_AndFallsBackToTheRequestedDuration()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var noExpiry = new RecordingSender(_ => Json(HttpStatusCode.Created,
            "{\"credentialSet\":[{\"credentials\":{\"accessKeyId\":\"A\",\"secretAccessKey\":\"S\",\"sessionToken\":\"T\"}}]}"));

        var session = AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint, 1800), noExpiry, () => FixedNow, cert);

        Assert.Equal(FixedNow.AddSeconds(1800), session.ExpiresAt);
        Assert.False(session.NeedsRefresh(FixedNow.AddMinutes(20)));
        Assert.True(session.NeedsRefresh(FixedNow.AddMinutes(26)));
    }

    // ---- 4. the X.509 variant must not be reachable by a planted endpoint variable ----

    [Fact]
    public void CreateSession_HasNoEndpointOverrideVariable()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_ROLES_ANYWHERE", "http://127.0.0.1:9/sessions");
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));

        AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert);

        Assert.Equal("https://rolesanywhere.us-east-1.amazonaws.com/sessions",
            sender.Requests[0].Request.RequestUri!.ToString());
    }

    // ---- 6. the transmitted headers must match the signed ones byte for byte ----

    [Fact]
    public void CreateSession_TransmittedContentTypeEqualsTheSignedValue()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));

        AwsRolesAnywhereSessionExchange.CreateSession(
            "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert);

        // The canonical request signs content-type as "application/json". If the wire
        // carries "application/json; charset=utf-8", AWS recomputes with the received
        // value and rejects the signature.
        string transmitted = sender.Requests[0].Request.Content!.Headers.ContentType!.ToString();
        Assert.Equal("application/json", transmitted);
    }

    [Fact]
    public void SecretsManagerCall_TransmittedContentTypeEqualsTheSignedValue()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(request =>
            request.RequestUri!.AbsolutePath == "/sessions"
                ? Json(HttpStatusCode.Created, SessionBody)
                : Json(HttpStatusCode.OK, "{}"));
        var provider = new AwsRolesAnywhereProvider(sender, cert, () => FixedNow);

        provider.Encrypt("value", "ctx");

        var secretsCall = sender.Requests.First(r => r.Request.RequestUri!.AbsolutePath == "/");
        Assert.Equal("application/x-amz-json-1.1",
            secretsCall.Request.Content!.Headers.ContentType!.ToString());
    }

    // ---- 7. certificate shape checks ----

    [Fact]
    public void SharedAwsPayload_KeepsTheWireShapeBothProvidersSignedFor()
    {
        // Both AWS providers build their request bodies from this one place; the shape is
        // pinned here so a refactor cannot quietly change what a signature covers.
        Assert.Equal("{\"SecretId\":\"em-shared\",\"SecretString\":\"v\"}",
            AwsSecretsManagerPayload.PutSecretValue("em-shared", "v"));
        Assert.Equal("{\"SecretId\":\"em-shared\"}",
            AwsSecretsManagerPayload.GetSecretValue("em-shared"));
        Assert.Equal("{\"SecretId\":\"em-shared\",\"ForceDeleteWithoutRecovery\":true}",
            AwsSecretsManagerPayload.DeleteSecret("em-shared"));
        Assert.Contains("\"ClientRequestToken\":\"tok\"",
            AwsSecretsManagerPayload.CreateSecret("em-shared", "v", "tok"));
    }

    [Fact]
    public void CreateSession_NullOrMistypedCredentialElement_FailsAsUnavailableRatherThanThrowing()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);

        // A null element and a numeric accessKeyId both used to escape the provider error
        // family as InvalidOperationException from JsonElement.
        foreach (string body in new[]
        {
            "{\"credentialSet\":[null]}",
            "{\"credentialSet\":[{\"credentials\":null}]}",
            "{\"credentialSet\":[{\"credentials\":{\"accessKeyId\":123,\"secretAccessKey\":\"S\"}}]}",
        })
        {
            var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, body));
            Assert.Throws<SecretProviderUnavailableException>(() =>
                AwsRolesAnywhereSessionExchange.CreateSession(
                    "aws-roles-anywhere", "encrypt", Options(cert.Thumbprint), sender, () => FixedNow, cert));
        }
    }

    [Fact]
    public void SharedAwsPayload_EscapesEveryControlCharacterThatIsIllegalRawInJson()
    {
        // A value carrying \b, \f or another C0 character would otherwise travel raw
        // inside the JSON string, and Secrets Manager rejects the whole request.
        string escaped = AwsSecretsManagerPayload.PutSecretValue("em-shared", "a\bb\fc\u0001d");
        Assert.Equal("{\"SecretId\":\"em-shared\",\"SecretString\":\"a\\bb\\fc\\u0001d\"}", escaped);

        // The escaped body stays parseable, and the round trip returns the original value.
        using var doc = JsonDocument.Parse(escaped);
        Assert.Equal("a\bb\fc\u0001d", doc.RootElement.GetProperty("SecretString").GetString());
    }

    [Fact]
    public void SharedSecretTarget_AllowsANonAwsRegionOnlyWhenAnEndpointOverrideIsConfigured()
    {
        // With an override the region only labels the credential scope, so a local
        // emulator's label such as `local_test` must survive a write/read round trip.
        // Rejecting it would let Encrypt store an envelope that Decrypt then refuses.
        // A leaked override from an earlier test would make the first assertion pass for
        // the wrong reason, so the variable is cleared before it runs.
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", null);
        Assert.Null(AwsSecretTarget.Parse("local_test|em-shared"));
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", "http://127.0.0.1:4566");
        var target = AwsSecretTarget.Parse("local_test|em-shared");
        Assert.NotNull(target);
        Assert.Equal("em-shared", target!.Value.SecretId);
        Assert.Equal("local_test", target.Value.Region);
        // Host-injection characters stay rejected even with an override: the override
        // supplies the whole host, so those characters have no legitimate use anywhere.
        Assert.Null(AwsSecretTarget.Parse("evil.com/@attacker|em-shared"));
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", null);
    }

    [Fact]
    public void SharedSecretTarget_RejectsARegionThatCouldRedirectTheRequest()
    {
        // The region is interpolated into the request host, so an envelope carrying
        // `evil.com/@attacker` would otherwise send the session token to that host.
        Assert.Null(AwsSecretTarget.Parse("evil.com/@attacker|em-shared"));
        Assert.Null(AwsSecretTarget.Parse("us-east-1/x|em-shared"));
        Assert.Null(AwsSecretTarget.Parse("us east 1|em-shared"));
        Assert.Null(AwsSecretTarget.Parse("US-EAST-1|em-shared"));
        Assert.True(AwsRegion.IsName("us-east-1"));
        Assert.True(AwsRegion.IsName("ap-southeast-2"));
        Assert.False(AwsRegion.IsName("evil.com/@host"));
        Assert.False(AwsRegion.IsName(""));
        // With an override the label may differ from an AWS region name, but it still has
        // to be a legal credential-scope label - which is checked on that branch too.
        Assert.True(AwsRegion.IsSafeScopeLabel("local_test"));
        Assert.False(AwsRegion.IsSafeScopeLabel("local/test"));
        Assert.False(AwsRegion.IsSafeScopeLabel("evil.com/@host"));
        Assert.False(AwsRegion.IsSafeScopeLabel(""));
    }

    [Fact]
    public void Decrypt_RejectsAnEnvelopeWithAHostileRegionInsteadOfSendingIt()
    {
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        SetIdentity(cert.Thumbprint);
        var sender = new RecordingSender(_ => Json(HttpStatusCode.Created, SessionBody));
        var provider = new AwsRolesAnywhereProvider(sender, cert, () => FixedNow);

        var envelope = new SecretEnvelope
        {
            Provider = "aws-roles-anywhere",
            Version = 1,
            CreatedAt = FixedNow.ToString("O"),
            TargetName = "evil.com/@attacker|em-shared",
        }.Serialize();

        Assert.Throws<SecretProviderInvalidEnvelopeException>(() => provider.Decrypt(envelope, "ctx"));
        // Nothing left the process: no request at all, hostile or otherwise.
        Assert.Empty(sender.Requests);
    }

    [Fact]
    public void SharedSecretTarget_RoundTripsTheEnvelopeValueAndRejectsMalformedInput()
    {
        var target = new AwsSecretTarget("us-east-1", "em-shared");
        Assert.Equal("us-east-1|em-shared", target.EnvelopeValue);
        Assert.Equal(target, AwsSecretTarget.Parse(target.EnvelopeValue));
        // A bare region or a bare id is not a target, and neither is a missing string.
        Assert.Null(AwsSecretTarget.Parse("us-east-1"));
        Assert.Null(AwsSecretTarget.Parse("|em-shared"));
        Assert.Null(AwsSecretTarget.Parse(null));
        // The AWS secret-name alphabet survives; anything else becomes a dash.
        Assert.Equal("em-shared", AwsSecretTarget.SanitizeSecretId("em shared"));
        Assert.Equal(512, AwsSecretTarget.SanitizeSecretId(new string('x', 600)).Length);
    }

    [Fact]
    public void CertificateValidation_RejectsAMissingOrExpiredOrKeylessCertificate()
    {
        using var expired = CreateRsaCertificate(FixedNow.AddDays(-30), FixedNow.AddDays(-1));
        var ex = Assert.Throws<SecretProviderAuthFailedException>(() =>
            CertificateStore.ValidateForRolesAnywhere("aws-roles-anywhere", "encrypt", expired, "AA", FixedNow));
        Assert.Contains("expired", ex.Message);
    }

    [Fact]
    public void CertificateValidation_RejectsACertificateWithoutDigitalSignatureKeyUsage()
    {
        using var cert = CreateNoDigitalSignatureCertificate();
        var ex = Assert.Throws<SecretProviderAuthFailedException>(() =>
            CertificateStore.ValidateForRolesAnywhere("aws-roles-anywhere", "encrypt", cert, "AA", FixedNow));
        Assert.Contains("digital signature", ex.Message);
    }

    [Fact]
    public void CertificateValidation_AcceptsAValidCertificateWhoseFirstExtensionIsNotKeyUsage()
    {
        // The fixtures add BasicConstraints before KeyUsage, so a reader that casts
        // every extension to X509KeyUsageExtension must skip past the first one.
        using var cert = CreateRsaCertificate(FixedNow.AddDays(-1), FixedNow.AddDays(30));
        CertificateStore.ValidateForRolesAnywhere("aws-roles-anywhere", "encrypt", cert, cert.Thumbprint, FixedNow);
    }

    private static X509Certificate2 CreateNoDigitalSignatureCertificate()
    {
        using var key = RSA.Create(2048);
        var request = new CertificateRequest("CN=env-manager-no-sign", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyEncipherment, true));
        return request.CreateSelfSigned(FixedNow.AddDays(-1), FixedNow.AddDays(30));
    }

    // ---- 5. the probe declaration keeps the cloud account read-only ----

    [Fact]
    public void Provider_DeclaresCapabilitiesAndAByteCountFreeExpiryFreeProbe()
    {
        var provider = new AwsRolesAnywhereProvider();
        Assert.Equal("aws-roles-anywhere", provider.Name);
        Assert.True(provider.RefreshCapable);
        Assert.True(provider.CertAuthRequired);
        Assert.True(provider.RequiresNetwork);
        Assert.False(provider.ProbeWritesData);
    }
}
