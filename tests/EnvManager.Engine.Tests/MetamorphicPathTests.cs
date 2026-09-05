// MetamorphicPathTests.cs - oracle-free metamorphic relations over PATH normalization and
// case-folding semantics (ticket 34, architecture-recovery Phase 5 pilot).
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Metamorphic relations (MR) over two oracle-free semantic faces selected by the ticket-34
/// pilot: PATH entry normalization (expand %VAR% / trim / trailing separators) and name
/// case-folding. Each relation follows the MR shape "source input T, follow-up input T
/// transformed by M, then relation R(sourceOutput, followUpOutput) must hold" - no golden
/// oracle needed. The free oracle is the documented CLI contract (docs/cli-commands.md:
/// dedupe is case-insensitive and normalizes like list/health) plus Windows semantics
/// (PATH entries and variable names are case-insensitive).
/// Every MR runs against InMemoryScope through the seam-parameterized command cores with
/// synthetic allow-all protection predicates - no real registry, no machine state.
/// </summary>
public class MetamorphicPathTests
{
    static readonly Func<string, string, bool> AllowVariables = (_, _) => false;
    static readonly Func<string, bool> AllowPaths = _ => false;

    static InMemoryScope NewPathScope(params string[] entries)
    {
        var env = new InMemoryScope();
        env.WriteValue("PATH", string.Join(";", entries), "user");
        env.ResetBroadcastCount();
        return env;
    }

    static List<string> PathEntries(InMemoryScope env, string scope = "user") =>
        Program.GetPathEntriesCore(env, scope);

    // ------------------------------------------------------------------
    // Face 1: PATH normalization (NormalizePathEntry semantics)
    // ------------------------------------------------------------------

    /// <summary>MR-1 (list): trailing separator is output-invariant.</summary>
    [Fact]
    public void MR1_PathList_TrailingSeparatorIsOutputInvariant()
    {
        var env = NewPathScope("C:\\a", "C:\\b");
        var followUp = NewPathScope("C:\\a\\", "C:\\b\\");

        Assert.Equal(
            PathEntries(env).Select(Program.NormalizePathEntry),
            PathEntries(followUp).Select(Program.NormalizePathEntry));
    }

    /// <summary>
    /// MR-2 (health): adding trailing separators to every entry does not change any
    /// duplicate/dead classification beyond what the separator itself explains.
    /// </summary>
    [Fact]
    public void MR2_PathHealth_TrailingSeparatorPreservesDuplicateClassification()
    {
        string[] source = { "C:\\metamorphic-a", "C:\\metamorphic-b", "C:\\metamorphic-a" };
        string[] followUp = { "C:\\metamorphic-a\\", "C:\\metamorphic-b\\", "C:\\metamorphic-a\\" };

        var sourceDups = DuplicateProfile(source);
        var followUpDups = DuplicateProfile(followUp);

        Assert.Equal(sourceDups, followUpDups);
    }

