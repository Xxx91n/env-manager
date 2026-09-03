// PowerShellSecretManagementContractTests.cs - PowerShellSecretManagementProvider contract mount (ticket 10; L1 via issue 15)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="PowerShellSecretManagementProvider"/>.
/// Backend-independent assertions always run. The backend-dependent assertions run
/// against a real pwsh SecretManagement vault (Microsoft.PowerShell.SecretStore in
/// the official non-interactive automation mode, Authentication=None - no password,
/// no cloud credential). Hosts without pwsh or the modules skip with the reason.
/// </summary>
public sealed class PowerShellSecretManagementContractTests : SecretProviderContractTests
{
    public PowerShellSecretManagementContractTests()
        : base(new SkippedProviderHarness(() => new PowerShellSecretManagementProvider()))
    {
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext()
    {
        using var _vault = SecretStoreVaultScope.Install();
        AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(new PowerShellSecretManagementL1Harness());
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext()
    {
        using var _vault = SecretStoreVaultScope.Install();
        AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(new PowerShellSecretManagementL1Harness());
    }

    /// <summary>
    /// Probes pwsh + modules and registers the EnvManager SecretStore vault in the
    /// official non-interactive automation mode. Skips (SkipException) when the host
    /// lacks the prerequisites instead of failing.
    /// </summary>
    private sealed class SecretStoreVaultScope : IDisposable
    {
        public static SecretStoreVaultScope Install()
        {
            if (!L1ToolProvisioner.IsSecretStoreAvailable())
            {
                throw new Xunit.SkipException("powershell-secretmanagement L1 smoke needs pwsh with Microsoft.PowerShell.SecretManagement/SecretStore installed (Install-Module ...)");
            }
            if (!L1ToolProvisioner.TryRegisterSecretStoreVault())
            {
                throw new Xunit.SkipException("powershell-secretmanagement L1 smoke could not register the EnvManager SecretStore vault non-interactively (pre-existing password-protected store?)");
            }
            return new SecretStoreVaultScope();
        }

        // the vault registration persists per user (documented automation mode); there
        // is nothing to tear down that would not break subsequent runs
        public void Dispose() { }
    }
}
