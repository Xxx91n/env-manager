// L1Harnesses.cs - neutral L1 backend harnesses (issue 15, architecture-recovery)
// License: Apache-2.0
//
// Per-provider ISecretProviderHarness implementations whose Seed/ReadRaw go straight
// to the L1 backend (emulator container, localhost Connect mock, or local CLI
// primitive), never through the provider's own Encrypt/Decrypt - the same neutrality
// rule the ticket-10 DPAPI harness established.

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using DotNet.Testcontainers.Containers;
using Testcontainers.LocalStack;
using Testcontainers.LowkeyVault;

namespace EnvManager.Engine.Tests;

// ---------------- vault-kv2 ----------------

internal sealed class VaultKv2L1Harness(string vaultAddr, string vaultToken) : ISecretProviderHarness
{
    public ISecretProvider CreateProvider() => new VaultKV2Provider();

    public string SeedSecret(string plaintext, string context)
    {
        var path = "env-manager/" + Sanitize(context);
        var body = "{\"data\":{\"value\":\"" + JsonEscape(plaintext) + "\"}}";
        VaultHttp("POST", "/v1/secret/data/" + path, body);
        return new SecretEnvelope { Provider = "vault-kv2", Version = 1, TargetName = "secret/" + path + ":value" }.Serialize();
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        var target = parsed.TargetName!; // "secret/<path>:value"
        var mountAndKey = target.Split(':');
        var mountAndPath = mountAndKey[0].Split('/', 2);
        var json = VaultHttp("GET", $"/v1/{mountAndPath[0]}/data/{mountAndPath[1]}", null);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("data").GetProperty("data").GetProperty(mountAndKey.Length > 1 ? mountAndKey[1] : "value").GetString() ?? "";
    }

    private string VaultHttp(string method, string path, string? body)
    {
        using var client = new HttpClient();
        using var req = new HttpRequestMessage(new HttpMethod(method), vaultAddr.TrimEnd('/') + path);
        req.Headers.Add("X-Vault-Token", vaultToken);
        if (body is not null)
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        using var res = client.SendAsync(req).GetAwaiter().GetResult();
        var content = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"L1 vault harness {method} {path} failed ({res.StatusCode}): {content}");
        }
        return content;
    }

    internal static string Sanitize(string s) => s.Replace("\\", "/").Replace(":", "_").Replace(" ", "_");

    internal static string JsonEscape(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\""); break;
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

// ---------------- aws-secretsmanager ----------------

internal sealed class AwsSecretsManagerL1Harness(LocalStackContainer container) : ISecretProviderHarness
{
    private const string Region = "us-east-1";

    public ISecretProvider CreateProvider() => new AwsSecretsManagerProvider();

    public string SeedSecret(string plaintext, string context)
    {
        var secretId = Sanitize(context);
        var create = "{\"Name\":\"" + VaultKv2L1Harness.JsonEscape(secretId) + "\",\"SecretString\":\"" + VaultKv2L1Harness.JsonEscape(plaintext) + "\"}";
        var res = Aws("secretsmanager.CreateSecret", create);
        if (!res.Success)
        {
            // provider.Encrypt already created this secret id -> update instead (still a
            // neutral backend write; the provider never issues PutSecretValue on Encrypt)
            var put = "{\"SecretId\":\"" + VaultKv2L1Harness.JsonEscape(secretId) + "\",\"SecretString\":\"" + VaultKv2L1Harness.JsonEscape(plaintext) + "\",\"ClientRequestToken\":\"" + Guid.NewGuid().ToString("N") + "\"}";
            res = Aws("secretsmanager.PutSecretValue", put);
            if (!res.Success)
            {
                throw new InvalidOperationException("L1 aws harness seed failed: " + res.Body);
            }
        }
        return new SecretEnvelope { Provider = "aws-secretsmanager", Version = 1, TargetName = Region + "|" + secretId }.Serialize();
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        var secretId = parsed.TargetName!.Split('|')[1];
        var body = "{\"SecretId\":\"" + VaultKv2L1Harness.JsonEscape(secretId) + "\"}";
        var res = Aws("secretsmanager.GetSecretValue", body);
        if (!res.Success)
        {
            throw new InvalidOperationException("L1 aws harness read failed: " + res.Body);
        }
        using var doc = JsonDocument.Parse(res.Body);
        return doc.RootElement.GetProperty("SecretString").GetString() ?? "";
    }

