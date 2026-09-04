// L1ToolProvisioner.cs - CLI tool discovery/provisioning for local L1 backends (issue 15)
// License: Apache-2.0
//
// Some L1 backends need a local CLI binary rather than a container:
//   - 1Password: the real op CLI (v2.39.0, pinned). Downloaded once per OS temp
//     session when not already discoverable (OP_PATH / PATH), because there is no
//     offline 1Password backend; the binary talks to the in-process Connect mock.
//   - sops (3.13.3, pinned) + age (1.3.2, pinned): download both when the host lacks
//     them; generate a throwaway age keypair per session.
//   - pwsh SecretManagement/SecretStore: use the host's modules when installed;
//     configure the official non-interactive automation mode (Authentication=None).
//
// Everything is cached under a single session root in the OS temp dir (never inside
// the repo; .scratch/ and OS temp are outside version control) and cleaned up by the
// OS, matching the temp-script hygiene rules.

using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace EnvManager.Engine.Tests;

internal static class L1ToolProvisioner
{
    // ---- pinned tool versions (verified live 2026-09-03) ----

    internal const string OpVersion = "2.39.0";
    internal const string SopsVersion = "3.13.3";
    internal const string AgeVersion = "1.3.2";

    private static readonly string SessionRoot =
        Path.Combine(Path.GetTempPath(), "env-manager-l1-matrix");

