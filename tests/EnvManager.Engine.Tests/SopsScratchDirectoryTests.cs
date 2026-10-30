// SopsScratchDirectoryTests.cs - the service-owned sops working directory (#177)
// License: Apache-2.0

using EnvManager.Secrets.Core;
using EnvManager.Secrets.Providers;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Pins the <c>ENVMANAGER_SOPS_SCRATCH_DIR</c> production seam: when the service delegates a
/// rotation it creates a directory it owns, hands it over through this variable, and deletes
/// the whole tree once the CLI child is gone - which is what makes "a stop leaves no sops
/// plaintext behind" reachable, because the CLI's own <c>finally</c> never runs when it is
/// terminated. These assertions are about the provider half of that contract only: the
/// directory it chooses, and the fallback that keeps a hand-run CLI working unchanged.
///
/// No sops binary is involved. The seam is a static directory factory precisely so the
/// placement and the fallback are decidable without a provider backend, a key, or a network,
/// and so a regression here fails on a machine with no sops installed at all.
///
/// The suite runs in a non-parallel collection and restores the ambient variable: environment
/// variables are process-global state, the same constraint <c>LocalAppDataRedirectTests</c>
/// documents.
/// </summary>
[Collection("LocalAppDataRedirectSerial")]
public sealed class SopsScratchDirectoryTests : IDisposable
{
    private const string ScratchRootVariable = "ENVMANAGER_SOPS_SCRATCH_DIR";

    private readonly string? _previous;
    private readonly string _scratchRoot;

    public SopsScratchDirectoryTests()
    {
        _previous = Environment.GetEnvironmentVariable(ScratchRootVariable);
        _scratchRoot = Path.Combine(Path.GetTempPath(), "em177-scratch-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_scratchRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ScratchRootVariable, _previous);
        TryDeleteDirectory(_scratchRoot);
    }

    [Fact]
    public void DelegatedRoot_IsUsedWhenTheServiceSetsTheVariable()
    {
        Environment.SetEnvironmentVariable(ScratchRootVariable, _scratchRoot);

        string dir = SopsProvider.CreateWorkingDirectory(SecretProviderErrors.OpEncrypt);
        try
        {
            AssertDirectlyUnder(_scratchRoot, dir);
            Assert.StartsWith("env-manager-sops-", Path.GetFileName(dir));
            Assert.True(
                Directory.Exists(dir),
                "the working directory has to exist before sops is started: " + dir);
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    [Fact]
    public void PercentTemp_IsUsedWhenTheVariableIsUnset_AndTheFallbackIsLogged()
    {
        TextWriter original = Console.Error;
        var captured = new StringWriter();
        string dir;
        try
        {
            Console.SetError(captured);
            Environment.SetEnvironmentVariable(ScratchRootVariable, null);
            dir = SopsProvider.CreateWorkingDirectory(SecretProviderErrors.OpDecrypt);
        }
        finally
        {
            Console.SetError(original);
            Environment.SetEnvironmentVariable(ScratchRootVariable, _previous);
        }

        try
        {
            AssertDirectlyUnder(Path.GetTempPath(), dir);
            Assert.True(Directory.Exists(dir), "the fallback directory has to exist: " + dir);
            // Both fallback shapes must leave a trace (#177): from inside the provider an
            // un-delegated call and a delegation whose variable never arrived look the same.
            Assert.Contains("has no delegated scratch root", captured.ToString());
        }
        finally
        {
            TryDeleteDirectory(dir);
        }
    }

    /// <summary>
    /// An unusable scratch root is a configuration problem, not a reason to refuse to
    /// encrypt: the provider falls back to %TEMP% and says so on stderr, which is the only
    /// diagnostic channel this process has and the one the service captures.
    /// </summary>
    [Fact]
    public void FallsBackAndWarns_WhenTheScratchRootCannotBeCreated()
    {
        // A file where the root's parent should be: creation fails with an IOException-family
        // error under any identity, which is the "not writable" case without needing a
        // read-only ACL the runner may not be able to set.
        string blocker = Path.Combine(Path.GetTempPath(), "em177-blocker-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "not a directory");
        string unusable = Path.Combine(blocker, "scratch");

        TextWriter original = Console.Error;
        var captured = new StringWriter();
        string dir;
        try
        {
            Console.SetError(captured);
            Environment.SetEnvironmentVariable(ScratchRootVariable, unusable);
            dir = SopsProvider.CreateWorkingDirectory(SecretProviderErrors.OpEncrypt);
        }
        finally
        {
            Console.SetError(original);
            Environment.SetEnvironmentVariable(ScratchRootVariable, _previous);
        }

        try
        {
            AssertDirectlyUnder(Path.GetTempPath(), dir);
            Assert.True(
                Directory.Exists(dir),
                "an unusable scratch root must fall back, never fail closed: " + dir);
            Assert.Contains("not usable", captured.ToString());
        }
        finally
        {
            TryDeleteDirectory(dir);
            try { File.Delete(blocker); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    /// <summary>
    /// The directory has to be the immediate child of the expected parent: a provider that
    /// nested one level deeper would still "be under %TEMP%", and the service deletes a tree,
    /// so an overlap with anything else on disk is the failure mode worth excluding.
    /// </summary>
    private static void AssertDirectlyUnder(string expectedParent, string dir)
    {
        string parent = Path.GetDirectoryName(dir) ?? string.Empty;
        Assert.True(
            string.Equals(
                expectedParent.TrimEnd(Path.DirectorySeparatorChar),
                parent.TrimEnd(Path.DirectorySeparatorChar),
                StringComparison.OrdinalIgnoreCase),
            "expected the working directory " + dir + " directly under " + expectedParent);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
