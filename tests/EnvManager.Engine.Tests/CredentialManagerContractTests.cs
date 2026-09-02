// CredentialManagerContractTests.cs - CredentialManagerProvider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="CredentialManagerProvider"/>. The backend-independent contract
/// assertions (fail-closed decrypt, malformed-format error) run here; the
/// backend-dependent assertions are Skipped with the reason below because the provider
/// has no self-contained offline backend for the L0 contract round.
/// </summary>
public sealed class CredentialManagerContractTests : SecretProviderContractTests
{
    public CredentialManagerContractTests()
        : base(new SkippedProviderHarness(() => new CredentialManagerProvider()))
    {
    }

    [Fact(Skip = "credential-manager writes to the Windows Credential Manager (CredWrite/CredRead); not an offline L0 backend this round - target L1 local smoke.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "credential-manager writes to the Windows Credential Manager (CredWrite/CredRead); not an offline L0 backend this round - target L1 local smoke.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();
}