    private (bool Success, string Body) Aws(string target, string body)
    {
        using var client = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, container.GetConnectionString().TrimEnd('/') + "/");
        req.Headers.Add("X-Amz-Target", target);
        req.Content = new StringContent(body, Encoding.UTF8, "application/x-amz-json-1.1");
        using var res = client.SendAsync(req).GetAwaiter().GetResult();
        return (res.IsSuccessStatusCode, res.Content.ReadAsStringAsync().GetAwaiter().GetResult());
    }

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || "/_+=.@-".IndexOf(c) >= 0) sb.Append(c); else sb.Append('-');
        }
        return sb.ToString();
    }
}

// ---------------- azure-keyvault ----------------

internal sealed class AzureKeyVaultL1Harness(LowkeyVaultContainer container) : ISecretProviderHarness
{
    private const string ApiVersion = "7.4";

    public ISecretProvider CreateProvider() => new AzureKeyVaultProvider();

    public string SeedSecret(string plaintext, string context)
    {
        var name = Sanitize(context);
        Kv("PUT", "/secrets/" + name + "?api-version=" + ApiVersion,
            "{\"value\":\"" + VaultKv2L1Harness.JsonEscape(plaintext) + "\"}");
        return new SecretEnvelope { Provider = "azure-keyvault", Version = 1, TargetName = BaseAddress + "|" + name }.Serialize();
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        var name = parsed.TargetName!.Split('|')[1];
        var body = Kv("GET", "/secrets/" + name + "?api-version=" + ApiVersion, null);
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.GetProperty("value").GetString() ?? "";
    }

    private string BaseAddress => container.GetBaseAddress();

    internal static string Sanitize(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s)
        {
            if (char.IsLetterOrDigit(c) || c == '-') sb.Append(c); else sb.Append('-');
        }
        var result = sb.ToString().Trim('-');
        return result.Length > 127 ? result.Substring(0, 127) : result;
    }

    private string Kv(string method, string path, string? body)
    {
        var token = GetToken();
        using var handler = new HttpClientHandler
        {
            // Lowkey Vault uses a self-signed CA; the provider relies on OS trust
            // (set up by the fixture), the harness may accept it directly.
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true,
        };
        using var client = new HttpClient(handler);
        using var req = new HttpRequestMessage(new HttpMethod(method), BaseAddress.TrimEnd('/') + path);
        req.Headers.Add("Authorization", "Bearer " + token);
        if (body is not null)
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        using var res = client.SendAsync(req).GetAwaiter().GetResult();
        var content = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"L1 azure harness {method} {path} failed ({res.StatusCode}): {content}");
        }
        return content;
    }

    private string GetToken()
    {
        using var client = new HttpClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, container.GetAuthTokenUrl() + "?api-version=2018-02-01&resource=https://vault.azure.net");
        req.Headers.Add("Metadata", "true");
        using var res = client.SendAsync(req).GetAwaiter().GetResult();
        var content = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException("L1 azure harness token request failed: " + content);
        }
        using var doc = JsonDocument.Parse(content);
        return doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("L1 azure harness token response has no access_token");
    }
}

// ---------------- credential-manager (Windows local backend) ----------------

internal sealed class CredentialManagerL1Harness : ISecretProviderHarness
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistEnterprise = 3;

    public ISecretProvider CreateProvider() => new CredentialManagerProvider();

    public string SeedSecret(string plaintext, string context)
    {
        // Neutral write: DPAPI-encrypt with the trusted helper primitive, then CredWriteW
        // directly - NOT through the provider's Encrypt.
        WriteCredential(Target(context), DpapiHelper.EncryptSecret(plaintext));
        return new SecretEnvelope { Provider = "credential-manager", Version = 1, TargetName = Target(context) }.Serialize();
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        return DpapiHelper.DecryptSecret(ReadCredential(parsed.TargetName!));
    }

    internal static string Target(string context) => "EnvManager\\" + context.Replace("\\", "_").Replace("/", "_");

    private static void WriteCredential(string targetName, string dpapiCipherBase64)
    {
        var credBlob = Encoding.UTF8.GetBytes(dpapiCipherBase64);
        var cred = new CredentialManagerL1Cred
        {
            Type = CredTypeGeneric,
            TargetName = targetName,
            Persist = CredPersistEnterprise,
            CredentialBlobSize = credBlob.Length,
            CredentialBlob = System.Runtime.InteropServices.Marshal.AllocHGlobal(credBlob.Length),
            UserName = Environment.UserName,
        };
        try
        {
            System.Runtime.InteropServices.Marshal.Copy(credBlob, 0, cred.CredentialBlob, credBlob.Length);
            if (!CredWriteW(ref cred, 0))
            {
                throw new System.ComponentModel.Win32Exception(
                    System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "L1 harness CredWriteW failed");
            }
        }
        finally
        {
            if (cred.CredentialBlob != System.IntPtr.Zero)
            {
                System.Runtime.InteropServices.Marshal.FreeHGlobal(cred.CredentialBlob);
            }
        }
    }

    private static string ReadCredential(string targetName)
    {
        if (!CredReadW(targetName, CredTypeGeneric, 0, out var credPtr))
        {
            throw new System.ComponentModel.Win32Exception(
                System.Runtime.InteropServices.Marshal.GetLastWin32Error(), "L1 harness CredReadW failed for " + targetName);
        }
        try
        {
            var cred = (CredentialManagerL1Cred)System.Runtime.InteropServices.Marshal.PtrToStructure(credPtr, typeof(CredentialManagerL1Cred))!;
            var blob = new byte[cred.CredentialBlobSize];
            System.Runtime.InteropServices.Marshal.Copy(cred.CredentialBlob, blob, 0, cred.CredentialBlobSize);
            return Encoding.UTF8.GetString(blob);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    internal static void DeleteCredential(string targetName) => CredDeleteW(targetName, CredTypeGeneric, 0);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct CredentialManagerL1Cred
    {
        public int Flags;
        public int Type;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] public string TargetName;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public System.IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public System.IntPtr Attributes;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] public string? TargetAlias;
        [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.LPWStr)] public string? UserName;
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CredWriteW(ref CredentialManagerL1Cred cred, int flags);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CredReadW(string target, int type, int flags, out System.IntPtr credential);

    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool CredDeleteW(string target, int type, int flags);

    [System.Runtime.InteropServices.DllImport("advapi32.dll")]
    private static extern void CredFree(System.IntPtr cred);
}

