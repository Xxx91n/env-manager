using EnvManager.Secrets.Core;
// DpapiCurrentUserProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// One-symbol-per-file split of the retired single-file secret provider module (issue 09); behavior unchanged.
// License: Apache-2.0

using System;
using System.Runtime.InteropServices;

// ticket 40: adapter-boundary typed error family (aliases below).

namespace EnvManager.Secrets.Providers;

// --- Phase 1: DpapiCurrentUserProvider (wraps existing DpapiHelper) ---

internal sealed class DpapiCurrentUserProvider : ISecretProvider
{
    public string Name => "dpapi-current-user";

    // Capability descriptors (ticket 41, spec Phase 6): declared per provider,
    // consumed by SecretProviderManager.ListProviders and surfaced through
    // `profile secret-provider list` so the GUI gates on data, not name lists.
    public bool RefreshCapable => false;
    public bool CertAuthRequired => false;
    public bool RequiresNetwork => false;

    // Local DPAPI CurrentUser: platform feature, always usable on Windows.
    public bool Available => true;

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
            ?? throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, $"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.Ciphertext))
            throw new SecretProviderInvalidEnvelopeException(Name, SecretProviderErrors.OpDecrypt, "Missing ciphertext in envelope");

        try
        {
            return DpapiHelper.DecryptSecret(parsed.Ciphertext);
        }
        catch (System.ComponentModel.Win32Exception w32)
        {
            throw new SecretProviderErrors.MappedWin32(Name, SecretProviderErrors.OpDecrypt,
                "DPAPI decrypt failed (Win32 error " + w32.NativeErrorCode + ")", w32);
        }
    }

    public bool CanRotate => false;
}

// ticket 40: file-local aliases - see CredentialManagerProvider.cs for the rationale.
internal using SecretProviderException = EnvManager.Secrets.Core.SecretProviderException;
internal using SecretProviderAuthFailedException = EnvManager.Secrets.Core.SecretProviderAuthFailedException;
internal using SecretProviderNotFoundException = EnvManager.Secrets.Core.SecretProviderNotFoundException;
internal using SecretProviderPermissionDeniedException = EnvManager.Secrets.Core.SecretProviderPermissionDeniedException;
internal using SecretProviderUnavailableException = EnvManager.Secrets.Core.SecretProviderUnavailableException;
internal using SecretProviderTimeoutException = EnvManager.Secrets.Core.SecretProviderTimeoutException;
internal using SecretProviderInvalidEnvelopeException = EnvManager.Secrets.Core.SecretProviderInvalidEnvelopeException;
internal using SecretProviderErrors = EnvManager.Secrets.Core.SecretProviderErrors;
