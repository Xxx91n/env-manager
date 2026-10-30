using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: the session-scoped write lock versus the `service` IPC gateway (#164).
/// The decision under test lives in src/CliRuntime.cs; this file measures what the gateway
/// actually acquires, not how the decision is spelled. Background mode runs
/// env-manager-service.exe inside the caller's session, so the CLI child it spawns for a
/// rotate shares the `Local\` namespace with the gateway that asked for it, while Service
/// mode puts that child in session 0 - which is why a gateway-held lock was invisible in one
/// mode and fatal in the other.
/// The assertions stay at the decision boundary on purpose: they never open the production
/// mutex, because a test that held `Local\EnvManager.RegistryMutation` would depend on the
/// machine's live write activity and could stall a real CLI write. The behavioural half of
/// this evidence runs in the Background E2E
/// (scripts/test-service-kill9-resilience.ps1), where a real rotate meets a real service.
/// </summary>
public class ServiceGatewayLockTests
{
    // ---- no `service` verb may take the gateway lock ----

    public static IEnumerable<object[]> ServiceArgv() => new[]
    {
        // The verb whose delegate needs the mutex the gateway used to take.
        new object[] { new[] { "service", "rotate", "vault-team" } },
        // And the verb whose service-side write must not be serialized by a gateway lock
        // either: holding it across the round-trip puts the gateway in a cycle with the
        // rotate child that is waiting for the same mutex.
        new object[] { new[] { "service", "refresh", "vault-team" } },
        new object[] { new[] { "service", "status" } },
        new object[] { new[] { "service", "ping" } },
        new object[] { new[] { "service", "health" } },
        new object[] { new[] { "service", "reload" } },
        new object[] { new[] { "service", "shutdown" } },
        // Usage-error paths: rejected before any state is touched.
        new object[] { new[] { "service" } },
        new object[] { new[] { "service", "bogus" } },
    };

    [Theory]
    [MemberData(nameof(ServiceArgv))]
    public void ServiceVerb_IsNotAWriteInvocation(string[] argv)
    {
        Assert.False(Program.IsWriteInvocationForFuzz(argv));
    }

    // ---- the write verbs, and the delegate that performs the write, still lock ----

    public static IEnumerable<object[]> WriteArgv() => new[]
    {
        new object[] { new[] { "set", "EM_TEST_X", "1" } },
        new object[] { new[] { "rename", "EM_TEST_A", "EM_TEST_B" } },
        new object[] { new[] { "change-scope", "EM_TEST_A", "system" } },
        new object[] { new[] { "delete", "EM_TEST_A" } },
        new object[] { new[] { "toggle", "EM_TEST_A" } },
        new object[] { new[] { "history", "undo" } },
        new object[] { new[] { "bulk", "import", "vars.json" } },
        new object[] { new[] { "profile", "add-var", "work", "EM_TEST_A", "x" } },
        // The command the service delegates a rotate to: this is the process that takes the
        // mutex for a Background rotate, which is what makes the gateway holding it fatal.
        new object[] { new[] { "profile", "secret-provider", "rotate" } },
        new object[] { new[] { "path", "add", "C:\\em-test-dir" } },
    };

    [Theory]
    [MemberData(nameof(WriteArgv))]
    public void WriteVerb_IsStillAWriteInvocation(string[] argv)
    {
        Assert.True(Program.IsWriteInvocationForFuzz(argv));
    }

    // ---- the decision, through the entry point Main calls ----

    [Fact]
    public void Gateway_AcquiresNothingForTheRequestsItForwards()
    {
        // AcquireMutationLock is what Program.Main calls before dispatch. Null means no mutex
        // was opened, so nothing in this process can block the child the service spawns for
        // the same request - the deadlock this ticket removed, asserted where it starts.
        Assert.Null(Program.AcquireMutationLock(new[] { "service", "rotate", "vault-team" }));
        Assert.Null(Program.AcquireMutationLock(new[] { "service", "refresh", "vault-team" }));
    }
}