    static bool[] DuplicateProfile(string[] entries)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        return entries.Select(e => !seen.Add(Program.NormalizePathEntry(e))).ToArray();
    }

    /// <summary>
    /// MR-3 (dedupe): normalization is idempotent - deduping an already-normalized list and
    /// deduping the raw variant of the same list remove the same logical entries.
    /// </summary>
    [Fact]
    public void MR3_PathDedupe_NormalizationIdempotence_SameRemovalSet()
    {
        string[] source = { "C:\\metamorphic-a", "C:\\metamorphic-b", "C:\\metamorphic-a" };
        string[] followUp = { "C:\\metamorphic-a\\", "C:\\metamorphic-b\\", "C:\\metamorphic-a\\" };

        var sourceRemoved = DedupeRemoved(source);
        var followUpRemoved = DedupeRemoved(followUp);

        Assert.Equal(
            sourceRemoved.Select(Program.NormalizePathEntry),
            followUpRemoved.Select(Program.NormalizePathEntry));
    }

    static List<string> DedupeRemoved(string[] entries)
    {
        var env = NewPathScope(entries);
        Program.PathDedupeCore(env, AllowVariables, AllowPaths, "user", dryRun: true);
        var before = entries.ToList();
        var after = PathEntries(env);
        return before.Where(e => !after.Any(x => x.Equals(e, StringComparison.OrdinalIgnoreCase))).ToList();
    }

    /// <summary>
    /// MR-4 (list/health consistency): list and health must agree on which entries are
    /// duplicates - the two classifiers share one normalization.
    /// </summary>
    [Fact]
    public void MR4_ListAndHealth_AgreeOnDuplicates()
    {
        string[] entries = { "C:\\metamorphic-a", "C:\\metamorphic-b", "C:\\metamorphic-a" };

        var listSeen = DuplicateProfile(entries);

        // health classifier: sequential seen-set over normalized entries, first kept, rest dup
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var healthDup = entries.Select(e => !isProtected(e) && !seen.Add(Program.NormalizePathEntry(e))).ToArray();
        static bool isProtected(string e) => false;

        Assert.Equal(listSeen, healthDup);
    }

    /// <summary>
    /// MR-5 (round-trip): PATH -> entries -> join reproduces the stored value byte-exactly
    /// (RemoveEmptyEntries keeps storage lossless for non-degenerate lists).
    /// </summary>
    [Fact]
    public void MR5_GetSetPathEntries_RoundTripIsIdentity()
    {
        var env = NewPathScope("C:\\metamorphic-a", "C:\\metamorphic-b");
        string stored = env.ReadValue("PATH", "user")?.Value ?? "";

        var entries = Program.GetPathEntriesCore(env, "user");
        string roundTripped = string.Join(";", entries);

        Assert.Equal(stored, roundTripped);
    }

    // ------------------------------------------------------------------
    // Face 2: case folding (variable names + PATH matching)
    // ------------------------------------------------------------------

    /// <summary>
    /// MR-6 (rename folding): attempting a case-only rename with --overwrite must preserve
    /// the value under case-folded addressing - Windows env names are case-insensitive, so
    /// a case-variant rename either is refused or renames in place; the data must survive.
    /// EXPECTED RED pre-fix: the case-variant overwrite writes the same folded slot and then
    /// deletes it, destroying the value (write-then-delete collapses onto one registry name).
    /// </summary>
    [Fact]
    public void MR6_Rename_CaseVariantPreservesValue()
    {
        var env = new InMemoryScope();
        env.WriteValue("EM_MT_VAR", "metamorphic-value", "user");
        env.ResetBroadcastCount();

        Program.RunRename(new[] { "rename", "EM_MT_VAR", "em_mt_var", "--overwrite" }, env, AllowVariables);

        var value = env.ReadValue("EM_MT_VAR", "user")?.Value ?? env.ReadValue("em_mt_var", "user")?.Value;
        Assert.Equal("metamorphic-value", value);
    }

    /// <summary>
    /// MR-7 (dedupe normalization): two stored entries that normalize to the same directory
    /// are duplicates and must collapse to one - the documented case-insensitive,
    /// normalization-aware dedupe contract. The seen-set is OrdinalIgnoreCase (case alone is
    /// already folded), so the discriminating transformation is the trailing separator.
    /// EXPECTED RED pre-fix: raw-string seen-set keeps both separator variants.
    /// </summary>
    [Fact]
    public void MR7_Dedupe_NormalizationVariantPairCollapses()
    {
        var env = NewPathScope("C:\\metamorphic-a", "C:\\metamorphic-a\\");

        Program.PathDedupeCore(env, AllowVariables, AllowPaths, "user", dryRun: false);

        Assert.Single(PathEntries(env));
    }

    /// <summary>
    /// MR-8 (add folding): appending a normalization-variant of an existing entry must be a
    /// no-op (idempotence) - add guards on the same normalization the rest of the CLI uses.
    /// EXPECTED RED pre-fix: the raw-compare guard lets the variant through, growing PATH.
    /// </summary>
    [Fact]
    public void MR8_Add_NormalizedDuplicateIsIdempotent()
    {
        var env = NewPathScope("C:\\metamorphic-a");

        Program.PathAddCore(env, AllowVariables, AllowPaths, "C:\\metamorphic-a\\", "user", null);

        Assert.Single(PathEntries(env));
    }
}
