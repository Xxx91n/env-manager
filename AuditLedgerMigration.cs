using System.Text;
using System.Text.Json;
using System.Security.Cryptography;

namespace EnvManager;

// v0.9.10: Phase E audit ledger unification.
// Migrates the CLI-level audit.json (List<AuditEntry>) into the service-level
// audit-ledger.jsonl (append-only, hash-chained). The ledger lives at
// %ProgramData%\EnvManager\audit-ledger.jsonl (same as service mode).
//
// After migration, the CLI still writes to audit.json for backward compatibility
// (the existing RecordSnapshotDiff and TryUndoProfileAudit paths), but the
// ledger becomes the authoritative audit trail. The GUI history page reads from
// audit.json until v0.9.11 aligns the frontend.
//
// Pattern: append-only event sourcing with hash chain (like Bitcoin's block headers,
// or git's commit chain). Each event's hash = SHA256(prev_hash || canonical_json(event)).
// Tamper detection: verify_ledger recomputes every hash.

partial class Program
{
    static string AuditLedgerPath
    {
        get
        {
            string programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";
            string dir = Path.Combine(programData, "EnvManager");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "audit-ledger.jsonl");
        }
    }

    /// <summary>
    /// Migrate audit.json entries to the hash-chained audit-ledger.jsonl.
    /// Usage: env-manager audit migrate-audit [--dry-run]
    /// Each audit.json entry becomes a ledger event with action="migrate".
    /// Idempotent: if the ledger already exists and is non-empty, the command
    /// reports the existing event count and exits without duplicating.
    /// </summary>
    static int RunAuditMigrate(string[] args)
    {
        bool dryRun = args.Contains("--dry-run");

        // Check if ledger already has events.
        if (File.Exists(AuditLedgerPath))
        {
            string existingLedger = File.ReadAllText(AuditLedgerPath);
            int existingCount = existingLedger.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
            if (existingCount > 0)
            {
                Console.WriteLine(JsonSerializer.Serialize(new
                {
                    migrated = 0,
                    existingLedgerEvents = existingCount,
                    message = "Ledger already exists with events. Migration is idempotent — already done.",
                }, JsonOptsIndented));
                return 0;
            }
        }

        // Read audit.json
        if (!File.Exists(AuditFilePath))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                migrated = 0,
                message = "No audit.json found. Nothing to migrate.",
            }, JsonOptsIndented));
            return 0;
        }

        string auditJson = File.ReadAllText(AuditFilePath);
        var entries = JsonSerializer.Deserialize<List<JsonElement>>(auditJson) ?? new();

        if (entries.Count == 0)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { migrated = 0 }, JsonOptsIndented));
            return 0;
        }

        if (dryRun)
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                dryRun = true,
                entriesToMigrate = entries.Count,
            }, JsonOptsIndented));
            return 0;
        }

        // Append each entry to the ledger with hash chain.
        string prevHash = new string('0', 64); // genesis hash
        int migrated = 0;
        string ledgerDir = Path.GetDirectoryName(AuditLedgerPath) ?? "";

        using (var stream = new FileStream(AuditLedgerPath, FileMode.Append, FileAccess.Write))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            foreach (var entry in entries)
            {
                string eventId = entry.TryGetProperty("id", out var id) ? id.GetString() ?? Guid.NewGuid().ToString("N") : Guid.NewGuid().ToString("N");
                string timestamp = entry.TryGetProperty("timestamp", out var ts) ? ts.GetString() ?? DateTimeOffset.UtcNow.ToString("O") : DateTimeOffset.UtcNow.ToString("O");
                string command = entry.TryGetProperty("command", out var cmd) ? cmd.GetString() ?? "" : "";
                string name = entry.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string scope = entry.TryGetProperty("scope", out var s) ? s.GetString() ?? "user" : "user";

                // Build event for hashing (without hash field).
                var eventForHash = new
                {
                    id = eventId,
                    timestamp,
                    actor = "CLI",
                    action = "migrate",
                    command,
                    name,
                    scope,
                    prevHash,
                };
                string eventJson = JsonSerializer.Serialize(eventForHash, JsonOpts);

                // SHA256(prev_hash || event_json)
                using var sha = SHA256.Create();
                byte[] hashInput = Encoding.UTF8.GetBytes(prevHash + eventJson);
                string hash = Convert.ToHexString(sha.ComputeHash(hashInput)).ToLowerInvariant();

                // Build final event with hash.
                var finalEvent = new
                {
                    id = eventId,
                    timestamp,
                    actor = "CLI",
                    action = "migrate",
                    command,
                    name,
                    scope,
                    prevHash = prevHash,
                    hash,
                    ledgerSchemaVersion = 1,
                };

                string line = JsonSerializer.Serialize(finalEvent, JsonOpts);
                writer.WriteLine(line);
                prevHash = hash;
                migrated++;
            }
        }

        DebugLog($"audit migrate: {migrated} entries migrated to ledger");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            migrated,
            ledgerPath = AuditLedgerPath,
        }, JsonOptsIndented));

        return 0;
    }

    /// <summary>
    /// Verify the audit ledger hash chain integrity.
    /// Usage: env-manager audit verify-ledger
    /// </summary>
    static int RunAuditVerifyLedger()
    {
        if (!File.Exists(AuditLedgerPath))
        {
            Console.WriteLine(JsonSerializer.Serialize(new { verified = true, events = 0, message = "No ledger file exists" }, JsonOptsIndented));
            return 0;
        }

        string[] lines = File.ReadAllLines(AuditLedgerPath);
        string prevHash = new string('0', 64);
        int verified = 0;

        using var sha = SHA256.Create();

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;

            var evt = JsonSerializer.Deserialize<JsonElement>(lines[i]);

            string storedHash = evt.GetProperty("hash").GetString() ?? "";
            string storedPrev = evt.GetProperty("prevHash").GetString() ?? "";

            if (storedPrev != prevHash)
            {
                Console.Error.WriteLine($"Error: hash chain broken at line {i + 1} (expected prev={prevHash[..12]}..., got={storedPrev[..12]}...)");
                return 1;
            }

            // Recompute hash: remove hash and ledgerSchemaVersion fields, then hash.
            var eventForHash = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(lines[i])!;
            eventForHash.Remove("hash");
            eventForHash.Remove("ledgerSchemaVersion");
            string eventJson = JsonSerializer.Serialize(eventForHash, JsonOpts);

            byte[] hashInput = Encoding.UTF8.GetBytes(prevHash + eventJson);
            string computed = Convert.ToHexString(sha.ComputeHash(hashInput)).ToLowerInvariant();

            if (computed != storedHash)
            {
                Console.Error.WriteLine($"Error: hash mismatch at line {i + 1} (expected={storedHash[..12]}..., computed={computed[..12]}...)");
                return 1;
            }

            prevHash = storedHash;
            verified++;
        }

        Console.WriteLine(JsonSerializer.Serialize(new { verified = true, events = verified }, JsonOptsIndented));
        return 0;
    }

    /// <summary>
    /// Export a DPAPI-encrypted survival kit from the audit ledger.
    /// Usage: env-manager audit export-survival-kit [--mount <id>] [--output <file>]
    /// </summary>
    static int RunAuditExportSurvivalKit(string[] args)
    {
        if (!File.Exists(AuditLedgerPath))
            return ArgError("Error: No audit ledger found. Run 'audit migrate-audit' first.");

        int mountIdx = Array.IndexOf(args, "--mount");
        string? mountId = mountIdx >= 0 && mountIdx + 1 < args.Length ? args[mountIdx + 1] : null;

        int outIdx = Array.IndexOf(args, "--output");
        string outputPath;
        if (outIdx >= 0 && outIdx + 1 < args.Length)
        {
            outputPath = Path.GetFullPath(args[outIdx + 1]);
        }
        else
        {
            string ledgerDir = Path.GetDirectoryName(AuditLedgerPath) ?? ".";
            outputPath = Path.Combine(ledgerDir, "mount-survival-kit.dpapi");
        }

        string[] lines = File.ReadAllLines(AuditLedgerPath);
        var events = new List<string>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            if (mountId != null && !line.Contains(mountId)) continue;
            events.Add(line);
        }

        if (events.Count == 0)
            return ArgError("Error: No events found " + (mountId != null ? $"for mount '{mountId}'" : "") + ".");

        // Build the kit
        var kit = new
        {
            mountId,
            events,
            exportedAt = DateTimeOffset.UtcNow.ToString("O"),
            schemaVersion = 1,
        };

        string kitJson = JsonSerializer.Serialize(kit, JsonOptsIndented);

        // Write plaintext to temp, then DPAPI-encrypt.
        string tmpPath = Path.Combine(Path.GetTempPath(), "envmanager-kit-" + Environment.ProcessId + ".json");
        File.WriteAllText(tmpPath, kitJson, new UTF8Encoding(false));

        try
        {
            string encrypted = DpapiHelper.EncryptSecret(kitJson);
            string tempOut = outputPath + ".tmp." + Environment.ProcessId;
            File.WriteAllText(tempOut, encrypted, new UTF8Encoding(false));
            File.Move(tempOut, outputPath, true);
        }
        finally
        {
            // Secure delete temp plaintext
            try { File.Delete(tmpPath); } catch { }
        }

        DebugLog($"audit export-survival-kit: {events.Count} events -> {outputPath}");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            exported = events.Count,
            output = outputPath,
        }, JsonOptsIndented));

        return 0;
    }

    /// <summary>
    /// Recover mount list from the audit ledger (replay create/delete events).
    /// Usage: env-manager audit recover-from-ledger
    /// </summary>
    static int RunAuditRecoverFromLedger()
    {
        if (!File.Exists(AuditLedgerPath))
            return ArgError("Error: No audit ledger found.");

        // Verify first
        int verifyResult = RunAuditVerifyLedger();
        if (verifyResult != 0)
            return ArgError("Error: Ledger verification failed. Recovery aborted — ledger may be tampered.");

        string[] lines = File.ReadAllLines(AuditLedgerPath);
        var mounts = new List<object>();

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var evt = JsonSerializer.Deserialize<JsonElement>(line);
            string action = evt.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
            string? mountId = evt.TryGetProperty("mountId", out var m) ? m.GetString() : null;
            if (mountId == null) mountId = evt.TryGetProperty("mount_id", out var m2) ? m2.GetString() : null;

            switch (action)
            {
                case "create":
                    if (mountId != null)
                        mounts.Add(new
                        {
                            id = mountId,
                            provider = evt.TryGetProperty("provider", out var p) ? p.GetString() : "",
                            profileName = evt.TryGetProperty("profileName", out var pn) ? (string?)pn.GetString() : null,
                            recovered = true,
                        });
                    break;
                case "delete":
                    if (mountId != null)
                        mounts.RemoveAll(m =>
                        {
                            var dict = (System.Collections.IDictionary)m;
                            return dict["id"]?.ToString() == mountId;
                        });
                    break;
            }
        }

        string ledgerDir = Path.GetDirectoryName(AuditLedgerPath) ?? ".";
        string recoveryPath = Path.Combine(ledgerDir, "recovered-mounts.json");
        string recoveryJson = JsonSerializer.Serialize(new
        {
            mounts,
            recoveredAt = DateTimeOffset.UtcNow.ToString("O"),
        }, JsonOptsIndented);

        File.WriteAllText(recoveryPath, recoveryJson, new UTF8Encoding(false));

        DebugLog($"audit recover-from-ledger: {mounts.Count} mounts reconstructed");
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            recovered = mounts.Count,
            recoveryPath,
        }, JsonOptsIndented));

        return 0;
    }
}
