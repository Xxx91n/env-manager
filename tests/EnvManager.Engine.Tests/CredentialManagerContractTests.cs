// CredentialManagerContractTests.cs - CredentialManagerProvider contract mount (ticket 10; L1 via issue 15)
// License: Apache-2.0

using System.Runtime.InteropServices;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="CredentialManagerProvider"/>. Backend-independent
/// assertions always run. The backend-dependent assertions run against the REAL
/// Windows Credential Manager (the provider's actual backend: advapi32 CredWriteW /
/// CredReadW with a DPAPI-encrypted blob) - no container or cloud credential needed;
/// they skip on non-Windows hosts. Entries are written under the EnvManager
/// namespace with fixed smoke names and deleted again in the finally block.
/// </summary>
public sealed class CredentialManagerContractTests : SecretProviderContractTests
{
    public CredentialManagerContractTests()
        : base(new SkippedProviderHarness(() => new CredentialManagerProvider()))
    {
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void RoundTrip_EncryptThenDecrypt_ReturnsPlaintext()
    {
        AssertWindowsPlatform();
        var harness = new CredentialManagerL1Harness();
        try
        {
            AssertRoundTrip_EncryptThenDecrypt_ReturnsPlaintext(harness);
        }
        finally
        {
            CleanupTargets(harness);
        }
    }

    [SkippableFact]
    [Trait("Category", "L1")]
    public void PlaintextNotEmbedded_EncryptOmitsPlaintext()
    {
        AssertWindowsPlatform();
        var harness = new CredentialManagerL1Harness();
        try
        {
            AssertPlaintextNotEmbedded_EncryptOmitsPlaintext(harness);
        }
        finally
        {
            CleanupTargets(harness);
        }
    }

    private static void AssertWindowsPlatform()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            throw new Xunit.SkipException("credential-manager L1 smoke needs Windows (advapi32 Credential Manager backend)");
        }
    }

    private static void CleanupTargets(CredentialManagerL1Harness harness)
    {
        // best-effort cleanup of the two well-known smoke targets (the canary test
        // only Encrypts, so only the round-trip target exists in practice)
        foreach (var target in new[] { CredentialManagerL1Harness.Target("em-contract-roundtrip"), CredentialManagerL1Harness.Target("em-contract-context") })
        {
            try { CredentialManagerL1Harness.DeleteCredential(target); } catch (Exception) { }
        }
    }
}
