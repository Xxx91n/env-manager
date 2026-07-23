// SecretProvider.cs - Phase 1-2 Secret Provider Architecture
// v0.8: ISecretProvider interface, versioned envelopes, Windows Credential Manager adapter
// License: Apache-2.0

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

// --- Phase 1: Versioned Envelope ---

internal sealed class SecretEnvelope
{
    [JsonPropertyName("provider")]
    public string Provider { get; set; } = "dpapi-current-user";

    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("createdAt")]
    public string? CreatedAt { get; set; }

    // For dpapi-current-user: base64 DPAPI blob
    // For credential-manager: CRED target name (e.g. "EnvManager\\<profile>\\<var>")
    [JsonPropertyName("ciphertext")]
    public string? Ciphertext { get; set; }

    // For credential-manager: the actual encrypted credential blob is in CredMan
    [JsonPropertyName("targetName")]
    public string? TargetName { get; set; }

    public string Serialize()
    {
        return JsonSerializer.Serialize(this, SecretEnvelopeJsonContext.Default.SecretEnvelope);
    }

    public static SecretEnvelope? TryParse(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return null;
        // Backwards compat: bare base64 DPAPI blob (no leading {)
        string trimmed = stored.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
            return null;
        try
        {
            return JsonSerializer.Deserialize(stored, SecretEnvelopeJsonContext.Default.SecretEnvelope);
        }
        catch
        {
            return null;
        }
    }

    // Backwards compat: detect if a stored value is a bare DPAPI base64 blob
    // (pre-v0.8 format, no envelope wrapper)
    public static bool IsBareBase64Blob(string stored)
    {
        if (string.IsNullOrEmpty(stored)) return false;
        string trimmed = stored.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] == '{') return false;
        try
        {
            // Must be valid base64
            Convert.FromBase64String(stored);
            return true;
        }
        catch
        {
            return false;
        }
    }
}

[JsonSerializable(typeof(SecretEnvelope))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = false)]
internal sealed partial class SecretEnvelopeJsonContext : JsonSerializerContext
{
}

[JsonSerializable(typeof(SecretProviderManager.ProviderConfig))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true, WriteIndented = true)]
internal sealed partial class ProviderConfigJsonContext : JsonSerializerContext
{
}

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

// --- Phase 1: SecretProviderManager (routes to active provider) ---


// --- Phase 4: PowerShell SecretManagement Provider ---

internal sealed class PowerShellSecretManagementProvider : ISecretProvider
{
    public string Name => "powershell-secretmanagement";

    // The envelope stores: { provider, version, vaultName, secretName }
    // The actual secret value lives in the PowerShell SecretManagement vault

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        // Determine vault name and secret name from context or generate
        string vaultName = "EnvManager";
        string secretName = context != null
            ? "EnvManager_" + SanitizeSecretName(context)
            : "EnvManager_" + Guid.NewGuid().ToString("N");

        // Build PowerShell script to set the secret
        string script = $"$ErrorActionPreference='Stop'; " +
            $"Set-Secret -Name '{EscapeForPowerShell(secretName)}' -Secret '{EscapeForPowerShell(plaintext)}' -Vault '{EscapeForPowerShell(vaultName)}'; " +
            $"Write-Output 'OK'";

        string output = RunPowerShell(script);
        if (!output.Contains("OK"))
            throw new InvalidOperationException($"Set-Secret failed: {output}");

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = vaultName + "\\" + secretName
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

        var parts = parsed.TargetName.Split("\\");
        if (parts.Length < 2)
            throw new InvalidOperationException("Invalid targetName format, expected vault\\secretName");

        string vaultName = parts[0];
        string secretName = parts[1];

        string script = $"$ErrorActionPreference='Stop'; " +
            $"$s = Get-Secret -Name '{EscapeForPowerShell(secretName)}' -Vault '{EscapeForPowerShell(vaultName)}' -AsPlainText; " +
            $"Write-Output $s";

