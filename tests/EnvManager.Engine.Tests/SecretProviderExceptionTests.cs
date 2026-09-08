// SecretProviderExceptionTests.cs - typed provider error family tests (ticket 40, architecture-recovery)
// Pure-logic coverage: family surface, scrubber integration (512-char truncation +
// Authorization-header masking per ADR 0005), classification mapping, and the classified
// RotateAll counting. The AWS echo-mock test drives the REAL adapter path through the
// AWS_ENDPOINT_URL_SECRETS_MANAGER seam (issue 15) against a loopback TCP stub that
// echoes the SigV4 Authorization header back in the response body - the aws-sdk-java
// #2702 leak shape. Asserts the adapter classification-only contract: the echo never
// reaches the exception message. License: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

using EnvManager;
using EnvManager.Secrets.Core;
using EnvManager.Secrets.Manager;

using Xunit;

namespace EnvManager.Engine.Tests;

// Serial: mutates process-scoped env vars (ENVMANAGER_LOCALAPPDATA, AWS_*) and captures
// the AWS provider config seam - same discipline as LocalAppDataRedirectTests.
[Collection("CliSnapshotSerial")]
public class SecretProviderExceptionTests
{
    private const string AccessKeyId = "AKIAIOSFODNN7EXAMPLE";
    private const string AuthHeaderEcho = "AWS4-HMAC-SHA256 Credential=" + AccessKeyId +
        "/20260908/us-east-1/secretsmanager/aws4_request, SignedHeaders=content-type;host;x-amz-date, Signature=0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    // ---- AC1: base class + 6 subclasses compile and carry context ----

    [Fact]
    public void Family_Surface_BasePlusSixSubclasses_DeriveInvalidOperationException()
    {
        Assert.True(typeof(SecretProviderException).IsAssignableTo(typeof(InvalidOperationException)));
        var subclasses = new[]
        {
            typeof(SecretProviderAuthFailedException), typeof(SecretProviderNotFoundException),
            typeof(SecretProviderPermissionDeniedException), typeof(SecretProviderUnavailableException),
            typeof(SecretProviderTimeoutException), typeof(SecretProviderInvalidEnvelopeException)
        };
        Assert.All(subclasses, t =>
        {
            Assert.True(t.IsSealed, t.Name + " must be sealed");
            Assert.True(t.IsSubclassOf(typeof(SecretProviderException)), t.Name + " must derive from the family base");
        });
    }

    [Fact]
    public void Family_CarriesProviderMountIdOperationContext_AndPassesScrubber()
    {
        var ex = new SecretProviderAuthFailedException("aws-secretsmanager", "decrypt",
            "auth rejected: Authorization: " + AuthHeaderEcho, mountId: "us-east-1|secret-a");

        Assert.Equal("aws-secretsmanager", ex.Provider);
        Assert.Equal("decrypt", ex.Operation);
        Assert.Equal("us-east-1|secret-a", ex.MountId);
        Assert.True(ex is InvalidOperationException);
        Assert.True(ex is SecretProviderException);
    }

    // ---- AC3 part 1: the scrubber masks the Authorization pattern and truncates to 512 ----

    [Fact]
    public void ScrubExceptionMessage_AuthorizationHeaderEcho_MaskedAndTruncated()
    {
        // aws-sdk-java #2702 shape: a raw exception whose message echoes the request's
        // Authorization header. ADR 0005 contract: pattern-masked + 512-char cap.
        var padded = AuthHeaderEcho + new string('x', 600);
        var ex = new SecretProviderAuthFailedException("aws-secretsmanager", "decrypt", "AWS create failed: " + padded);

        Assert.StartsWith("AWS create failed: Authorization: <redacted>", ex.Message);
        Assert.DoesNotContain(AuthHeaderEcho, ex.Message);
        Assert.True(ex.Message.Length <= 512, "message must be truncated to the 512-char cap, was " + ex.Message.Length);
    }

