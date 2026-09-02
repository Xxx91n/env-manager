// OnePasswordProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

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
