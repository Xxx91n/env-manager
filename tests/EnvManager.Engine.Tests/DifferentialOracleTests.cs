// DifferentialOracleTests.cs - differential oracle fixture (architecture-recovery issue 11)
// License: Apache-2.0

using EnvManager;
using Microsoft.Win32;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Runtime gate for the differential oracle suite. The fixture drives the REAL registry
/// (RegistryScope as oracle) alongside InMemoryScope, so it must never run as part of the
/// default <c>dotnet test</c> sweep: that sweep runs on arbitrary dev machines and CI
/// images where registry mutations are not rolled back. Instead the suite is mounted by
/// <c>scripts/test-with-restore.ps1</c>, which snapshots HKCU/HKLM environment first and
/// reconciles both hives afterwards (the hard-boundary real-registry isolation red line).
///
/// Mechanics: <see cref="DifferentialOracleFactAttribute"/> skips every test unless the
/// environment variable <c>EM_DIFFERENTIAL_ORACLE=1</c> is set. The smoke harness sets it
/// for exactly one <c>dotnet test</c> invocation filtered to this suite, inside its own
/// snapshot/restore window. Outside that window the tests self-report as skipped with the
/// reason, mirroring the L1/L2 Skip convention of the secret-provider contract mounts.
/// </summary>
public sealed class DifferentialOracleFactAttribute : FactAttribute
{
    public DifferentialOracleFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("EM_DIFFERENTIAL_ORACLE"), "1", StringComparison.Ordinal))
        {
            Skip = "differential oracle drives the real registry; run only inside scripts/test-with-restore.ps1 (set EM_DIFFERENTIAL_ORACLE=1)";
        }
    }
}

/// <summary>
/// The whole suite shares one non-parallel collection: several tests mutate the same real
/// HKCU values (notably PATH), and concurrent differential runs would clobber each other.
/// </summary>
[CollectionDefinition("DifferentialOracle", DisableParallelization = true)]
public sealed class DifferentialOracleCollection;

/// <summary>
/// Differential oracle: the same operation script runs against InMemoryScope (double) and
/// RegistryScope (real Windows registry, oracle). After every operation the fixture asserts
/// BOTH the terminal state (raw unexpanded value + registry value kind per variable, per
/// scope) AND the broadcast count agree between the two implementations. "InMemoryScope is
/// faithful to Windows" thereby becomes a pinned fact instead of an assumption
/// (spec Phase 3, A1; research/next-wave-patterns.md).
///
/// Semantic matrix (handoff 11 现状勘察, verified against the current seam):
///   1. REG_EXPAND_SZ preservation: %VAR% values keep their unexpanded raw form; the kind
///      rule is upgrade-only - a value containing % lands as REG_EXPAND_SZ, and a later
///      %-free overwrite keeps the existing expanded kind (exactly what RegistryScope's
///      SetValue does, op-for-operation mirrored by the double).
///   2. PATH value length boundaries at 1024 chars and ~30000 chars (command-layer gate
///      MaxLength = 32767 rejects beyond it on both implementations).
///   3. Empty-entry semantics: an empty variable value means "current directory" on
///      Windows; both stores must persist the empty string as a PRESENT value.
///   4. Variable names containing '=' are rejected by the command layer before any seam
///      call - no write, no broadcast, on both implementations.
///   5. System-scope writes require elevation: HKLM Session Manager Environment is
///      admin-only. Elevated sessions diff user↔system writes normally; non-elevated
///      sessions pin the oracle's refusal, with the double documented as elevation-blind
///      (EngineScope contract notes).
/// </summary>
[Collection("DifferentialOracle")]
public class DifferentialOracleTests
{
    const string TestPrefix = "EM_DIFF_";
    const int MaxLengthGate = 32767;

    readonly InMemoryScope _memory = new();
    readonly RegistryScope _registry = new();

    // The differential pair must see the same protection predicate on both sides; no
    // EM_DIFF_ name can collide with a real protected entry, so allow-all is safe here.
    static Func<string, string, bool> AllowAllVariables() => (_, _) => false;
    static Func<string, bool> AllowAllPaths() => _ => false;

    string UniqueName(string stem) => TestPrefix + stem + "_" + Environment.TickCount64.ToString("X");