        string output = RunPowerShell(script);
        return output.TrimEnd();
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            var parts = parsed.TargetName.Split("\\");
            if (parts.Length >= 2)
            {
                string script = $"$ErrorActionPreference='Stop'; " +
                    $"Remove-Secret -Name '{EscapeForPowerShell(parts[1])}' -Vault '{EscapeForPowerShell(parts[0])}' -ErrorAction SilentlyContinue";
                try { RunPowerShell(script); } catch { }
            }
        }
    }

    private static string RunPowerShell(string script)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -NonInteractive -Command \"" + EscapeForPowerShell(script) + "\"",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start pwsh process");
        proc.WaitForExit(30000); // 30s timeout
        if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("pwsh timed out"); }

        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException($"pwsh exited {proc.ExitCode}: {stderr}");
        return stdout;
    }

    private static string EscapeForPowerShell(string s)
    {
        // Escape single quotes by doubling them
        return s.Replace("'", "''");
    }

    private static string SanitizeSecretName(string s)
    {
        return s.Replace("\\", "_").Replace("/", "_").Replace(":", "_");
    }
}

// --- Phase 5: HashiCorp Vault KV v2 Adapter ---

internal sealed class VaultKV2Provider : ISecretProvider
{
    public string Name => "vault-kv2";

    // Envelope: { provider, version, mountPath, secretPath, secretKey }
    // The profile stores only the mount path, secret path, and key name
    // The actual secret value is fetched from Vault's KV v2 secret engine via HTTP API

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";

        // Vault KV v2 stores secrets as key-value maps
        // The context string provides the secret path; we use a fixed key "value"
        string secretPath = context != null
            ? "env-manager/" + SanitizePath(context)
            : "env-manager/" + Guid.NewGuid().ToString("N");

        // Write to Vault
        string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR")
            ?? throw new InvalidOperationException("VAULT_ADDR environment variable not set");
        string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN")
            ?? throw new InvalidOperationException("VAULT_TOKEN environment variable not set");

        // Enforce TLS (refuse http:// unless explicitly localhost)
        if (!vaultAddr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLocalhost(vaultAddr))
                throw new InvalidOperationException("TLS mandatory: VAULT_ADDR must use https:// for non-localhost addresses");
        }

        // Build JSON payload: { "data": { "value": "<plaintext>" } }
        string payload = "{\"data\":{\"value\":\"" + JsonEscape(plaintext) + "\"}}";

        string apiUrl = vaultAddr.TrimEnd('/') + "/v1/secret/data/" + secretPath;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
        client.Timeout = TimeSpan.FromSeconds(10);

        var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
        var response = client.PostAsync(apiUrl, content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Vault write failed ({response.StatusCode}): {err}");
        }

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = "secret/" + secretPath + ":value"
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

        // TargetName format: "secret/<path>:value"
        // Extract mount (secret), path, and key (value)
        int colonIdx = parsed.TargetName.IndexOf(':');
        string secretKey = colonIdx >= 0 ? parsed.TargetName.Substring(colonIdx + 1) : "value";
        string mountAndPath = colonIdx >= 0 ? parsed.TargetName.Substring(0, colonIdx) : parsed.TargetName;
        int slashIdx = mountAndPath.IndexOf('/');
        string mount = slashIdx >= 0 ? mountAndPath.Substring(0, slashIdx) : "secret";
        string secretPath = slashIdx >= 0 ? mountAndPath.Substring(slashIdx + 1) : "";

        string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR")
            ?? throw new InvalidOperationException("VAULT_ADDR environment variable not set");
        string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN")
            ?? throw new InvalidOperationException("VAULT_TOKEN environment variable not set");

        if (!vaultAddr.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            if (!IsLocalhost(vaultAddr))
                throw new InvalidOperationException("TLS mandatory: VAULT_ADDR must use https:// for non-localhost addresses");
        }

        string apiUrl = vaultAddr.TrimEnd('/') + "/v1/" + mount + "/data/" + secretPath;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
        client.Timeout = TimeSpan.FromSeconds(10);

        var response = client.GetAsync(apiUrl).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Vault read failed ({response.StatusCode}): {err}");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        // Parse the Vault KV v2 response: { "data": { "data": { "value": "<plaintext>" } } }
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var data = doc.RootElement.GetProperty("data").GetProperty("data");
        if (data.TryGetProperty(secretKey, out var val))
        {
            return val.GetString() ?? "";
        }
        throw new InvalidOperationException($"Key '{secretKey}' not found in Vault secret at path '{mount}/{secretPath}'");
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            try
            {
                string vaultAddr = Environment.GetEnvironmentVariable("VAULT_ADDR");
                string vaultToken = Environment.GetEnvironmentVariable("VAULT_TOKEN");
                if (vaultAddr == null || vaultToken == null) return;

                int colonIdx = parsed.TargetName.IndexOf(':');
                string mountAndPath = colonIdx >= 0 ? parsed.TargetName.Substring(0, colonIdx) : parsed.TargetName;
                int slashIdx = mountAndPath.IndexOf('/');
                string mount = slashIdx >= 0 ? mountAndPath.Substring(0, slashIdx) : "secret";
                string secretPath = slashIdx >= 0 ? mountAndPath.Substring(slashIdx + 1) : "";

                string apiUrl = vaultAddr.TrimEnd('/') + "/v1/" + mount + "/metadata/" + secretPath;

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("X-Vault-Token", vaultToken);
                client.Timeout = TimeSpan.FromSeconds(10);
                client.DeleteAsync(apiUrl).GetAwaiter().GetResult();
            }
            catch { }
        }
    }

    private static string SanitizePath(string s)
    {
        return s.Replace("\\", "/").Replace(":", "_").Replace(" ", "_");
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    private static bool IsLocalhost(string addr)
    {
        return addr.Contains("127.0.0.1") || addr.Contains("localhost") || addr.Contains("[::1]");
    }
}

