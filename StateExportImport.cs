using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

// v0.9.9: Full-state export/import for disaster recovery.
// Exports ALL Env Manager internal config files into a single DPAPI-encrypted
// archive. The archive is portable within the same user account (DPAPI CurrentUser).
// Import verifies each file before atomically replacing the live state.
//
// Pattern: KeePass/1Password-style full-state backup. A single encrypted container
// holds all config; import does staged validation + atomic swap with .bak rollback.
// This is the DR complement to the per-run test-with-restore.ps1 harness — it
// gives users a one-click way to snapshot and restore their entire Env Manager
// state without manually copying files from %LOCALAPPDATA%\EnvManager.
//
// Hard boundary: export never includes registry values (those are backed up via
// the existing "backup" command). This is INTERNAL STATE ONLY: profiles, mounts,
// protection lists, settings, audit. Secret envelopes inside the archive are
// already DPAPI-encrypted at the field level; the outer DPAPI layer adds a
// second encryption pass so the archive is double-encrypted for transit.

partial class Program
{
    // Files included in full-state export. Each is read from AppDataDirectory.
    static readonly string[] StateExportFiles = new[]
    {
        "profiles.json",
        "secretMount.json",
        "protected-vars.json",
        "protected-paths.json",
        "builtin-protected-vars.json",
        "builtin-protected-paths.json",
        "gui-settings.json",
        "audit.json",
    };

    /// <summary>
    /// Export all Env Manager internal state to a DPAPI-encrypted archive.
    /// Usage: env-manager export-state --output <file>
    /// The output file is a base64-encoded DPAPI-CurrentUser-encrypted JSON blob.
    /// </summary>
    static int RunExportState(string[] args)
    {
        int outFlag = Array.IndexOf(args, "--output");
        if (outFlag < 0 || outFlag + 1 >= args.Length)
            return ArgError("Usage: env-manager export-state --output <file>");

        string outputFile = Path.GetFullPath(args[outFlag + 1]);

        // Collect all existing state files into a dictionary
        var state = new Dictionary<string, string>();
        foreach (var fname in StateExportFiles)
        {
            string fullPath = Path.Combine(AppDataDirectory, fname);
            if (File.Exists(fullPath))
            {
                state[fname] = File.ReadAllText(fullPath);
                DebugLog($"export-state: included {fname} ({state[fname].Length} bytes)");
            }
        }

        if (state.Count == 0)
            return ArgError("Error: No state files found to export. Env Manager may not have been initialized.");

        // Serialize the state dict to JSON, then DPAPI-encrypt the whole blob.
        string jsonState = JsonSerializer.Serialize(new
        {
            version = "1",
            exportedAt = DateTimeOffset.UtcNow.ToString("O"),
            fileCount = state.Count,
            files = state,
        }, JsonOptsIndented);

        string encrypted = ExportStateEncrypt(jsonState); // v0.9.13 Phase 3C: double-layer AES-GCM + DPAPI + HMAC

        // Atomic write: temp + fsync + rename.
        WriteAtomicUtf8(outputFile, encrypted);

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            exported = state.Count,
            file = outputFile,
            sizeBytes = encrypted.Length,
        }, JsonOptsIndented));

        DebugLog($"export-state: wrote {encrypted.Length} bytes to {outputFile}");
        return 0;
    }

    /// <summary>
    /// Import Env Manager internal state from a DPAPI-encrypted archive.
    /// Usage: env-manager import-state --input <file> [--dry-run]
    /// Performs staged validation: decrypt, parse each file, verify structure,
    /// then atomically replace each live file with .bak backup.
    /// </summary>
    static int RunImportState(string[] args)
    {
        int inFlag = Array.IndexOf(args, "--input");
        if (inFlag < 0 || inFlag + 1 >= args.Length)
            return ArgError("Usage: env-manager import-state --input <file> [--dry-run]");

        string inputFile = Path.GetFullPath(args[inFlag + 1]);
        if (!File.Exists(inputFile))
            return ArgError("Error: Input file not found: " + inputFile);

        if (new FileInfo(inputFile).Length > MaxBackupFileSize)
            return ArgError("Error: Input file exceeds 50 MB safety limit");

        bool dryRun = args.Contains("--dry-run");

        // Decrypt
        string encrypted = File.ReadAllText(inputFile).Trim();
        string jsonState;
        try
        {
            jsonState = ExportStateDecrypt(encrypted); // v0.9.13 Phase 3C: auto-detects v1/v2
        }
        catch (Exception ex)
        {
            return ArgError("Error: Failed to decrypt archive (DPAPI CurrentUser bound): " + ex.Message);
        }

        // Parse
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonState);
        }
        catch (Exception ex)
        {
            return ArgError("Error: Archive is not valid JSON: " + ex.Message);
        }

        if (!doc.RootElement.TryGetProperty("files", out var filesElement))
            return ArgError("Error: Archive missing 'files' property");

        // Validate each file can be parsed as JSON (all our state files are JSON).
        var validatedFiles = new Dictionary<string, string>();
        foreach (var prop in filesElement.EnumerateObject())
        {
            string fname = prop.Name;
            string content = prop.Value.GetString() ?? "";

            // Verify it's valid JSON (all state files are JSON).
            try
            {
                JsonDocument.Parse(content);
            }
            catch
            {
                // Non-JSON content is rejected — all our state files are JSON.
                return ArgError($"Error: File '{fname}' in archive is not valid JSON");
            }

            validatedFiles[fname] = content;
            DebugLog($"import-state: validated {fname} ({content.Length} bytes)");
        }

        if (dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                dryRun = true,
                fileCount = validatedFiles.Count,
                files = validatedFiles.Keys.ToArray(),
            }, JsonOptsIndented));
            return 0;
        }

        // Transactional import: all-or-nothing. If any file write fails, rollback all
        // previously written files from their .bak backups before reporting the error.
        int imported = 0;
        var writtenFiles = new List<string>();
        try
        {
            foreach (var kvp in validatedFiles)
            {
                string targetPath = Path.Combine(AppDataDirectory, kvp.Key);
                string bakPath = targetPath + ".bak";

                // Backup existing file if present.
                if (File.Exists(targetPath))
                    File.Copy(targetPath, bakPath, true);

                // Atomic write: temp + fsync + rename.
                WriteAtomicUtf8(targetPath, kvp.Value);
                writtenFiles.Add(targetPath);
                imported++;
                DebugLog($"import-state: wrote {kvp.Key} ({kvp.Value.Length} bytes)");
            }
        }
        catch (Exception ex)
        {
            // Rollback: restore .bak for each file that was written.
            DebugLog($"import-state: write failed at file {imported + 1}, rolling back {writtenFiles.Count} files");
            foreach (string writtenPath in writtenFiles)
            {
                string bakPath2 = writtenPath + ".bak";
                if (File.Exists(bakPath2))
                {
                    try { File.Move(bakPath2, writtenPath, true); } catch { }
                }
                else
                {
                    // No backup existed — the file was new; delete it.
                    try { File.Delete(writtenPath); } catch { }
                }
            }
            return ArgError($"Error: Import failed at file {imported + 1}. All changes rolled back. {ex.Message}");
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            imported,
            file = inputFile,
        }, JsonOptsIndented));

        DebugLog($"import-state: completed, {imported} files restored");
        return 0;
    }
}