    // ---- differential primitives -------------------------------------------------

    /// <summary>Runs one operation against both implementations, double first.</summary>
    void RunBoth(Action<IEnvironmentScope> operation)
    {
        operation(_memory);
        operation(_registry);
    }

    /// <summary>
    /// The core differential assertion: for every tracked variable the raw value AND value
    /// kind must agree between double and oracle, and the double's broadcast counter must
    /// equal <paramref name="expectedBroadcasts"/>. Pre-existing host variables outside the
    /// tracked set are ignored; a divergence inside the tracked set fails with a diff.
    /// </summary>
    void AssertTerminalStateAndBroadcastsMatch(string scope, int expectedBroadcasts, params string[] trackedNames)
    {
        Assert.Equal(expectedBroadcasts, _memory.BroadcastCount);

        foreach (string name in trackedNames)
        {
            EnvValueSnapshot? fromMemory = _memory.ReadValue(name, scope);
            EnvValueSnapshot? fromRegistry = _registry.ReadValue(name, scope);

            if (fromRegistry == null)
            {
                Assert.Null(fromMemory);
                continue;
            }

            Assert.NotNull(fromMemory);
            Assert.True(string.Equals(fromMemory!.Value, fromRegistry.Value, StringComparison.Ordinal),
                $"[{name}@{scope}] raw value drift:\n  in-memory: {FormatValue(fromMemory.Value)}\n  registry : {FormatValue(fromRegistry.Value)}");
            Assert.True(fromMemory.Kind == fromRegistry.Kind,
                $"[{name}@{scope}] registry value kind drift:\n  in-memory: {fromMemory.Kind}\n  registry : {fromRegistry.Kind}");
        }
    }

    static string FormatValue(string value)
    {
        if (value.Length <= 120) return value;
        return value[..60] + "...<" + value.Length + " chars>..." + value[^40..];
    }

    /// <summary>Cleans every variable this fixture created from both stores (seam delete also
    /// removes toggle backups, so a mid-test failure cannot leak backup names).</summary>
    void Cleanup(params string[] names)
    {
        foreach (string name in names)
        {
            _memory.DeleteValue(name, "user");
            _registry.DeleteValue(name, "user");
        }
    }

    // ---- semantic matrix 1: REG_EXPAND_SZ preservation ---------------------------

