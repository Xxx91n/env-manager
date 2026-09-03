// OnePasswordContractTests.cs - OnePasswordProvider contract mount (ticket 10; L1 via issue 15)
// License: Apache-2.0

using System.Runtime.InteropServices;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="OnePasswordProvider"/>. Backend-independent
/// assertions always run.
///
/// L1 coverage (issue 15): the REAL op CLI (2.39.0, pinned; downloaded to the OS
/// temp session dir when absent) runs the provider's Decrypt path against an
/// in-process 1Password Connect REST stub on localhost - the community-documented
/// Connect-mock pattern, since no offline 1Password backend exists. The full
/// Encrypt->Decrypt round trip is NOT convertible without cloud credentials: the
/// op CLI refuses item creation over Connect by design (live-verified v2.39.0:
/// "op item create" doesn't work with Connect. Please unset OP_CONNECT_HOST and
/// OP_CONNECT_TOKEN to use this command.), and item creation otherwise requires an
/// authenticated 1Password account (cloud credential). Those two assertions stay
/// Skipped with that evidence-based reason; the decrypt-direction smoke proves the
/// real binary discovery, argument marshaling, and envelope parsing against a
/// localhost backend double.
/// </summary>
public sealed class OnePasswordContractTests : SecretProviderContractTests
{
    public OnePasswordContractTests()
        : base(new SkippedProviderHarness(() => new OnePasswordProvider()))
    {
    }

