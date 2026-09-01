namespace EnvManager;

// v0.7 DPAPI-CurrentUser helper for encrypting secret variable values held in launch profiles.
// Implemented via P/Invoke on crypt32.dll to avoid any NuGet dependency, keeping the project
// build-compatible across MSVC and MinGW GNU toolchains. The secret value lives only transiently
// in process memory as a managed byte[]; copies are cleared after use to limit exposure.
internal static partial class DpapiHelper
{
    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CryptProtectData(ref DATA_BLOB pDataIn, string? szDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataBlob);

    [System.Runtime.InteropServices.DllImport("crypt32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn, out string? ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, out DATA_BLOB pDataBlob);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct DATA_BLOB
    {
        public int cbData;
        public IntPtr pbData;
    }

    // Matches System.Security.Cryptography.ProtectedData with DataProtectionScope.CurrentUser
    // when called with no entropy: CryptProtectData writes CurrentUser-scope ciphertext that
    // only the same user + machine can decrypt.
    private const int CryptProtectUiForbidden = 0x01;

    public static string EncryptSecret(string plaintext)
    {
        if (plaintext == null) plaintext = "";
        byte[] plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        try
        {
            var inBlob = new DATA_BLOB();
            inBlob.cbData = plainBytes.Length;
            inBlob.pbData = System.Runtime.InteropServices.Marshal.AllocHGlobal(plainBytes.Length);
            try
            {
                System.Runtime.InteropServices.Marshal.Copy(plainBytes, 0, inBlob.pbData, plainBytes.Length);
                if (!CryptProtectData(ref inBlob, "EnvManager.Secret", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob))
                {
                    int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                    throw new System.ComponentModel.Win32Exception(err, "CryptProtectData failed (Win32 error " + err + ")");
                }
                try
                {
                    byte[] cipher = new byte[outBlob.cbData];
                    System.Runtime.InteropServices.Marshal.Copy(outBlob.pbData, cipher, 0, outBlob.cbData);
                    return Convert.ToBase64String(cipher);
                }
                finally
                {
                    if (outBlob.pbData != IntPtr.Zero) NativeMethods.LocalFree(outBlob.pbData);
                }
            }
            finally
            {
                if (inBlob.pbData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(inBlob.pbData);
            }
        }
        finally
        {
            for (int i = 0; i < plainBytes.Length; i++) plainBytes[i] = 0;
        }
    }

    public static string DecryptSecret(string ciphertextBase64)
    {
        if (string.IsNullOrEmpty(ciphertextBase64)) return "";
        byte[] cipher = Convert.FromBase64String(ciphertextBase64);
        var inBlob = new DATA_BLOB();
        inBlob.cbData = cipher.Length;
        inBlob.pbData = System.Runtime.InteropServices.Marshal.AllocHGlobal(cipher.Length);
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(cipher, 0, inBlob.pbData, cipher.Length);
            if (!CryptUnprotectData(ref inBlob, out _, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, CryptProtectUiForbidden, out var outBlob))
            {
                int err = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                throw new System.ComponentModel.Win32Exception(err, "CryptUnprotectData failed (Win32 error " + err + ")");
            }
            try
            {
                byte[] plain = new byte[outBlob.cbData];
                System.Runtime.InteropServices.Marshal.Copy(outBlob.pbData, plain, 0, outBlob.cbData);
                try { return System.Text.Encoding.UTF8.GetString(plain); }
                finally { for (int i = 0; i < plain.Length; i++) plain[i] = 0; }
            }
            finally
            {
                if (outBlob.pbData != IntPtr.Zero) NativeMethods.LocalFree(outBlob.pbData);
            }
        }
        finally
        {
            if (inBlob.pbData != IntPtr.Zero) System.Runtime.InteropServices.Marshal.FreeHGlobal(inBlob.pbData);
        }
    }
}
