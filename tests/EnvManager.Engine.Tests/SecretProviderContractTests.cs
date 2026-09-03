// SecretProviderContractTests.cs - shared ISecretProvider contract test base (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Abstract contract suite over <see cref="ISecretProvider"/>: one shared set of behavior
/// assertions, each expressed only through the <see cref="ISecretProviderHarness"/> seam.
/// Every concrete provider mounts one sealed subclass; the compliance gate
/// (<see cref="SecretProviderContractComplianceTests"/>) fails the build if an
/// implementation is added without a matching mount.
///
/// The four core assertions are fail-closed decryption, round-trip, stable error on
/// malformed format, and plaintext-never-in-the-envelope. The first two are backend
/// independent and inherited/run by every subclass; the last two need a real backend and
/// are wired per subclass (DPAPI runs them on its real local backend; issue 15 L1 mounts
/// run them against real backends - emulator containers, localhost Connect mock, or local
/// CLI tooling - with affinity-based skips when the host lacks the backend).
/// </summary>
public abstract class SecretProviderContractTests
{
    private readonly ISecretProviderHarness _harness;

    internal SecretProviderContractTests(ISecretProviderHarness harness)
    {
        _harness = harness;
    }

    /// <summary>The concrete ISecretProvider implementation this subclass mounts.</summary>
    internal Type ProviderType => _harness.CreateProvider().GetType();

    // ---- core assertion 1: fail-closed decryption (backend independent) ----

    [Fact]
    public void FailClosed_DecryptRejectsForeignProviderEnvelope()
    {
        var provider = _harness.CreateProvider();
        var foreign = new SecretEnvelope { Provider = "em-contract-foreign-provider", Ciphertext = "aa" }.Serialize();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.Decrypt(foreign, "em-contract-context"));

        Assert.Contains("Provider mismatch", ex.Message);
    }

    // ---- core assertion 2: stable error on malformed format (backend independent) ----

    [Fact]
    public void MalformedFormat_DecryptRejectsNonEnvelopeGarbage()
    {
        var provider = _harness.CreateProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.Decrypt("not-a-valid-envelope!!", "em-contract-context"));

        Assert.Contains("Invalid secret envelope", ex.Message);
    }

    // ---- core assertion 3: round-trip (backend dependent; wired per subclass) ----

    protected void AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext() =>
        AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(_harness);

    // Issue 15: L1 mounts run the same assertions against a real backend harness while
    // the base class keeps the ticket-10 backend-independent Fact set unchanged.
    internal void AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(ISecretProviderHarness l1Harness)
    {
        const string plain = "contract-round-trip-42";
        const string context = "em-contract-roundtrip";

        var provider = l1Harness.CreateProvider();

        // Symmetric: provider Encrypt -> provider Decrypt.
        string envelope = provider.Encrypt(plain, context);
        Assert.Equal(plain, provider.Decrypt(envelope, context));

        // Neutral seed (bypasses provider Encrypt) -> provider Decrypt.
        string seeded = l1Harness.SeedSecret(plain, context);
        Assert.Equal(plain, provider.Decrypt(seeded, context));

        // Neutral read (bypasses provider Decrypt) of the provider's own Encrypt output.
        Assert.Equal(plain, l1Harness.ReadRawSecret(envelope, context));
    }

    // ---- core assertion 4: plaintext never in the envelope (backend dependent) ----

    protected void AssertPlaintextNotEmbedded_EncryptOmitsPlaintext() =>
        AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(_harness);

    // Issue 15: L1 overload - same canary against a real backend harness.
    internal void AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(ISecretProviderHarness l1Harness)
    {
        const string plain = "em-contract-canary-42";

        var provider = l1Harness.CreateProvider();

        string envelope = provider.Encrypt(plain, "em-contract-context");

        Assert.DoesNotContain(plain, envelope);
    }
}
