// AwsEndpointOverrideTests.cs - the endpoint override must not leak a signed request
// License: Apache-2.0

// AWS_ENDPOINT_URL_SECRETS_MANAGER points every Secrets Manager call at another address.
// https:// is fine anywhere; http:// is fine only for a host on this machine. This file
// pins that rule at the single check both providers pass through, and pins the provider
// path: a rejected override fails before any request leaves the process.

using System;
using System.Net.Http;

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

    private readonly System.Collections.Generic.Dictionary<string, string?> _saved = new();

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

    [Fact]
    public void SecretsManagerProvider_RefusesPlainHttpToAnotherHostBeforeSending()
    {
        SetCredentials("http://localhost.attacker.com:4566");

        var provider = new AwsSecretsManagerProvider();
        var ex = Assert.Throws<SecretProviderPermissionDeniedException>(() => provider.Encrypt("value-f1", "em\\VAR"));

        Assert.Contains(AwsEndpointOverride.VariableName, ex.Message);
        Assert.Equal("aws-secretsmanager", ex.Provider);
    }

    [Fact]
    public void SecretsManagerProvider_RefusesAnUnparseableOverrideBeforeSending()
    {
        SetCredentials("not an address");

        var provider = new AwsSecretsManagerProvider();
        Assert.Throws<SecretProviderPermissionDeniedException>(() => provider.Encrypt("value-f1", "em\\VAR"));
    }

    // Decrypt reaches the same CallAwsApi helper, so one path is checked here and the
    // other two providers' calls are covered by the shared helper above. The envelope is
    // well-formed so the failure can only come from the endpoint check.
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
        Assert.Throws<SecretProviderPermissionDeniedException>(() => provider.Decrypt(parsed.Serialize(), "em\\VAR"));
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
