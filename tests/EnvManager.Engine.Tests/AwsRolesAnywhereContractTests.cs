// AwsRolesAnywhereContractTests.cs - AwsRolesAnywhereProvider contract mount
// License: Apache-2.0

using EnvManager;

using Xunit;
using EnvManager.Secrets.Providers;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Contract mount for <see cref="AwsRolesAnywhereProvider"/>. Backend-independent
/// assertions always run. There is no backend-dependent leg here on purpose: AWS
/// validates the certificate chain and revocation server-side, no emulator implements
/// CreateSession, and a certificate enrolled in a real trust anchor cannot live in CI.
/// The offline behaviour that does not need AWS is covered by
/// <see cref="AwsRolesAnywhereOfflineTests"/>; the wire protocol itself is proven by one
/// manual smoke run against a throwaway trust anchor.
/// </summary>
[Collection(L1ContainerCollection.Name)]
public sealed class AwsRolesAnywhereContractTests : SecretProviderContractTests
{
    public AwsRolesAnywhereContractTests()
        : base(new SkippedProviderHarness(() => new AwsRolesAnywhereProvider()))
    {
    }
}