// --- Phase 6: SOPS Encrypted Envelopes ---

internal sealed class SopsProvider : ISecretProvider
{
    public string Name => "sops";

    // Envelope: { provider, version, createdAt, ciphertext (sops-encrypted JSON) }
    // The profile stores the full sops-encrypted JSON as the ciphertext field.
    // sops decrypts the JSON at launch time, extracting the "value" key.

    private static readonly string SOPS_BINARY = FindSopsBinary();

    private static string FindSopsBinary()
    {
        // Check SOPS_PATH env var first, then search PATH
        string envPath = Environment.GetEnvironmentVariable("SOPS_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // Search common locations on PATH
        string[] searchDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string dir in searchDirs)
        {
            string candidate = Path.Combine(dir.Trim('"'), "sops.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.Trim('"'), "sops");
            if (File.Exists(candidate)) return candidate;
        }

        // Check common install locations
        string[] commonPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "sops", "sops.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "sops", "sops.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "sops", "sops.exe")
        };
        foreach (string p in commonPaths)
        {
            if (File.Exists(p)) return p;
        }

        return "sops"; // fallback to PATH lookup
    }

    private static void EnsureSopsAvailable()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = SOPS_BINARY,
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("sops binary not found");
            proc.WaitForExit(5000);
            if (!proc.HasExited || proc.ExitCode != 0)
                throw new InvalidOperationException("sops binary not functional");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "sops binary not found. Install sops and ensure it is on PATH, or set SOPS_PATH env var. " +
                "See https://github.com/getsops/sops for installation instructions.");
        }
    }

    public string Encrypt(string plaintext, string? context = null)
    {
        EnsureSopsAvailable();

        string tempDir = Path.Combine(Path.GetTempPath(), "env-manager-sops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string plainFile = Path.Combine(tempDir, "secret.json");
        string encFile = Path.Combine(tempDir, "secret.enc.json");

        try
        {
            // Write JSON: { "value": "<plaintext>" }
            string jsonContent = "{\"value\":\"" + JsonEscape(plaintext ?? "") + "\"}";
            File.WriteAllText(plainFile, jsonContent, Encoding.UTF8);

            // Run: sops -e --output <enc> <plain>
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = SOPS_BINARY,
                Arguments = "-e --output \"" + encFile + "\" \"" + plainFile + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            // Pass through sops env vars for encryption key providers
            string[] sopsEnvVars = { "SOPS_AGE_RECIPIENT", "SOPS_AGE_KEY_FILE", "SOPS_PGP_FP",
                "SOPS_KMS_ARN", "SOPS_KMS_CONTEXT", "SOPS_AZURE_KV",
                "SOPS_GCP_KMS", "SOPS_HCVAULT_ADDR", "SOPS_HCVAULT_TOKEN",
                "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY", "AWS_REGION",
                "AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET" };
            foreach (var envVar in sopsEnvVars)
            {
                var val = Environment.GetEnvironmentVariable(envVar);
                if (val != null)
                    psi.EnvironmentVariables[envVar] = val;
            }

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to start sops process");
            proc.WaitForExit(30000);
            if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("sops encryption timed out"); }

            if (proc.ExitCode != 0)
            {
                string stderr = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException("sops encryption failed (exit " + proc.ExitCode + "): " + stderr);
            }

            if (!File.Exists(encFile))
                throw new InvalidOperationException("sops did not produce encrypted output file");

            string encryptedJson = File.ReadAllText(encFile, Encoding.UTF8);

            var envelope = new SecretEnvelope
            {
                Provider = Name,
                Version = 1,
                CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
                Ciphertext = encryptedJson
            };
            return envelope.Serialize();
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { }
        }
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.Ciphertext))
            throw new InvalidOperationException("Missing ciphertext (sops-encrypted JSON) in envelope");

        EnsureSopsAvailable();

        string tempDir = Path.Combine(Path.GetTempPath(), "env-manager-sops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        string encFile = Path.Combine(tempDir, "secret.enc.json");
        string plainFile = Path.Combine(tempDir, "secret.dec.json");

        try
        {
            File.WriteAllText(encFile, parsed.Ciphertext, Encoding.UTF8);

            // Run: sops -d --output <plain> <enc>
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = SOPS_BINARY,
                Arguments = "-d --output \"" + plainFile + "\" \"" + encFile + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            string[] sopsEnvVars = { "SOPS_AGE_KEY_FILE", "SOPS_AGE_SECRET_KEY",
                "GNUPGHOME", "AWS_ACCESS_KEY_ID", "AWS_SECRET_ACCESS_KEY",
                "AWS_REGION", "AZURE_TENANT_ID", "AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET" };
            foreach (var envVar in sopsEnvVars)
            {
                var envVal = Environment.GetEnvironmentVariable(envVar);
                if (envVal != null)
                    psi.EnvironmentVariables[envVar] = envVal;
            }

            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("Failed to start sops process");
            proc.WaitForExit(30000);
            if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("sops decryption timed out"); }

            if (proc.ExitCode != 0)
            {
                string stderr = proc.StandardError.ReadToEnd();
                throw new InvalidOperationException("sops decryption failed (exit " + proc.ExitCode + "): " + stderr);
            }

            if (!File.Exists(plainFile))
                throw new InvalidOperationException("sops did not produce decrypted output file");

            string decryptedJson = File.ReadAllText(plainFile, Encoding.UTF8);
            using var doc = System.Text.Json.JsonDocument.Parse(decryptedJson);
            if (doc.RootElement.TryGetProperty("value", out var val))
                return val.GetString() ?? "";
            throw new InvalidOperationException("Decrypted sops JSON does not contain value key");
        }
        finally
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); }
            catch { }
        }
    }

    // sops is file-based; the envelope is self-contained. Delete is a no-op.
    public void Delete(string envelope, string? context = null) { }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

