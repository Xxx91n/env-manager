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

    private const string VaultName = "EnvManager";

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        string secretName = context != null
            ? "EnvManager_" + SanitizeSecretName(context)
            : "EnvManager_" + Guid.NewGuid().ToString("N");

        EnsureSecretManagementAvailable();
        EnsureVaultRegistered();

        string script =
            "$ErrorActionPreference='Stop'; " +
            "Set-Secret -Name '" + EscapeForPowerShell(secretName) + "' " +
            "-Secret (ConvertTo-SecureString '" + EscapeForPowerShell(plaintext) + "' -AsPlainText -Force) " +
            "-Vault '" + EscapeForPowerShell(VaultName) + "'; " +
            "Write-Output 'OK'";

        string output = RunPowerShell(script);
        if (!output.Contains("OK"))
            throw new InvalidOperationException("Set-Secret failed: " + StripClixml(output));

        var envelope = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = VaultName + "\\" + secretName
        };
        return envelope.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name)
            throw new InvalidOperationException("Provider mismatch: expected " + Name + ", got " + parsed.Provider);
        if (string.IsNullOrEmpty(parsed.TargetName))
            throw new InvalidOperationException("Missing targetName in envelope");

        var parts = parsed.TargetName.Split("\\");
        if (parts.Length < 2)
            throw new InvalidOperationException("Invalid targetName format, expected vault\\secretName");

        string vaultName = parts[0];
        string secretName = parts[1];

        EnsureSecretManagementAvailable();

        string script =
            "$ErrorActionPreference='Stop'; " +
            "$s = Get-Secret -Name '" + EscapeForPowerShell(secretName) + "' " +
            "-Vault '" + EscapeForPowerShell(vaultName) + "' -AsPlainText; " +
            "Write-Output $s";

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
                string script =
                    "$ErrorActionPreference='Stop'; " +
                    "Remove-Secret -Name '" + EscapeForPowerShell(parts[1]) + "' " +
                    "-Vault '" + EscapeForPowerShell(parts[0]) + "' -ErrorAction SilentlyContinue";
                try { RunPowerShell(script); } catch { }
            }
        }
    }

    // v0.7.5: probe that the PowerShell SecretManagement module is installed.
    // This is called BEFORE a real Set-Secret so we do not surface the raw
    // CLIXML "Set-Secret is not recognized" catastrophe to the user. Instead
    // we get a clear actionable error pointing at Install-Module.
    private static void EnsureSecretManagementAvailable()
    {
        string probe =
            "$ErrorActionPreference='Stop'; " +
            // v0.7.10: use a wildcard pattern so we match BOTH the historical
            // short name 'Microsoft.SecretManagement' and the canonical package
            // name 'Microsoft.PowerShell.SecretManagement' published on the
            // PowerShell Gallery. The prior literal probe returned zero even
            // when the module was correctly installed, so every activation
            // threw a false 'PowerShell SecretManagement module is not
            // installed' error even on a correctly-provisioned host.
            "$m = Get-Module -ListAvailable *SecretManagement*; " +
            "if ($null -eq $m) { Write-Output 'MISSING_MODULE' } else { Write-Output 'OK' }";
        string moduleCheck = RunPowerShell(probe);
        if (!moduleCheck.Contains("OK"))
            throw new InvalidOperationException(
                "PowerShell SecretManagement module is not installed. " +
                "Run: pwsh -Command \"Install-Module Microsoft.PowerShell.SecretManagement, Microsoft.PowerShell.SecretStore -Scope CurrentUser -Force\" " +
                "then retry. (Vault: " + VaultName + ")");
    }

    // v0.7.5: auto-register the EnvManager vault if it is not already registered.
    // Idempotent: Register-SecretVault errors if already registered, so we
    // probe with Get-SecretVault first and only register when absent.
    private static void EnsureVaultRegistered()
    {
        string probe =
            "$ErrorActionPreference='Stop'; " +
            "try { $v = Get-SecretVault -Name '" + EscapeForPowerShell(VaultName) + "' -ErrorAction Stop; if ($null -ne $v) { Write-Output 'OK' } else { Write-Output 'REGISTER' } } " +
            "catch { Write-Output 'REGISTER' }";
        string vaultCheck = RunPowerShell(probe);
        if (!vaultCheck.Contains("OK"))
        {
            string register =
                "$ErrorActionPreference='Stop'; " +
                // v0.7.11: use the canonical PowerShell Gallery module name
                // Microsoft.PowerShell.SecretStore. The prior short form
                // 'Microsoft.SecretStore' fails with "Could not load and
                // retrieve module information ... The specified module
                // 'Microsoft.SecretStore' was not loaded because no valid module
                // file was found in any module directory." even after the
                // documented Install-Module command has been run, because the
                // actual installed module name is 'Microsoft.PowerShell.SecretStore'.
                "Register-SecretVault -Name '" + EscapeForPowerShell(VaultName) + "' " +
                "-ModuleName Microsoft.PowerShell.SecretStore -DefaultVault -AllowClobber; " +
                "Write-Output 'OK'";
            string reg = RunPowerShell(register);
            if (!reg.Contains("OK"))
                throw new InvalidOperationException("Failed to register SecretManagement vault '" + VaultName + "': " + StripClixml(reg));
        }
    }

    // v0.7.4: RunPowerShell no longer uses `pwsh -Command "<escaped script>"`.
    // The previous approach caused a nested-quoting catastrophe:
    // EscapeForPowerShell doubled every single quote, then the outer double
    // quotes wrapped the script, and pwsh re-parsed the inner ''Stop'' as a
    // broken token -> "Unexpected token 'Stop''". Any value containing a
    // single quote (e.g. a secret with an apostrophe) hit the same wall.
    //
    // Fix: pass the script via `-EncodedCommand` as base64 of UTF-16LE,
    // which is the canonical Microsoft-recommended way to invoke pwsh with
    // arbitrary content. No shell quoting, no escape doubling, no tokenization
    // ambiguity. CREATE_NO_WINDOW stays so no terminal flashes.
    //
    // v0.7.5: stderr is parsed through `StripClixml` before being thrown so
    // callers see a clean human-readable message instead of the raw CLIXML
    // serialization wrapper (the `#< CLIXML <Objs ...>` blob that pwsh emits
    // when stderr is redirected by a non-interactive host).
    private static string RunPowerShell(string script)
    {
        string encoded = Convert.ToBase64String(
            Encoding.Unicode.GetBytes(script));
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start pwsh process");
        // Per .NET guidance (MS docs: "WaitForExit" + multiple redirected
        // streams): synchronously ReadToEnd after WaitForExit can deadlock when
        // either pipe fills before we read from it, which is the real cause of
        // the "pwsh window hang" -- pwsh blocks writing to a full stdout/stderr
        // pipe while we wait for it to exit. Drain both pipes async-first so
        // the child process never blocks on pipe backpressure, then wait.
        var stdoutBuf = new System.Text.StringBuilder();
        var stderrBuf = new System.Text.StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) { stdoutBuf.AppendLine(e.Data); } };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) { stderrBuf.AppendLine(e.Data); } };
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();
        proc.WaitForExit(30000); // 30s timeout
        if (!proc.HasExited) { try { proc.Kill(); } catch { } throw new InvalidOperationException("pwsh timed out after 30s"); }
        // For async-redirected streams WaitForExit(int) may return while the
        // async drains are still flushing: call WaitForExit() (no timeout) to
        // guarantee both async readers have delivered all data before we read.
        proc.WaitForExit();
        string stdout = stdoutBuf.ToString();
        string stderr = stderrBuf.ToString();
        if (proc.ExitCode != 0)
            throw new InvalidOperationException("pwsh exited " + proc.ExitCode + ": " + StripClixml(stderr));
        return stdout;
    }

    // v0.7.5: strips CLIXML serialization wrapper that pwsh emits when stderr
    // is redirected by a non-interactive host. The wrapper looks like:
    //   #< CLIXML <Objs Version="1.1.0.1" ...><S S="Error">...escaped text...</S>...</Objs>
    // We extract the inner text of every <S S="Error"> element, restore the
    // encoded control sequences (_x001B_ -> ESC, _x000D_ -> CR, _x000A_ -> LF),
    // and concatenate. If the input is not CLIXML we return it unchanged.
    private static string StripClixml(string s)
    {
        if (string.IsNullOrEmpty(s)) return s ?? "";
        int cli = s.IndexOf("CLIXML", StringComparison.OrdinalIgnoreCase);
        if (cli < 0) return s;
        string text = s;
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("<S S=\"Error\">", i, StringComparison.OrdinalIgnoreCase);
            if (open < 0) break;
            int contentStart = open + "<S S=\"Error\">".Length;
            int close = text.IndexOf("</S>", contentStart, StringComparison.OrdinalIgnoreCase);
            if (close < 0) break;
            string inner = text.Substring(contentStart, close - contentStart);
            inner = inner.Replace("_x001B_", "\u001B")
                          .Replace("_x000D_", "\r")
                          .Replace("_x000A_", "\n")
                          .Replace("_x0009_", "\t");
            // strip ANSI color sequences (ESC [ digit ; ... m)
            inner = System.Text.RegularExpressions.Regex.Replace(inner, "\u001B\\[[0-9;]*m", "");
            sb.Append(inner);
            i = close + "</S>".Length;
        }
        string result = sb.ToString();
        return string.IsNullOrEmpty(result) ? s : result.Trim();
    }

    private static string EscapeForPowerShell(string s)
    {
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
            // v0.9.13 Phase 4F: record provider binary hash for tamper detection
            try { Program.RecordProviderHash("sops", SOPS_BINARY); } catch { }
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
            // v0.7.11: write UTF-8 WITHOUT BOM. Encoding.UTF8 emits a BOM (0xEF 0xBB 0xBF)
            // and sops >= 3.x rejects BOM-prefixed JSON with "invalid character 'ï'
            // looking for beginning of value" when unmarshalling, failing activation.
            string jsonContent = "{\"value\":\"" + JsonEscape(plaintext ?? "") + "\"}";
            File.WriteAllText(plainFile, jsonContent, new UTF8Encoding(false));

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
            // v0.7.11: env var names verified against official SOPS age docs
            // (https://getsops.io/docs/usage/identities/age/). The recipient list env
            // var is SOPS_AGE_RECIPIENTS (plural; 'S' required) - the prior singular
            // 'SOPS_AGE_RECIPIENT' is NOT recognized by sops and activation failed with
            // 'config file not found and no keys provided through command line options'
            // even with the env var set. SOPS_AGE_KEY_FILE / SOPS_AGE_KEY / SOPS_AGE_KEY_CMD
            // override the private-key file lookup; only SOPS_AGE_KEY_FILE is forwarded
            // here because it is the only one a normal desktop user would set.
            string[] sopsEnvVars = { "SOPS_AGE_RECIPIENTS", "SOPS_AGE_KEY_FILE", "SOPS_PGP_FP",
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
            // v0.7.11: no BOM (sops >= 3.x rejects BOM-prefixed JSON).
            File.WriteAllText(encFile, parsed.Ciphertext, new UTF8Encoding(false));

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

            // v0.7.11: decrypt env var names verified against official SOPS age
            // docs. SOPS_AGE_SECRET_KEY is NOT an official env var (official: KEY_FILE,
            // KEY, KEY_CMD); add SOPS_AGE_KEY as a convenient alternative.
            string[] sopsEnvVars = { "SOPS_AGE_KEY_FILE", "SOPS_AGE_KEY",
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

// --- Phase 8: 1Password CLI Provider ---

internal sealed class OnePasswordProvider : ISecretProvider
{
    public string Name => "1password";

    // Envelope: { provider, version, createdAt, targetName (vault|itemId|field) }
    // The actual secret value is fetched via the 1Password CLI (op) at launch time.

    private static readonly string OP_BINARY = FindOpBinary();

    private static string FindOpBinary()
    {
        string envPath = Environment.GetEnvironmentVariable("OP_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        string[] searchDirs = (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string dir in searchDirs)
        {
            string candidate = Path.Combine(dir.Trim('"'), "op.exe");
            if (File.Exists(candidate)) return candidate;
            candidate = Path.Combine(dir.Trim('"'), "op");
            if (File.Exists(candidate)) return candidate;
        }

        string[] commonPaths = {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "1Password CLI", "op.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "1Password CLI", "op.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "1Password CLI", "op.exe")
        };
        foreach (string p in commonPaths)
            if (File.Exists(p)) return p;

        return "op";
    }

    private static void EnsureOpAvailable()
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OP_BINARY,
            Arguments = "--version",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        try
        {
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc == null) throw new InvalidOperationException("1Password CLI (op) binary not found");
            proc.WaitForExit(5000);
            if (!proc.HasExited || proc.ExitCode != 0)
                throw new InvalidOperationException("1Password CLI (op) binary not functional");
            // v0.9.13 Phase 4F: record provider binary hash for tamper detection
            try { Program.RecordProviderHash("op", OP_BINARY); } catch { }
        }
        catch (System.ComponentModel.Win32Exception)
        {
            throw new InvalidOperationException(
                "1Password CLI (op) not found. Install op and ensure it is on PATH, or set OP_PATH env var.");
        }
    }

    public string Encrypt(string plaintext, string? context = null)
    {
        EnsureOpAvailable();
        string vaultName = Environment.GetEnvironmentVariable("OP_VAULT") ?? "EnvManager";
        string itemName = context != null
            ? context.Split('/')[0]
            : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OP_BINARY,
            Arguments = "item create --category=Password --title=" + ShellQuote(itemName) + " --vault=" + ShellQuote(vaultName) + " password=" + ShellQuote(plaintext ?? "") + " --format=json",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        string[] opEnvVars = { "OP_ACCOUNT", "OP_SERVICE_ACCOUNT_TOKEN", "OP_ACCESS_TOKEN" };
        foreach (var envVar in opEnvVars)
        {
            var v = Environment.GetEnvironmentVariable(envVar);
            if (v != null) psi.EnvironmentVariables[envVar] = v;
        }

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start op process");
        proc.WaitForExit(30000);
        if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("1Password CLI timed out"); }
        if (proc.ExitCode != 0)
        {
            string stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException("1Password CLI create failed (exit " + proc.ExitCode + "): " + stderr);
        }

        string json = proc.StandardOutput.ReadToEnd();
        string itemId = "";
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("id", out var id)) itemId = id.GetString() ?? "";
        }
        catch { }

        var env = new SecretEnvelope
        {
            Provider = Name,
            Version = 1,
            CreatedAt = DateTimeOffset.UtcNow.ToString("O"),
            TargetName = vaultName + "|" + itemId + "|password"
        };
        return env.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope)
            ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name) throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName)) throw new InvalidOperationException("Missing targetName in envelope");

        EnsureOpAvailable();
        var parts = parsed.TargetName.Split('|');
        if (parts.Length < 2) throw new InvalidOperationException("Invalid targetName format, expected vault|itemId|field");

        string itemId = parts[1];
        string fieldName = parts.Length > 2 ? parts[2] : "password";

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = OP_BINARY,
            Arguments = "item get " + ShellQuote(itemId) + " --field " + ShellQuote(fieldName) + " --reveal",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        string[] opEnvVars = { "OP_ACCOUNT", "OP_SERVICE_ACCOUNT_TOKEN", "OP_ACCESS_TOKEN" };
        foreach (var envVar in opEnvVars)
        {
            var v = Environment.GetEnvironmentVariable(envVar);
            if (v != null) psi.EnvironmentVariables[envVar] = v;
        }

        using var proc = System.Diagnostics.Process.Start(psi);
        if (proc == null) throw new InvalidOperationException("Failed to start op process");
        proc.WaitForExit(30000);
        if (!proc.HasExited) { proc.Kill(); throw new InvalidOperationException("1Password CLI timed out"); }
        if (proc.ExitCode != 0)
        {
            string stderr = proc.StandardError.ReadToEnd();
            throw new InvalidOperationException("1Password CLI get failed (exit " + proc.ExitCode + "): " + stderr);
        }
        return proc.StandardOutput.ReadToEnd().TrimEnd();
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed == null || string.IsNullOrEmpty(parsed.TargetName)) return;
        try
        {
            EnsureOpAvailable();
            var parts = parsed.TargetName.Split('|');
            if (parts.Length < 2) return;
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = OP_BINARY,
                Arguments = "item delete " + ShellQuote(parts[1]) + " --archive",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc != null) proc.WaitForExit(15000);
        }
        catch { }
    }

    public bool CanRotate => true;
    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        Delete(oldEnvelope, context);
        return Encrypt(plaintext, context);
    }

    private static string ShellQuote(string s)
    {
        // Use double-quote wrapping with internal quote doubling
        return "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
    }
}

