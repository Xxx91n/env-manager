// AuditVerifyTests.cs - audit-ledger hash-chain verification (architecture-recovery ticket 43)
// License: Apache-2.0

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Pins the <c>audit verify</c> hash-chain verification core (architecture-recovery
/// ticket 43, spec Phase 6) against a synthetic audit-ledger.jsonl built through the
/// canonical writer contract (the same one <c>audit migrate-audit</c> uses since
/// v0.9.10): line = event(id, timestamp, actor, action, command, name, scope,
/// prevHash) + hash + ledgerSchemaVersion, serialized with the engine JsonOpts;
/// hash = SHA256(prevHash || canonical_json(event)). Genesis prevHash = 64 zeros.
///
/// Scenarios (issue 43 acceptance): normal chain, tamper injection, missing line -
/// plus the report-vs-strict exit-code contract, missing/absent ledger, torn line,
/// and the fail-closed unsupportedEntry shape for service-writer snake_case lines.
///
/// Ledgers are written to the OS temp directory and passed via --ledger; the suite
/// never touches the real registry, %ProgramData%, or machine environment state.
/// </summary>
[Collection("CliSnapshotSerial")]
public class AuditVerifyTests
{
    private static string Genesis => new string('0', 64);

    /// <summary>
    /// Independent reimplementation of the CLI migrate-audit writer (hash oracle):
    /// building the hash from the event JSON and the line from the same event fields
    /// guarantees a self-consistent chain without calling production writer code.
    /// </summary>
    private static (string Line, string Hash) LedgerLine(string prevHash, string command, string name)
    {
        var @event = new
        {
            id = Guid.NewGuid().ToString("N"),
            timestamp = DateTimeOffset.UtcNow.ToString("O"),
            actor = "CLI",
            action = "migrate",
            command,
            name,
            scope = "user",
            prevHash,
        };
        string eventJson = JsonSerializer.Serialize(@event, Program.JsonOpts);
        string hash = Sha256Hex(prevHash + eventJson);
        var line = new
        {
            @event.id,
            @event.timestamp,
            @event.actor,
            @event.action,
            @event.command,
            @event.name,
            @event.scope,
            @event.prevHash,
            hash,
            ledgerSchemaVersion = 1,
        };
        return (JsonSerializer.Serialize(line, Program.JsonOpts), hash);
    }

    private static string Sha256Hex(string input)
    {
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();
    }

    private static string WriteLedger(params string[] lines)
    {
        string path = Path.Combine(Path.GetTempPath(), "em43-ledger-" + Guid.NewGuid().ToString("N") + ".jsonl");
        File.WriteAllLines(path, lines);
        return path;
    }

    private static (int Exit, string Stdout) RunVerify(params string[] args)
    {
        TextWriter original = Console.Out;
        var buffer = new StringWriter();
        try
        {
            Console.SetOut(buffer);
            int exit = AuditVerify.Run(args);
            return (exit, buffer.ToString());
        }
        finally
        {
            Console.SetOut(original);
        }
    }

    [Fact]
    public void Verify_NormalChain_StrictExitsZero()
    {
        var (line1, hash1) = LedgerLine(Genesis, "set", "EM43_A");
        var (line2, hash2) = LedgerLine(hash1, "set", "EM43_B");
        var (line3, _) = LedgerLine(hash2, "delete", "EM43_A");
        string path = WriteLedger(line1, line2, line3);
        try
        {
            var (exit, stdout) = RunVerify("verify", "--ledger", path, "--strict");
            Assert.Equal(0, exit);
            Assert.Contains("\"verified\": true", stdout);
            Assert.Contains("\"events\": 3", stdout);
            Assert.DoesNotContain("hashMismatch", stdout);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_TamperedEntry_StrictExitsOneWithHashMismatch()
    {
        var (line1, hash1) = LedgerLine(Genesis, "set", "EM43_A");
        var (line2, hash2) = LedgerLine(hash1, "set", "EM43_B");
        var (line3, _) = LedgerLine(hash2, "delete", "EM43_A");
        // Tamper: rewrite the recorded name inside line 2 without recomputing its hash.
        string tampered = line2.Replace("\"name\":\"EM43_B\"", "\"name\":\"EM43_EVIL\"", StringComparison.Ordinal);
        Assert.NotEqual(line2, tampered);
        string path = WriteLedger(line1, tampered, line3);
        try
        {
            var (exit, stdout) = RunVerify("verify", "--ledger", path, "--strict");
            Assert.Equal(1, exit);
            Assert.Contains("\"verified\": false", stdout);
            Assert.Contains("hashMismatch", stdout);
            Assert.Contains("\"line\": 2", stdout);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_MissingMiddleLine_StrictExitsOneWithPrevHashMismatch()
    {
        var (line1, hash1) = LedgerLine(Genesis, "set", "EM43_A");
        var (line2, hash2) = LedgerLine(hash1, "set", "EM43_B");
        var (line3, _) = LedgerLine(hash2, "delete", "EM43_A");
        // Line 2 removed: line 3 still chains from hash2, but the running prev is hash1.
        string path = WriteLedger(line1, line3);
        try
        {
            var (exit, stdout) = RunVerify("verify", "--ledger", path, "--strict");
            Assert.Equal(1, exit);
            Assert.Contains("prevHashMismatch", stdout);
            Assert.Contains("\"line\": 2", stdout);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_BrokenChain_WithoutStrict_IsReportModeAndExitsZero()
    {
        var (line1, hash1) = LedgerLine(Genesis, "set", "EM43_A");
        var (line2, _) = LedgerLine(hash1, "set", "EM43_B");
        string tampered = line2.Replace("\"name\":\"EM43_B\"", "\"name\":\"EM43_EVIL\"", StringComparison.Ordinal);
        string path = WriteLedger(line1, tampered);
        try
        {
            var (exit, stdout) = RunVerify("verify", "--ledger", path);
            Assert.Equal(0, exit);
            Assert.Contains("\"verified\": false", stdout);
            Assert.Contains("hashMismatch", stdout);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_MissingLedgerFile_IsTriviallyVerifiedExitZero()
    {
        string path = Path.Combine(Path.GetTempPath(), "em43-absent-" + Guid.NewGuid().ToString("N") + ".jsonl");
        var (exit, stdout) = RunVerify("verify", "--ledger", path, "--strict");
        Assert.Equal(0, exit);
        Assert.Contains("No ledger file exists", stdout);
        Assert.Contains("\"events\": 0", stdout);
    }

    [Fact]
    public void Verify_TornLine_StrictExitsOneWithParseError()
    {
        var (line1, hash1) = LedgerLine(Genesis, "set", "EM43_A");
        var (line2, _) = LedgerLine(hash1, "set", "EM43_B");
        string path = WriteLedger(line1, line2, "{not-json");
        try
        {
            var (exit, stdout) = RunVerify("verify", "--ledger", path, "--strict");
            Assert.Equal(1, exit);
            Assert.Contains("parseError", stdout);
            Assert.Contains("\"line\": 3", stdout);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Verify_ServiceWriterSnakeCaseEntry_StrictExitsOneAsUnsupported()
    {
        var (line1, hash1) = LedgerLine(Genesis, "set", "EM43_A");
        // Rust service writer shape (service/src/audit_ledger.rs): snake_case fields,
        // hash over a camelCase json! projection that no reader can re-derive.
        string serviceLine = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["timestamp"] = DateTimeOffset.UtcNow.ToString("O"),
            ["actor"] = "service",
            ["provider"] = null,
            ["mount_id"] = "mount-1",
            ["profile_name"] = null,
            ["action"] = "create",
            ["reason"] = null,
            ["prev_hash"] = hash1,
            ["hash"] = Sha256Hex("service-writer-hash-input-not-re-derivable"),
            ["ledger_schema_version"] = 1,
        }, Program.JsonOpts);
        string path = WriteLedger(line1, serviceLine);
        try
        {
            var (exit, stdout) = RunVerify("verify", "--ledger", path, "--strict");
            Assert.Equal(1, exit);
            Assert.Contains("unsupportedEntry", stdout);
        }
        finally { File.Delete(path); }
    }
}
