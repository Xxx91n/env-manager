// L1ContainerCollection.cs - serialize container-backed test classes (issue 15)
// License: Apache-2.0
//
// Vault/LocalStack/Lowkey images pull and start in parallel when xUnit runs the mounts
// in different collections; on shared CI runners the concurrent pulls starve the Vault
// fixture's wait strategy and blew the job budget (CI 2026-09-04). One collection =
// container fixtures start one after another.

using Xunit;

namespace EnvManager.Engine.Tests;

[CollectionDefinition(Name)]
public sealed class L1ContainerCollection
{
    public const string Name = "em-l1-containers";
}
