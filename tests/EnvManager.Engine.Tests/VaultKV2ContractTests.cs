// VaultKV2ContractTests.cs - VaultKV2Provider contract mount (ticket 10; L1 via issue 15)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="VaultKV2Provider"/>. Backend-independent assertions
/// (fail-closed decrypt, malformed-format error) always run. The backend-dependent
/// assertions target a real HashiCorp Vault dev server (generic Testcontainers
/// container; no official .NET module exists) and skip when no Docker runtime is
/// reachable or EM_L1_MATRIX is not opted in.
/// </summary>
public sealed class VaultKV2ContractTests : SecretProviderContractTests
{
    public VaultKV2ContractTests()
        : base(new SkippedProviderHarness(() => new VaultKV2Provider()))
    {
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext()
    {
        var vault = L1ContainerFixtures.StartVaultDevServer();
        Environment.SetEnvironmentVariable("VAULT_ADDR", vault.Addr);
        Environment.SetEnvironmentVariable("VAULT_TOKEN", vault.RootToken);
        try
        {
            AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(new VaultKv2L1Harness(vault.Addr, vault.RootToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VAULT_ADDR", null);
            Environment.SetEnvironmentVariable("VAULT_TOKEN", null);
        }
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext()
    {
        var vault = L1ContainerFixtures.StartVaultDevServer();
        Environment.SetEnvironmentVariable("VAULT_ADDR", vault.Addr);
        Environment.SetEnvironmentVariable("VAULT_TOKEN", vault.RootToken);
        try
        {
            AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(new VaultKv2L1Harness(vault.Addr, vault.RootToken));
        }
        finally
        {
            Environment.SetEnvironmentVariable("VAULT_ADDR", null);
            Environment.SetEnvironmentVariable("VAULT_TOKEN", null);
        }
    }
}