// ---------------- powershell-secretmanagement ----------------

internal sealed class PowerShellSecretManagementL1Harness : ISecretProviderHarness
{
    private const string VaultName = "EnvManager";

    public ISecretProvider CreateProvider() => new PowerShellSecretManagementProvider();

    public string SeedSecret(string plaintext, string context)
    {
        var secretName = SecretName(context);
        var script =
            "$ErrorActionPreference='Stop'; " +
            "Set-Secret -Name '" + Escape(secretName) + "' " +
            "-Secret (ConvertTo-SecureString '" + Escape(plaintext) + "' -AsPlainText -Force) " +
            "-Vault '" + Escape(VaultName) + "'; 'OK'";
        RunPwsh(script);
        return new SecretEnvelope { Provider = "powershell-secretmanagement", Version = 1, TargetName = VaultName + "\\" + secretName }.Serialize();
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        var parts = parsed.TargetName!.Split('\\');
        var script =
            "$ErrorActionPreference='Stop'; " +
            "Get-Secret -Name '" + Escape(parts[^1]) + "' -Vault '" + Escape(parts[0]) + "' -AsPlainText";
        return RunPwsh(script).TrimEnd();
    }

    internal static string SecretName(string context) => "EnvManager_" + context.Replace("\\", "_").Replace("/", "_").Replace(":", "_");

    internal static string Escape(string s) => s.Replace("'", "''");

    internal static string RunPwsh(string script)
    {
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "pwsh",
            Arguments = "-NoProfile -NonInteractive -EncodedCommand " + encoded,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("L1 harness: failed to start pwsh");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60000);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException("L1 harness pwsh failed (" + proc.ExitCode + "): " + stderr);
        }
        return stdout;
    }
}

// ---------------- 1password (real op CLI against localhost Connect mock) ----------------

internal sealed class OnePasswordL1Harness(string opBinary, OpConnectMock mock) : ISecretProviderHarness
{
    private const string VaultName = "EnvManager";

    private const string VaultId = "01980000-0000-7000-8000-000000000001";

    public ISecretProvider CreateProvider() => new OnePasswordProvider();

    public string SeedSecret(string plaintext, string context)
    {
        // Neutral write: seed the item straight into the Connect mock via POST
        // (op item create is refused by the op CLI over Connect, live-verified
        // v2.39.0), then let the provider's real Decrypt read it back.
        var itemName = context.Split('/')[0];
        var body = JsonSerializer.Serialize(new
        {
            title = itemName,
            category = "PASSWORD",
            fields = new[] { new { type = "CONCEALED", purpose = "PASSWORD", label = "password", value = plaintext } },
        });
        var json = Connect("POST", $"/v1/vaults/{VaultId}/items", body);
        using var doc = JsonDocument.Parse(json);
        var itemId = doc.RootElement.GetProperty("id").GetString()
            ?? throw new InvalidOperationException("L1 op harness: create response has no id");
        return new SecretEnvelope { Provider = "1password", Version = 1, TargetName = VaultName + "|" + itemId + "|password" }.Serialize();
    }

