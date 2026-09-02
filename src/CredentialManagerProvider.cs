// CredentialManagerProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace EnvManager;

// --- Phase 2: CredentialManagerProvider (advapi32.dll P/Invoke) ---

internal sealed class CredentialManagerProvider : ISecretProvider
{
    public string Name => "credential-manager";

    // CRED_TYPE_GENERIC = 1
    private const int CRED_TYPE_GENERIC = 1;

    // CRED_PERSIST_ENTERPRISE = 3 (roams with user profile)
    private const int CRED_PERSIST_ENTERPRISE = 3;

    // Maximum credential blob size (512 bytes for Generic, per MS docs)
    private const int MAX_CRED_BLOB = 512;

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        byte[] plainBytes = Encoding.UTF8.GetBytes(plaintext);
        if (plainBytes.Length > MAX_CRED_BLOB)
            throw new InvalidOperationException(
                $"Credential Manager blob too large ({plainBytes.Length} bytes, max {MAX_CRED_BLOB}). " +
                "Use dpapi-current-user provider for larger secrets.");

        // Target name: EnvManager\<context> or EnvManager\<generated-uuid>
        string targetName = context != null
            ? "EnvManager\\" + SanitizeTargetName(context)
            : "EnvManager\\" + Guid.NewGuid().ToString("N");

        // DPAPI-encrypt the plaintext before storing in CredMan
        // so even if CredMan is dumped, the blob is still encrypted
        string dpapiCipher = DpapiHelper.EncryptSecret(plaintext);

        byte[] credBlob = Encoding.UTF8.GetBytes(dpapiCipher);

        var cred = new CREDENTIALW
        {
            Type = CRED_TYPE_GENERIC,
            TargetName = targetName,
            Persist = CRED_PERSIST_ENTERPRISE,
            CredentialBlobSize = credBlob.Length,
            CredentialBlob = Marshal.AllocHGlobal(credBlob.Length),
            UserName = Environment.UserName
        };

        try
        {
            Marshal.Copy(credBlob, 0, cred.CredentialBlob, credBlob.Length);

            if (!CredWriteW(ref cred, 0))
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err,
                    $"CredWriteW failed (Win32 error {err})");
            }
        }
        finally
        {
            if (cred.CredentialBlob != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(cred.CredentialBlob);
            }
            // Zero the DPAPI ciphertext bytes from managed memory
            for (int i = 0; i < credBlob.Length; i++) credBlob[i] = 0;
        }

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = targetName
        };
        return envelope.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName))
            throw new InvalidOperationException("Missing targetName in envelope");

        IntPtr credPtr = IntPtr.Zero;
        try
        {
            if (!CredReadW(parsed.TargetName, CRED_TYPE_GENERIC, 0, out credPtr))
            {
                int err = Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err,
                    $"CredReadW failed for target '{parsed.TargetName}' (Win32 error {err})");
            }

            var cred = (CREDENTIALW)Marshal.PtrToStructure(credPtr, typeof(CREDENTIALW))!;
            if (cred.CredentialBlob == IntPtr.Zero || cred.CredentialBlobSize == 0)
                throw new InvalidOperationException("Credential blob is empty");

            byte[] credBlob = new byte[cred.CredentialBlobSize];
            Marshal.Copy(cred.CredentialBlob, credBlob, 0, cred.CredentialBlobSize);
            try
            {
                string dpapiCipher = Encoding.UTF8.GetString(credBlob);
                return DpapiHelper.DecryptSecret(dpapiCipher);
            }
            finally
            {
                for (int i = 0; i < credBlob.Length; i++) credBlob[i] = 0;
            }
        }
        finally
        {
            if (credPtr != IntPtr.Zero) CredFree(credPtr);
        }
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            CredDeleteW(parsed.TargetName, CRED_TYPE_GENERIC, 0);
        }
    }

    private static string SanitizeTargetName(string s)
    {
        // Target name cannot contain backslash as separator conflict
        return s.Replace("\\", "_").Replace("/", "_");
    }

    // --- P/Invoke: advapi32.dll Credential Manager ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIALW
    {
        public int Flags;
        public int Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string? UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref CREDENTIALW cred, int flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    private static extern void CredFree(IntPtr cred);
}
