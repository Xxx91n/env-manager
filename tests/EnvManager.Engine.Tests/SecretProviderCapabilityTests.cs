using Xunit;
using EnvManager.Secrets.Core;
using EnvManager.Secrets.Manager;
using EnvManager.Secrets.Providers;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 41 capability-descriptor pins (spec Phase 6: capability metadata +
/// real Available). Declared capability values are static per-provider facts,
/// so they are pinned directly; availability composition is pinned through
/// fake providers (no machine environment dependence, no network, no cloud).
/// Hermetic: fakes never touch a real backend.
/// </summary>
[Collection("CliSnapshotSerial")]
public class SecretProviderCapabilityTests
{
    // Fake used to prove the composition rule without real backends.
    private sealed class FakeProvider : ISecretProvider
    {
        public string Name => "fake";
        public bool RefreshCapable { get; set; }
        public bool CertAuthRequired { get; set; }
        public bool RequiresNetwork { get; set; }
        public bool Available { get; set; } = true;
        public bool ProbeThrows { get; set; }

        public string Encrypt(string plaintext, string? context = null)
        {
            if (ProbeThrows) throw new InvalidOperationException("probe failed");
            return "fake-envelope";
        }

        public string Decrypt(string envelope, string? context = null)
        {
            if (ProbeThrows) throw new InvalidOperationException("probe failed");
            return "fake-plaintext";
        }
    }

    // --- declared capability matrix (macro review §4.4, 2026-09 backend facts) ---

    [Fact]
    public void DpapiCurrentUser_DeclaresLocalCreatedOnlyCapabilities()
    {
        var p = new DpapiCurrentUserProvider();
        Assert.Equal("dpapi-current-user", p.Name);
        Assert.False(p.RefreshCapable);
        Assert.False(p.CertAuthRequired);
        Assert.False(p.RequiresNetwork);
        Assert.True(p.Available);
    }

    [Fact]
    public void CredentialManager_DeclaresLocalCreatedOnlyCapabilities()
    {
        var p = new CredentialManagerProvider();
        Assert.Equal("credential-manager", p.Name);
        Assert.False(p.RefreshCapable);
        Assert.False(p.CertAuthRequired);
        Assert.False(p.RequiresNetwork);
        Assert.True(p.Available);
    }

    [Fact]
    public void PowerShellSecretManagement_DeclaresLocalCreatedOnlyCapabilities()
    {
        var p = new PowerShellSecretManagementProvider();
        Assert.Equal("powershell-secretmanagement", p.Name);
        Assert.False(p.RefreshCapable);
        Assert.False(p.CertAuthRequired);
        Assert.False(p.RequiresNetwork);
        Assert.True(p.Available);
    }

    [Fact]
    public void VaultKV2_DeclaresRefreshCapableCertAuthNetworkCapabilities()
    {
        var p = new VaultKV2Provider();
        Assert.Equal("vault-kv2", p.Name);
        Assert.True(p.RefreshCapable);
        Assert.True(p.CertAuthRequired);
        Assert.True(p.RequiresNetwork);
    }

    [Fact]
    public void Sops_DeclaresCreatedOnlyNoCertAuthCapabilities()
    {
        var p = new SopsProvider();
        Assert.Equal("sops", p.Name);
        Assert.False(p.RefreshCapable);
        Assert.False(p.CertAuthRequired);
        Assert.False(p.RequiresNetwork);
    }

    [Fact]
    public void AzureKeyVault_DeclaresRefreshCapableCertAuthNetworkCapabilities()
    {
        var p = new AzureKeyVaultProvider();
        Assert.Equal("azure-keyvault", p.Name);
        Assert.True(p.RefreshCapable);
        Assert.True(p.CertAuthRequired);
        Assert.True(p.RequiresNetwork);
    }

    [Fact]
    public void OnePassword_DeclaresRefreshCapableNoCertAuthNetworkCapabilities()
    {
        var p = new OnePasswordProvider();
        Assert.Equal("1password", p.Name);
        Assert.True(p.RefreshCapable);
        Assert.False(p.CertAuthRequired);
        Assert.True(p.RequiresNetwork);
    }

    [Fact]
    public void AwsSecretsManager_DeclaresRefreshCapableNoCertAuthNetworkCapabilities()
    {
        var p = new AwsSecretsManagerProvider();
        Assert.Equal("aws-secretsmanager", p.Name);
        Assert.True(p.RefreshCapable);
        Assert.False(p.CertAuthRequired);
        Assert.True(p.RequiresNetwork);
    }

    // --- GetCapabilities surface ---

    [Fact]
    public void GetCapabilities_ReflectsDeclaredValuesForKnownProviders()
    {
        var (refresh, cert, network) = SecretProviderManager.GetCapabilities("vault-kv2");
        Assert.True(refresh);
        Assert.True(cert);
        Assert.True(network);

        var (refresh2, cert2, network2) = SecretProviderManager.GetCapabilities("DPAPI-CURRENT-USER");
        Assert.False(refresh2);
        Assert.False(cert2);
        Assert.False(network2);
    }

    [Fact]
    public void GetCapabilities_ThrowsFailClosedOnUnknownProvider()
    {
        Assert.Throws<InvalidOperationException>(() => SecretProviderManager.GetCapabilities("__nonexistent_provider__"));
    }

    // --- availability composition (fake-driven, deterministic) ---

    [Fact]
    public void LocalProvider_AvailableGateDecides_Alone()
    {
        var off = new FakeProvider { RequiresNetwork = false, Available = false };
        Assert.False(SecretProviderManager.IsProviderAvailable(off));

        // Local providers never probe: Encrypt throws would fail the probe, yet
        // availability stays true because the gate is the declared one only.
        var local = new FakeProvider { RequiresNetwork = false, Available = true, ProbeThrows = true };
        Assert.True(SecretProviderManager.IsProviderAvailable(local));
    }

    [Fact]
    public void NetworkProvider_SentinelProbeDecides()
    {
        // Gate passes, probe round-trip succeeds -> available.
        var ok = new FakeProvider { RequiresNetwork = true, Available = true, ProbeThrows = false };
        Assert.True(SecretProviderManager.IsProviderAvailable(ok));

        // Gate passes, probe throws -> unavailable (fail closed).
        var bad = new FakeProvider { RequiresNetwork = true, Available = true, ProbeThrows = true };
        Assert.False(SecretProviderManager.IsProviderAvailable(bad));

        // Gate fails -> never probes.
        var gated = new FakeProvider { RequiresNetwork = true, Available = false, ProbeThrows = true };
        Assert.False(SecretProviderManager.IsProviderAvailable(gated));
    }

    // --- real network providers: cheap gates in a scrubbed environment ---
    // Environment variables are process-scoped and restored in finally (same
    // discipline as AwsSecretsManagerContractTests); no network I/O happens.

    [Fact]
    public void NetworkProvider_Gates_ReflectScrubbedEnvironment()
    {
        var vars = new[] { "VAULT_ADDR", "VAULT_TOKEN", "AZURE_KEYVAULT_URI", "AWS_REGION", "AWS_DEFAULT_REGION", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY" };
        var saved = vars.ToDictionary(v => v, v => Environment.GetEnvironmentVariable(v));
        try
        {
            foreach (var v in vars) Environment.SetEnvironmentVariable(v, null);

            Assert.False(new VaultKV2Provider().Available);
            Assert.False(new AzureKeyVaultProvider().Available);
            Assert.False(new AwsSecretsManagerProvider().Available);
            Assert.False(SecretProviderManager.IsProviderAvailable(new VaultKV2Provider()));

            Environment.SetEnvironmentVariable("VAULT_ADDR", "https://127.0.0.1:1"); // loopback, connect refused instantly - hermetic
            Environment.SetEnvironmentVariable("VAULT_TOKEN", "test-token");
            // Gate passes; the sentinel probe then fails closed against a
            // loopback port that refuses connections instantly (no real network).
            Assert.False(SecretProviderManager.IsProviderAvailable(new VaultKV2Provider()));
        }
        finally
        {
            foreach (var kv in saved) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }
}