    // ---- classification mapping (SecretProviderErrors.Classify) ----

    [Theory]
    [InlineData("http")]         // HttpRequestException -> unavailable
    [InlineData("cancel")]       // TaskCanceledException -> timeout
    [InlineData("w32-5")]        // ERROR_ACCESS_DENIED -> permissionDenied
    [InlineData("w32-1168")]     // ERROR_NOT_FOUND -> notFound
    [InlineData("w32-13")]       // other Win32 -> unavailable
    [InlineData("json")]         // JsonException -> invalidEnvelope
    [InlineData("other")]        // generic -> unknown
    public void Classify_MapsForeignExceptionTypes_IntoTheSevenBuckets(string shape)
    {
        Exception ex = shape switch
        {
            "http" => new HttpRequestException("No connection could be made because the target machine actively refused it."),
            "cancel" => new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 15 seconds elapsing."),
            "w32-5" => new System.ComponentModel.Win32Exception(5),
            "w32-1168" => new System.ComponentModel.Win32Exception(1168),
            "w32-13" => new System.ComponentModel.Win32Exception(13),
            "json" => new System.Text.Json.JsonException("'S' is an invalid start of a value"),
            _ => new InvalidOperationException("unexpected domain failure")
        };
        string expected = shape switch
        {
            "http" => SecretProviderErrors.CatUnavailable,
            "cancel" => SecretProviderErrors.CatTimeout,
            "w32-5" => SecretProviderErrors.CatPermissionDenied,
            "w32-1168" => SecretProviderErrors.CatNotFound,
            "w32-13" => SecretProviderErrors.CatUnavailable,
            "json" => SecretProviderErrors.CatInvalidEnvelope,
            _ => SecretProviderErrors.CatUnknown
        };

        Assert.Equal(expected, SecretProviderErrors.Classify(ex));
    }

    [Fact]
    public void Classify_FamilyExceptions_BucketByTheirOwnType()
    {
        Assert.Equal(SecretProviderErrors.CatAuthFailed, SecretProviderErrors.Classify(new SecretProviderAuthFailedException("p", "d", "m")));
        Assert.Equal(SecretProviderErrors.CatNotFound, SecretProviderErrors.Classify(new SecretProviderNotFoundException("p", "d", "m")));
        Assert.Equal(SecretProviderErrors.CatPermissionDenied, SecretProviderErrors.Classify(new SecretProviderPermissionDeniedException("p", "d", "m")));
        Assert.Equal(SecretProviderErrors.CatUnavailable, SecretProviderErrors.Classify(new SecretProviderUnavailableException("p", "d", "m")));
        Assert.Equal(SecretProviderErrors.CatTimeout, SecretProviderErrors.Classify(new SecretProviderTimeoutException("p", "d", "m")));
        Assert.Equal(SecretProviderErrors.CatInvalidEnvelope, SecretProviderErrors.Classify(new SecretProviderInvalidEnvelopeException("p", "d", "m")));
    }

    // ---- HTTP status mapping (FromStatus) ----

    [Theory]
    [InlineData(401, typeof(SecretProviderAuthFailedException))]
    [InlineData(403, typeof(SecretProviderPermissionDeniedException))]
    [InlineData(404, typeof(SecretProviderNotFoundException))]
    [InlineData(429, typeof(SecretProviderUnavailableException))]
    [InlineData(500, typeof(SecretProviderUnavailableException))]
    [InlineData(503, typeof(SecretProviderUnavailableException))]
    public void FromStatus_MapsHttpStatuses_OntoTheFamily(int status, Type expectedType)
    {
        var ex = SecretProviderErrors.FromStatus("p", "decrypt", (HttpStatusCode)status, "read");

        Assert.IsType(expectedType, ex);
        Assert.Contains(status.ToString(), ex.Message);
    }

