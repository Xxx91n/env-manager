// ISecretProviderHarness.cs - secret provider contract test harness seam (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Neutral backend fixture seam for the <see cref="ISecretProvider"/> contract suite.
/// A harness wraps one concrete provider and lets the contract assertions seed and read
/// its backing store WITHOUT going through the provider's own Encrypt/Decrypt, so a
/// symmetric read/write bug inside a provider cannot hide itself (industry pattern:
/// gocloud.dev secrets/drivertest, WopiHost LockProviderConformanceTests).
/// </summary>
internal interface ISecretProviderHarness
{
    /// <summary>The concrete provider under test.</summary>
    ISecretProvider CreateProvider();

    /// <summary>
    /// Seed <paramref name="plaintext"/> into the backend without the provider's Encrypt,
    /// returning an envelope string the provider can later Decrypt.
    /// </summary>
    string SeedSecret(string plaintext, string context);

    /// <summary>
    /// Read the stored secret back without the provider's Decrypt, given the envelope
    /// string the provider produced via Encrypt.
    /// </summary>
    string ReadRawSecret(string sutEnvelope, string context);
}

/// <summary>
/// Harness for providers whose real backend is unavailable in this environment. It
/// still creates the concrete provider so the backend-independent contract assertions
/// (fail-closed decrypt, malformed-format error) run against it; the backend-dependent
/// assertions are marked Skipped on the subclass instead.
/// </summary>
internal sealed class SkippedProviderHarness : ISecretProviderHarness
{
    private readonly Func<ISecretProvider> _factory;

    public SkippedProviderHarness(Func<ISecretProvider> factory) => _factory = factory;

    public ISecretProvider CreateProvider() => _factory();

    public string SeedSecret(string plaintext, string context) =>
        throw new NotSupportedException("Backend unavailable: this provider has no self-contained offline backend for the L0 contract round.");

    public string ReadRawSecret(string sutEnvelope, string context) =>
        throw new NotSupportedException("Backend unavailable: this provider has no self-contained offline backend for the L0 contract round.");
}
