// OnePasswordContractTests.cs - OnePasswordProvider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="OnePasswordProvider"/>. The backend-independent contract
/// assertions (fail-closed decrypt, malformed-format error) run here; the
/// backend-dependent assertions are Skipped with the reason below because the provider
/// has no self-contained offline backend for the L0 contract round.
/// </summary>
public sealed class OnePasswordContractTests : SecretProviderContractTests
{
    public OnePasswordContractTests()
        : base(new SkippedProviderHarness(() => new OnePasswordProvider()))
    {
    }

    [Fact(Skip = "1password requires the op CLI plus an authenticated 1Password account/CLI token; external backend - target L1/L2.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "1password requires the op CLI plus an authenticated 1Password account/CLI token; external backend - target L1/L2.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();
}
