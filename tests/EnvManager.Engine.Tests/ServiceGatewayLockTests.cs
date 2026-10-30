using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: the session-scoped write lock versus the `service` IPC gateway (#164).
/// The gateway's classification decision lives in src/CliRuntime.cs; this file measures the
/// consequence rather than restating the argument. Background mode runs
/// env-manager-service.exe inside the caller's session, so the CLI child it spawns for a
/// rotate shares the `Local\` namespace with the gateway that asked for it, while Service
/// mode puts that child in session 0. These tests pin the mutex decision the gateway makes
/// and the contention a same-session delegate meets - the pair of outcomes that differs
/// between the two modes once the namespace is shared. Mutex ownership is tracked per
/// thread, so a second thread stands in for the second process without spawning one.
/// The class joins CliSnapshotSerial for the same reason the other process-global cases do:
/// it asserts on a session-wide kernel object, so it must not share the parallel pool with
/// anything that could take the same lock.
/// </summary>
[Collection("CliSnapshotSerial")]
public class ServiceGatewayLockTests
{
    // Shared with the production classifier: a rename there must not leave this file
    // asserting on a stale literal.
    static string MutexName => Program.MutationMutexName;

    // The gateway's own budget is 30 s; a probe well below it separates "ran" from
    // "waited out the budget" without making the suite slow. 3 s leaves room for thread
    // startup on a loaded runner while still failing well inside the real budget.
    static readonly TimeSpan ProbeBudget = TimeSpan.FromSeconds(3);

    // Taking the holder's end can legitimately wait if a real CLI write is running on the
    // box; only the probe's budget carries an assertion.
    static readonly TimeSpan HolderBudget = TimeSpan.FromSeconds(30);

    // ---- classification: which `service` verbs hold the gateway mutex ----

    public static IEnumerable<object[]> LockFreeServiceArgv() => new[]
    {
        // The one verb whose delegate needs the mutex the gateway used to hold.
        new object[] { new[] { "service", "rotate", "vault-team" } },
        // Probes write nothing and must not queue behind a rotate's budget.
        new object[] { new[] { "service", "status" } },
        new object[] { new[] { "service", "ping" } },
        new object[] { new[] { "service", "health" } },
    };

    [Theory]
    [MemberData(nameof(LockFreeServiceArgv))]
    public void ServiceVerb_TakesNoGatewayLock(string[] argv)
    {
        Assert.False(Program.IsWriteInvocationForFuzz(argv));
    }

    public static IEnumerable<object[]> LockHoldingServiceArgv() => new[]
    {
        // No child, no deadlock: these keep the mutex, which is the only cross-process
        // serializer for the secretMount.json rewrite they trigger.
        new object[] { new[] { "service", "refresh", "vault-team" } },
        new object[] { new[] { "service", "reload" } },
        new object[] { new[] { "service", "shutdown" } },
    };

    [Theory]
    [MemberData(nameof(LockHoldingServiceArgv))]
    public void ServiceVerb_StillTakesTheGatewayLock(string[] argv)
    {
        Assert.True(Program.IsWriteInvocationForFuzz(argv));
    }

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
        new object[] { new[] { "profile", "secret-provider", "rotate" } },
        new object[] { new[] { "path", "add", "C:\\em-test-dir" } },
    };

    [Theory]
    [MemberData(nameof(WriteArgv))]
    public void WriteVerb_IsStillAWriteInvocation(string[] argv)
    {
        Assert.True(Program.IsWriteInvocationForFuzz(argv));
    }

    // ---- the two outcomes that distinguish Background from Service mode ----

    [Fact]
    public void BackgroundRotate_GatewayTakesNoLock_SoItsSameSessionDelegateAcquires()
    {
        // Exactly what Program.Main does for the rotate the GUI issues in Background mode.
        Mutex? gateway = Program.AcquireMutationLock(new[] { "service", "rotate", "vault-team" });
        try
        {
            Assert.Null(gateway);

            // The service's delegated child, same session.
            Assert.True(TryAcquireFromOtherThread(),
                "the delegated rotate must reach the session mutex instead of waiting out the gateway's budget");
        }
        finally
        {
            Release(gateway);
        }
    }

    [Fact]
    public void HeldSessionLock_WouldBlockThatSameDelegate()
    {
        // Falsification of the pre-fix behaviour: with the gateway sitting on the mutex -
        // which is what Main did while it classified every `service` verb as a write - the
        // delegate cannot proceed. Keeps the test above from passing vacuously.
        using var holder = new Mutex(false, MutexName);
        bool owned = false;
        try
        {
            owned = holder.WaitOne(HolderBudget);
        }
        catch (AbandonedMutexException)
        {
            // A previous holder died inside the budget; ownership is granted all the same.
            owned = true;
        }
        Assert.True(owned, "could not take the session mutex as the gateway would");
        try
        {
            Assert.False(TryAcquireFromOtherThread(),
                "a held session mutex must block the same-session delegate - the failure this ticket removed");
        }
        finally
        {
            holder.ReleaseMutex();
        }
    }

    [Fact]
    public void ServiceRefresh_StillTakesTheSessionLock()
    {
        // refresh has no delegate to deadlock against, and it is the reason the mutex stays:
        // the service rewrites secretMount.json, a file another session's CLI can also
        // write, and this gateway is the only party that can serialize the two.
        Mutex? gateway = Program.AcquireMutationLock(new[] { "service", "refresh", "vault-team" });
        try
        {
            Assert.NotNull(gateway);
            Assert.False(TryAcquireFromOtherThread(),
                "a concurrent CLI writer must still wait behind a service refresh");
        }
        finally
        {
            Release(gateway);
        }
    }

    [Fact]
    public void DirectWrite_StillTakesTheSessionLock()
    {
        // The invariant the fix must not weaken: a write verb run through the same entry
        // point still owns the mutex for the life of the call.
        Mutex? direct = Program.AcquireMutationLock(new[] { "set", "EM_TEST_X", "1" });
        try
        {
            Assert.NotNull(direct);
            Assert.False(TryAcquireFromOtherThread(),
                "a second same-session writer must still wait behind a `set`");
        }
        finally
        {
            Release(direct);
        }
    }

    /// <summary>
    /// Waits on a fresh handle from another thread - a faithful stand-in for another
    /// process in the same session, because mutex ownership is tracked per thread.
    /// </summary>
    static bool TryAcquireFromOtherThread()
    {
        bool acquired = false;
        var probe = new Thread(() =>
        {
            using var mutex = new Mutex(false, MutexName);
            try
            {
                acquired = mutex.WaitOne(ProbeBudget);
                if (acquired) mutex.ReleaseMutex();
            }
            catch (AbandonedMutexException)
            {
                // Prior owner died holding it; the wait still granted ownership.
                acquired = true;
                mutex.ReleaseMutex();
            }
        })
        { IsBackground = true };

        probe.Start();
        probe.Join(ProbeBudget + TimeSpan.FromSeconds(5));
        return acquired;
    }

    static void Release(Mutex? mutex)
    {
        if (mutex == null) return;
        mutex.ReleaseMutex();
        mutex.Dispose();
    }
}
