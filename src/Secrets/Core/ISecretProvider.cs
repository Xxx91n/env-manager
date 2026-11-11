// ISecretProvider.cs - secret provider architecture
// One-symbol-per-file split of the retired single-file secret provider module; behavior unchanged.
// License: Apache-2.0

namespace EnvManager.Secrets.Core;

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

    // --- Capability descriptors ---
// Declared per provider from the 2026-09 backend facts (macro review §4.4); consumed by
// SecretProviderManager.ListProviders and `profile secret-provider list` so capability is
// data the GUI gates on instead of hardcoded provider-name rules.

// Whether secrets mounted from this provider can be re-read periodically by the
// service (refresh policy "Periodic"); false => "CreatedOnly" mounts only.
bool RefreshCapable => false;

// Whether the provider's production auth model is certificate-bound
// (Vault AppRole/TLS, Azure SP certificate) rather than static tokens.
bool CertAuthRequired => false;

// Whether Encrypt/Decrypt reach a remote endpoint (network egress expected).
bool RequiresNetwork => false;

// Cheap deterministic self-check (no process spawn, no network I/O): required
// env config / provider binary present. SecretProviderManager end-to-end probes
// RequiresNetwork providers only when this gate passes.
bool Available => true;

// Whether an availability probe may write to the provider's backend. The default keeps
// the sentinel Encrypt/Decrypt/Delete round-trip that network providers have always run;
// a provider whose backend is a customer cloud account declares false and is probed
// read-only through ProbeReadOnly, so opening the profile editor cannot create secrets
// in an account nobody asked to modify. Not part of the `[refresh/certauth/network]`
// line that `profile secret-provider list` prints; it only selects the probe below.
bool ProbeWritesData => true;

// The read-only probe itself: runs only when ProbeWritesData is false, and must fail
// when the provider's identity or backend is unusable. Default no-op suits local
// providers, which never probe at all.
void ProbeReadOnly() { }
}