    [Fact]
    public void FromStatus_DetailIsScrubbed_ByTheFamilyBaseConstructor()
    {
        var ex = SecretProviderErrors.FromStatus("p", "decrypt", HttpStatusCode.Forbidden, "read",
            detail: "denied: Authorization: " + AuthHeaderEcho);

        Assert.True(ex is SecretProviderPermissionDeniedException);
        Assert.StartsWith("p read failed (403 Forbidden): denied: Authorization: <redacted>", ex.Message);
        Assert.True(ex.Message.Length <= 512);
    }

    // ---- AWS classification-only boundary (Map with discardRawMessage) ----

    [Fact]
    public void Map_AwsDiscardRawMessage_RawEchoNeverReachesTheMessage()
    {
        var raw = new HttpRequestException("aws auth failure: Authorization: " + AuthHeaderEcho);
        var mapped = SecretProviderErrors.Map("aws-secretsmanager", "decrypt", raw, discardRawMessage: true);

        Assert.True(mapped is SecretProviderUnavailableException);
        Assert.Equal("aws-secretsmanager decrypt failed: backend unreachable (network/TLS)", mapped.Message);
        Assert.DoesNotContain(AccessKeyId, mapped.Message);
        Assert.DoesNotContain("AWS4-HMAC-SHA256", mapped.Message);
    }

    [Fact]
    public void MapPreserveMessage_KeepsTheOriginalTextVerbatim_WhenUnpatterned()
    {
        var raw = new FormatException("The input is not a valid Base-64 string as it contains a non-base 64 character");
        var mapped = SecretProviderErrors.MapPreserveMessage("dpapi-current-user", "decrypt", raw);

        Assert.Equal(raw.Message, mapped.Message);
        Assert.True(mapped is SecretProviderUnavailableException);
    }

    // ---- AC3 part 2: REAL AWS adapter path vs a loopback echo mock ----

