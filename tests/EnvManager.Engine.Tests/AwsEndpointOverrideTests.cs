// AwsEndpointOverrideTests.cs - the endpoint override must not leak a signed request
// License: Apache-2.0

// AWS_ENDPOINT_URL_SECRETS_MANAGER points every Secrets Manager call at another address.
// https:// is fine anywhere; http:// is fine only for a host on this machine. This file
// pins that rule at the single check both providers pass through, and pins the provider
// path: a rejected override fails before any request leaves the process.

using System;
using System.Collections.Generic;

using EnvManager.Secrets.Core;
using EnvManager.Secrets.Providers;

using Xunit;

namespace EnvManager.Engine.Tests;

// Serial: mutates process-scoped AWS env vars - same discipline as the other AWS tests.
[Collection("CliSnapshotSerial")]
public sealed class AwsEndpointOverrideTests : IDisposable
{
    private static readonly string[] EnvVars =
    {
        "AWS_REGION", "AWS_DEFAULT_REGION", "AWS_ENDPOINT_URL_SECRETS_MANAGER",
        "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_SESSION_TOKEN",
    };

    private readonly Dictionary<string, string?> _saved = new();

    public AwsEndpointOverrideTests()
    {
        foreach (string name in EnvVars)
            _saved[name] = Environment.GetEnvironmentVariable(name);
    }

    public void Dispose()
    {
        foreach (var kvp in _saved) Environment.SetEnvironmentVariable(kvp.Key, kvp.Value);
    }

    // ---- the rule itself ----

    [Theory]
    [InlineData("https://secretsmanager.us-east-1.amazonaws.com")]
    [InlineData("https://localstack.example.com:4566")]
    [InlineData("http://127.0.0.1:4566")]
    [InlineData("http://localhost:4566")]
    [InlineData("http://LocalHost:4566")]
    [InlineData("http://[::1]:4566")]
    public void RequireSafeEndpoint_AcceptsHttpsAnywhereAndHttpOnThisMachine(string endpoint)
    {
        Assert.Equal(endpoint, AwsEndpointOverride.RequireSafeEndpoint("aws-secretsmanager", "encrypt", endpoint));
    }

    [Theory]
    [InlineData("http://192.168.1.5:4566")]
    [InlineData("http://localstack:4566")]
    [InlineData("http://secretsmanager.us-east-1.amazonaws.com")]
    // Substring checks let these through; a parsed host name does not.
    [InlineData("http://localhost.attacker.com")]
    [InlineData("http://127.0.0.1.evil.com")]
    public void RequireSafeEndpoint_RefusesPlainHttpToAnotherHost(string endpoint)
    {
        var ex = Assert.Throws<SecretProviderPermissionDeniedException>(() =>
            AwsEndpointOverride.RequireSafeEndpoint("aws-secretsmanager", "encrypt", endpoint));

        Assert.Contains("https://", ex.Message);
        Assert.Contains(AwsEndpointOverride.VariableName, ex.Message);
        // The address itself stays out of the message: it can carry credentials.
        Assert.DoesNotContain(endpoint, ex.Message);
    }

    [Theory]
    [InlineData("127.0.0.1:4566")]
    [InlineData("localhost:4566")]
    [InlineData("ftp://127.0.0.1:4566")]
    [InlineData("file:///c:/secrets")]
    [InlineData("http://")]
    public void RequireSafeEndpoint_RefusesAnAddressThatIsNotHttp(string endpoint)
    {
        Assert.Throws<SecretProviderPermissionDeniedException>(() =>
            AwsEndpointOverride.RequireSafeEndpoint("aws-secretsmanager", "encrypt", endpoint));
    }

    [Fact]
    public void RequireSafeEndpoint_LeavesTheDefaultEndpointAlone()
    {
        Assert.Null(AwsEndpointOverride.RequireSafeEndpoint("aws-secretsmanager", "encrypt", null));
        Assert.Null(AwsEndpointOverride.RequireSafeEndpoint("aws-secretsmanager", "encrypt", ""));
    }

    // ---- the provider path: a rejected override never reaches the wire ----

    // The provider has no sender seam, so "nothing was sent" is guaranteed structurally:
    // the check runs inside AwsSigV4Client.Call before any request object exists. What
    // these tests pin is the refusal itself plus the operation label, which used to be
    // the constant "aws-api" and now names the verb the caller asked for.
    [Fact]
    public void SecretsManagerProvider_RefusesPlainHttpToAnotherHostOnEncrypt()
    {
        SetCredentials("http://localhost.attacker.com:4566");

        var provider = new AwsSecretsManagerProvider();
        var ex = Assert.Throws<SecretProviderPermissionDeniedException>(() => provider.Encrypt("value-f1", "em\\VAR"));

        Assert.Contains(AwsEndpointOverride.VariableName, ex.Message);
        Assert.Equal("aws-secretsmanager", ex.Provider);
        Assert.Equal(SecretProviderErrors.OpEncrypt, ex.Operation);
        Assert.Equal(SecretProviderErrors.CatPermissionDenied, SecretProviderErrors.Classify(ex));
    }

