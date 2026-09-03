# ADR 0014: No TxR/TxF — Compensatory Writes Are the Only Sustainable Mutation Route

Date: 2026-09-03
Status: Accepted

## Context

Windows environment variable management has no OS-level transactional primitive.
A batch apply (profile apply writes N variables + M PATH entries across two
hives) can fail midway — crash, permission denial, value-kind mismatch — and the
Windows APIs offer no way to group those mutations into one atomic unit. The
tempting "fix" is the Windows Kernel Transaction Manager: Transacted Registry
(TxR) promises exactly that atomicity. That temptation must be rejected on the
record:

- **TxR/TxF are deprecated-by-omission.** Microsoft Learn's Transactional NTFS
  portal states: "Microsoft strongly recommends developers utilize alternative
  means to achieve your application's needs" and "TxF may not be available in
  future versions of Microsoft Windows"
  (learn.microsoft.com/en-us/windows/win32/fileio/transactional-ntfs-portal,
  verified 2026-09-03).
- **The official alternatives page confirms the gap.** "Alternatives to using
  Transactional NTFS"
  (learn.microsoft.com/en-us/windows/win32/fileio/deprecation-of-txf) notes
  "extremely limited developer interest in this API platform since Windows
  Vista" and that Microsoft is "considering deprecating TxF APIs in a future
  version of Windows". Its scenario table offers: whole-file replacement for
  single "document-like" files, installer-style coordination (Windows
  Installer) for updates to multiple files and/or the registry hive, embedded
  databases for structured data, and SQL Filestreams for file+SQL transactions.
  **No registry multi-value transaction primitive exists in that list** — the
  closest official answer for registry batches is "use an installer", which does
  not fit an interactive per-variable editor.
- Building Env Manager's write path on TxR would couple the product's core to an
  API platform that may be removed in a future Windows release, with no
  migration path.

## Decision

**TxR/TxF (and KTM generally) are non-goals.** No Env Manager code may create a
KTM transaction, call TxR/TxF APIs, or take a dependency that does.

**Compensatory writes + three-layer locking + audit recovery are the
institutionalized route** for every registry mutation — the paradigm already
implemented in code and hereby binding by name:

1. **Verified writes with restore-on-failure.** `SetVariable`
   (src/VariableWrite.cs) captures the prior raw value and registry kind,
   writes, verifies the exact persisted value and kind, and restores the prior
   state (or deletes a newly-created value) on any failure. PATH mutations must
   go through `SetPathEntries` — never a direct PATH registry write path.
2. **Write-verify-delete ordering.** `rename` / `change-scope`
   (src/VariableRename.cs, src/VariableChangeScope.cs) write and verify the
   target before deleting the source; never delete-then-set.
3. **Backup preservation on apply/unapply** (src/ProfileEffective.cs): the
   pre-apply snapshot is the compensation input for recovery.
4. **Three-layer write serialization, never bypassed**: the
   `Local\EnvManager.RegistryMutation` cross-process mutex (src/Program.cs) +
   the Rust `CLI_RWLOCK` write lock (frontend/src-tauri/src/main.rs) + the
   frontend `writeChain` serialization.
5. **Audit recovery**: CLI-level `audit.json` (src/AuditCommand.cs) and the
   append-only hash-chained service ledger (service/src/audit_ledger.rs) make
   every recorded mutation reversible after the fact.

### Mutation/model test first targets (by reference, not re-implementation)

The architecture-recovery Phase 3 test upgrade (differential, state-machine
model, mutation testing — see .scratch/architecture-recovery/spec.md Phase 3)
must hit these red lines first. The suites already exist or are specified in
that spec; this ADR only names the targets and does not duplicate them:

- **rename/change-scope write-verify-delete ordering** — pinned by
  `WritePathSeamTests` (tests/EnvManager.Engine.Tests/), first target for the
  upcoming model/mutation suites.
- **apply/unapply backup preservation (and single-broadcast timing)** — pinned
  by `WritePathSeamTests` and `ProfileSeamValidationTests`, same first-batch
  target list.

## Consequences

- **No OS-level atomicity for multi-value batches — compensation replaces it.**
  A failed batch is detectable (verified writes), recoverable (backups + audit
  recovery), and race-free (three-layer locks), but readers can observe
  intermediate states mid-write. Acceptable for an interactive editor; the GUI
  serializes all writes through `writeChain` so its own reads never race a
  partial batch.
- The paradigm matches Windows reality: the official alternatives list's only
  registry answer is installer-style coordination, and Env Manager is not an
  installer — compensatory writes are the honest substitute, not a stopgap.
- **Adoption gate**: any new write path (new command, new scope, service-side
  mutation) must reuse this compensatory pattern. A change introducing KTM,
  TxR, or TxF dependency fails review on this ADR alone.
- If a future Windows version ships a real registry multi-value transactional
  primitive, revisit this decision with a new ADR; do not adopt TxR silently.
