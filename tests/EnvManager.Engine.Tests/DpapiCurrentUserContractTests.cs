// DpapiCurrentUserContractTests.cs - DPAPI provider contract mount (ticket 10, architecture-recovery)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// L0 contract mount for <see cref="DpapiCurrentUserProvider"/>: runs the full shared
/// contract assertion set against the real local DPAPI backend (crypt32 CurrentUser scope)
/// - no network, no cloud credentials. DPAPI is the only provider with a self-contained
/// local backend, so it is the only mount whose backend-dependent assertions run.
/// </summary>
public sealed class DpapiCurrentUserContractTests : SecretProviderContractTests
{
    public DpapiCurrentUserContractTests()
        : base(new DpapiCurrentUserHarness())
    {
    }

    [Fact]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() => AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() => AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();

    private sealed class DpapiCurrentUserHarness : ISecretProviderHarness
    {
        public ISecretProvider CreateProvider() => new DpapiCurrentUserProvider();

        public string SeedSecret(string plaintext, string context)
        {
            // Neutral backend writer: DPAPI-encrypt via the lower-level DpapiHelper primitive
            // (already trusted and exercised elsewhere), NOT through the provider's Encrypt.
            string raw = DpapiHelper.EncryptSecret(plaintext);
            return new SecretEnvelope
            {
                Provider = "dpapi-current-user",
                Version = 1,
                Ciphertext = raw
            }.Serialize();
        }

        public string ReadRawSecret(string sutEnvelope, string context)
        {
            // Neutral backend reader: unwrap the envelope the provider produced and
            // DPAPI-decrypt its ciphertext directly, NOT through the provider's Decrypt.
            var parsed = SecretEnvelope.TryParse(sutEnvelope)
                ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
            return DpapiHelper.DecryptSecret(parsed.Ciphertext ?? "");
        }
    }
}
