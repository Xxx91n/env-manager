using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: PATH entry parsing/dedupe (NormalizePathEntry).
/// Environment expansion uses process-scoped variables set and cleared by the
/// test itself - no dependency on machine environment state.
/// </summary>
public class PathNormalizeTests
{
    [Fact]
    public void Positive_TrimsTrailingSeparators()
    {
        Assert.Equal("C:\\Tools\\A", Program.NormalizePathEntry("C:\\Tools\\A\\"));
    }

    [Fact]
    public void Positive_TrimsWhitespaceAndMixedSeparators()
    {
        Assert.Equal("C:/A/B", Program.NormalizePathEntry("  C:/A/B/  "));
    }

    [Fact]
    public void Boundary_ExpandsProcessEnvironmentVariable()
    {
        Environment.SetEnvironmentVariable("EM_TEST_TARGET", "C:\\em-test-target", EnvironmentVariableTarget.Process);
        try
        {
            Assert.Equal("C:\\em-test-target", Program.NormalizePathEntry("%EM_TEST_TARGET%\\"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("EM_TEST_TARGET", null, EnvironmentVariableTarget.Process);
        }
    }

    [Fact]
    public void Boundary_RootDriveTrailingSeparatorBecomesBare()
    {
        Assert.Equal("C:", Program.NormalizePathEntry("C:\\"));
    }

    [Fact]
    public void Boundary_PreservesCaseForCallerComparison()
    {
        Assert.Equal("C:\\Windows", Program.NormalizePathEntry("C:\\Windows\\"));
    }
}
