// AzureKeyVaultContractTests.cs - AzureKeyVaultProvider contract mount (ticket 10; L1 via issue 15)
// License: Apache-2.0

using EnvManager;

using Testcontainers.LowkeyVault;
using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="AzureKeyVaultProvider"/>. Backend-independent
/// assertions always run. The backend-dependent assertions target Lowkey Vault
/// (official Testcontainers.LowkeyVault module): the KV API runs on its https port
/// and the container's token port fakes the AAD endpoint, so the provider is driven
/// through its managed-identity path via the IDENTITY_ENDPOINT production seam
/// (App Service convention; the env var is only set inside the L1 test scope).
/// The container's self-signed default certificate is trusted for the test run.
/// </summary>
public sealed class AzureKeyVaultContractTests : SecretProviderContractTests
{
    public AzureKeyVaultContractTests()
        : base(new SkippedProviderHarness(() => new AzureKeyVaultProvider()))
    {
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext()
    {
        var container = L1ContainerFixtures.StartLowkeyVault();
        using var _scope = LowkeyVaultScope.Install(container);
        AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(new AzureKeyVaultL1Harness(container));
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext()
    {
        var container = L1ContainerFixtures.StartLowkeyVault();
        using var _scope = LowkeyVaultScope.Install(container);
        AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(new AzureKeyVaultL1Harness(container));
    }

    /// <summary>
    /// Installs the container's default certificate into the CurrentUser Root store
    /// for the duration of the test process (per-test install is idempotent) and
    /// points the provider at the emulator through the production seams.
    /// </summary>
    private sealed class LowkeyVaultScope : IDisposable
    {
        private readonly LowkeyVaultContainer _container;
        private System.Security.Cryptography.X509Certificates.X509Certificate2Collection? _installed;

        private LowkeyVaultScope(LowkeyVaultContainer container) => _container = container;

        public static LowkeyVaultScope Install(LowkeyVaultContainer container)
        {
            var scope = new LowkeyVaultScope(container);
            scope.InstallCertificate();
            return scope;
        }

        private void InstallCertificate()
        {
            try
            {
                _installed = _container.GetCertificateAsync().GetAwaiter().GetResult();
                using var root = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.Root,
                    System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
                root.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                foreach (var cert in _installed)
                {
                    if (!root.Certificates.Contains(cert))
                    {
                        root.Add(cert);
                    }
                }
                root.Close();
            }
            catch (Exception)
            {
                // trust install failure falls through to the TLS-error path in the
                // provider; the test then fails with a meaningful message
            }
        }

        public void Dispose()
        {
            if (_installed is null) return;
            try
            {
                using var root = new System.Security.Cryptography.X509Certificates.X509Store(
                    System.Security.Cryptography.X509Certificates.StoreName.Root,
                    System.Security.Cryptography.X509Certificates.StoreLocation.CurrentUser);
                root.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadWrite);
                foreach (var cert in _installed)
                {
                    root.Remove(cert);
                }
                root.Close();
                _installed = null;
            }
            catch (Exception) { }
        }
    }
}
