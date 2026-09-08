// ISecretProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
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

    // --- Capability descriptors (ticket 41, spec Phase 6: capability-descriptor matrix) ---
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
}
