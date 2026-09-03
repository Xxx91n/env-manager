using CsCheck;
using EnvManager;

using Xunit;

namespace EnvManager.Engine.Tests;

/// <summary>
/// State-machine model testing over the write-path command cores (architecture-recovery issue 12,
/// spec Phase 3). The engine side (InMemoryScope behind the IEnvironmentScope seam, driven through
/// the Run* command cores with synthetic protection predicates) and a dictionary + broadcast-count
/// model advance in lockstep under randomly generated operation sequences, via CsCheck's stateful
/// testing API (4.8 GenOperation + SampleModelBased - the current shape of the Machine pattern the
/// ticket names; the legacy Machine base class no longer exists in CsCheck 4.x).
///
/// Pinned hard boundaries (docs/agents/hard-boundaries.md):
/// - rename/change-scope write-verify-delete order: for every successful rename/change-scope the
///   target write is recorded on the seam BEFORE the source delete. This order is not implied by
///   final state or broadcast count (a delete-then-write mutation produces identical end state and
///   broadcast count), so the windowed seam-op assertion is the only catcher - which is exactly the
///   acceptance form exercised by the injected mutation round.
/// - protected variables / protected PATH entries are never written, deleted, renamed, or
///   scope-changed; protected PATH entries are never removed.
/// - broadcast timing: exactly one WM_SETTINGCHANGE-equivalent broadcast per operation that
///   actually wrote, zero for rejections and no-ops (a delete of an absent variable still
///   broadcasts, matching the registry mechanics).
///
/// Any divergence between engine and model (or any inline assertion) fails the iteration and
/// CsCheck shrinks the failing initial state + operation sequence to the shortest, simplest form.
///
/// Known edge excluded from the operation domain: self-rename (old == new) with --overwrite
/// currently deletes the variable (write target, verify, then delete source removes the target
/// too). That is a pre-existing product decision outside this ticket's contract surface; the
/// generator only produces old != new rename pairs. Reported in the ticket-12 report.
/// </summary>
public class WritePathStateMachineTests
{
    // Synthetic protection domain: none of these names collide with the real protection lists
    // loaded from %LOCALAPPDATA%\EnvManager; the synthetic predicates below are the contract.
    const string LockedVar = "EM_LOCKED_VAR";
    const string LockedPathDir = "C:\\em-locked-dir";

    static readonly string[] VarNames = ["EMA", "EMB", LockedVar];
    static readonly string[] VarValues = ["v1", "v2", "v%p%"]; // v%p% exercises ExpandString kind policy
    static readonly string[] PathDirs = ["C:\\em-a", "C:\\em-b", LockedPathDir];
    static readonly string[] AllScopes = ["user", "system"];

    static bool IsLockedVar(string name) => name.Equals(LockedVar, StringComparison.OrdinalIgnoreCase);
    static bool IsLockedPathDir(string dir) => dir.Equals(LockedPathDir, StringComparison.OrdinalIgnoreCase);

    // ---- model state ----

    internal readonly record struct VarKey(string Scope, string Name);