// --- Phase 9: AWS Secrets Manager Provider ---

internal sealed class AwsSecretsManagerProvider : ISecretProvider
{
    public string Name => "aws-secretsmanager";

    // Envelope: { provider, version, createdAt, targetName (region|secretId) }
    // Uses AWS SigV4 signed REST API calls. TLS mandatory (HTTPS only).

    private const string SERVICE = "secretsmanager";
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    public string Encrypt(string plaintext, string? context = null)
    {
        if (plaintext == null) plaintext = "";
        string region = Environment.GetEnvironmentVariable("AWS_REGION")
            ?? Environment.GetEnvironmentVariable("AWS_DEFAULT_REGION")
            ?? throw new InvalidOperationException("AWS_REGION or AWS_DEFAULT_REGION not set");
        string secretId = context != null ? SanitizeSecretId(context) : "env-manager-" + Guid.NewGuid().ToString("N").Substring(0, 12);

        string body = "{\"Name\":\"" + JsonEscape(secretId) + "\",\"SecretString\":\"" + JsonEscape(plaintext) + "\"}";
        var response = CallAwsApi(region, "secretsmanager.CreateSecret", body);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AWS create failed ({response.StatusCode}): {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

        var env = new SecretEnvelope { Provider = Name, Version = 1, CreatedAt = DateTimeOffset.UtcNow.ToString("O"), TargetName = region + "|" + secretId };
        return env.Serialize();
    }

    public string Decrypt(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope) ?? throw new InvalidOperationException("Invalid secret envelope format");
        if (parsed.Provider != Name) throw new InvalidOperationException($"Provider mismatch: expected {Name}, got {parsed.Provider}");
        if (string.IsNullOrEmpty(parsed.TargetName)) throw new InvalidOperationException("Missing targetName");

        var parts = parsed.TargetName.Split('|');
        if (parts.Length < 2) throw new InvalidOperationException("Invalid targetName format");
        string region = parts[0];
        string secretId = parts[1];

        string body = "{\"SecretId\":\"" + JsonEscape(secretId) + "\"}";
        var response = CallAwsApi(region, "secretsmanager.GetSecretValue", body);
        if (!response.IsSuccessStatusCode) throw new InvalidOperationException($"AWS read failed ({response.StatusCode}): {response.Content.ReadAsStringAsync().GetAwaiter().GetResult()}");

        string json = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("SecretString", out var val)) return val.GetString() ?? "";
        throw new InvalidOperationException("AWS response does not contain SecretString");
    }

    public void Delete(string envelope, string? context = null)
    {
        var parsed = SecretEnvelope.TryParse(envelope);
        if (parsed == null || string.IsNullOrEmpty(parsed.TargetName)) return;
        try
        {
            var parts = parsed.TargetName.Split('|');
            if (parts.Length < 2) return;
            string body = "{\"SecretId\":\"" + JsonEscape(parts[1]) + "\",\"ForceDeleteWithoutRecovery\":true}";
            CallAwsApi(parts[0], "secretsmanager.DeleteSecret", body);
        }
        catch { }
    }

    public bool CanRotate => true;
    public string Rotate(string oldEnvelope, string? context = null)
    {
        string plaintext = Decrypt(oldEnvelope, context);
        var parsed = SecretEnvelope.TryParse(oldEnvelope);
        if (parsed == null || string.IsNullOrEmpty(parsed.TargetName)) return Encrypt(plaintext, context);
        var parts = parsed.TargetName.Split('|');
        if (parts.Length < 2) return Encrypt(plaintext, context);
        string body = "{\"SecretId\":\"" + JsonEscape(parts[1]) + "\",\"SecretString\":\"" + JsonEscape(plaintext) + "\"}";
        var resp = CallAwsApi(parts[0], "secretsmanager.PutSecretValue", body);
        if (!resp.IsSuccessStatusCode) throw new InvalidOperationException("AWS rotation (PutSecretValue) failed");
        return oldEnvelope;
    }

    private static System.Net.Http.HttpResponseMessage CallAwsApi(string region, string target, string body)
    {
        string host = "secretsmanager." + region + ".amazonaws.com";
        string accessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID") ?? "";
        string secretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY") ?? "";
        string sessionToken = Environment.GetEnvironmentVariable("AWS_SESSION_TOKEN") ?? "";
        if (string.IsNullOrEmpty(accessKey) || string.IsNullOrEmpty(secretKey))
            throw new InvalidOperationException("AWS_ACCESS_KEY_ID and AWS_SECRET_ACCESS_KEY required");

        string amzDate = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssZ");
        string dateStamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd");
        string credentialScope = dateStamp + "/" + region + "/" + SERVICE + "/aws4_request";

        string canonicalHeaders = "content-type:application/x-amz-json-1.1\nhost:" + host + "\nx-amz-date:" + amzDate + "\n" +
            (!string.IsNullOrEmpty(sessionToken) ? "x-amz-security-token:" + sessionToken + "\n" : "");
        string signedHeaders = "content-type;host;x-amz-date" + (!string.IsNullOrEmpty(sessionToken) ? ";x-amz-security-token" : "");
        string payloadHash = HexSHA256(body);
        string canonicalRequest = "POST\n/\n\n" + canonicalHeaders + "\n" + signedHeaders + "\n" + payloadHash;
        string stringToSign = "AWS4-HMAC-SHA256\n" + amzDate + "\n" + credentialScope + "\n" + HexSHA256(canonicalRequest);

        byte[] kDate = HmacSHA256(Encoding.UTF8.GetBytes("AWS4" + secretKey), dateStamp);
        byte[] kRegion = HmacSHA256(kDate, region);
        byte[] kService = HmacSHA256(kRegion, SERVICE);
        byte[] kSigning = HmacSHA256(kService, "aws4_request");
        byte[] signature = HmacSHA256(kSigning, stringToSign);
        string auth = "AWS4-HMAC-SHA256 Credential=" + accessKey + "/" + credentialScope + ", SignedHeaders=" + signedHeaders + ", Signature=" + BytesToHex(signature);

        using var client = new System.Net.Http.HttpClient();
        client.Timeout = Timeout;
        var content = new System.Net.Http.StringContent(body, Encoding.UTF8, "application/x-amz-json-1.1");
        // Per AWS SigV4: Authorization, X-Amz-Target, X-Amz-Date, X-Amz-Security-Token
        // are REQUEST headers, not content headers. Adding "Authorization" to
        // HttpContent throws "Misused header name, 'Authorization'" because the
        // .NET dispatcher treats it as a content header.
        var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, "https://" + host + "/");
        request.Content = content;
        request.Headers.Add("X-Amz-Target", target);
        request.Headers.Add("X-Amz-Date", amzDate);
        request.Headers.Add("Authorization", auth);
        if (!string.IsNullOrEmpty(sessionToken)) request.Headers.Add("X-Amz-Security-Token", sessionToken);
        return client.SendAsync(request).GetAwaiter().GetResult();
    }

    private static string HexSHA256(string s)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        return BytesToHex(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
    }

    private static byte[] HmacSHA256(byte[] key, string data) => new System.Security.Cryptography.HMACSHA256(key).ComputeHash(Encoding.UTF8.GetBytes(data));
    private static byte[] HmacSHA256(byte[] key, byte[] data) => new System.Security.Cryptography.HMACSHA256(key).ComputeHash(data);

    private static string BytesToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (byte b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string SanitizeSecretId(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) { if (char.IsLetterOrDigit(c) || "/_+=.@-".IndexOf(c) >= 0) sb.Append(c); else sb.Append('-'); }
        string r = sb.ToString();
        return r.Length > 512 ? r.Substring(0, 512) : r;
    }

    private static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s) {
            switch (c) {
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
        ["azure-keyvault"] = new AzureKeyVaultProvider(),
        ["1password"] = new OnePasswordProvider(),
        ["aws-secretsmanager"] = new AwsSecretsManagerProvider()
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
        if (!_providers.TryGetValue(name, out var provider))
            throw new InvalidOperationException($"Unknown secret provider: {name}");

        // v0.7.5: probe the provider with a sentinel Encrypt/Decrypt round-trip
        // before committing it as the active provider. A provider that cannot
        // complete the round-trip (pwsh missing module, Vault no VAULT_ADDR,
        // cloud credentials missing) is REJECTED here so the user gets an
        // actionable error at config time instead of a CLIXML catastrophe at
        // add-secret time. This matches the PowerToys pattern of validating
        // extension dependencies at config time, not at use time.
        try
        {
            // Use a truly off-name sentinel so a real profile variable named
            // "__compat_probe__" never collides. Delete is best-effort because
            // some providers are pure-local and others have side effects.
            const string probeContext = "__env_manager_compat_probe__";
            string envelope = provider.Encrypt("__probe_value__", probeContext);
            try { provider.Decrypt(envelope, probeContext); } catch { /* async/network providers may not round-trip immediately */ }
            try { provider.Delete(envelope, probeContext); } catch { /* best-effort cleanup */ }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Cannot activate provider '" + name + "': " + ex.Message +
                ". Fix the provider environment first (e.g. install pwsh modules, " +
                "set VAULT_ADDR, or configure cloud credentials).");
        }

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
