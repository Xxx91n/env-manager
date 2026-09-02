// SecretProviderContractComplianceTests.cs - contract compliance gate (ticket 10, architecture-recovery)
// License: Apache-2.0

using System.Reflection;

using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Reflection compliance gate (EF Core Specification-Tests style): every concrete
/// <see cref="ISecretProvider"/> implementation must map to exactly one concrete
/// <see cref="SecretProviderContractTests"/> subclass. Adding a ninth provider without a
/// matching mount turns this gate red, so contract coverage cannot silently rot.
/// </summary>
public sealed class SecretProviderContractComplianceTests
{
    [Fact]
    public void EveryProviderImplementation_HasExactlyOneContractMount()
    {
        Assembly production = typeof(ISecretProvider).Assembly;
        Assembly tests = typeof(SecretProviderContractTests).Assembly;

        var implementations = production.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ISecretProvider).IsAssignableFrom(t))
            .OrderBy(t => t.FullName)
            .ToList();

        var mounts = tests.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(SecretProviderContractTests).IsAssignableFrom(t))
            .ToList();

        Assert.NotEmpty(implementations);

        foreach (var impl in implementations)
        {
            var matches = mounts
                .Where(m => ((SecretProviderContractTests)Activator.CreateInstance(m)!).ProviderType == impl)
                .ToList();

            Assert.True(
                matches.Count == 1,
                $"ISecretProvider implementation '{impl.Name}' must map to exactly one contract mount; found {matches.Count}. " +
                "Add a sealed SecretProviderContractTests subclass that creates this provider.");
        }
    }

    [Fact]
    public void EveryContractMount_MapsToARealImplementation()
    {
        Assembly production = typeof(ISecretProvider).Assembly;
        Assembly tests = typeof(SecretProviderContractTests).Assembly;

        var implementations = production.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(ISecretProvider).IsAssignableFrom(t))
            .ToList();

        var mounts = tests.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract && typeof(SecretProviderContractTests).IsAssignableFrom(t))
            .ToList();

        foreach (var mount in mounts)
        {
            var providerType = ((SecretProviderContractTests)Activator.CreateInstance(mount)!).ProviderType;

            Assert.True(
                implementations.Contains(providerType),
                $"Contract mount '{mount.Name}' maps to '{providerType.Name}', which is not an ISecretProvider implementation.");
        }
    }
}
