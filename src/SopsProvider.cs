// SopsProvider.cs - secret provider architecture (ticket 09, architecture-recovery)
// Split from the retired single-file src/SecretProvider.cs; behavior unchanged.
// License: Apache-2.0

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

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
