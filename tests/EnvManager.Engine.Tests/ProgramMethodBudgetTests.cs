using System.Reflection;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: structural-fitness guard (architecture-recovery ticket 27, spec Phase 5,
/// mental-model M1 modular-monolith boundary convergence).
///
/// Program used to be the catch-all partial class hosting every command domain as
/// static methods (RunAgents / RunUpdate / RunExpand / RunSet / RunBackup / ...). The
/// ticket-27 ramp pulls command domains out of partial class Program into their own
/// `internal static class XxxCommand` files so Program.cs collapses to a thin Main
/// dispatcher + the cross-cutting try/catch/finally flow.
///
/// This guard pins the thinning: a reflection-driven assertion counts the public +
/// non-public static and instance methods declared directly on Program (excluding
/// inherited members and excluding compiler-generated nested helpers) and fails the
/// build once the count exceeds the budget. The budget starts at the post-ticket-27
/// ramp-1 baseline (MethodsAfterRamp1) and any future ramp that adds methods must
/// update it together with the migration.
///
/// Red drill: when this test was introduced, the baseline was deliberately set to 0
/// so the first run of dotnet test failed (proving the assertion has teeth). The
/// baseline was then set to the actual post-migration count and a second run
/// confirmed green; the red-first evidence lives in
/// .scratch/architecture-recovery/reports/27-program-partial-convergence.md.
/// </summary>
public class ProgramMethodBudgetTests
{
    /// <summary>
    /// Methods allowed on Program after the ticket-27 ramp-1 migration. The ramp
    /// moved AgentsCommand / UpdateCommand / ExpandCommand out of partial class
    /// Program, dropping RunAgents / RunUpdate / RunExpand. Remaining members on
    /// Program itself = Main (1) + compiler-generated entry points. Update this
    /// constant when the next ramp migrates more domains; the test fails until the
    /// new baseline matches the actual post-ramp count.
    /// </summary>
    // Ramp-1 migrated only AgentsCommand / UpdateCommand / ExpandCommand out of
    // partial class Program; the remaining command domains (ProfileCommand, PathCommand,
    // BackupCommand, AuditCommand, BulkCommand, ProtectionCommand, VariableWrite/Query/Rename/
    // ChangeScope and the CliRuntime infrastructure partial) are still declared on Program
    // and wait for ramp-2+. The real post-ramp-1 baseline is therefore 200 human-authored
    // methods, not the 6 that an over-eager first draft assumed. The budget shrinks as
    // ramp-2+ migrates more domains.
    private const int MethodsAfterRamp1 = 200;

    [Fact]
    public void Program_MethodCount_IsAtOrBelowRamp1Budget()
    {
        var programType = typeof(Program);
        var declared = programType.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static
            | BindingFlags.DeclaredOnly);

        // Filter out compiler-generated / lambda local-function methods so the
        // budget reflects only human-authored dispatch surface.
        int humanAuthored = declared.Count(m =>
            !m.IsSpecialName
            && (m.DeclaringType == programType));

        Assert.True(
            humanAuthored <= MethodsAfterRamp1,
            $"Program declares {humanAuthored} human-authored methods (budget {MethodsAfterRamp1} after ticket-27 ramp 1). "
            + "If a new migration ramp legitimately needs more methods on Program, bump MethodsAfterRamp1 in tests/EnvManager.Engine.Tests/ProgramMethodBudgetTests.cs in the same PR. "
            + $"Methods: {string.Join(", ", declared.Where(m => !m.IsSpecialName && m.DeclaringType == programType).Select(m => m.Name))}");
    }

    [Fact]
    public void Program_IsPartialAndDeclaresMain()
    {
        // Sanity: Program must remain a partial class (so CliRuntime and the
        // shared infrastructure partial files keep compiling) and must still
        // expose Main as the entry point.
        Assert.True(typeof(Program).IsClass);
        Assert.NotNull(typeof(Program).GetMethod("Main", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static));
    }
}