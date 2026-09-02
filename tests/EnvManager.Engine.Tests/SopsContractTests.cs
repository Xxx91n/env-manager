// SopsContractTests.cs - SopsProvider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="SopsProvider"/>. The backend-independent contract
/// assertions (fail-closed decrypt, malformed-format error) run here; the
/// backend-dependent assertions are Skipped with the reason below because the provider
/// has no self-contained offline backend for the L0 contract round.
/// </summary>
public sealed class SopsContractTests : SecretProviderContractTests
{
    public SopsContractTests()
        : base(new SkippedProviderHarness(() => new SopsProvider()))
    {
    }

    [Fact(Skip = "sops requires the sops binary plus age/PGP/KMS key material; not provisioned offline - target L1.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "sops requires the sops binary plus age/PGP/KMS key material; not provisioned offline - target L1.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();
}