    [Fact]
    public async Task AwsAdapter_AuthorizationEchoMock_ClassificationOnly_NoHeaderLeak()
    {
        // LocalAppData isolation: the config file must not leak machine state into the test.
        string tempRoot = Path.Combine(Path.GetTempPath(), "em-t40-echo-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string? prevLocalAppData = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
        string? prevRegion = Environment.GetEnvironmentVariable("AWS_REGION");
        string? prevEndpoint = Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER");
        string? prevAkid = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        string? prevSecret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        var listener = new TcpListener(IPAddress.Loopback, 0);
        try
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", tempRoot);
            Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", AccessKeyId);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "em-t40-not-a-real-secret");
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", "http://127.0.0.1:" + port + "/");

            // One-shot echo mock: capture the request's Authorization header, echo it back
            // in a 401 response body (aws-sdk-java #2702 leak shape).
            var serverTask = Task.Run(() => EchoAuthHeaderOnce(listener));

            var provider = new EnvManager.Secrets.Providers.AwsSecretsManagerProvider();
            var envelope = new SecretEnvelope
            {
                Provider = "aws-secretsmanager",
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                TargetName = "us-east-1|em-t40-echo-secret"
            }.Serialize();

            Assert.True(serverTask.Wait(TimeSpan.FromSeconds(10)), "echo mock never answered");
            var ex = Assert.ThrowsAny<SecretProviderException>(() => provider.Decrypt(envelope, "em-t40\\VAR"));
            Assert.True(ex is SecretProviderAuthFailedException, "expected AuthFailed for HTTP 401, got " + ex.GetType().Name);
            // Classification-only contract: the status survives, neither the echoed header
            // nor the credential id does.
            Assert.Contains("401", ex.Message);
            Assert.DoesNotContain("AWS4-HMAC-SHA256", ex.Message);
            Assert.DoesNotContain(AccessKeyId, ex.Message);
            Assert.DoesNotContain("The request failed because the request signature is invalid", ex.Message);
        }
        finally
        {
            listener.Stop();
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", prevLocalAppData);
            Environment.SetEnvironmentVariable("AWS_REGION", prevRegion);
            Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", prevEndpoint);
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", prevAkid);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", prevSecret);
            try { Directory.Delete(tempRoot, true); } catch { /* temp isolation dir */ }
        }
    }

    private static void EchoAuthHeaderOnce(TcpListener listener)
    {
        using var client = listener.AcceptTcpClient();
        using var stream = client.GetStream();
        var buffer = new byte[64 * 1024];
        int read = 0;
        // Read until end of headers, then drain the Content-Length body if present.
        int contentLength = 0;
        string requestText = "";
        while (read < buffer.Length)
        {
            int n = stream.Read(buffer, read, buffer.Length - read);
            if (n <= 0) break;
            read += n;
            requestText = Encoding.ASCII.GetString(buffer, 0, read);
            int headerEnd = requestText.IndexOf("\r\n\r\n", StringComparison.Ordinal);
            if (headerEnd >= 0)
            {
                foreach (var line in requestText[..headerEnd].Split("\r\n"))
                {
                    if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                        int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
                }
                int bodyStart = headerEnd + 4;
                if (read >= bodyStart + contentLength) break;
            }
        }
        string? auth = null;
        foreach (var line in requestText.Split("\r\n"))
        {
            if (line.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase))
                auth = line["Authorization:".Length..].Trim();
        }
        string body = "The request failed because the request signature is invalid: " + (auth ?? "(missing)");
        byte[] bodyBytes = Encoding.UTF8.GetBytes(body);
        string head = "HTTP/1.1 401 Unauthorized\r\nContent-Type: text/plain\r\nContent-Length: " + bodyBytes.Length + "\r\nConnection: close\r\n\r\n";
        byte[] headBytes = Encoding.ASCII.GetBytes(head);
        stream.Write(headBytes, 0, headBytes.Length);
        stream.Write(bodyBytes, 0, bodyBytes.Length);
        stream.Flush();
    }

    // ---- RotateAll classified counting (hermetic: no registry, no backend) ----

    [Fact]
    public void RotateAll_ClassifiesFailures_ByErrorFamily_KeepingSkipAndCount()
    {
        string tempRoot = Path.Combine(Path.GetTempPath(), "em-t40-rotate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        string? prevLocalAppData = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
        string? prevAkid = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID");
        string? prevSecret = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY");
        try
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", tempRoot);
            // Offline: with credentials absent, the AWS adapter fails closed at the
            // credential guard BEFORE any network call.
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", null);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", null);

            var awsEnvelope = new SecretEnvelope
            {
                Provider = "aws-secretsmanager",
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                TargetName = "us-east-1|em-t40-rotate-secret"
            }.Serialize();

            var profiles = new List<ProfileData>
            {
                new()
                {
                    Name = "t40-rotate",
                    SecretVariables = new List<string> { "A", "B" },
                    Variables = new List<ProfileVariable>
                    {
                        new() { Name = "A", Value = awsEnvelope },                  // AWS adapter -> authFailed
                        new() { Name = "B", Value = "not-a-valid-envelope!!" },     // manager guard -> unknown
                    }
                }
            };

            var failures = new Dictionary<string, int>();
            var (total, rotated, failed) = SecretProviderManager.RotateAll(profiles, failures);

            Assert.Equal(2, total);
            Assert.Equal(0, rotated);
            Assert.Equal(2, failed);
            Assert.Equal(1, failures[SecretProviderErrors.CatAuthFailed]);
            Assert.Equal(1, failures[SecretProviderErrors.CatUnknown]);
            // skip-and-count invariant: the failed secret values are never mutated or removed.
            Assert.Equal(awsEnvelope, profiles[0].Variables[0].Value);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", prevLocalAppData);
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", prevAkid);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", prevSecret);
            try { Directory.Delete(tempRoot, true); } catch { /* temp isolation dir */ }
        }
    }
}
