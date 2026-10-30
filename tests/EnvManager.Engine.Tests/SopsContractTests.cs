// SopsContractTests.cs - SopsProvider contract mount
// License: Apache-2.0

using EnvManager;

using Xunit;
using EnvManager.Secrets.Providers;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="SopsProvider"/>. Backend-independent assertions
/// always run. The backend-dependent assertions run against the REAL sops binary
/// with a throwaway age keypair (age is a fully local, offline key system - no
/// cloud, no KMS): sops 3.13.3 + age 1.3.2 pinned, downloaded to the OS temp
/// session dir when the host lacks them. The provider itself shells out to sops
/// exactly as in production; the harness drives sops directly for the neutral
/// seed/read legs.
/// </summary>
public sealed class SopsContractTests : SecretProviderContractTests
{
    public SopsContractTests()
        : base(new SkippedProviderHarness(() => new SopsProvider()))
    {
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext()
    {
        using var scope = SopsScope.Install();
        AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(scope.Harness);
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext()
    {
        using var scope = SopsScope.Install();
        AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(scope.Harness);
    }

    private sealed class SopsScope : IDisposable
    {
        internal SopsL1Harness Harness { get; }

        private SopsScope(SopsL1Harness harness) => Harness = harness;

        /// <summary>
        /// CI lanes that carry the sops evidence set this to 1 after putting the pinned
        /// sops + age on PATH (scripts/install-sops-age.ps1). It makes the round trip
        /// REQUIRED: every path that would otherwise skip fails instead, so a lane that
        /// lost its tools goes red rather than reporting a skipped run as "Passed!".
        /// Independent of EM_L1_MATRIX, because sops + age is a local offline key system
        /// that needs neither Docker nor a cloud credential.
        /// </summary>
        internal const string RequiredEnvVar = "EM_L1_SOPS";

        private static bool Required =>
            string.Equals(Environment.GetEnvironmentVariable(RequiredEnvVar), "1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(Environment.GetEnvironmentVariable(RequiredEnvVar), "true", StringComparison.OrdinalIgnoreCase);

        // One exit for every "cannot run" path. The wording leads with NOT RUN so the
        // skip reason cannot be read as a pass in a log or a TRX.
        private static Exception NotRun(string reason)
        {
            var text = "sops round trip NOT RUN (skipped, not a pass): " + reason;
            return Required
                ? new InvalidOperationException(text + $" - {RequiredEnvVar}=1 makes this test required on this lane")
                : new Xunit.SkipException(text);
        }

        internal static SopsScope Install()
        {
            var bundle = L1ToolProvisioner.TryGetSopsBundle();
            if (bundle is null && !Required)
            {
                // host lacks the tools: downloading them is a network side effect, so
                // it only happens with the matrix opt-in
                L1MatrixAffinity.AssertAffinityOrSkip("sops+age pinned-release download");
                bundle = L1ToolProvisioner.EnsureSopsBundle();
            }
            if (bundle is null)
            {
                throw NotRun(Required
                    ? "sops + age not found on PATH (the lane must run scripts/install-sops-age.ps1 before the tests)"
                    : "needs the sops + age binaries (host PATH lookup and pinned release download both unavailable)");
            }
            var recipients = SopsL1Harness.ExtractPublicKey(bundle.Value.AgeKeyFile);
            if (recipients is null)
            {
                throw NotRun("could not extract the age public key from the generated key file");
            }
            // First-exec warmup: host binaries can stall on AV/SmartScan on some Windows
            // hosts (same observation as the op CLI warmup); retry until --version answers.
            var warmOk = false;
            for (var attempt = 0; attempt < 5 && !warmOk; attempt++)
            {
                try
                {
                    var probe = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = bundle.Value.Sops,
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
                throw NotRun("sops --version warmup did not complete (host stalled the binary; retry the run)");
            }
            Environment.SetEnvironmentVariable("SOPS_PATH", bundle.Value.Sops);
            Environment.SetEnvironmentVariable("SOPS_AGE_KEY_FILE", bundle.Value.AgeKeyFile);
            Environment.SetEnvironmentVariable("SOPS_AGE_RECIPIENTS", recipients);
            return new SopsScope(new SopsL1Harness(bundle.Value.Sops, bundle.Value.AgeKeyFile));
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable("SOPS_PATH", null);
            Environment.SetEnvironmentVariable("SOPS_AGE_KEY_FILE", null);
            Environment.SetEnvironmentVariable("SOPS_AGE_RECIPIENTS", null);
        }
    }
}