    [DifferentialOracleFact]
    public void Diff_ExpandString_ValueWithPercent_PreservesRawValueAndExpandsKind()
    {
        string name = UniqueName("EXPAND");
        try
        {
            RunBoth(env => Assert.Equal(WriteOutcome.Verified, env.WriteValue(name, "%USERPROFILE%\\em-diff", "user")));

            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, name);

            // Windows oracle: a value containing % must land as REG_EXPAND_SZ with the
            // unexpanded raw text preserved (DoNotExpand read-back).
            EnvValueSnapshot? snap = _registry.ReadValue(name, "user");
            Assert.Equal(RegistryValueKind.ExpandString, snap?.Kind);
            Assert.Equal("%USERPROFILE%\\em-diff", snap?.Value);
        }
        finally { Cleanup(name); }
    }

    [DifferentialOracleFact]
    public void Diff_ExpandString_OverwriteWithoutPercent_PreservesExpandedKind()
    {
        string name = UniqueName("KEEPKIND");
        try
        {
            RunBoth(env => env.WriteValue(name, "%USERPROFILE%\\em-diff", "user"));
            _memory.ResetBroadcastCount();

            // Kind policy is upgrade-only: an existing REG_EXPAND_SZ overwritten with a
            // %-free value stays REG_EXPAND_SZ (both stores do this op-for-operation).
            RunBoth(env => Assert.Equal(WriteOutcome.Verified, env.WriteValue(name, "plain-literal", "user")));

            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, name);
            Assert.Equal(RegistryValueKind.ExpandString, _registry.ReadValue(name, "user")?.Kind);
        }
        finally { Cleanup(name); }
    }

    [DifferentialOracleFact]
    public void Diff_ToggleRoundTrip_PreservesRawValueAndKindExactly()
    {
        string name = UniqueName("TOGGLE");
        try
        {
            const string raw = "%ProgramData%\\em-diff;%SystemRoot%";
            RunBoth(env => env.WriteValue(name, raw, "user"));
            _memory.ResetBroadcastCount();

            // disable: the raw value moves to <name>_EnvManager_disabled, original gone.
            RunBoth(env => Assert.True(env.Toggle(name, "user").Success));
            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, name + "_EnvManager_disabled");
            Assert.Null(_memory.ReadValue(name, "user"));
            Assert.Null(_registry.ReadValue(name, "user"));

            // restore: raw value + kind come back byte-exact, backup gone.
            _memory.ResetBroadcastCount();
            RunBoth(env => Assert.True(env.Toggle(name, "user").Success));
            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, name);
            Assert.Null(_memory.ReadValue(name + "_EnvManager_disabled", "user"));
            Assert.Null(_registry.ReadValue(name + "_EnvManager_disabled", "user"));
            Assert.Equal(RegistryValueKind.ExpandString, _registry.ReadValue(name, "user")?.Kind);
        }
        finally { Cleanup(name); }
    }

    // ---- semantic matrix 2: PATH value length boundaries -------------------------

    [DifferentialOracleFact]
    public void Diff_Path_At1024Boundary_MatchesExactly()
    {
        const int boundary = 1024;
        string entry = "C:\\em-diff-" + new string('a', 32) + "\\x";
        entry += new string('b', boundary - entry.Length);
        Assert.Equal(boundary, entry.Length);

        RunPathCase(new[] { entry }, entry);
    }

    [DifferentialOracleFact]
    public void Diff_Path_Near30000Chars_MatchesExactly()
    {
        // ~30000 chars: comfortably inside the 32767 command-layer gate but far beyond the
        // legacy 1024 truncation zone that setx and older editors suffer from (A1 evidence).
        const int targetLength = 29999;
        var segments = new List<string>();
        int used = 0;
        int i = 0;
        while (used < targetLength)
        {
            string seg = "C:\\em-diff-s" + i++;
            segments.Add(seg);
            used += seg.Length + 1;
        }
        string joined = string.Join(";", segments);
        Assert.True(joined.Length is > 29000 and < 31000, $"fixture bug: joined length {joined.Length}");

        RunPathCase(segments, joined);
    }

    /// <summary>
    /// Shared PATH-case body: writes the entries through the PATH list core so both sides
    /// experience the same join/normalize/broadcast pipeline, diffs raw value + kind, and
    /// restores the original PATH (exact name casing, raw bytes, kind) in finally - the
    /// suite must leave the host PATH byte-identical or the harness's final snapshot
    /// comparison reports drift.
    /// </summary>
    void RunPathCase(IReadOnlyList<string> entries, string expectedRaw)
    {
        PathOriginal? original = CapturePathOriginal();
        try
        {
            // Align initial states: the double starts with no PATH at all while the oracle
            // starts with the host's real PATH (typically REG_EXPAND_SZ). The kind policy
            // preserves an existing kind, so the pair must start identical - delete PATH
            // on both sides first (the finally below puts the real one back byte-exact).
            RunBoth(env => env.DeleteValue("PATH", "user"));
            _memory.ResetBroadcastCount();

            RunBoth(env => Assert.True(Program.SetPathEntriesCore(
                env, AllowAllVariables(), AllowAllPaths(), entries.ToList(), "user")));

            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, "PATH");
            Assert.Equal(expectedRaw, _registry.ReadValue("PATH", "user")?.Value);
            Assert.Equal(RegistryValueKind.String, _registry.ReadValue("PATH", "user")?.Kind);
        }
        finally
        {
            RestorePathOriginal(original);
        }
    }

    [DifferentialOracleFact]
    public void Diff_Path_OverMaxLength_RejectedOnBothSides()
    {
        // 32767 is the command-layer gate (Program.MaxLength). The registry itself would
        // accept more; both implementations must reject identically, and neither side may
        // broadcast or touch the previous value.
        var entries = new List<string>();
        int used = 0;
        int i = 0;
        while (used <= MaxLengthGate)
        {
            string seg = "C:\\em-diff-o" + i++;
            entries.Add(seg);
            used += seg.Length + 1;
        }
        Assert.True(string.Join(";", entries).Length > MaxLengthGate,
            $"fixture bug: joined length {string.Join(";", entries).Length}");

        EnvValueSnapshot? before = _registry.ReadValue("PATH", "user");
        _memory.ResetBroadcastCount();

        bool okMemory = Program.SetPathEntriesCore(_memory, AllowAllVariables(), AllowAllPaths(), entries, "user");
        bool okRegistry = Program.SetPathEntriesCore(_registry, AllowAllVariables(), AllowAllPaths(), entries, "user");

        Assert.False(okMemory);
        Assert.False(okRegistry);

        EnvValueSnapshot? after = _registry.ReadValue("PATH", "user");
        Assert.Equal(before?.Value, after?.Value);
        Assert.Equal(before?.Kind, after?.Kind);
        Assert.Equal(0, _memory.BroadcastCount);
    }

    // ---- semantic matrix 3: empty-entry semantics --------------------------------

    [DifferentialOracleFact]
    public void Diff_EmptyValue_PersistsAsPresentEmptyString_CurrentDirectorySemantics()
    {
        string name = UniqueName("EMPTY");
        try
        {
            // An empty environment variable value means "current directory" on Windows.
            // Both stores must persist the empty string as a PRESENT value (not delete,
            // not a placeholder).
            RunBoth(env => Assert.Equal(WriteOutcome.Verified, env.WriteValue(name, "", "user")));

            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, name);
            Assert.True(_registry.Exists(name, "user"), "oracle must keep the value present (empty = current directory semantics)");
            Assert.True(_memory.Exists(name, "user"));
            Assert.Equal("", _registry.ReadValue(name, "user")?.Value);
        }
        finally { Cleanup(name); }
    }

    [DifferentialOracleFact]
    public void Diff_Path_EmptySegments_FoldedByIdenticalPipeline()
    {
        // ";;C:\em-diff-e;;" folds to "C:\em-diff-e" through the CLI's
        // Split(RemoveEmptyEntries)+Join pipeline; both sides must fold identically.
        List<string> entries = ";;C:\\em-diff-e;;".Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
        RunPathCase(entries, "C:\\em-diff-e");
    }

    // ---- semantic matrix 4: names containing '=' are rejected --------------------

    [DifferentialOracleFact]
    public void Diff_NameWithEquals_RejectedBeforeAnyWrite_OnBothSides()
    {
        const string name = "EM=DIFF_BAD";
        _memory.ResetBroadcastCount();

        // The command layer refuses '=' names before any seam call: both runs return
        // false, neither store gains the variable, and no broadcast fires.
        RunBoth(env => Assert.False(Program.WriteVariableCore(env, AllowAllVariables(), name, "x", "user")));

        Assert.Equal(0, _memory.BroadcastCount);
        Assert.False(_memory.Exists(name, "user"));
        Assert.False(_registry.Exists(name, "user"));
    }

    // ---- semantic matrix 5: system scope requires elevation ----------------------

    [DifferentialOracleFact]
    public void Diff_SystemScope_WriteRequiresElevation()
    {
        string name = UniqueName("SYS");
        bool elevated = IsElevated();
        try
        {
            if (!elevated)
            {
                // Real Windows semantics: HKLM Session Manager Environment is admin-only.
                // The oracle must refuse the write outright (writable open denied) and
                // leave the store untouched; the double is elevation-blind by documented
                // contract (EngineScope contract notes), so this leg pins the oracle's
                // refusal instead of forcing a false equivalence.
                bool oracleRefused = false;
                try
                {
                    oracleRefused = _registry.WriteValue(name, "em-diff-sys", "system") != WriteOutcome.Verified;
                }
                catch (Exception error) when (error is UnauthorizedAccessException or System.Security.SecurityException)
                {
                    oracleRefused = true;
                }
                Assert.True(oracleRefused, "non-elevated system-scope write must not succeed on the oracle");
                Assert.False(_registry.Exists(name, "system"));
                return;
            }

            // Elevated session: the write lands on the real hive; the double must agree
            // on outcome, raw value, kind, and broadcast count (the true differential leg).
            RunBoth(env => Assert.Equal(WriteOutcome.Verified, env.WriteValue(name, "em-diff-sys", "system")));
            AssertTerminalStateAndBroadcastsMatch("system", expectedBroadcasts: 1, name);
        }
        finally
        {
            if (elevated) { _registry.DeleteValue(name, "system"); }
        }
    }

    static bool IsElevated()
    {
        using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    // ---- end-to-end operation script: mixed sequence, one running diff ------------

    [DifferentialOracleFact]
    public void Diff_MixedOperationScript_TerminalStateAndBroadcastsMatch()
    {
        string v1 = UniqueName("V1");
        string renamed = v1 + "r";
        string v2 = UniqueName("V2");
        try
        {
            // Step 1: create two variables (plain + expand) - one broadcast each.
            RunBoth(env => Assert.Equal(WriteOutcome.Verified, env.WriteValue(v1, "plain-value", "user")));
            RunBoth(env => Assert.Equal(WriteOutcome.Verified, env.WriteValue(v2, "%PATH%\\em-diff", "user")));
            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 2, v1, v2);
            _memory.ResetBroadcastCount();

            // Step 2: rename v1 -> renamed (write-verify-delete, single broadcast).
            RunBoth(env => Assert.Equal(0, Program.RunRename(new[] { "rename", v1, renamed, "--scope", "user" }, env, AllowAllVariables())));
            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, v1, renamed, v2);
            _memory.ResetBroadcastCount();

            // Steps 3+4: change-scope v2 user -> system -> user (elevation-gated; a
            // non-elevated oracle throws on the writable HKLM open, which the gate skips).
            if (IsElevated())
            {
                RunBoth(env => Assert.Equal(0, Program.RunChangeScope(new[] { "change-scope", v2, "system", "--scope", "user" }, env, AllowAllVariables())));
                AssertTerminalStateAndBroadcastsMatch("system", expectedBroadcasts: 1, v2);
                _memory.ResetBroadcastCount();

                RunBoth(env => Assert.Equal(0, Program.RunChangeScope(new[] { "change-scope", v2, "user", "--scope", "system" }, env, AllowAllVariables())));
                AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, v2);
                _memory.ResetBroadcastCount();
            }

            // Step 5: toggle disable + restore on renamed (one broadcast each; the disable
            // count was consumed by the reset above).
            RunBoth(env => Assert.True(env.Toggle(renamed, "user").Success));
            _memory.ResetBroadcastCount();
            RunBoth(env => Assert.True(env.Toggle(renamed, "user").Success));
            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, renamed, v2);
            _memory.ResetBroadcastCount();

            // Step 6: delete v2 (single broadcast).
            RunBoth(env => Assert.True(env.DeleteValue(v2, "user")));
            AssertTerminalStateAndBroadcastsMatch("user", expectedBroadcasts: 1, renamed, v2);
        }
        finally { Cleanup(v1, renamed, v2); }
    }

    // ---- exact PATH capture/restore helpers (host-safety, harness-consistent) -----

    sealed record PathOriginal(string Name, string Value, RegistryValueKind Kind);

    /// <summary>Captures the host PATH exactly: original name casing, raw unexpanded value, kind.</summary>
    static PathOriginal? CapturePathOriginal()
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey("Environment", false);
        if (key == null) return null;
        string? exactName = key.GetValueNames().FirstOrDefault(n => n.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        if (exactName == null) return null;
        object? raw = key.GetValue(exactName, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
        if (raw == null) return null;
        return new PathOriginal(exactName, raw.ToString() ?? "", key.GetValueKind(exactName));
    }

    /// <summary>Restores the captured PATH byte- and kind-exact, preserving original name casing.</summary>
    static void RestorePathOriginal(PathOriginal? original)
    {
        using RegistryKey? key = Registry.CurrentUser.OpenSubKey("Environment", true);
        if (key == null) throw new InvalidOperationException("cannot reopen HKCU\\Environment to restore PATH");

        string? currentName = key.GetValueNames().FirstOrDefault(n => n.Equals("PATH", StringComparison.OrdinalIgnoreCase));
        if (original == null)
        {
            if (currentName != null) key.DeleteValue(currentName, false);
            return;
        }

        // Remove the test-written name first when its casing differs, so the original
        // casing is what remains in the key.
        if (currentName != null && !string.Equals(currentName, original.Name, StringComparison.Ordinal))
        {
            key.DeleteValue(currentName, false);
        }
        key.SetValue(original.Name, original.Value, original.Kind);
    }
}
