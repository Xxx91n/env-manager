// AwsSecretsManagerContractTests.cs - AwsSecretsManagerProvider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="AwsSecretsManagerProvider"/>. The backend-independent contract
/// assertions (fail-closed decrypt, malformed-format error) run here; the
/// backend-dependent assertions are Skipped with the reason below because the provider
/// has no self-contained offline backend for the L0 contract round.
/// </summary>
public sealed class AwsSecretsManagerContractTests : SecretProviderContractTests
{
    public AwsSecretsManagerContractTests()
        : base(new SkippedProviderHarness(() => new AwsSecretsManagerProvider()))
    {
    }

    [Fact(Skip = "aws-secretsmanager requires AWS credentials and region and makes live SigV4 REST calls; cloud backend - target L2.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "aws-secretsmanager requires AWS credentials and region and makes live SigV4 REST calls; cloud backend - target L2.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();
}