    internal sealed class VarKeyComparer : IEqualityComparer<VarKey>
    {
        public static readonly VarKeyComparer Instance = new();
        public bool Equals(VarKey x, VarKey y) =>
            x.Scope.Equals(y.Scope, StringComparison.OrdinalIgnoreCase) &&
            x.Name.Equals(y.Name, StringComparison.OrdinalIgnoreCase);
        public int GetHashCode(VarKey k) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(k.Scope),
            StringComparer.OrdinalIgnoreCase.GetHashCode(k.Name));
    }

    /// <summary>Model = per-(scope,name) value dictionary + expected broadcast count.</summary>
    internal sealed class ModelState
    {
        public Dictionary<VarKey, string> Values { get; } = new(VarKeyComparer.Instance);
        public int Broadcasts { get; private set; }
        public void AddBroadcast() => Broadcasts++;
    }

    /// <summary>Engine state: recording seam double plus the synthetic protection predicates.</summary>
    internal sealed class EngineState
    {
        public TraceScope Env { get; } = new();
        public Func<string, string, bool> IsProtectedVariable { get; } =
            static (name, _) => IsLockedVar(name);
        public Func<string, bool> IsProtectedPathEntry { get; } =
            static entry => entry.TrimEnd('\\', '/').Trim().Equals(LockedPathDir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Wraps InMemoryScope recording seam op order (mirrors WritePathSeamTests.RecordingScope).
    /// The recorded window per operation is what pins the write-verify-delete order.
    /// </summary>
    internal sealed class TraceScope : IEnvironmentScope
    {
        public readonly List<string> Ops = new();
        readonly InMemoryScope _inner = new();
        public InMemoryScope Inner => _inner;
        public int BroadcastCount => _inner.BroadcastCount;

        public IReadOnlyList<EnvVariable> ListVariables(string scope)
        {
            Ops.Add("list:" + scope);
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
    }

    // ---- operations ----

    internal abstract record WriteOp
    {
        public abstract string Describe { get; }

        internal sealed record SetOp(string Name, string Value, string Scope, bool Overwrite) : WriteOp
        {
            public override string Describe => $"Set({Name}={Value},{Scope},ow:{Overwrite})";
        }

        internal sealed record DeleteOp(string Name, string Scope) : WriteOp
        {
            public override string Describe => $"Delete({Name},{Scope})";
        }

        internal sealed record RenameOp(string OldName, string NewName, string Scope, bool Overwrite) : WriteOp
        {
            public override string Describe => $"Rename({Scope}:{OldName}->{NewName},ow:{Overwrite})";
        }

        internal sealed record ChangeScopeOp(string Name, string FromScope, string ToScope, bool Overwrite) : WriteOp
        {
            public override string Describe => $"ChangeScope({Name},{FromScope}->{ToScope},ow:{Overwrite})";
        }

        internal sealed record PathAddOp(string Dir, string Scope) : WriteOp
        {
            public override string Describe => $"PathAdd({Dir},{Scope})";
        }

        internal sealed record PathRemoveOp(string Dir, string Scope) : WriteOp
        {
            public override string Describe => $"PathRemove({Dir},{Scope})";
        }
    }

    // ---- generators (small alphabets so shrinking yields readable minimal counterexamples) ----

    static Gen<string> Pick(params string[] items) => Gen.Int[0, items.Length - 1].Select(i => items[i]);

    static readonly (string OldName, string NewName)[] RenamePairs =
        VarNames.SelectMany(o => VarNames.Where(n => n != o), (o, n) => (o, n)).ToArray();

    static readonly (string From, string To)[] ScopePairs =
        [("user", "user"), ("user", "system"), ("system", "user"), ("system", "system")];

    static readonly Gen<string> GenVarName = Pick(VarNames);
    static readonly Gen<string> GenValue = Pick(VarValues);
    static readonly Gen<string> GenScope = Pick(AllScopes);
    static readonly Gen<string> GenPathDir = Pick(PathDirs);
    static readonly Gen<bool> GenOverwrite = Gen.Bool;
    static readonly Gen<(string OldName, string NewName)> GenRenamePair =
        Gen.Int[0, RenamePairs.Length - 1].Select(i => RenamePairs[i]);
    static readonly Gen<(string From, string To)> GenScopePair =
        Gen.Int[0, ScopePairs.Length - 1].Select(i => ScopePairs[i]);

    static readonly Gen<WriteOp> GenWriteOp = Gen.Frequency<WriteOp>(
        (4, from n in GenVarName from v in GenValue from s in GenScope from o in GenOverwrite
            select (WriteOp)new WriteOp.SetOp(n, v, s, o)),
        (3, from n in GenVarName from s in GenScope
            select (WriteOp)new WriteOp.DeleteOp(n, s)),
        (3, from p in GenRenamePair from s in GenScope from o in GenOverwrite
            select (WriteOp)new WriteOp.RenameOp(p.OldName, p.NewName, s, o)),
        (3, from n in GenVarName from p in GenScopePair from o in GenOverwrite
            select (WriteOp)new WriteOp.ChangeScopeOp(n, p.From, p.To, o)),
        (2, from d in GenPathDir from s in GenScope
            select (WriteOp)new WriteOp.PathAddOp(d, s)),
        (2, from d in GenPathDir from s in GenScope
            select (WriteOp)new WriteOp.PathRemoveOp(d, s)));

    static readonly GenOperation<EngineState, ModelState> EngineOperation =
        GenWriteOp.Operation<EngineState, ModelState>(
            static op => op.Describe,
            static (engine, op) => RunOnEngine(engine, op),
            static (model, op) => RunOnModel(model, op));

    // ---- engine-side runners (assert exit codes, broadcast deltas, write-verify-delete order) ----

    static void RunOnEngine(EngineState engine, WriteOp op)
    {
        switch (op)
        {
            case WriteOp.SetOp set: RunSetOnEngine(engine, set); break;
            case WriteOp.DeleteOp delete: RunDeleteOnEngine(engine, delete); break;
            case WriteOp.RenameOp rename: RunRenameOnEngine(engine, rename); break;
            case WriteOp.ChangeScopeOp change: RunChangeScopeOnEngine(engine, change); break;
            case WriteOp.PathAddOp add: RunPathAddOnEngine(engine, add); break;
            case WriteOp.PathRemoveOp remove: RunPathRemoveOnEngine(engine, remove); break;
        }
    }

    static void RunSetOnEngine(EngineState engine, WriteOp.SetOp op)
    {
        var env = engine.Env;
        string? existing = env.ReadValue(op.Name, op.Scope)?.Value;
        bool shouldWrite = !IsLockedVar(op.Name) && (op.Overwrite || existing == null || existing == op.Value);
        var args = new List<string> { "set", op.Name, op.Value, "--scope", op.Scope };
        if (op.Overwrite) args.Add("--overwrite");

        int before = env.BroadcastCount;
        int rc = Program.RunSet(args.ToArray(), env, engine.IsProtectedVariable);
        AssertExit(op, rc, shouldWrite ? 0 : 1, $"pre-state existing={(existing == null ? "<absent>" : existing)}");
        AssertBroadcastDelta(op, env.BroadcastCount - before, shouldWrite ? 1 : 0);
    }

    static void RunDeleteOnEngine(EngineState engine, WriteOp.DeleteOp op)
    {
        var env = engine.Env;
        bool shouldDelete = !IsLockedVar(op.Name);

        int before = env.BroadcastCount;
        int rc = Program.RunDelete(["delete", op.Name, "--scope", op.Scope], env, engine.IsProtectedVariable);
        AssertExit(op, rc, shouldDelete ? 0 : 1, "protected variables cannot be deleted");
        // The delete mechanics broadcast even when the variable did not exist (registry parity).
        AssertBroadcastDelta(op, env.BroadcastCount - before, shouldDelete ? 1 : 0);
    }

    static void RunRenameOnEngine(EngineState engine, WriteOp.RenameOp op)
    {
        var env = engine.Env;
        bool oldExists = env.ReadValue(op.OldName, op.Scope) != null;
        bool newExists = env.ReadValue(op.NewName, op.Scope) != null;
        bool shouldRename = !IsLockedVar(op.OldName) && !IsLockedVar(op.NewName) && oldExists
            && (op.Overwrite || !newExists);
        var args = new List<string> { "rename", op.OldName, op.NewName, "--scope", op.Scope };
        if (op.Overwrite) args.Add("--overwrite");

        int before = env.BroadcastCount;
        int windowStart = env.Ops.Count;
        int rc = Program.RunRename(args.ToArray(), env, engine.IsProtectedVariable);
        AssertExit(op, rc, shouldRename ? 0 : 1,
            $"pre-state oldExists={oldExists} newExists={newExists}");
        AssertBroadcastDelta(op, env.BroadcastCount - before, shouldRename ? 1 : 0);

        if (shouldRename)
        {
            // Hard boundary: write+verify target BEFORE deleting source. Never delete-then-write.
            AssertWriteBeforeDelete(env, windowStart,
                $"write:{op.NewName}@{op.Scope}", $"delete:{op.OldName}@{op.Scope}", "rename", op.Describe);
        }
    }

    static void RunChangeScopeOnEngine(EngineState engine, WriteOp.ChangeScopeOp op)
    {
        var env = engine.Env;
        bool sameScope = op.FromScope == op.ToScope;
        string? oldValue = sameScope ? null : env.ReadValue(op.Name, op.FromScope)?.Value;
        string? targetValue = sameScope ? null : env.ReadValue(op.Name, op.ToScope)?.Value;
        bool shouldMove = !sameScope && !IsLockedVar(op.Name) && oldValue != null
            && (op.Overwrite || targetValue == null);
        var args = new List<string> { "change-scope", op.Name, op.ToScope, "--scope", op.FromScope };
        if (op.Overwrite) args.Add("--overwrite");

        int before = env.BroadcastCount;
        int windowStart = env.Ops.Count;
        int rc = Program.RunChangeScope(args.ToArray(), env, engine.IsProtectedVariable);
        AssertExit(op, rc, sameScope ? 0 : (shouldMove ? 0 : 1),
            $"pre-state oldValue={(oldValue == null ? "<absent>" : oldValue)} targetValue={(targetValue == null ? "<absent>" : targetValue)}");
        AssertBroadcastDelta(op, env.BroadcastCount - before, shouldMove ? 1 : 0);

        if (shouldMove)
        {
            AssertWriteBeforeDelete(env, windowStart,
                $"write:{op.Name}@{op.ToScope}", $"delete:{op.Name}@{op.FromScope}", "change-scope", op.Describe);
        }
    }

    static void RunPathAddOnEngine(EngineState engine, WriteOp.PathAddOp op)
    {
        var env = engine.Env;
        // Mirrors PathCommand.PathAdd over the seam cores (the command is not seam-parameterized;
        // its add semantics are: duplicate check, then append, then one verified PATH write).
        var entries = Program.GetPathEntriesCore(env, op.Scope);
        bool duplicate = entries.Any(e => e.Equals(op.Dir, StringComparison.OrdinalIgnoreCase));
        bool shouldWrite = !duplicate;

        int before = env.BroadcastCount;
        if (!duplicate)
        {
            entries.Add(op.Dir);
            bool wrote = Program.SetPathEntriesCore(env, engine.IsProtectedVariable, engine.IsProtectedPathEntry,
                entries, op.Scope);
            if (!wrote)
                throw new InvalidOperationException($"{op.Describe}: PATH write did not verify (in-memory write must verify)");
        }
        AssertBroadcastDelta(op, env.BroadcastCount - before, shouldWrite ? 1 : 0);
    }

    static void RunPathRemoveOnEngine(EngineState engine, WriteOp.PathRemoveOp op)
    {
        var env = engine.Env;
        // Mirrors PathCommand.PathRemove over the seam cores (remove-all matching, then one
        // verified PATH write; protected entries are never removed).
        var entries = Program.GetPathEntriesCore(env, op.Scope);
        int removed = entries.RemoveAll(e => e.Equals(op.Dir, StringComparison.OrdinalIgnoreCase));
        bool shouldWrite = removed > 0 && !IsLockedPathDir(op.Dir);

        int before = env.BroadcastCount;
        if (removed > 0)
        {
            bool wrote = Program.SetPathEntriesCore(env, engine.IsProtectedVariable, engine.IsProtectedPathEntry,
                entries, op.Scope);
            if (wrote != shouldWrite)
                throw new InvalidOperationException(
                    $"{op.Describe}: PATH write outcome {wrote}, expected {shouldWrite} (protected entries must refuse removal)");
        }
        AssertBroadcastDelta(op, env.BroadcastCount - before, shouldWrite ? 1 : 0);
    }

    static void AssertExit(WriteOp op, int actualRc, int expectedRc, string detail)
    {
        if (actualRc != expectedRc)
            throw new InvalidOperationException($"{op.Describe}: exit {actualRc}, expected {expectedRc} ({detail})");
    }

    static void AssertBroadcastDelta(WriteOp op, int actualDelta, int expectedDelta)
    {
        if (actualDelta != expectedDelta)
            throw new InvalidOperationException(
                $"{op.Describe}: broadcast delta {actualDelta}, expected {expectedDelta} (exactly one broadcast on actual write, none on rejection/no-op)");
    }

    static void AssertWriteBeforeDelete(TraceScope env, int windowStart, string writeOp, string deleteOp, string command, string describe)
    {
        var window = env.Ops.GetRange(windowStart, env.Ops.Count - windowStart);
        int writeIdx = window.IndexOf(writeOp);
        int deleteIdx = window.IndexOf(deleteOp);
        if (writeIdx < 0 || deleteIdx < 0 || deleteIdx < writeIdx)
            throw new InvalidOperationException(
                $"{command} write-verify-delete order violated for {describe}: expected '{writeOp}' before '{deleteOp}', seam ops were [{string.Join(", ", window)}]");
    }

    // ---- model-side runners (pure expectation, mirrors the command contracts) ----

    static void RunOnModel(ModelState model, WriteOp op)
    {
        switch (op)
        {
            case WriteOp.SetOp set:
                if (!IsLockedVar(set.Name))
                {
                    string? existing = GetModelValue(model, set.Scope, set.Name);
                    if (set.Overwrite || existing == null || existing == set.Value)
                    {
                        model.Values[new VarKey(set.Scope, set.Name)] = set.Value;
                        model.AddBroadcast();
                    }
                }
                break;

            case WriteOp.DeleteOp delete:
                if (!IsLockedVar(delete.Name))
                {
                    model.Values.Remove(new VarKey(delete.Scope, delete.Name));
                    model.AddBroadcast(); // broadcast fires even when the variable did not exist
                }
                break;

            case WriteOp.RenameOp rename:
                if (!IsLockedVar(rename.OldName) && !IsLockedVar(rename.NewName))
                {
                    string? oldValue = GetModelValue(model, rename.Scope, rename.OldName);
                    if (oldValue != null)
                    {
                        string? targetValue = GetModelValue(model, rename.Scope, rename.NewName);
                        if (rename.Overwrite || targetValue == null)
                        {
                            model.Values[new VarKey(rename.Scope, rename.NewName)] = oldValue;
                            model.Values.Remove(new VarKey(rename.Scope, rename.OldName));
                            model.AddBroadcast();
                        }
                    }
                }
                break;

            case WriteOp.ChangeScopeOp change:
                if (change.FromScope != change.ToScope && !IsLockedVar(change.Name))
                {
                    string? oldValue = GetModelValue(model, change.FromScope, change.Name);
                    if (oldValue != null)
                    {
                        string? targetValue = GetModelValue(model, change.ToScope, change.Name);
                        if (change.Overwrite || targetValue == null)
                        {
                            model.Values[new VarKey(change.ToScope, change.Name)] = oldValue;
                            model.Values.Remove(new VarKey(change.FromScope, change.Name));
                            model.AddBroadcast();
                        }
                    }
                }
                break;

            case WriteOp.PathAddOp add:
            {
                var entries = GetModelPathEntries(model, add.Scope);
                if (!entries.Any(e => e.Equals(add.Dir, StringComparison.OrdinalIgnoreCase)))
                {
                    entries.Add(add.Dir);
                    model.Values[new VarKey(add.Scope, "PATH")] = string.Join(";", entries);
                    model.AddBroadcast();
                }
                break;
            }

            case WriteOp.PathRemoveOp remove:
            {
                var entries = GetModelPathEntries(model, remove.Scope);
                int removed = entries.RemoveAll(e => e.Equals(remove.Dir, StringComparison.OrdinalIgnoreCase));
                if (removed > 0 && !IsLockedPathDir(remove.Dir))
                {
                    model.Values[new VarKey(remove.Scope, "PATH")] = string.Join(";", entries);
                    model.AddBroadcast();
                }
                break;
            }
        }
    }

    static string? GetModelValue(ModelState model, string scope, string name) =>
        model.Values.TryGetValue(new VarKey(scope, name), out var value) ? value : null;

    static List<string> GetModelPathEntries(ModelState model, string scope)
    {
        string pathValue = GetModelValue(model, scope, "PATH") ?? "";
        return pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // ---- equality + failure rendering ----

    static bool StatesEqual(EngineState actual, ModelState model)
    {
        var actualValues = new Dictionary<VarKey, string>(VarKeyComparer.Instance);
        foreach (var scope in AllScopes)
            foreach (var variable in actual.Env.Inner.ListVariables(scope))
                if (!variable.IsDisabled)
                    actualValues[new VarKey(variable.Scope, variable.Name)] = variable.Value;
        if (actualValues.Count != model.Values.Count)
            return false;
        foreach (var (key, value) in model.Values)
            if (!actualValues.TryGetValue(key, out var actualValue) || actualValue != value)
                return false;
        return actual.Env.BroadcastCount == model.Broadcasts;
    }

    static string PrintActualState(EngineState engine)
    {
        var parts = new List<string>();
        foreach (var scope in AllScopes)
            foreach (var variable in engine.Env.Inner.ListVariables(scope))
                parts.Add($"{variable.Scope}:{variable.Name}={variable.Value}" + (variable.IsDisabled ? "(disabled)" : ""));
        parts.Add($"broadcasts={engine.Env.BroadcastCount}");
        return string.Join("; ", parts);
    }

    static string PrintModelState(ModelState model)
    {
        var parts = model.Values.Select(kv => $"{kv.Key.Scope}:{kv.Key.Name}={kv.Value}").ToList();
        parts.Add($"broadcasts={model.Broadcasts}");
        return string.Join("; ", parts);
    }

    // ---- the state machine test ----

    [Fact]
    public void WritePathStateMachine_ModelTracksEngineAcrossRandomSequences()
    {
        Gen.Const(() => (new EngineState(), new ModelState()))
            .SampleModelBased(
                EngineOperation,
                StatesEqual,
                iter: 1000,
                printActual: PrintActualState,
                printModel: PrintModelState);
    }
}
