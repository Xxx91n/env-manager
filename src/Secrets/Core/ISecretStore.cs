// ISecretStore.cs - two-layer secret port (ticket 39, architecture-recovery)
// Domain facade above the ISecretProvider transport adapters (spec Phase 6 story 19):
// CLI call sites depend on this port's five domain verbs; external SDK types never
// leak past the adapter boundary. License: Apache-2.0

using System.Collections.Generic;

namespace EnvManager.Secrets.Core;

// --- Two-layer port: domain facade over the transport adapters ---

internal interface ISecretStore
{
    // Encrypt plaintext into a versioned envelope via the active provider
    // (the store-side verb for binding a secret into the store).
    string Mount(string plaintext, string? context = null);

    // Re-encrypt every secret across all profiles with the active provider.
    // Returns (totalSecrets, rotatedCount, failedCount).
    (int total, int rotated, int failed) Rotate(List<ProfileData> profiles);

    // Serialize all secrets of a profile into a DPAPI-encrypted export blob.
    string Export(ProfileData profile);

    // Import secrets from a DPAPI-encrypted export blob into a profile.
    // Returns per-secret (name, success) results.
    List<(string name, bool success)> Import(ProfileData profile, string encryptedBackup);

    // Decrypt an envelope back to plaintext (reveal path). Callers own the
    // zero-leak duty: plaintext is transient process memory only, never logged.
    string Reveal(string envelope, string? context = null);
}
