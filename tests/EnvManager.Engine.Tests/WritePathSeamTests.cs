using EnvManager;
using Microsoft.Win32;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// Domain: write-path command behavior through the IEnvironmentScope seam
/// (architecture-recovery issue 03). Every command core runs against InMemoryScope with
/// synthetic protection predicates - no real registry access, no machine state.
/// Protected-rejection, success-write, error-code, and broadcast-timing assertions lock the
/// hard boundaries: protected entries are untouchable and rename/change-scope never
/// delete before the write is verified.
/// </summary>
public class WritePathSeamTests
{
    // None of these names collide with real protected lists; the synthetic predicates
    // below are what the tests assert against.
    const string FreeName = "EM_TEST_FOO";
    const string LockedName = "EM_TEST_LOCKED";
    const string LockedPath = "C:\\em-test-locked-dir";

    static Func<string, string, bool> LockLockedName() =>
        (name, _) => name.Equals(LockedName, StringComparison.OrdinalIgnoreCase);

    static Func<string, bool> LockLockedPath() =>
        entry => entry.TrimEnd('\\', '/').Equals(LockedPath, StringComparison.OrdinalIgnoreCase);

    /// <summary>Wraps InMemoryScope and records seam operation order for contract tests.</summary>
    sealed class RecordingScope : IEnvironmentScope
    {
        public readonly List<string> Ops = new();
        readonly InMemoryScope _inner = new();

        public IReadOnlyList<EnvVariable> ListVariables(string scope)
        {
            Ops.Add("list");
            return _inner.ListVariables(scope);
        }

        public EnvValueSnapshot? ReadValue(string name, string scope)
        {
            Ops.Add("read:" + name + "@" + scope);
            return _inner.ReadValue(name, scope);
        }

        public bool Exists(string name, string scope) => _inner.Exists(name, scope);

        public WriteOutcome WriteValue(string name, string? value, string scope)
        {
            Ops.Add("write:" + name + "@" + scope);
            return _inner.WriteValue(name, value, scope);
        }

        public void WriteValuePreservingKind(string name, string value, string scope)
        {
            Ops.Add("write:" + name + "@" + scope);
            _inner.WriteValuePreservingKind(name, value, scope);
        }

        public bool DeleteValue(string name, string scope)
        {
            Ops.Add("delete:" + name + "@" + scope);
            return _inner.DeleteValue(name, scope);
        }

        public void DeleteValueWithoutNotify(string name, string scope)
        {
            Ops.Add("delete:" + name + "@" + scope);
            _inner.DeleteValueWithoutNotify(name, scope);
        }

        public ToggleResult Toggle(string name, string scope)
        {
            Ops.Add("toggle:" + name);
            return _inner.Toggle(name, scope);
        }

        public void BroadcastSettingChange()
        {
            Ops.Add("broadcast");
            _inner.BroadcastSettingChange();
        }

        public InMemoryScope Inner => _inner;
    }

    // ---- set ----

