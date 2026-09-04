// LocalAppDataRedirectTests.cs - CI user-state isolation seam (architecture-recovery issue 24)
// License: Apache-2.0

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Pins the <c>ENVMANAGER_LOCALAPPDATA</c> production seam (architecture-recovery issue 24):
/// when the variable is set, <c>Program.LocalAppDataRoot</c> returns it and every user-state
/// file resolution composed on that root (AppDataDirectory, profiles.json, audit, secret
/// mounts, provider config, provider hash, protection stores) follows the redirect; when
/// unset or empty, the resolution is byte-identical to the pre-issue-24 shell folder.
/// This is the cross-process equivalent of the in-process SetAppDataDirectoryForTests seam
/// (ticket 18): CI integration suites set the variable in the CLI subprocess environment so
/// profiles.json and friends land in the job-private directory, not the runner user profile.
///
/// The assertions deliberately stop at <c>LocalAppDataRoot</c>: the composed path getters
/// (<c>AppDataDirectory</c> etc.) first consult their own test-override fields, which other
/// parallel test classes legitimately mutate — asserting through them here would be flaky.
/// The seam property reads the process environment directly, so it is deterministic.
///
/// The suite runs in a non-parallel collection and restores the ambient variable in a
/// finally block: environment variables are process-global state (AGENTS.md testing rules:
/// Process-scoped and cleared in-test).
/// </summary>
[CollectionDefinition("LocalAppDataRedirectSerial", DisableParallelization = true)]
public sealed class LocalAppDataRedirectSerialCollection;

[Collection("LocalAppDataRedirectSerial")]
public sealed class LocalAppDataRedirectTests
{
    [Fact]
    public void RedirectVariable_WhenSet_IsReturnedAsTheUserStateRoot()
    {
        var redirect = Path.Combine(Path.GetTempPath(), "em24-redirect-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", redirect);

            Assert.Equal(redirect, Program.LocalAppDataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", previous);
        }
    }

    [Fact]
    public void RedirectVariable_WhenUnset_FallsBackToShellLocalApplicationData()
    {
        var previous = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
        try
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", null);

            var shellLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(shellLocal, Program.LocalAppDataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", previous);
        }
    }

    [Fact]
    public void RedirectVariable_WhenEmpty_FallsBackToShellLocalApplicationData()
    {
        var previous = Environment.GetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA");
        try
        {
            // An empty value must behave exactly like an unset value, not like a
            // redirect to the process working directory.
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", string.Empty);

            var shellLocal = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Assert.Equal(shellLocal, Program.LocalAppDataRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ENVMANAGER_LOCALAPPDATA", previous);
        }
    }
}