    [Fact]
    public void SecretsManagerProvider_RefusesAnUnparseableOverrideOnEncrypt()
    {
        SetCredentials("not an address");

        var provider = new AwsSecretsManagerProvider();
        Assert.Throws<SecretProviderPermissionDeniedException>(() => provider.Encrypt("value-f1", "em\\VAR"));
    }

    [Fact]
    public void SecretsManagerProvider_RefusesPlainHttpToAnotherHostOnDecrypt()
    {
        SetCredentials("http://localstack:4566");

        var parsed = new SecretEnvelope
        {
            Provider = "aws-secretsmanager",
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = "us-east-1|em-shared",
        };

        var provider = new AwsSecretsManagerProvider();
        var ex = Assert.Throws<SecretProviderPermissionDeniedException>(() => provider.Decrypt(parsed.Serialize(), "em\\VAR"));

        Assert.Equal(SecretProviderErrors.OpDecrypt, ex.Operation);
    }

    // ---- the transport: a redirect must not re-send the request elsewhere ----

    // A signed request carries the session token and, on a write, the plaintext secret in
    // its body. A 307 from a loopback emulator would hand both to whatever host the
    // Location header names, and that host never passed the endpoint check. The sender
    // refuses to follow, so the redirect comes back as an ordinary response instead.
    [Fact]
    public void HttpClientSender_DoesNotFollowARedirect()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        int port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        string received = "";
        Exception? stubError = null;

        // The stub runs on its own thread because the sender blocks the test thread for
        // the whole round trip. Any fault is kept and re-thrown below: an unhandled
        // exception on a thread stops the whole test process, background or not.
        var serverThread = new System.Threading.Thread(() =>
        {
            try
            {
                using var socket = listener.AcceptTcpClient();
                using var stream = socket.GetStream();
                stream.ReadTimeout = 5000;
                received = ReadRequestThroughBody(stream);
                byte[] reply = System.Text.Encoding.ASCII.GetBytes(
                    "HTTP/1.1 307 Temporary Redirect\r\nLocation: http://127.0.0.1:1/elsewhere\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                stream.Write(reply, 0, reply.Length);
            }
            catch (Exception ex) { stubError = ex; }
        }) { IsBackground = true, Name = "em-redirect-stub" };
        serverThread.Start();

        try
        {
            using var request = new System.Net.Http.HttpRequestMessage(
                System.Net.Http.HttpMethod.Post, "http://127.0.0.1:" + port + "/");
            request.Content = new System.Net.Http.StringContent("{\"SecretString\":\"em-canary\"}");
            request.Headers.TryAddWithoutValidation("X-Amz-Security-Token", "em-session-token");
            // Fail fast if the sender does follow: port 1 has nothing listening.
            using var response = new HttpClientSender().Send(request, TimeSpan.FromSeconds(5));

            Assert.Equal(System.Net.HttpStatusCode.TemporaryRedirect, response.StatusCode);
            Assert.True(serverThread.Join(TimeSpan.FromSeconds(10)), "redirect stub did not finish");
            if (stubError != null) throw new InvalidOperationException("redirect stub faulted", stubError);
            Assert.Contains("em-canary", received);
        }
        finally
        {
            listener.Stop();
            serverThread.Join(TimeSpan.FromSeconds(5));
        }
    }

    // One Read can stop inside the headers, so the body is read separately by the length
    // the headers declare. Returns the raw bytes so the caller can assert on the payload.
    private static string ReadRequestThroughBody(System.Net.Sockets.NetworkStream stream)
    {
        var text = new System.Text.StringBuilder();
        var buffer = new byte[4096];
        int contentLength = -1;
        int headerEnd = -1;
        while (headerEnd < 0 || text.Length < headerEnd + 4 + contentLength)
        {
            int read = stream.Read(buffer, 0, buffer.Length);
            if (read == 0) break;
            text.Append(System.Text.Encoding.ASCII.GetString(buffer, 0, read));
            if (headerEnd < 0)
            {
                int at = text.ToString().IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (at >= 0)
                {
                    headerEnd = at;
                    foreach (string line in text.ToString().Split("\r\n"))
                    {
                        if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                            contentLength = int.Parse(line.Substring("Content-Length:".Length).Trim());
                    }
                }
            }
        }
        return text.ToString();
    }

    private static void SetCredentials(string endpoint)
    {
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        Environment.SetEnvironmentVariable("AWS_DEFAULT_REGION", null);
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
        Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", null);
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", endpoint);
    }
}
