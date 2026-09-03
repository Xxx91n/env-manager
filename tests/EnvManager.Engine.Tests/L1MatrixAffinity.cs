// L1MatrixAffinity.cs - L1 backend-affinity skip logic (issue 15, architecture-recovery)
// License: Apache-2.0
//
// The ticket-10 contract suite pins the backend-independent assertions (fail-closed
// decrypt, malformed-format error) on every ISecretProvider mount. Issue 15 turns the
// backend-dependent assertions (round-trip / plaintext-never) from static Skips into
// real runs against L1 backends:
//
//   - Local, no-container backends (Windows Credential Manager, pwsh SecretStore,
//     sops+age, 1Password op CLI against a localhost Connect mock): whenever the
//     required tooling is present on the host.
//   - Emulator containers (Vault dev server, LocalStack, Lowkey Vault): whenever a
//     Docker-compatible container runtime is reachable and EM_L1_MATRIX=1 opts in.
//
// Everything is affinity-gated: a host that lacks Docker or the CLI tools simply
// skips with the reason (xunit.skippablefact), exactly like the previous static Skip,
// but a provisioned host - including the CI ubuntu runner - runs the full matrix
// without any cloud credential.

using Xunit;

namespace EnvManager.Engine.Tests;

internal static class L1MatrixAffinity
{
    /// <summary>
    /// Master opt-in for the container-backed half of the L1 matrix. When unset/0 the
    /// container affinity is denied even on Docker hosts (a plain dotnet test never
    /// pulls images as a side effect).
    /// </summary>
    internal const string MatrixEnvVar = "EM_L1_MATRIX";

    internal const string Category = "L1";

    /// <summary>Fail the test (not skip) when this is set but the runtime is unusable.</summary>
    internal const string StrictEnvVar = "EM_L1_STRICT";

    private static readonly Lazy<bool> DockerAvailable = new(ProbeDocker);

    private static bool ProbeDocker()
    {
        try
        {
            using var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "docker",
                Arguments = "version --format {{.Server.Version}}",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            });
            if (probe is null)
            {
                return false;
            }

            _ = probe.StandardOutput.ReadToEnd();
            _ = probe.StandardError.ReadToEnd();
            return probe.WaitForExit(15000) && probe.ExitCode == 0;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // docker binary not on PATH
            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Affinity: container runtime reachable AND EM_L1_MATRIX opted in.</summary>
    internal static bool IsDockerAffinityAvailable(out string unavailableReason)
    {
        unavailableReason = "";
        var optedIn = Environment.GetEnvironmentVariable(MatrixEnvVar);
        if (!string.Equals(optedIn, "1", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(optedIn, "true", StringComparison.OrdinalIgnoreCase))
        {
            unavailableReason = $"container L1 matrix is opt-in: set {MatrixEnvVar}=1";
            return false;
        }

        if (!DockerAvailable.Value)
        {
            unavailableReason = "no reachable Docker-compatible container runtime";
            return false;
        }

        return true;
    }

    /// <summary>
    /// One final pre-start check right before a container starts. Throws
    /// SkipTestException on missing affinity so a slow race (Docker dying mid-run)
    /// still skips instead of burning the suite red; throws fail-closed when
    /// EM_L1_STRICT=1.
    /// </summary>
    internal static void AssertAffinityOrSkip(string backend)
    {
        if (IsDockerAffinityAvailable(out var reason))
        {
            return;
        }

        var strict = string.Equals(Environment.GetEnvironmentVariable(StrictEnvVar), "1", StringComparison.OrdinalIgnoreCase);
        if (strict)
        {
            throw new InvalidOperationException($"L1 backend '{backend}' is required (EM_L1_STRICT=1) but unavailable: {reason}");
        }

        throw new Xunit.SkipException($"L1 backend '{backend}' unavailable: {reason}");
    }

    /// <summary>Skip the current test with the reason (xunit.skippablefact).</summary>
    internal static void Skip(string reason) => throw new Xunit.SkipException(reason);
}
