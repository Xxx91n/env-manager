// DpapiCurrentUserProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

using System;
using System.Runtime.InteropServices;

namespace EnvManager;

// --- Phase 1: DpapiCurrentUserProvider (wraps existing DpapiHelper) ---

internal sealed class DpapiCurrentUserProvider : ISecretProvider
{
    public string Name => "dpapi-current-user";

    public string Encrypt(string plaintext, string? context = null)
    {
        string cipherBase64 = DpapiHelper.EncryptSecret(plaintext);
        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            Ciphertext = cipherBase64
        };
        return envelope.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        // Backwards compat: bare base64 DPAPI blob from pre-v0.8
        if (SecretEnvelope.IsBareBase64Blob(envelope))
        {
            return DpapiHelper.DecryptSecret(envelope);
        }

        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.Ciphertext))
            throw new InvalidOperationException("Missing ciphertext in envelope");

        return DpapiHelper.DecryptSecret(parsed.Ciphertext);
    }

    public bool CanRotate => false;
}