    private string Connect(string method, string path, string? body)
    {
        using var client = new HttpClient();
        using var req = new HttpRequestMessage(new HttpMethod(method), mock.ConnectHost.TrimEnd('/') + path);
        req.Headers.Add("Authorization", "Bearer " + mock.Token);
        if (body is not null)
        {
            req.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }
        using var res = client.SendAsync(req).GetAwaiter().GetResult();
        var content = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!res.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"L1 op harness Connect {method} {path} failed ({res.StatusCode}): {content}");
        }
        return content;
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        var parts = parsed.TargetName!.Split('|');
        return Op("item get " + Quote(parts[1]) + " --field " + Quote(parts.Length > 2 ? parts[2] : "password") + " --reveal").TrimEnd();
    }

    private string Op(string args)
    {
        // L1 diagnostics: log op's exact stdout/stderr for the smoke's own invocations
        var result = OpRaw(args);
        System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "env-manager-l1-opmock.log"),
            "[harness] op " + args + " => " + result.Replace("\n", " | ")[..Math.Min(300, result.Length)] + Environment.NewLine);
        return result;
    }

    private string OpRaw(string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = opBinary,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        psi.EnvironmentVariables["OP_CONNECT_HOST"] = mock.ConnectHost;
        psi.EnvironmentVariables["OP_CONNECT_TOKEN"] = mock.Token;
        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("L1 harness: failed to start op");
        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit(60000);
        if (proc.ExitCode != 0)
        {
            throw new InvalidOperationException("L1 harness op failed (" + proc.ExitCode + "): " + stderr);
        }
        return stdout;
    }

    internal string RunProviderArgs(string[] parts)
    {
        var itemId = parts[1];
        var fieldName = parts.Length > 2 ? parts[2] : "password";
        return OpRaw("item get " + Quote(itemId) + " --field " + Quote(fieldName) + " --vault=" + Quote(parts[0]) + " --format=json --reveal");
    }

    internal string RunProviderArgsDebug(string[] parts)
    {
        var itemId = parts[1];
        var fieldName = parts.Length > 2 ? parts[2] : "password";
        // --debug makes op print its own request log to stderr
        return OpRaw("item get " + Quote(itemId) + " --field " + Quote(fieldName) + " --vault=" + Quote(parts[0]) + " --format=json --reveal --debug");
    }

    private static string Quote(string s) => "\"" + (s ?? "").Replace("\"", "\\\"") + "\"";
}

// ---------------- sops + age ----------------

internal sealed class SopsL1Harness(string sopsBinary, string ageKeyFile) : ISecretProviderHarness
{
    public ISecretProvider CreateProvider() => new SopsProvider();

    public string SeedSecret(string plaintext, string context)
    {
        // Neutral write: sops -e run directly on {"value": ...} - the provider's Encrypt
        // is never involved.
        var ciphertext = RunSops("-e", "{\"value\":\"" + VaultKv2L1Harness.JsonEscape(plaintext) + "\"}");
        return new SecretEnvelope { Provider = "sops", Version = 1, Ciphertext = ciphertext }.Serialize();
    }

    public string ReadRawSecret(string sutEnvelope, string context)
    {
        var parsed = SecretEnvelope.TryParse(sutEnvelope)
            ?? throw new InvalidOperationException("harness: provider emitted a non-envelope");
        var decrypted = RunSops("-d", parsed.Ciphertext!);
        using var doc = JsonDocument.Parse(decrypted);
        return doc.RootElement.GetProperty("value").GetString() ?? "";
    }

    private string RunSops(string flag, string json)
    {
        var dir = Path.Combine(Path.GetTempPath(), "env-manager-l1-sops-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var inFile = Path.Combine(dir, "in.json");
            var outFile = Path.Combine(dir, "out.json");
            File.WriteAllText(inFile, json, new UTF8Encoding(false));
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = sopsBinary,
                Arguments = flag + " --output \"" + outFile + "\" \"" + inFile + "\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            psi.EnvironmentVariables["SOPS_AGE_KEY_FILE"] = ageKeyFile;
            var recipients = ExtractPublicKey(ageKeyFile);
            if (recipients is not null)
            {
                psi.EnvironmentVariables["SOPS_AGE_RECIPIENTS"] = recipients;
            }
            using var proc = System.Diagnostics.Process.Start(psi)
                ?? throw new InvalidOperationException("L1 harness: failed to start sops");
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(60000);
            if (proc.ExitCode != 0)
            {
                throw new InvalidOperationException("L1 harness sops " + flag + " failed (" + proc.ExitCode + "): " + stderr);
            }
            return File.ReadAllText(outFile, Encoding.UTF8);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    internal static string? ExtractPublicKey(string ageKeyFile)
    {
        try
        {
            var m = Regex.Match(File.ReadAllText(ageKeyFile), "public key: (age1[A-Za-z0-9]+)");
            return m.Success ? m.Groups[1].Value : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
