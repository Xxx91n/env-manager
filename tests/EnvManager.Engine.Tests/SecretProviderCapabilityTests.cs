using Xunit;
using EnvManager.Secrets.Core;
using EnvManager.Secrets.Manager;
using EnvManager.Secrets.Providers;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Capability-descriptor pins (spec Phase 6: capability metadata +
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
        /// <summary>When set, the probe throws this instead of a generic exception, so the
        /// reason-code tests can pin a specific failure family.</summary>
        public Exception? ProbeThrown { get; set; }
        public bool ProbeWritesData { get; set; } = true;
        public int ReadOnlyProbes { get; private set; }
        public int Writes { get; private set; }

        public string Encrypt(string plaintext, string? context = null)
        {
            if (ProbeThrows) throw ProbeThrown ?? new InvalidOperationException("probe failed");
            Writes++;
            return "fake-envelope";
        }

        public string Decrypt(string envelope, string? context = null)
        {
            if (ProbeThrows) throw ProbeThrown ?? new InvalidOperationException("probe failed");
            return "fake-plaintext";
        }

        public void ProbeReadOnly()
        {
            if (ProbeThrows) throw ProbeThrown ?? new InvalidOperationException("probe failed");
            ReadOnlyProbes++;
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

    [Fact]
    public void AwsRolesAnywhere_DeclaresCertAuthNetworkCapabilitiesAndAReadOnlyProbe()
    {
        var p = new AwsRolesAnywhereProvider();
        Assert.Equal("aws-roles-anywhere", p.Name);
        Assert.True(p.RefreshCapable);
        Assert.True(p.CertAuthRequired);
        Assert.True(p.RequiresNetwork);
        // The certificate exchange must never create secrets in a customer account.
        Assert.False(p.ProbeWritesData);
    }

    // --- the four cloud/CLI providers: the probe must not write (#277) ---

    // Opening the profile editor calls `serve list`, which probes every network
    // provider. All four of these keep their data in a customer account, so the
    // probe has to be read-only: the old sentinel Encrypt/Decrypt/Delete round-trip
    // created and then deleted a secret on each mount. One row per provider, naming
    // what the read-only probe is expected to prove.
    [Theory]
    [InlineData("vault-kv2")]
    [InlineData("azure-keyvault")]
    [InlineData("aws-secretsmanager")]
    [InlineData("1password")]
    public void CloudProvider_DeclaresAReadOnlyProbe(string name)
    {
        ISecretProvider provider = name switch
        {
            "vault-kv2" => new VaultKV2Provider(),
            "azure-keyvault" => new AzureKeyVaultProvider(),
            "aws-secretsmanager" => new AwsSecretsManagerProvider(),
            "1password" => new OnePasswordProvider(),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "unknown provider")
        };
        Assert.Equal(name, provider.Name);
        Assert.True(provider.RequiresNetwork);
        Assert.False(provider.ProbeWritesData);
    }

    [Fact]
    public void ReadOnlyProbe_IsUsedForProvidersThatDeclareNoWrites()
    {
        var probeOnly = new FakeProvider { RequiresNetwork = true, Available = true, ProbeWritesData = false };
        Assert.True(SecretProviderManager.IsProviderAvailable(probeOnly));
        // Encrypt/Decrypt were never called; the read-only probe ran instead.
        Assert.Equal(1, probeOnly.ReadOnlyProbes);

        var writeProbe = new FakeProvider { RequiresNetwork = true, Available = true, ProbeWritesData = true };
        Assert.True(SecretProviderManager.IsProviderAvailable(writeProbe));
        Assert.Equal(0, writeProbe.ReadOnlyProbes);
        Assert.Equal(1, writeProbe.Writes);
    }

    // --- the reason reaches the caller instead of being swallowed ---

    [Fact]
    public void FailedProbe_ReportsAReasonCode_InsteadOfSwallowingIt()
    {
        // auth / permission failures carry their own code, so the CLI and the GUI can
        // say what went wrong instead of printing a bare "(unavailable)".
        var auth = new FakeProvider
        {
            RequiresNetwork = true,
            ProbeWritesData = false,
            ProbeThrows = true,
            ProbeThrown = new SecretProviderAuthFailedException("fake", "probe", "credential rejected")
        };
        var (available, reason) = SecretProviderManager.ProbeAvailability(auth);
        Assert.False(available);
        Assert.Equal(SecretProviderErrors.ReasonAuth, reason);

        var permission = new FakeProvider
        {
            RequiresNetwork = true,
            ProbeWritesData = false,
            ProbeThrows = true,
            ProbeThrown = new SecretProviderPermissionDeniedException("fake", "probe", "permission denied")
        };
        var (available2, reason2) = SecretProviderManager.ProbeAvailability(permission);
        Assert.False(available2);
        Assert.Equal(SecretProviderErrors.ReasonPermission, reason2);
    }

    [Fact]
    public void GateFailure_CarriesNoReason_BecauseNothingWasProbed()
    {
        // "unavailable" without a reason is a configuration gap (no VAULT_ADDR, no
        // op binary); inventing a reason there would send the user chasing a credential
        // that was never sent.
        var neverConfigured = new FakeProvider { RequiresNetwork = true, Available = false, ProbeWritesData = false };
        var (available, reason) = SecretProviderManager.ProbeAvailability(neverConfigured);
        Assert.False(available);
        Assert.Null(reason);
        Assert.Equal(0, neverConfigured.ReadOnlyProbes);
    }

    [Fact]
    public void SuccessfulProbe_CarriesNoReason()
    {
        var ok = new FakeProvider { RequiresNetwork = true, Available = true, ProbeWritesData = false };
        var (available, reason) = SecretProviderManager.ProbeAvailability(ok);
        Assert.True(available);
        Assert.Null(reason);
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
            // Gate passes; the probe then fails closed against a loopback port that
            // refuses connections instantly (no real network). The failed read is
            // mapped to the network reason, so `list` can say why.
            var (available, reason) = SecretProviderManager.ProbeAvailability(new VaultKV2Provider());
            Assert.False(available);
            Assert.Equal(SecretProviderErrors.ReasonNetwork, reason);
        }
        finally
        {
            foreach (var kv in saved) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }

    // The activate error is parsed by the GUI:
    //   ProfilePage.svelte: /^Cannot activate provider '([^']+)':\s*(.+?)\s*\.\s*Fix the
    //   provider environment first/i
    // Rewording either half silently turns the localized message back into raw CLI text,
    // so both halves are pinned here. A probe failure is the case worth pinning: the old
    // message claimed the environment was unconfigured, and that is no longer the likely
    // cause once the probe is read-only.
    [Fact]
    public void ActivateError_KeepsTheTemplateProfilePageParses()
    {
        var vars = new[] { "VAULT_ADDR", "VAULT_TOKEN" };
        var saved = vars.ToDictionary(v => v, v => Environment.GetEnvironmentVariable(v));
        try
        {
            foreach (var v in vars) Environment.SetEnvironmentVariable(v, null);
            Environment.SetEnvironmentVariable("VAULT_ADDR", "https://127.0.0.1:1");
            Environment.SetEnvironmentVariable("VAULT_TOKEN", "test-token");

            var ex = Assert.Throws<InvalidOperationException>(() => SecretProviderManager.SetActiveProvider("vault-kv2"));

            // The GUI regex needs this exact skeleton: name in single quotes, the upstream
            // text, then the trailing hint sentence.
            Assert.Matches(
                new System.Text.RegularExpressions.Regex("^Cannot activate provider 'vault-kv2':\\s*.+?\\.\\s*Fix the provider environment first"),
                ex.Message);
            // The reason-specific hint must still start with the phrase the regex anchors on.
            Assert.Equal(SecretProviderErrors.ReasonNetwork,
                SecretProviderErrors.ReasonCode(new SecretProviderUnavailableException("x", "probe", "probe failed")));
        }
        finally
        {
            foreach (var kv in saved) Environment.SetEnvironmentVariable(kv.Key, kv.Value);
        }
    }
}