    [Fact]
    public void Set_WritesValue_BroadcastsOnce_ExitsZero()
    {
        var env = new InMemoryScope();
        int rc = Program.RunSet(new[] { "set", FreeName, "bar" }, env, LockLockedName());
        Assert.Equal(0, rc);
        Assert.Equal("bar", env.ReadValue(FreeName, "user")?.Value);
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void Set_ProtectedVariable_RejectsWithoutWrite()
    {
        var env = new InMemoryScope();
        int rc = Program.RunSet(new[] { "set", LockedName, "x" }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Null(env.ReadValue(LockedName, "user"));
        Assert.Equal(0, env.BroadcastCount);
    }

    [Fact]
    public void Set_ExistingDifferentValueWithoutOverwrite_FailsAndPreserves()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "old", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunSet(new[] { "set", FreeName, "new" }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("old", env.ReadValue(FreeName, "user")?.Value);
        Assert.Equal(0, env.BroadcastCount);
    }

    [Fact]
    public void Set_ExistingDifferentValueWithOverwrite_Succeeds()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "old", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunSet(new[] { "set", FreeName, "new", "--overwrite" }, env, LockLockedName());
        Assert.Equal(0, rc);
        Assert.Equal("new", env.ReadValue(FreeName, "user")?.Value);
    }

    // ---- delete ----

    [Fact]
    public void Delete_RemovesValue_BroadcastsOnce_ExitsZero()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "bar", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunDelete(new[] { "delete", FreeName }, env, LockLockedName());
        Assert.Equal(0, rc);
        Assert.Null(env.ReadValue(FreeName, "user"));
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void Delete_ProtectedVariable_RejectsWithoutDelete()
    {
        var env = new InMemoryScope();
        env.WriteValue(LockedName, "keep", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunDelete(new[] { "delete", LockedName }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("keep", env.ReadValue(LockedName, "user")?.Value);
        Assert.Equal(0, env.BroadcastCount);
    }

    // ---- toggle ----

    [Fact]
    public void Toggle_DisableThenRestore_ExactValueAndKind()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "with%var%", "user");
        env.ResetBroadcastCount();

        int rcDisable = Program.RunToggle(new[] { "toggle", FreeName }, env, LockLockedName());
        Assert.Equal(0, rcDisable);
        Assert.Null(env.ReadValue(FreeName, "user"));
        var backup = env.ReadValue(FreeName + "_EnvManager_disabled", "user");
        Assert.NotNull(backup);
        Assert.Equal("with%var%", backup?.Value);
        Assert.Equal(1, env.BroadcastCount);

        int rcRestore = Program.RunToggle(new[] { "toggle", FreeName }, env, LockLockedName());
        Assert.Equal(0, rcRestore);
        var restored = env.ReadValue(FreeName, "user");
        Assert.Equal("with%var%", restored?.Value);
        Assert.Equal(RegistryValueKind.ExpandString, restored?.Kind);
        Assert.Null(env.ReadValue(FreeName + "_EnvManager_disabled", "user"));
        Assert.Equal(2, env.BroadcastCount);
    }

    [Fact]
    public void Toggle_ProtectedVariable_Rejects()
    {
        var env = new InMemoryScope();
        env.WriteValue(LockedName, "keep", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunToggle(new[] { "toggle", LockedName }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("keep", env.ReadValue(LockedName, "user")?.Value);
        Assert.Equal(0, env.BroadcastCount);
    }

    [Fact]
    public void Toggle_MissingVariable_Fails()
    {
        var env = new InMemoryScope();
        int rc = Program.RunToggle(new[] { "toggle", FreeName }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal(0, env.BroadcastCount);
    }

    // ---- rename (write-verify-delete contract) ----

    [Fact]
    public void Rename_MovesValue_SourceGone_OneBroadcast()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "val", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunRename(new[] { "rename", FreeName, "EM_TEST_RENAMED" }, env, LockLockedName());
        Assert.Equal(0, rc);
        Assert.Null(env.ReadValue(FreeName, "user"));
        Assert.Equal("val", env.ReadValue("EM_TEST_RENAMED", "user")?.Value);
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void Rename_SourceProtected_RejectsAndPreservesSource()
    {
        var env = new InMemoryScope();
        env.WriteValue(LockedName, "keep", "user");
        int rc = Program.RunRename(new[] { "rename", LockedName, "EM_TEST_X" }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("keep", env.ReadValue(LockedName, "user")?.Value);
        Assert.Null(env.ReadValue("EM_TEST_X", "user"));
    }

    [Fact]
    public void Rename_TargetProtected_RejectsAndPreservesSource()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "val", "user");
        int rc = Program.RunRename(new[] { "rename", FreeName, LockedName }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("val", env.ReadValue(FreeName, "user")?.Value);
        Assert.Null(env.ReadValue(LockedName, "user"));
    }

    [Fact]
    public void Rename_TargetExistsWithoutOverwrite_Fails()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "a", "user");
        env.WriteValue("EM_TEST_T", "b", "user");
        int rc = Program.RunRename(new[] { "rename", FreeName, "EM_TEST_T" }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("a", env.ReadValue(FreeName, "user")?.Value);
    }

    /// <summary>
    /// Contract test for the rename hard boundary: the target write must be recorded
    /// BEFORE the source delete. Reordering the implementation to delete-then-write makes
    /// this test fail (the falsification was demonstrated live during issue 03).
    /// </summary>
    [Fact]
    public void Rename_WritesTargetBeforeDeletingSource()
    {
        var rec = new RecordingScope();
        rec.Inner.WriteValue(FreeName, "val", "user");
        int rc = Program.RunRename(new[] { "rename", FreeName, "EM_TEST_RENAMED" }, rec, LockLockedName());
        Assert.Equal(0, rc);

        int writeIdx = rec.Ops.IndexOf("write:EM_TEST_RENAMED@user");
        int deleteIdx = rec.Ops.IndexOf("delete:EM_TEST_FOO@user");
        Assert.True(writeIdx >= 0, "rename must write the target");
        Assert.True(deleteIdx >= 0, "rename must delete the source");
        Assert.True(writeIdx < deleteIdx,
            $"write-verify-delete violated: ops were [{string.Join(", ", rec.Ops)}]");
    }

    // ---- change-scope ----

    [Fact]
    public void ChangeScope_MovesUserToSystem_SourceGone_OneBroadcast()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "val", "user");
        env.ResetBroadcastCount();
        int rc = Program.RunChangeScope(new[] { "change-scope", FreeName, "system", "--scope", "user" }, env, LockLockedName());
        Assert.Equal(0, rc);
        Assert.Null(env.ReadValue(FreeName, "user"));
        Assert.Equal("val", env.ReadValue(FreeName, "system")?.Value);
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void ChangeScope_Protected_RejectsAndPreserves()
    {
        var env = new InMemoryScope();
        env.WriteValue(LockedName, "keep", "user");
        int rc = Program.RunChangeScope(new[] { "change-scope", LockedName, "system", "--scope", "user" }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("keep", env.ReadValue(LockedName, "user")?.Value);
        Assert.Null(env.ReadValue(LockedName, "system"));
    }

    [Fact]
    public void ChangeScope_CollisionWithoutOverwrite_Fails()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "userVal", "user");
        env.WriteValue(FreeName, "sysVal", "system");
        int rc = Program.RunChangeScope(new[] { "change-scope", FreeName, "system", "--scope", "user" }, env, LockLockedName());
        Assert.Equal(1, rc);
        Assert.Equal("userVal", env.ReadValue(FreeName, "user")?.Value);
        Assert.Equal("sysVal", env.ReadValue(FreeName, "system")?.Value);
    }

    [Fact]
    public void ChangeScope_WritesTargetBeforeDeletingSource()
    {
        var rec = new RecordingScope();
        rec.Inner.WriteValue(FreeName, "val", "user");
        int rc = Program.RunChangeScope(new[] { "change-scope", FreeName, "system", "--scope", "user" }, rec, LockLockedName());
        Assert.Equal(0, rc);
        int writeIdx = rec.Ops.IndexOf("write:EM_TEST_FOO@system");
        int deleteIdx = rec.Ops.IndexOf("delete:EM_TEST_FOO@user");
        Assert.True(writeIdx >= 0 && deleteIdx > writeIdx,
            $"write-verify-delete violated: ops were [{string.Join(", ", rec.Ops)}]");
    }

    // ---- PATH list write path ----

    [Fact]
    public void SetPathEntries_WritesJoinedPath()
    {
        var env = new InMemoryScope();
        bool ok = Program.SetPathEntriesCore(env, LockLockedName(), LockLockedPath(),
            new List<string> { "C:\\a", "C:\\b" }, "user");
        Assert.True(ok);
        Assert.Equal("C:\\a;C:\\b", env.ReadValue("PATH", "user")?.Value);
        Assert.Equal(1, env.BroadcastCount);
    }

    [Fact]
    public void SetPathEntries_RemovingProtectedPathEntry_Rejects()
    {
        var env = new InMemoryScope();
        env.WriteValue("PATH", LockedPath + ";C:\\keep", "user");
        env.ResetBroadcastCount();
        bool ok = Program.SetPathEntriesCore(env, LockLockedName(), LockLockedPath(),
            new List<string> { "C:\\keep" }, "user");
        Assert.False(ok);
        Assert.Equal(LockedPath + ";C:\\keep", env.ReadValue("PATH", "user")?.Value);
        Assert.Equal(0, env.BroadcastCount);
    }

    [Fact]
    public void GetPathEntries_SplitsSemicolonList()
    {
        var env = new InMemoryScope();
        env.WriteValue("PATH", "C:\\a;C:\\b;C:\\c", "user");
        var entries = Program.GetPathEntriesCore(env, "user");
        Assert.Equal(new[] { "C:\\a", "C:\\b", "C:\\c" }, entries);
    }

    [Fact]
    public void GetPathEntries_MissingPathReturnsEmpty()
    {
        var env = new InMemoryScope();
        Assert.Empty(Program.GetPathEntriesCore(env, "user"));
    }

    // ---- scope isolation sanity for the double ----

    [Fact]
    public void InMemoryScope_UserAndSystemAreIsolated()
    {
        var env = new InMemoryScope();
        env.WriteValue(FreeName, "u", "user");
        Assert.Null(env.ReadValue(FreeName, "system"));
        env.WriteValue(FreeName, "s", "system");
        Assert.Equal("u", env.ReadValue(FreeName, "user")?.Value);
    }
}
