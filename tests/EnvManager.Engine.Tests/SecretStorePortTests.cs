using Xunit;
using EnvManager.Secrets.Core;
using EnvManager.Secrets.Manager;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Ticket 39 port pin: the two-layer secret port (ISecretStore domain facade above
/// the ISecretProvider transport adapters, spec Phase 6 story 19). Pins the five
/// domain-verb surface of the interface and the fail-closed reveal route through
/// the port (same routing core as the transport-level Decrypt). Hermetic: no real
/// registry, no machine state; the reveal probes never decrypt real secrets.
/// </summary>
[Collection("CliSnapshotSerial")]
public class SecretStorePortTests
{
    [Fact]
    public void ISecretStore_ExposesExactlyTheFiveDomainVerbs()
    {
        var names = typeof(ISecretStore).GetMethods()
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(new[] { "Export", "Import", "Mount", "Reveal", "Rotate" }, names);
    }

    [Fact]
    public void SecretProviderManager_Implements_ISecretStore()
    {
        Assert.True(typeof(SecretProviderManager).IsAssignableTo(typeof(ISecretStore)));
    }

    [Fact]
    public void Store_Reveal_UnknownProvider_FailsClosed()
    {
        var envelope = new SecretEnvelope
        {
            Provider = "em-t39-unknown-provider",
            Version = 1,
            Ciphertext = "aa",
        }.Serialize();

        ISecretStore store = SecretProviderManager.Instance;
        Assert.Throws<InvalidOperationException>(() => store.Reveal(envelope, "ctx"));
    }

    [Fact]
    public void Store_Reveal_NonEnvelopeGarbage_FailsClosed()
    {
        ISecretStore store = SecretProviderManager.Instance;
        Assert.Throws<InvalidOperationException>(() => store.Reveal("not-a-valid-envelope-or-base64!!", "ctx"));
    }
}