    [Fact(Skip = "op item create is refused by the 1Password CLI over Connect (live-verified v2.39.0: 'op item create' doesn't work with Connect); item creation requires an authenticated 1Password account = cloud credential, out of scope for the no-cloud L1 matrix - target L2.")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext() =>
        AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext();

    [Fact(Skip = "op item create is refused by the 1Password CLI over Connect (live-verified v2.39.0); Encrypt-side canary requires an authenticated 1Password account = cloud credential - target L2.")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext() =>
        AssertPlaintextNotEmbedded_EncryptOmitsPlaintext();

    [SkippableFact]
    [Trait("Category", "L1")]
    public void DecryptDirection_RealOpCli_ReadsConnectMockSeed()
    {
        using var scope = OpScope.Install();

        // neutral seed straight into the Connect stub, then the provider's real
        // Decrypt (op item get --field password --reveal over Connect) reads it back
        const string plain = "op-connect-decrypt-smoke-42";
        const string context = "em-contract-decryptsmoke";
        var envelope = scope.Harness.SeedSecret(plain, context);
        // Diagnostic: run the provider's exact op invocation via the harness and surface
        // op's own stderr in the failure message if it does not return the plaintext.
        var parsed = SecretEnvelope.TryParse(envelope)!;
        var parts = parsed.TargetName!.Split('|');
        string direct;
        try
        {
            direct = scope.Harness.RunProviderArgsDebug(parts);
        }
        catch (Exception ex)
        {
            Assert.Fail("harness-direct op call failed: " + ex.Message);
            return;
        }
        var provider = scope.Harness.CreateProvider();
        try
        {
            Assert.Equal(plain, provider.Decrypt(envelope, context));
        }
        catch (Exception ex)
        {
            Assert.Fail("provider Decrypt failed but harness-direct returned: " + direct + " | provider error: " + ex.Message);
        }
    }

    private sealed class OpScope : IDisposable
    {
        internal OnePasswordL1Harness Harness { get; }

        private readonly OpConnectMock _mock;

        private OpScope(OpConnectMock mock, OnePasswordL1Harness harness)
        {
            _mock = mock;
            Harness = harness;
        }

        internal static OpScope Install()
        {
            var opBinary = L1ToolProvisioner.TryGetOpBinary();
            if (opBinary is null)
            {
                // host lacks op: the pinned download is a network side effect, so it
                // only happens with the matrix opt-in
                L1MatrixAffinity.AssertAffinityOrSkip("op CLI pinned-release download");
                opBinary = L1ToolProvisioner.EnsureOpBinary();
            }
            if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
            {
                // Local Windows dev hosts only: op's desktop-app integration probe wedges
                // even with OP_DISABLE_DESKTOP_APP=1/OP_BIOMETRIC_UNLOCK_ENABLED=false on
                // machines with the proxy+AV combination (observed: request storm through
                // the mock, op never converges; standalone repro shows op blocking BEFORE
                // the first HTTP request). Ubuntu CI has no desktop app, so Connect mode
                // runs cleanly there; Windows evidence and the exact repro are in report 15.
                throw new Xunit.SkipException(
                    "1password L1 decrypt smoke is disabled on Windows dev hosts: op's desktop-app " +
                    "integration probe wedges before HTTP on this machine class (OP_DISABLE_DESKTOP_APP=1 " +
                    "and OP_BIOMETRIC_UNLOCK_ENABLED=false do not unblock it locally; runs on the ubuntu L1 lane).");
            }
            if (opBinary is null)
            {
                throw new Xunit.SkipException("1password L1 smoke needs the op CLI (host lookup and the pinned 2.39.0 download both unavailable)");
            }
            // First-exec warmup: fresh binaries can stall on AV/SmartScan on some Windows
            // hosts (observed nondeterministically in dev); retry until --version answers,
            // skip with the reason if it never does.
            var warmOk = false;
            for (var attempt = 0; attempt < 5 && !warmOk; attempt++)
            {
                try
                {
                    var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = opBinary,
                        Arguments = "--version",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true,
                    });
                    if (probe is null) break;
                    _ = probe.StandardOutput.ReadToEnd();
                    _ = probe.StandardError.ReadToEnd();
                    warmOk = probe.WaitForExit(20000) && probe.ExitCode == 0;
                    if (!warmOk) { try { probe.Kill(); } catch { } }
                }
                catch (Exception) { }
            }
            if (!warmOk)
            {
                throw new Xunit.SkipException("1password L1 smoke: op --version warmup did not complete (host stalled the fresh binary; retry the run)");
            }
            var mock = L1ContainerFixtures.ConnectMock.Value;
            // op v2.3x consults the Windows system proxy (registry) even for loopback
            // Connect targets; a local Clash-style proxy then holds/mangles the exchange
            // (observed: request storm through the proxy, op never converges). Force the
            // loopback out of any proxy path for the test process.
            Environment.SetEnvironmentVariable("HTTP_PROXY", "");
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "");
            Environment.SetEnvironmentVariable("NO_PROXY", "*");
            Environment.SetEnvironmentVariable("OP_PATH", opBinary);
            // op v2.3x probes the 1Password desktop app (named-pipe delegated session) before
            // any HTTP when the integration isn't explicitly disabled; without a running app
            // this probe stalls forever (community-documented NmRequestDelegatedSession hang).
            Environment.SetEnvironmentVariable("OP_DISABLE_DESKTOP_APP", "1");
            Environment.SetEnvironmentVariable("OP_BIOMETRIC_UNLOCK_ENABLED", "false");
            Environment.SetEnvironmentVariable("OP_VAULT", "EnvManager");
            // op reads these directly (provider forwards OP_ACCOUNT/OP_SERVICE_ACCOUNT_TOKEN
            // only, and neither is set - Connect mode is driven by the inherited env)
            Environment.SetEnvironmentVariable("OP_CONNECT_HOST", mock.ConnectHost);
            Environment.SetEnvironmentVariable("OP_CONNECT_TOKEN", mock.Token);
            return new OpScope(mock, new OnePasswordL1Harness(opBinary, mock));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("OP_PATH", null);
            Environment.SetEnvironmentVariable("OP_VAULT", null);
            Environment.SetEnvironmentVariable("OP_CONNECT_HOST", null);
            Environment.SetEnvironmentVariable("OP_CONNECT_TOKEN", null);
        }
    }
}
