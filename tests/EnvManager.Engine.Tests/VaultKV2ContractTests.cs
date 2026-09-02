// VaultKV2ContractTests.cs - VaultKV2Provider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="VaultKV2Provider"/>. The backend-independent contract
/// assertions (fail-closed decrypt, malformed-format error) run here; the
/// backend-dependent assertions are Skipped with the reason below because the provider
/// has no self-contained offline backend for the L0 contract round.
/// </summary>
public sealed class VaultKV2ContractTests : SecretProviderContractTests
{
    public VaultKV2ContractTests()
        : base(new SkippedProviderHarness(() => new VaultKV2Provider()))
    {
    }

    [Fact(Skip = "vault-kv2 requires a HashiCorp Vault server reachable via VAULT_ADDR/VAULT_TOKEN; no dev server in CI - target L1 Vault dev server.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "vault-kv2 requires a HashiCorp Vault server reachable via VAULT_ADDR/VAULT_TOKEN; no dev server in CI - target L1 Vault dev server.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();
}
