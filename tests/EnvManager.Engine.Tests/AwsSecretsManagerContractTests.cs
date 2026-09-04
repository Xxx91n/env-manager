// AwsSecretsManagerContractTests.cs - AwsSecretsManagerProvider contract mount (ticket 10; L1 via issue 15)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="AwsSecretsManagerProvider"/>. Backend-independent
/// assertions always run. The backend-dependent assertions target LocalStack
/// (pinned to 4.4.0, the last token-free community image) through the provider's
/// own SigV4 path with dummy credentials - the endpoint is redirected to the
/// emulator with the official AWS service-specific endpoint override env var
/// (AWS_ENDPOINT_URL_SECRETS_MANAGER, wired into the provider as a production seam;
/// real AWS deployments never set it, so behavior is unchanged).
/// </summary>
[Collection(L1ContainerCollection.Name)]
public sealed class AwsSecretsManagerContractTests : SecretProviderContractTests
{
    public AwsSecretsManagerContractTests()
        : base(new SkippedProviderHarness(() => new AwsSecretsManagerProvider()))
    {
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext()
    {
        var container = L1ContainerFixtures.StartLocalStack();
        RunAgainstLocalStack(container, harness => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(harness));
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext()
    {
        var container = L1ContainerFixtures.StartLocalStack();
        RunAgainstLocalStack(container, harness => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(harness));
    }

    private static void RunAgainstLocalStack(Testcontainers.LocalStack.LocalStackContainer container, Action<AwsSecretsManagerL1Harness> run)
    {
        Environment.SetEnvironmentVariable("AWS_REGION", "us-east-1");
        Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", "test");
        Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", "test");
        // dummy session token too: exercises the optional SigV4 token header path
        Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", "test-session-token");
        Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", container.GetConnectionString());
        try
        {
            run(new AwsSecretsManagerL1Harness(container));
        }
        finally
        {
            Environment.SetEnvironmentVariable("AWS_REGION", null);
            Environment.SetEnvironmentVariable("AWS_ACCESS_KEY_ID", null);
            Environment.SetEnvironmentVariable("AWS_SECRET_ACCESS_KEY", null);
            Environment.SetEnvironmentVariable("AWS_SESSION_TOKEN", null);
            Environment.SetEnvironmentVariable("AWS_ENDPOINT_URL_SECRETS_MANAGER", null);
        }
    }
}
