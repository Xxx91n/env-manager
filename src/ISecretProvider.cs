// ISecretProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

namespace EnvManager;

// --- Phase 1: ISecretProvider Interface ---

internal interface ISecretProvider
{
    string Name { get; }

    // Encrypt plaintext into an envelope string (JSON)
    string Encrypt(string plaintext, string? context = null);

    // Decrypt an envelope string back to plaintext
    string Decrypt(string envelope, string? context = null);

    // Whether this provider supports key rotation
    bool CanRotate => false;

    // Rotate: re-encrypt with a new key (optional, default no-op)
    string Rotate(string oldEnvelope, string? context = null)
    {
        return oldEnvelope;
    }

    // Delete any provider-side state (e.g. CredMan entry) for a given envelope
    void Delete(string envelope, string? context = null) { }
}
