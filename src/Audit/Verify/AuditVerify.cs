using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace EnvManager;

// architecture-recovery ticket 43 (spec Phase 6, mental-model M1 bounded contexts):
// "audit verify" - the hash-chain verification step for the shared append-only audit
// ledger (audit-ledger.jsonl). ADR 0014 names the ledger as the audit-recovery
// source; "a chain you never verify is still just a log" (AGT ADR-0017), so this
// command exists and build.yml gates the verify job on "audit verify --strict".
//
// Chain contract (ledger schema v1, written by audit migrate-audit and mirrored by
// service/src/audit_ledger.rs): every line stores prevHash (hex SHA-256 of the
// previous line's hash; genesis = 64 zeros) and hash = SHA256(prevHash ||
// canonical_json(line minus hash/ledgerSchemaVersion)). The canonical recompute
// below is the same one RunAuditVerifyLedger has used since v0.9.10: deserialize the
// stored line into a Dictionary (insertion order = written key order), drop the two
// envelope fields, re-serialize with Program.JsonOpts, hash.
//
// Failure shapes (report-and-continue: every broken line is reported, the chain
// continues from each line's stored hash):
//   parseError       - line is not valid JSON (torn write / corruption)
//   missingField     - hash or prevHash absent (unrecognizable shape)
//   prevHashMismatch - linkage break (tampered/removed/reordered line)
//   hashMismatch     - entry_hash integrity failure (content tampered)
//   unsupportedEntry - service-writer snake_case entry (prev_hash/ledger_schema_version):
//                      service/src/audit_ledger.rs hashes a camelCase json! projection
//                      that no reader can re-derive from the stored line (pre-existing
//                      writer divergence, documented in reports/43). Fail-closed: the
//                      entry counts as unverified, but the chain continues from its
//                      stored hash so later lines are still checked.
//
// Exit codes: report mode (default) always exits 0 after printing the JSON verdict;
// --strict exits 0 only when every line verified (empty/missing ledger = trivially
// OK, same contract as verify-ledger) and 1 on any finding.

internal static class AuditVerify
{
    internal static int Run(string[] args)
    {
        bool strict = args.Contains("--strict");
        string? ledgerOverride = null;
        for (int i = 1; i < args.Length - 1; i++)
        {
            if (args[i] == "--ledger") ledgerOverride = args[++i];
        }

        // Default path routes through the production caller seam (RunAuditCommand resolves
        // AuditLedgerPath the same way), keeping this class independent of Program internals.
        string ledgerPath = ledgerOverride ?? Program.ResolveAuditLedgerPathForCommand(args);
        if (!File.Exists(ledgerPath))
        {
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                verified = true,
                events = 0,
                ledgerPath,
                message = "No ledger file exists",
            }, Program.JsonOptsIndented));
            return 0;
        }

        AuditVerifyReport report = VerifyLedgerFile(ledgerPath);
        Console.WriteLine(JsonSerializer.Serialize(new
        {
            verified = report.Verified,
            events = report.Events,
            breakages = report.Breakages,
            ledgerPath,
        }, Program.JsonOptsIndented));
        return strict && !report.Verified ? 1 : 0;
    }

    /// <summary>
    /// Walk the ledger file line by line. Blank lines are skipped (same as the writer).
    /// Every broken line yields one breakage record; the running prevHash continues
    /// from each line's stored hash (absent for unparseable lines) so downstream
    /// lines are still verified.
    /// </summary>
    internal static AuditVerifyReport VerifyLedgerFile(string ledgerPath)
    {
        string[] lines = File.ReadAllLines(ledgerPath);
        string prevHash = new string('0', 64);
        int events = 0;
        var breakages = new List<object>();
        using var sha = SHA256.Create();

        for (int i = 0; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            LedgerLineOutcome outcome = VerifyLedgerLine(lines[i], prevHash, sha);
            if (outcome.ErrorKind != null)
            {
                breakages.Add(new { line = i + 1, kind = outcome.ErrorKind, detail = outcome.ErrorDetail });
            }
            else
            {
                events++;
            }
            if (outcome.StoredHash is { Length: > 0 }) prevHash = outcome.StoredHash;
        }

        return new AuditVerifyReport(breakages.Count == 0, events, breakages);
    }

    private static LedgerLineOutcome VerifyLedgerLine(string line, string prevHash, SHA256 sha)
    {
        JsonElement evt;
        try
        {
            evt = JsonSerializer.Deserialize<JsonElement>(line);
        }
        catch (JsonException ex)
        {
            return new LedgerLineOutcome(null, "parseError", Program.ScrubExceptionMessage(ex.Message));
        }

        if (evt.ValueKind != JsonValueKind.Object)
            return new LedgerLineOutcome(null, "parseError", "ledger line is not a JSON object");

        if (evt.TryGetProperty("prev_hash", out _) || evt.TryGetProperty("ledger_schema_version", out _))
        {
            string rustHash = evt.TryGetProperty("hash", out var rh) && rh.ValueKind == JsonValueKind.String
                ? rh.GetString() ?? ""
                : "";
            return new LedgerLineOutcome(
                rustHash.Length > 0 ? rustHash : null,
                "unsupportedEntry",
                "service-writer snake_case entry is not re-derivable by this verifier (pre-existing writer divergence; see reports/43)");
        }

        if (!evt.TryGetProperty("hash", out var hashEl) || !evt.TryGetProperty("prevHash", out var prevEl))
            return new LedgerLineOutcome(null, "missingField", "ledger line is missing hash/prevHash");

        string storedHash = hashEl.GetString() ?? "";
        string storedPrev = prevEl.GetString() ?? "";

        if (storedPrev != prevHash)
            return new LedgerLineOutcome(storedHash, "prevHashMismatch",
                "expected prev=" + Clip(prevHash, 12) + "..., got=" + Clip(storedPrev, 12) + "...");

        // Canonical recompute: dictionary insertion order minus the two envelope fields,
        // serialized with the engine Program.JsonOpts - byte-identical to the writer's hash input.
        var eventForHash = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(line)!;
        eventForHash.Remove("hash");
        eventForHash.Remove("ledgerSchemaVersion");
        string eventJson = JsonSerializer.Serialize(eventForHash, Program.JsonOpts);
        byte[] hashInput = Encoding.UTF8.GetBytes(prevHash + eventJson);
        string computed = Convert.ToHexString(sha.ComputeHash(hashInput)).ToLowerInvariant();

        if (computed != storedHash)
            return new LedgerLineOutcome(storedHash, "hashMismatch",
                "expected=" + Clip(storedHash, 12) + "..., computed=" + Clip(computed, 12) + "...");

        return new LedgerLineOutcome(storedHash, null, null);
    }

    private static string Clip(string value, int maxLength)
        => string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}

/// <summary>Verdict of one ledger walk: overall integrity, verified event count, and every breakage.</summary>
internal sealed record AuditVerifyReport(bool Verified, int Events, List<object> Breakages);

/// <summary>Per-line outcome: the stored hash (for chain continuation) or the failure shape.</summary>
internal sealed record LedgerLineOutcome(string? StoredHash, string? ErrorKind, string? ErrorDetail);
