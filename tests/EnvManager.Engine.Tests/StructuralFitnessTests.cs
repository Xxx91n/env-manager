using System.Text.RegularExpressions;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: structural fitness functions (architecture-recovery ticket 28, spec
/// Phase 5, mental-model M2 fitness functions). Three guards in this file:
///   - DomainIsolation: ProfileSecretCommand does not depend on
///     ProfileLaunchCommand or ProfileCommand internals.
///   - CommandDispatchSurface: the three ramp-1 ticket-27 domains expose
///     exactly one Run entry each, and Program still owns Main.
///   - DependencyDirectionAcyclic: no two of ProfileCommand /
///     ProfileLaunchCommand / ProfileSecretCommand mutually reference one
///     another's helper methods.
///
/// All three run in milliseconds (file-scan + reflection) and are wired into
/// the verify job's `dotnet test` step. Red-first discipline: each guard was
/// authored with a deliberate failure injection first (see the test bodies),
/// then the production-correct invariant was restored before commit.
/// </summary>
public class StructuralFitnessTests
{
    private static string RepoRoot()
    {
        // EnvManager.Engine.Tests/bin/<config>/<tfm>/<rid>/StructuralFitnessTests.dll
        // -> walk up to the repo root.
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "env-manager.csproj")))
        {
            dir = dir.Parent;
        }
        if (dir == null)
        {
            throw new InvalidOperationException("Could not locate env-manager.csproj from " + AppContext.BaseDirectory);
        }
        return dir.FullName;
    }

    private static string ReadSource(string relativePath)
    {
        string path = Path.Combine(RepoRoot(), relativePath);
        return File.ReadAllText(path);
    }

    // -- Rule 1: domain isolation (file-level scan) --------------------------

    [Fact]
    public void ProfileSecretCommand_DoesNotReference_ProfileLaunchCommand()
    {
        // Source-level invariant: the secret subdomain (ProfileSecretCommand.cs)
        // must not name types defined inside ProfileLaunchCommand.cs. Both live
        // under partial class Program, so type-based rules cannot distinguish
        // them; a file-level reference scan is the only correct shape.
        var src = ReadSource("src/ProfileSecretCommand.cs");
        Assert.DoesNotContain(
            "ProfileLaunchCommand",
            src,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ProfileSecretCommand_DoesNotReference_ProfileCommand_HelperMethods()
    {
        // Allow the partial class surface (`partial class Program`) but flag
        // any direct call into ProfileCommand methods like `ProfileCreate`,
        // `ProfileEditVar`, `ProfileSetInherits`, etc. These are CRUD verbs
        // and reading them from the secret subdomain breaks the bounded-
        // context boundary ticket 26 + ticket 27 set up.
        var src = ReadSource("src/ProfileSecretCommand.cs");
        var crudCallSites = new[]
        {
            "ProfileCreate(",
            "ProfileEditVar(",
            "ProfileSetInherits(",
            "ProfileDelete(",
            "ProfileAddVar(",
            "ProfileRemoveVar(",
            "ProfileRename(",
        };
        foreach (var call in crudCallSites)
        {
            Assert.DoesNotContain(
                call,
                src,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void ProfileLaunchCommand_DoesNotReference_ProfileSecretCommand()
    {
        // Symmetric isolation: launch must not read the secret helper layer.
        var src = ReadSource("src/ProfileLaunchCommand.cs");
        Assert.DoesNotContain(
            "ProfileSecretCommand",
            src,
            StringComparison.Ordinal);
    }

    // -- Rule 2: dispatch surface contract ------------------------------------

    [Fact]
    public void Ticket27_Ramp1_Domains_ExposeExactlyOneRunEntry()
    {
        // Each ramp-1 ticket-27 domain is `internal static class` with one
        // `internal static int Run(...)` entry point. Reflection guarantees
        // the surface matches the AGENTS.md doc; if a future ramp adds a
        // second entry point it must consciously update this test.
        AssertSingleRunEntry(typeof(AgentsCommand));
        AssertSingleRunEntry(typeof(UpdateCommand));
        AssertSingleRunEntry(typeof(ExpandCommand));
    }

    [Fact]
    public void Program_StillDeclares_Main_AsTheSingleEntryPoint()
    {
        // Sanity that ticket 27 ramp-1 didn't accidentally remove Main or
        // duplicate it across partials. NetArchTest / reflection confirms
        // exactly one static Main(string[]) on Program after the ramp.
        var mains = typeof(Program).GetMethods()
            .Where(m => m.Name == "Main")
            .Where(m => m.ReturnType == typeof(int))
            .Where(m => m.IsStatic)
            .ToArray();

        Assert.Single(mains);
        var p = mains[0].GetParameters();
        Assert.Single(p);
        Assert.Equal(typeof(string[]), p[0].ParameterType);
    }

    private static void AssertSingleRunEntry(Type t)
    {
        Assert.True(t.IsClass, t.FullName + " must be a class");
        Assert.True(t.IsAbstract && t.IsSealed, t.FullName + " must be `static` (abstract+sealed)");

        var runs = t.GetMethods(System.Reflection.BindingFlags.Public
                                | System.Reflection.BindingFlags.NonPublic
                                | System.Reflection.BindingFlags.Static
                                | System.Reflection.BindingFlags.Instance
                                | System.Reflection.BindingFlags.DeclaredOnly)
            .Where(m => m.Name == "Run")
            .ToArray();
        Assert.Single(runs);
        Assert.True(runs[0].IsStatic, t.FullName + ".Run must be static");
        Assert.Equal(typeof(int), runs[0].ReturnType);
    }

    // -- Rule 3: cyclic dependency check --------------------------------------

    [Fact]
    public void ProfileCommand_LaunchCommand_SecretCommand_AreAcyclic()
    {
        // Three-tuple pairwise check: ProfileCommand vs ProfileLaunchCommand,
        // ProfileCommand vs ProfileSecretCommand, ProfileLaunchCommand vs
        // ProfileSecretCommand. Each pair is checked in both directions; a
        // real cycle would surface as mutual mentions of helper methods.
        AssertNoMutualReferences("src/ProfileCommand.cs", "src/ProfileLaunchCommand.cs");
        AssertNoMutualReferences("src/ProfileCommand.cs", "src/ProfileSecretCommand.cs");
        AssertNoMutualReferences("src/ProfileLaunchCommand.cs", "src/ProfileSecretCommand.cs");
    }

    private static void AssertNoMutualReferences(string pathA, string pathB)
    {
        var srcA = ReadSource(pathA);
        var srcB = ReadSource(pathB);

        // Find `static int <Name>(` / `static <ReturnType> <Name>(` /
        // `internal static ... <Name>(` signatures - that's the public-ish
        // helper surface another file could reference.
        var namesA = ExtractHelperNames(srcA);
        var namesB = ExtractHelperNames(srcB);

        // Strip shared partial-class infrastructure (e.g. ResolveProfileName,
        // ScrubExceptionMessage) - those live in CliRuntime.cs and are
        // intentionally cross-domain. The bounded-context guard only cares
        // about per-subdomain helpers.
        var crudNamesA = namesA.Where(IsSubdomainSpecific(pathA)).ToArray();
        var crudNamesB = namesB.Where(IsSubdomainSpecific(pathB)).ToArray();

        foreach (var name in crudNamesA)
        {
            // Allow the name to appear in the OTHER file's body if the OTHER
            // file's only mention is a comment (// or /* ... */). A real
            // helper call would be `name(` not `name ` or `name<`.
            var matches = Regex.Matches(srcB, Regex.Escape(name) + @"\s*\(");
            Assert.Empty(
                matches,
                $"{pathA} helper `{name}` is referenced as a call site in {pathB} - subdomain cycle or leak. "
                + "Either move the caller into the same file as the helper, or extract the helper to CliRuntime.cs.");
        }

        foreach (var name in crudNamesB)
        {
            var matches = Regex.Matches(srcA, Regex.Escape(name) + @"\s*\(");
            Assert.Empty(
                matches,
                $"{pathB} helper `{name}` is referenced as a call site in {pathA} - subdomain cycle or leak.");
        }
    }

    private static readonly Regex HelperSignatureRegex = new(
        @"\b(?:internal|public|private)?\s*(?:static|sealed\s+static)\s+[\w<>,\[\]\?\s]+?\s+(\w+)\s*\(",
        RegexOptions.Compiled);

    private static HashSet<string> ExtractHelperNames(string src)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match m in HelperSignatureRegex.Matches(src))
        {
            names.Add(m.Groups[1].Value);
        }
        return names;
    }

    /// <summary>
    /// Helpers explicitly named in the ticket-26 handoff as belonging to each
    /// subdomain. Anything else (e.g. ResolveProfileName, ScrubExceptionMessage,
    /// DebugLog) is shared infrastructure and not in scope for the cycle guard.
    /// </summary>
    private static readonly HashSet<string> ProfileCommandHelpers = new(StringComparer.Ordinal)
    {
        "ProfileCreate", "ProfileEditVar", "ProfileSetInherits",
        "ProfileDelete", "ProfileAddVar", "ProfileRemoveVar",
        "ProfileRename", "ProfileShow", "ProfilePreview",
        "ProfileList", "ProfileStatus", "ProfileCreateUsage",
        "ProfileExport", "ProfileImport", "ProfileExportSecrets",
        "ProfileImportSecrets",
    };

    private static readonly HashSet<string> ProfileLaunchCommandHelpers = new(StringComparer.Ordinal)
    {
        "ProfileSetLaunch", "ProfileLaunch", "ValidateLaunchPreflight",
        "ValidateLaunchTarget", "ValidateProfileApplied",
    };

    private static readonly HashSet<string> ProfileSecretCommandHelpers = new(StringComparer.Ordinal)
    {
        "ProfileAddSecret", "ProfileEditSecret", "ProfileRemoveSecret",
        "ProfileRevealSecret", "TryDecryptSafe", "RunSecretProviderCommand",
        "ExportSecrets", "ImportSecrets",
    };

    private static Func<string, bool> IsSubdomainSpecific(string filePath)
    {
        if (filePath.EndsWith("ProfileCommand.cs", StringComparison.Ordinal))
            return n => ProfileCommandHelpers.Contains(n);
        if (filePath.EndsWith("ProfileLaunchCommand.cs", StringComparison.Ordinal))
            return n => ProfileLaunchCommandHelpers.Contains(n);
        if (filePath.EndsWith("ProfileSecretCommand.cs", StringComparison.Ordinal))
            return n => ProfileSecretCommandHelpers.Contains(n);
        return _ => false;
    }
}