    private static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);

    private static bool IsLinux => RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

    // ---------------- op CLI ----------------

    /// <summary>Discovery only: host OP_PATH/PATH or an already-cached download. Never touches the network.</summary>
    internal static string? TryGetOpBinary()
    {
        var existing = FindOnPath("op");
        if (existing is not null) return existing;
        var cached = Path.Combine(SessionRoot, "op");
        var cachedPath = IsWindows ? Path.Combine(cached, "op.exe") : Path.Combine(cached, "op");
        return File.Exists(cachedPath) ? cachedPath : null;
    }

    /// <summary>Discovery + pinned-release download into the session cache (network).</summary>
    internal static string? EnsureOpBinary()
    {
        var existing = TryGetOpBinary();
        if (existing is not null) return existing;
        if (!(IsWindows || IsLinux)) return null;
        var cached = Path.Combine(SessionRoot, "op");
        var cachedPath = IsWindows ? Path.Combine(cached, "op.exe") : Path.Combine(cached, "op");
        try
        {
            Directory.CreateDirectory(cached);
            var url = IsWindows
                ? $"https://cache.agilebits.com/dist/1P/op2/pkg/v{OpVersion}/op_windows_amd64_v{OpVersion}.zip"
                : $"https://cache.agilebits.com/dist/1P/op2/pkg/v{OpVersion}/op_linux_amd64_v{OpVersion}.zip";
            var zipPath = Path.Combine(cached, "op.zip");
            Download(url, zipPath);
            ZipFile.ExtractToDirectory(zipPath, cached, overwriteFiles: true);
            File.Delete(zipPath);
            if (!IsWindows) ChmodExecutable(cachedPath);
            return File.Exists(cachedPath) ? cachedPath : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------- sops + age ----------------

    /// <summary>Discovery only: host binaries + an existing session key file. Never touches the network.</summary>
    internal static (string Sops, string AgeKeyFile)? TryGetSopsBundle() => BuildSopsBundle(allowDownload: false);

    /// <summary>Discovery + pinned-release download of the missing binaries (network).</summary>
    internal static (string Sops, string AgeKeyFile)? EnsureSopsBundle() => BuildSopsBundle(allowDownload: true);

    private static (string Sops, string AgeKeyFile)? BuildSopsBundle(bool allowDownload)
    {
        var sopsHost = FindOnPath("sops");
        var ageHost = FindOnPath("age");
        var ageKeygenHost = FindOnPath("age-keygen");

        var bundleDir = Path.Combine(SessionRoot, "sops-age");
        var sopsPath = sopsHost ?? Path.Combine(bundleDir, IsWindows ? "sops.exe" : "sops");
        var agePath = ageHost ?? Path.Combine(bundleDir, IsWindows ? "age.exe" : "age");
        var ageKeygenPath = ageKeygenHost ?? Path.Combine(bundleDir, IsWindows ? "age-keygen.exe" : "age-keygen");
        var keyFile = Path.Combine(bundleDir, "age-keys.txt");

        try
        {
            Directory.CreateDirectory(bundleDir);

            if (sopsHost is null)
            {
                // sops 3.13.3 does not publish a windows release asset; winget/scoop
                // installs cover Windows hosts
                if (IsWindows || !allowDownload) return null;
                Download($"https://github.com/getsops/sops/releases/download/v{SopsVersion}/sops-v{SopsVersion}.linux.amd64", sopsPath);
                ChmodExecutable(sopsPath);
            }

            if (ageHost is null || ageKeygenHost is null)
            {
                if (!allowDownload) return null;
                if (IsWindows)
                {
                    var url = $"https://github.com/FiloSottile/age/releases/download/v{AgeVersion}/age-v{AgeVersion}-windows-amd64.zip";
                    var zipPath = Path.Combine(bundleDir, "age.zip");
                    Download(url, zipPath);
                    ZipFile.ExtractToDirectory(zipPath, bundleDir, overwriteFiles: true);
                    File.Delete(zipPath);
                }
                else
                {
                    var url = $"https://github.com/FiloSottile/age/releases/download/v{AgeVersion}/age-v{AgeVersion}-linux-amd64.tar.gz";
                    var tgzPath = Path.Combine(bundleDir, "age.tar.gz");
                    Download(url, tgzPath);
                    Run("tar", $"-xzf \"{tgzPath}\" -C \"{bundleDir}\"");
                    File.Delete(tgzPath);
                }
            }

            // throwaway keypair per session; SOPS_AGE_KEY_FILE format = one identity per line
            if (!File.Exists(keyFile))
            {
                var outText = Run(ageKeygenPath, $"-o \"{keyFile}\"");
                if (!File.Exists(keyFile))
                {
                    // older age-keygen prints the key; fall back to parsing stdout
                    var m = System.Text.RegularExpressions.Regex.Match(outText, "AGE-SECRET-KEY-[A-Z0-9]+");
                    if (!m.Success) return null;
                    var publicKeyLine = System.Text.RegularExpressions.Regex.Match(outText, "age1[A-Za-z0-9]+");
                    File.WriteAllText(keyFile, m.Value + (publicKeyLine.Success ? Environment.NewLine + "# public key: " + publicKeyLine.Value : ""), new UTF8Encoding(false));
                }
            }

            return File.Exists(sopsPath) && File.Exists(keyFile) ? (sopsPath, keyFile) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------------- pwsh SecretStore ----------------

    internal static bool IsSecretStoreAvailable()
    {
        if (FindOnPath("pwsh") is null) return false;
        var probe = Run("pwsh", "-NoProfile -NonInteractive -Command \"if (Get-Module -ListAvailable *SecretManagement*) { 'OK' } else { 'MISSING' }\"");
        return probe.Contains("OK");
    }

    /// <summary>
    /// Idempotently registers the EnvManager SecretStore vault in the official
    /// non-interactive automation mode (Set-SecretStoreConfiguration
    /// -Authentication None -Interaction None; documented CI pattern - no password
    /// prompt, no stored password). Registered vaults persist per user, so a second
    /// registration attempt is tolerated (exit code ignored, probe decides).
    /// </summary>
    internal static bool TryRegisterSecretStoreVault()
    {
        if (!IsSecretStoreAvailable()) return false;
        // One-session setup: register the vault if absent, force the official
        // non-interactive store config (Authentication=None), then verify with a REAL
        // Set/Get round-trip. The CI Linux lane proved the old probe could short-circuit
        // on "vault registered" while the store itself still demanded a password, so the
        // round-trip is now the only success signal; on failure the pwsh transcript is
        // embedded in the returned reason for the skip message.
        var script =
            "$ErrorActionPreference='Stop'; " +
            "try { $null = Get-SecretVault -Name EnvManager -ErrorAction Stop } " +
            "catch { Register-SecretVault -Name EnvManager -ModuleName Microsoft.PowerShell.SecretStore -AllowClobber | Out-Null } " +
            "Set-SecretStoreConfiguration -Authentication None -Interaction None -PasswordTimeout 86400 -Confirm:$false; " +
            "Set-Secret -Name em-l1-probe -Secret (ConvertTo-SecureString 'probe' -AsPlainText -Force) -Vault EnvManager; " +
            "$g = Get-Secret -Name em-l1-probe -Vault EnvManager -AsPlainText; " +
            "Remove-Secret -Name em-l1-probe -Vault EnvManager; " +
            "if ($g -eq 'probe') { 'L1STORE-OK' } else { throw \"roundtrip mismatch: $g\" }";
        var result = Run("pwsh", "-NoProfile -NonInteractive -Command \"" + script.Replace("\"", "'") + "\"");
        if (result.Contains("L1STORE-OK")) return true;
        Console.Error.WriteLine("[L1] SecretStore setup transcript: " + result);
        return false;
    }

    // ---------------- helpers ----------------

    private static string? FindOnPath(string binary)
    {
        var envVar = IsWindows ? "OP_PATH" : null;
        if (binary == "op" && envVar is not null)
        {
            var forced = Environment.GetEnvironmentVariable(envVar);
            if (!string.IsNullOrEmpty(forced) && File.Exists(forced)) return forced;
        }
        if (binary == "sops")
        {
            var forced = Environment.GetEnvironmentVariable("SOPS_PATH");
            if (!string.IsNullOrEmpty(forced) && File.Exists(forced)) return forced;
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim('"'), IsWindows ? binary + ".exe" : binary);
                if (File.Exists(candidate)) return candidate;
            }
            catch (Exception) { }
        }
        return null;
    }

    private static void Download(string url, string targetPath)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("env-manager-l1-matrix");
        using var response = client.SendAsync(request).GetAwaiter().GetResult();
        response.EnsureSuccessStatusCode();
        using var fs = File.Create(targetPath);
        response.Content.CopyToAsync(fs).GetAwaiter().GetResult();
    }

    private static string Run(string fileName, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi);
            if (proc is null) return "";
            var stdout = proc.StandardOutput.ReadToEnd();
            var stderr = proc.StandardError.ReadToEnd();
            proc.WaitForExit(120000);
            return stdout + stderr;
        }
        catch (Exception ex)
        {
            return ex.GetType().Name;
        }
    }

    private static void ChmodExecutable(string path)
    {
        if (IsWindows) return;
        try { Run("chmod", $"+x \"{path}\""); } catch (Exception) { }
    }
}