// --- Phase 7: Azure Key Vault Provider ---

internal sealed class AzureKeyVaultProvider : ISecretProvider
{
    public string Name => "azure-keyvault";

    // Envelope: { provider, version, createdAt, targetName (vaultUri|secretName) }
    // The profile stores only the vault URI and secret name.
    // The actual secret value lives in Azure Key Vault and is fetched via REST API.

    private const string API_VERSION = "7.4";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiry;

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";

        string vaultUri = Environment.GetEnvironmentVariable("AZURE_KEYVAULT_URI")
            ?? throw new InvalidOperationException(
                "AZURE_KEYVAULT_URI environment variable not set (e.g. https://myvault.vault.azure.net)");

        // Enforce TLS: Azure Key Vault is always HTTPS
        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure Key Vault requires HTTPS (TLS mandatory)");

        string secretName = context != null
            ? SanitizeSecretName(context)
            : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        string token = GetBearerToken();

        // Build PUT request body
        string payload = "{\"value\":\"" + JsonEscape(plaintext) + "\"}";
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = Timeout;

        var content = new System.Net.Http.StringContent(payload, Encoding.UTF8, "application/json");
        var response = client.PutAsync(apiUrl, content).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            throw new InvalidOperationException($"Azure Key Vault write failed ({response.StatusCode}): {err}");
        }

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = vaultUri.TrimEnd('/') + "|" + secretName
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

        int pipeIdx = parsed.TargetName.IndexOf('|');
        if (pipeIdx < 0)
            throw new InvalidOperationException("Invalid targetName format, expected vaultUri|secretName");

        string vaultUri = parsed.TargetName.Substring(0, pipeIdx);
        string secretName = parsed.TargetName.Substring(pipeIdx + 1);

        if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Azure Key Vault requires HTTPS (TLS mandatory)");

        string token = GetBearerToken();
        string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

        using var client = new System.Net.Http.HttpClient();
        client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
        client.Timeout = Timeout;

        var response = client.GetAsync(apiUrl).GetAwaiter().GetResult();
        if (!response.IsSuccessStatusCode)
        {
            string err = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                throw new InvalidOperationException($"Azure Key Vault secret {secretName} not found");
            throw new InvalidOperationException($"Azure Key Vault read failed ({response.StatusCode}): {err}");
        }

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("value", out var val))
            return val.GetString() ?? "";
        throw new InvalidOperationException("Azure Key Vault response does not contain value field");
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && !string.IsNullOrEmpty(parsed.TargetName))
        {
            try
            {
                int pipeIdx = parsed.TargetName.IndexOf('|');
                if (pipeIdx < 0) return;

                string vaultUri = parsed.TargetName.Substring(0, pipeIdx);
                string secretName = parsed.TargetName.Substring(pipeIdx + 1);

                if (!vaultUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    return;

                string token = GetBearerToken();
                string apiUrl = vaultUri.TrimEnd('/') + "/secrets/" + secretName + "?api-version=" + API_VERSION;

                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", "Bearer " + token);
                client.Timeout = Timeout;
                client.DeleteAsync(apiUrl).GetAwaiter().GetResult();
            }
            catch { }
        }
    }

    public bool CanRotate => true;

    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        return Encrypt(plaintext, context);
    }

    private string GetBearerToken()
    {
        if (_cachedToken != null && DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5))
            return _cachedToken;

        string? token = TryGetManagedIdentityToken() ?? TryGetServicePrincipalToken();

        if (string.IsNullOrEmpty(token))
            throw new InvalidOperationException(
                "Failed to obtain Azure access token. Either run on an Azure VM with managed identity, " +
                "or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.");

        _cachedToken = token;
        return token;
    }

    private string? TryGetManagedIdentityToken()
    {
        try
        {
            string imdsUrl = "http://169.254.169.254/metadata/identity/oauth2/token" +
                "?api-version=2018-02-01&resource=https://vault.azure.net";

            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("Metadata", "true");
            client.Timeout = TimeSpan.FromSeconds(10);

            var response = client.GetAsync(imdsUrl).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
            {
                if (doc.RootElement.TryGetProperty("expires_on", out var exp))
                {
                    if (long.TryParse(exp.GetString(), out long expUnix))
                        _tokenExpiry = DateTimeOffset.FromUnixTimeSeconds(expUnix);
                }
                return token.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private string? TryGetServicePrincipalToken()
    {
        string? clientId = Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");
        string? clientSecret = Environment.GetEnvironmentVariable("AZURE_CLIENT_SECRET");
        string? tenantId = Environment.GetEnvironmentVariable("AZURE_TENANT_ID");

        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(tenantId))
            return null;

        try
        {
            string tokenUrl = "https://login.microsoftonline.com/" + tenantId + "/oauth2/v2.0/token";

            string formData = "client_id=" + Uri.EscapeDataString(clientId) +
                "&client_secret=" + Uri.EscapeDataString(clientSecret) +
                "&scope=" + Uri.EscapeDataString("https://vault.azure.net/.default") +
                "&grant_type=client_credentials";

            using var client = new System.Net.Http.HttpClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            var content = new System.Net.Http.StringContent(formData, Encoding.UTF8, "application/x-www-form-urlencoded");
            var response = client.PostAsync(tokenUrl, content).GetAwaiter().GetResult();
            if (!response.IsSuccessStatusCode)
                return null;

            string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("access_token", out var token))
            {
                if (doc.RootElement.TryGetProperty("expires_in", out var exp))
                {
                    if (int.TryParse(exp.GetString(), out int expSeconds))
                        _tokenExpiry = DateTimeOffset.UtcNow.AddSeconds(expSeconds);
                }
                return token.GetString();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string SanitizeSecretName(string s)
    {
        // Azure Key Vault secret names: alphanumeric and hyphens only, max 127 chars
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '-')
                sb.Append(c);
            else
                sb.Append('-');
        }
        string result = sb.ToString().Trim('-');
        if (result.Length > 127) result = result.Substring(0, 127);
        return result;
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}

internal static class SecretProviderManager
{
    private static readonly Dictionary<string, ISecretProvider> _providers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["dpapi-current-user"] = new DpapiCurrentUserProvider(),
        ["credential-manager"] = new CredentialManagerProvider(),
        ["powershell-secretmanagement"] = new PowerShellSecretManagementProvider(),
        ["vault-kv2"] = new VaultKV2Provider(),
        ["sops"] = new SopsProvider(),
        ["azure-keyvault"] = new AzureKeyVaultProvider()
    };

    private const string PROVIDER_CONFIG_FILE = "secret-providers.json";
    private const string DEFAULT_PROVIDER = "dpapi-current-user";

    // Config model
    internal sealed class ProviderConfig
    {
        [JsonPropertyName("activeProvider")]
        public string ActiveProvider { get; set; } = DEFAULT_PROVIDER;

        [JsonPropertyName("fallbackPolicy")]
        public string FallbackPolicy { get; set; } = "fail-closed"; // or "legacy-dpapi"
    }

    

    private static string GetConfigPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnvManager");
        return Path.Combine(dir, PROVIDER_CONFIG_FILE);
    }

    private static ProviderConfig LoadConfig()
    {
        try
        {
            string path = GetConfigPath();
            if (File.Exists(path))
            {
                string json = File.ReadAllText(path);
                var cfg = JsonSerializer.Deserialize(json, ProviderConfigJsonContext.Default.ProviderConfig);
                if (cfg != null) return cfg;
            }
        }
        catch
        {
            // Fall through to default
        }
        return new ProviderConfig();
    }

    public static ISecretProvider GetActiveProvider()
    {
        var config = LoadConfig();
        if (_providers.TryGetValue(config.ActiveProvider, out var provider))
            return provider;

        // Fail-closed: unknown provider = error, not silent fallback
        // Exception: if fallbackPolicy is legacy-dpapi and the stored envelope
        // is a bare DPAPI blob, DpapiCurrentUserProvider can still decrypt it
        if (config.FallbackPolicy == "legacy-dpapi")
            return _providers[DEFAULT_PROVIDER];

        throw new InvalidOperationException(
            $"Active secret provider '{config.ActiveProvider}' is not available. " +
            $"Install or configure the provider, or set fallbackPolicy to 'legacy-dpapi'.");
    }

    public static string Encrypt(string plaintext, string? context = null)
    {
        var provider = GetActiveProvider();
        return provider.Encrypt(plaintext, context);
    }

    public static string Decrypt(string envelope, string? context = null)
    {
        // First: check if it's a bare DPAPI blob (pre-v0.8 backwards compat)
        if (SecretEnvelope.IsBareBase64Blob(envelope))
        {
            return _providers[DEFAULT_PROVIDER].Decrypt(envelope, context);
        }

        // Parse envelope to find provider
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed == null)
            throw new InvalidOperationException("Invalid secret envelope: not a JSON envelope and not a valid base64 blob");

        if (_providers.TryGetValue(parsed.Provider, out var provider))
        {
            return provider.Decrypt(envelope, context);
        }

        // Unknown provider: fail-closed
        throw new InvalidOperationException(
            $"Secret provider '{parsed.Provider}' is not available. " +
            "The provider may not be installed or configured on this machine.");
    }

    public static void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed != null && _providers.TryGetValue(parsed.Provider, out var provider))
        {
            provider.Delete(envelope, context);
        }
    }

    // List available providers and their status
    public static List<(string Name, bool Available)> ListProviders()
    {
        var result = new List<(string, bool)>();
        foreach (var kvp in _providers)
        {
            result.Add((kvp.Key, true));
        }
        return result;
    }

    // Get the active provider name from config
    public static string GetActiveProviderName()
    {
        return LoadConfig().ActiveProvider;
    }

    // Set the active provider (persists to config file)
    public static void SetActiveProvider(string name)
    {
        if (!_providers.ContainsKey(name))
            throw new InvalidOperationException($"Unknown secret provider: {name}");

        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EnvManager");
        Directory.CreateDirectory(dir);

        var config = LoadConfig();
        config.ActiveProvider = name;
        string json = JsonSerializer.Serialize(config, ProviderConfigJsonContext.Default.ProviderConfig);
        File.WriteAllText(GetConfigPath(), json);
    }

    // Phase 3: Key Rotation - re-encrypt all secrets in all profiles with the active provider
    // Returns (totalSecrets, rotatedCount, failedCount)
    public static (int total, int rotated, int failed) RotateAll(System.Collections.Generic.List<ProfileData> profiles)
    {
        var provider = GetActiveProvider();
        int total = 0, rotated = 0, failed = 0;

        foreach (var profile in profiles)
        {
            foreach (var v in profile.Variables)
            {
                if (!profile.SecretVariables.Any(s => s.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
                    continue;
                total++;
                try
                {
                    // Decrypt with whatever provider encrypted it
                    string plaintext = Decrypt(v.Value, profile.Name + "\\" + v.Name);
                    // Re-encrypt with the active provider
                    v.Value = Encrypt(plaintext, profile.Name + "\\" + v.Name);
                    rotated++;
                }
                catch
                {
                    // Decryption failed (wrong provider, deleted CredMan entry, etc.)
                    failed++;
                }
            }
        }
        return (total, rotated, failed);
    }

    // Phase 3: Export secrets from a profile to an encrypted backup file
    // The backup is itself DPAPI-encrypted (CurrentUser scope) regardless of the provider,
    // so the backup is portable within the same user account.
    public static string ExportSecrets(ProfileData profile)
    {
        var secretsToExport = new System.Collections.Generic.List<(string name, string envelope)>();
        foreach (var v in profile.Variables)
        {
            if (profile.SecretVariables.Any(s => s.Equals(v.Name, StringComparison.OrdinalIgnoreCase)))
            {
                secretsToExport.Add((v.Name, v.Value));
            }
        }
        var exportData = new
        {
            profileName = profile.Name,
            exportedAt = DateTimeOffset.UtcNow.ToString("O"),
            secrets = secretsToExport.Select(s => new { name = s.name, envelope = s.envelope }).ToList()
        };
        string json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        return DpapiHelper.EncryptSecret(json);
    }

    // Phase 3: Import secrets from an encrypted backup into a profile
    // Returns list of (name, success) tuples
    public static System.Collections.Generic.List<(string name, bool success)> ImportSecrets(ProfileData profile, string encryptedBackup)
    {
        var results = new System.Collections.Generic.List<(string, bool)>();
        // Decrypt the backup (DPAPI CurrentUser - same user that exported it)
        string json = DpapiHelper.DecryptSecret(encryptedBackup);
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var secrets = doc.RootElement.GetProperty("secrets");
        foreach (var secret in secrets.EnumerateArray())
        {
            string name = secret.GetProperty("name").GetString() ?? "";
            string envelope = secret.GetProperty("envelope").GetString() ?? "";
            if (string.IsNullOrEmpty(name))
            {
                results.Add((name, false));
                continue;
            }
            try
            {
                // Verify the envelope can be decrypted by trying to decrypt it
                _ = Decrypt(envelope, profile.Name + "\\" + name);

                // Remove existing variable with same name, then add the imported one
                profile.Variables.RemoveAll(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
                profile.Variables.Add(new ProfileVariable { Name = name, Value = envelope });
                if (!profile.SecretVariables.Any(s => s.Equals(name, StringComparison.OrdinalIgnoreCase)))
                    profile.SecretVariables.Add(name);

                results.Add((name, true));
            }
            catch
            {
                results.Add((name, false));
            }
        }
        return results;
    }
}
