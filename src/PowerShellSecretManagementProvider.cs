// PowerShellSecretManagementProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace EnvManager;

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
