// AzureKeyVaultContractTests.cs - AzureKeyVaultProvider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="AzureKeyVaultProvider"/>. The backend-independent contract
/// assertions (fail-closed decrypt, malformed-format error) run here; the
/// backend-dependent assertions are Skipped with the reason below because the provider
/// has no self-contained offline backend for the L0 contract round.
/// </summary>
public sealed class AzureKeyVaultContractTests : SecretProviderContractTests
{
    public AzureKeyVaultContractTests()
        : base(new SkippedProviderHarness(() => new AzureKeyVaultProvider()))
    {
    }

    [Fact(Skip = "azure-keyvault requires AZURE_KEYVAULT_URI plus managed identity or service-principal credentials; cloud backend - target L2.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "azure-keyvault requires AZURE_KEYVAULT_URI plus managed identity or service-principal credentials; cloud backend - target L2.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();
}
