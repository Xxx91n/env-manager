# Env Manager - Project Operating Instructions

This document is the single source of truth for the Env Manager project. All developers, AI agents, and LLMs must follow this specification. When any project feature or structure changes, update this file in the same commit. Detailed references live in `docs/` - keep this file concise; link out instead of inlining large tables.

---

## context-mode routing (MANDATORY)

- File edits (including patches) MUST go through ctx_batch_execute / ctx_execute_file, not apply_patch.
- ctx_* first; fallback to Codex builtins only when ctx_* can't do the same job. Read-to-analyze / search / large grep: ctx_batch_execute(commands, queries) or ctx_search(queries) - never Get-Content/Select-String into context. For data analysis use ctx_execute(code) and print only the answer.
- Web/HTTP: ctx_fetch_and_index(url, source) then ctx_search(queries). curl/wget/inline HTTP are forbidden.
- Shell OK for git, mkdir, rm, mv, cd, ls, npm install, dotnet build, cargo build, vitest, scripts/build-all.ps1 (execution, not analysis; output is bounded and acceptable).
- Windows paths in ctx sandbox: use bash form /d/Aworker/env-manager/... (lowercase drive, no D:\). PowerShell cmdlets need `pwsh -NoProfile -Command "..."`. `$`-using PowerShell logic must go in a `.ps1` and run with `-File` (inline `$` is stripped by the host transport).
- After resume: `ctx_search(sort:"timeline")` before asking the user anything. Search prior session memory before re-reading sources.
- Output artifacts as files + path + one-line summary; never inline large content. Descriptive source labels for `ctx_search(source:"label")`.
- Keep this block at the very top. Any later agent editing this file must keep the context-mode routing block intact and on top. Extended project spec follows.

---


## CodeGraph (MANDATORY for code exploration)

CodeGraph is the project's indexed code intelligence layer. The index lives at `.codegraph/` (gitignored). All agents and LLMs working on this project MUST use CodeGraph as the FIRST step for code exploration — it returns verbatim source of relevant symbols grouped by file in one capped call, far more efficient than manual Grep/Read loops.

**How to use**:
- Via MCP: call `codegraph_explore` with `projectPath: "D:\Aworker\env-manager"` and a query (symbol names, file names, or natural-language question).
- Via CLI: `codegraph explore "<query>"` or `codegraph query "<symbol>"` or `codegraph node <symbol>` or `codegraph files`.
- After any code change: run `codegraph sync .` to incrementally update the index. For a full rebuild: `codegraph index .`.
- Check index status: `codegraph status .`.

**When to call FIRST (before reading files)**:
- "How does X work?" or "Where is X defined?"
- "What calls Y?" or "What is the blast radius of changing Z?"
- Surveying an area before an edit
- Finding the call path between symbols

**When NOT needed**: trivial one-file edits where you already know the exact line, or after CodeGraph has already returned the source in this session (treat returned source as already Read — do NOT re-open those files).

**Index sync is mandatory after code changes** (same commit that changes code must update the index). The index is gitignored and never committed.
## Project Overview

- **Name**: Env Manager
- **Version**: 0.9.30
- **License**: Apache-2.0
- **Repository**: https://github.com/Xxx91n/env-manager
- **Languages**: C# (.NET 10), TypeScript, Svelte 4, Rust
- **Goal**: A modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support, inspired by Microsoft PowerToys environment variable editor but standalone and agent-friendly.

## Architecture

Four layers:
1. **CLI backend** (`src/`) - C# .NET 10 console app, reads/writes Windows Registry directly, compiles to `env-manager-cli.exe`. `src/Program.cs` is a thin Main dispatcher; shared CLI runtime infrastructure (constants, ValidCommands, JsonOpts, DebugLog, help text, ScrubExceptionMessage, SecretString, provider-hash recording, mutation lock, environment snapshot, atomic writes) lives in `src/CliRuntime.cs`; each command domain (profile, path, service, audit, agents, update, backup, protection, variable write/query, expand, bulk) lives in its own module file (issue 05, issue 06, issue 21).
2. **Tauri shell** (`frontend/src-tauri/`) - Rust app, embeds CLI as bundled resource (the five `tauri.conf.json` bundle.resources are staged into `frontend/src-tauri/bin/` by `frontend/scripts/prebuild.mjs` during local builds and by a fail-closed staging step in the CI verify job before any cargo compile), spawns CLI subprocesses, returns JSON via Tauri IPC.
3. **Svelte frontend** (`frontend/src/`) - TypeScript + Svelte 4 + TailwindCSS in WebView2. Talks to Rust only via `invoke('run_cli', ...)`.
4. **Service crate** (`service/`) - Rust standalone binary (`env-manager-service.exe`), manages secret mount lifecycle via named pipe IPC. Optional: runs as Windows service (`--mode=service`) or background process (`--mode=background`). The CLI `service` subcommand is a thin IPC gateway to this binary. See ADR 0001 and `docs/secret-architecture-blueprint.md` for the design review roadmap.

The GUI has NO local web server. Dev: Vite at `localhost:5173`. Production: Tauri embeds static assets via its `tauri://` custom protocol.

See [docs/architecture.md](docs/architecture.md) for IPC bridge, race condition prevention, system tray, toast, caching, auto-update, security hardening, modal dialog system, rename/change-scope contracts, profile audit history, and the GUI/CLI alignment table. See [docs/secret-architecture-decision-summary.md](docs/secret-architecture-decision-summary.md) for the Phase A-E secret architecture roadmap and ADR 0001.

## Project Structure

```
env-manager/
+- src/                        # All C# sources (issue 05 moved them here; csproj default globbing compiles them)
|   +- Program.cs             # Thin Main dispatch: command switch, crash-dialog disable, LenientArgs recovery, mutex+snapshot wiring (issue 05, issue 06, issue 21)
|   +- CliRuntime.cs          # Shared CLI runtime: constants, ValidCommands, JsonOpts, DebugLog, help text, ScrubExceptionMessage, SecretString, protection predicates, provider hash, mutation lock, env snapshot, atomic writes (issue 21)
|   +- Models.cs              # Data contracts: EnvVariable, BackupData, ProfileVariable, ResolvedPathEntry, ProfileData
|   +- EngineScope.cs         # IEnvironmentScope engine seam (architecture-recovery issue 01, expand phase)
|   +- RegistryScope.cs       # Production IEnvironmentScope: registry + WM_SETTINGCHANGE P/Invoke (pure move)
|   +- InMemoryScope.cs       # In-memory IEnvironmentScope test double (user/system isolated, broadcast counter)
|   +- VariableWrite.cs       # Write-path command cores + set/delete/toggle wrappers (issue 03, issue 05)
|   +- VariableQuery.cs       # Scope parsing, list/get projection, raw reads, WM_SETTINGCHANGE broadcast (issue 05)
|   +- VariableRename.cs / VariableChangeScope.cs  # Rename / change-scope write-verify-delete contract
|   +- ProfileCommand.cs      # Profile domain: list/show/create/delete/apply/unapply, launch + secrets, secret-provider CLI (issue 05)
|   +- ProfileEffective.cs    # Profile apply/unapply write path + pre-flight validation, seam-parameterized (issue 04)
|   +- ProfileStorage.cs      # profiles.json load/save + test redirect seams
|   +- PathCommand.cs         # Path domain: list/add/remove/move/rename/dedupe/health + NormalizePathEntry/StripVerbatimPrefix (issue 05, issue 06)
|   +- BackupCommand.cs       # Backup domain: backup/restore/diff/merge/validate + file path validator (issue 05)
|   +- ProtectionCommand.cs   # Protection domain + protected collections (IsProtectedVariable/IsProtectedPathEntry) (issue 05)
|   +- ServiceCommand.cs      # Service domain: IPC gateway to env-manager-service (issue 05)
|   +- AuditCommand.cs        # Audit domain: audit list/encrypt-file/ledger routing + AuditEntry/LoadAuditHistory/RecordSnapshotDiff/history command (issue 05, issue 06)
|   +- AgentsCommand.cs       # Agents domain: CLI spec emitter (issue 05)
|   +- UpdateCommand.cs       # Update domain: update check + version compare (issue 05)
|   +- ArgTokenizer.cs        # LenientArgs tokenizer (retained; System.CommandLine is a non-goal)
|   +- AuditCrypto.cs / AuditLedgerMigration.cs / ProfileAudit.cs / SchemaMigration.cs / SecretMount.cs / ServiceIpc.cs / StateExportImport.cs / NativeMethods.cs
|   +- SecretEnvelope.cs / SecretEnvelopeJsonContext.cs / ProviderConfigJsonContext.cs / ISecretProvider.cs  # Secret provider core
|   +- DpapiCurrentUserProvider.cs / CredentialManagerProvider.cs / PowerShellSecretManagementProvider.cs / VaultKV2Provider.cs / SopsProvider.cs / AzureKeyVaultProvider.cs / OnePasswordProvider.cs / AwsSecretsManagerProvider.cs  # One provider per file (issue 09 split)
|   +- SecretProviderManager.cs  # Active-provider routing, rotation, export/import
|   +- ExpandCommand.cs / BulkCommand.cs / DpapiHelper.cs  # EnvFeatures.cs retired (issue 06 split)
+- env-manager.csproj          # .NET 10 project (AssemblyName: env-manager-cli)
+- AGENTS.md                   # This file (project-level operating instructions)
+- AGENTS.cli.md               # CLI-level agent guide (distributed with CLI binary)
+- README.md / docs/i18n/      # English landing README + localized translations (docs/i18n/README.<locale>.md; zh_CN is the complete reference, others track it)
+- CONTEXT.md                  # Internal development process record (design review session decisions A1-A11 + Risk Matrix)
+- docs/                       # User documentation (cli-commands, architecture, build-and-release, backup-and-profiles, secret-architecture-blueprint, secret-providers-guide, adr/)
+- docs/agents/                # Agent-specific reference (issue-tracker, domain)
+- docs/history/              # Process artifacts (ui-audit, session records)
+- scripts/                    # Build orchestrator (build.mjs), test harness, rsvg-convert wrapper, migration scripts, snapshot scripts
+- service/                    # Rust service crate (env-manager-service.exe, named pipe IPC, reconcile loop, audit ledger)
+- frontend/                   # Tauri GUI application (src/, src-tauri/, tests/)
+- release/                    # Build output (gitignored): portable/, cli-only/, msi/
+- bin/ obj/ dist/             # Intermediate build output (gitignored)
```

## CLI Command Quick Reference

Full table, scope, debug, error handling, profiles, toggle, path editor, path resolution: see [docs/cli-commands.md](docs/cli-commands.md).

Read-only (concurrent-safe, read-locked): `list`, `get`, `backup`, `diff`, `validate`, `agents`, `profile list/show/status, launch, secret-provider list, export-secrets, reveal-secret`, `path list, path health (no --fix)`, `path dedupe --dry-run`, `history list`, `bulk export`, `expand`, `protection list`, `audit list`, `update check`, `service status/health/ping`. `export-state`, `audit verify-ledger/migrate-audit/export-survival-kit --dry-run`.

Write (serialized, write-locked): `set`, `rename`, `change-scope`, `delete`, `toggle`, `restore`, `merge`, `profile create/delete/apply/unapply/add-var/remove-var/edit-var/rename, set-launch, add-secret/edit-secret/remove-secret, secret-provider set/rotate, import-secrets`, `path add/remove/move-up/move-down/rename/dedupe, path health --fix`, `history undo/delete`, `bulk import`, `audit encrypt-file`, `protection add-path/remove-path/add-var/remove-var`, `service refresh/rotate/reload/shutdown`. `import-state`, `audit migrate-audit/recover-from-ledger`.

All commands: `env-manager-cli <command> [arguments] [--flags]`. `--debug`/`-d` anywhere enables verbose stderr. `--scope user|system` (default user). Exit 0/1; `profile apply` also uses 2 = success with preflight warnings (--strict treats warnings as errors, ticket 19).

## Hard Boundaries (Red Lines)

All invariants that must never be violated are in [docs/agents/hard-boundaries.md](docs/agents/hard-boundaries.md) (~108 KiB, 279 lines). Read it before any code change.

**Top-level constraints (most critical):**
- **Protected variables/PATH**: built-in protected entries cannot be set/deleted/toggled/renamed/scope-changed. See `IsProtectedVariable` / `IsProtectedPathEntry`.
- **Cross-process mutex**: all writes acquire `Local\EnvManager.RegistryMutation` mutex + Rust `CLI_RWLOCK` write lock + frontend `writeChain` serialization. Three layers, never bypass.
- **Rename/scope-change contract**: write+verify target before deleting source. Never delete-then-set. Registry mutations are compensatory-write only: TxR/TxF are non-goals (ADR 0014, docs/adr/0014-no-txr-txf-compensatory-writes.md).
- **GUI 3-way save ordering**: `rename(old scope)` -> `changeScope(overwrite flag)` -> `setVariable(value, overwrite flag)`. `--overwrite` only from explicit user confirmation.
- **Secrets never in registry**: DPAPI-encrypted on disk, plaintext only in transient launcher process memory. `profile launch` is the only apply path for Launch profiles.
- **Live test harness**: any registry-mutating CLI smoke test MUST use `scripts/test-with-restore.ps1`. Never run raw registry-mutating commands.

For the full 279-line list (protected vars, profile audit, secret providers, GUI boundaries, build rules, etc.), read [docs/agents/hard-boundaries.md](docs/agents/hard-boundaries.md).

## i18n (Internationalization)

10 languages: en, zh, ja, ko, de, fr, es, pt, ru, ar. Engine: `svelte-i18n` (ICU MessageFormat).

**i18n sync is mandatory when adding any new user-facing string** (button label, message, dialog text, error):
1. Add the key to `frontend/src/lib/translations/en.json` (the reference).
2. Add the same key with translated value to ALL other 9 translation files.
3. Use `$t('key')` in Svelte components - never hardcode display text.
4. Register any new locale in `frontend/src/lib/i18n.ts` (both `register()` call and `supportedLocales` array).

ICU caveat: single quotes `'` are escape characters. Never wrap a `{placeholder}` in single quotes - `'{name}'` produces the literal text `{name}`. Use bare `{name}` or double single quotes `''` for a literal quote.

Default locale (en) loads synchronously via `addMessages()` so the UI renders under Tauri's custom protocol. Other locales load lazily.

## Testing

Frontend unit tests use Vitest with jsdom. Tests live alongside source as `*.test.ts`. Setup at `frontend/tests/setup.ts` mocks `@tauri-apps/api/core` `invoke` and `svelte-i18n`.

C# engine unit tests use xUnit in `tests/EnvManager.Engine.Tests/`, covering the pure-logic domains: argument tokenizing (`LenientArgs.Tokenize`), exception message scrubbing (`ScrubExceptionMessage`), and PATH entry normalization (`NormalizePathEntry`), plus write-path seam behavior tests (`WritePathSeamTests`: set/delete/toggle/rename/change-scope/PATH-list command cores run against `InMemoryScope` with synthetic protection predicates, locking protected-entry rejection, the rename/change-scope write-verify-delete order, scope selection, and broadcast timing - architecture-recovery issue 03). Run `dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj`; the same step runs in the `build.yml` `verify` job and gates PRs. Tests never touch the real registry and never depend on machine environment state (any env var use is Process-scoped and cleared in-test). `env-manager.csproj` grants `InternalsVisibleTo` to `EnvManager.Engine.Tests` and `EnvManager.Fuzz` and excludes `tests/**` from its compile glob, so release artifacts are unchanged.

Launch-profile injection and secret redaction are verified by a three-layer net (architecture-recovery issue 07):

- `tests/launch-env-injection.Tests.ps1` (Pester, CI Tier 3): golden env diff (injected set must exactly match the profile's resolved variables, ignoring only the variables cmd.exe synthesizes in the child: COMSPEC/PATHEXT/PROMPT), probe-process echo (the launched child re-reads its injected values), and the Launch-never-writes-registry invariant (injected names absent from HKCU\Environment after launch). Upgrades the older `scripts/test-launch-env.ps1` probe pattern, which remains a manual inspector tool.
- `tests/canary-redaction.Tests.ps1` (Pester, CI Tier 3): canary zero-leak negative assertions across all output sinks (profile show/preview/list, history list audit trail, launch stdout, error stderr) plus a positive control proving the canary reaches the child env block, and masking-placeholder positive assertions (`<encrypted>` in show output, `<revealed>` in the audit reveal entry).
- `CanaryRedactionTests` (xUnit, `tests/EnvManager.Engine.Tests/`): pure-function canary regression over `ScrubExceptionMessage` — format-shaped canary values (password=/Bearer/VAULT_TOKEN=) never survive scrubbing, `<redacted>` placeholder appears, un-patterned values pass through unchanged (documented best-effort behavior, ADR 0005).
- `scripts/run-ci-tests.ps1` orchestrates four integration suites: launch-env-injection, canary-redaction, inheritance-protection, test-with-restore. Run it after building the CLI: `pwsh -NoProfile -File scripts/run-ci-tests.ps1 -CliExe <path-to-env-manager-cli.exe>`. The canary net + golden/snapshot layering (7-sink scan, `<encrypted>`/`<revealed>` placeholders, 17 CLI `.verified.txt` snapshots, 10-locale rendered i18n snapshots, IPC schema golden) is documented in docs/architecture.md "Canary Zero-Leak Assertion Net and Golden/Snapshot Layers".

Test-residue hygiene (architecture-recovery issue 22): the `test-with-restore.ps1` reconciliation block enforces a residue-zero assertion - the pre/post snapshot diff may only reference harness-registered `EM_TEST_*` value names and must be empty after compensatory reconciliation; names outside the registered set fail the run as `registry-foreign-drift`. `scripts/check-test-residue.ps1` is a read-only self-check that lists `EM_TEST_*` registry/profile residue (exit 0 = clean, 1 = residue found). User self-clean commands for legacy residue such as `EM_TEST_DST=v1` live in docs/build-and-release.md "Test residue hygiene"; agents and the harness never delete pre-existing values on the user machine.

Profile/secret seam migration is verified by `ProfileSeamValidationTests` (xUnit, `tests/EnvManager.Engine.Tests/`, architecture-recovery issue 04): the v0.7.7 inheritance-chain secret-propagation gate is exercised through the seam via `RunProfilePreflight` (explicit profile list, hermetic per-test profiles.json redirect via `SetProfilesFilePathForTests`), including a falsifiable launch-inherits-secret-launch poisoned-JSON variant that fails if the inherited-secret union walk regresses to own-list-only (red-first acceptance demonstrated live during ticket 04).

CI user-state isolation (architecture-recovery issue 24): the verify job's Pester step sets `ENVMANAGER_LOCALAPPDATA` to a job-private directory under `runner.temp`, and all CLI user-state file resolution routes through `Program.LocalAppDataRoot` in `src/CliRuntime.cs` (the variable wins when non-empty; `GetFolderPath` fallback otherwise - a process-level `LOCALAPPDATA` override is NOT honored by `GetFolderPath`). `LocalAppDataRedirectTests` (xUnit, serial collection) pins set/unset/empty routing, and a follow-up workflow step asserts after the run that the real `%LOCALAPPDATA%\EnvManager` was not created by the job. Two-level isolation discipline + env-block snapshot semantics are documented in docs/build-and-release.md "CI user-state isolation and env-block snapshot semantics (architecture-recovery issue 24)".

Write-path hard boundaries are additionally pinned by randomized state-machine model testing (architecture-recovery issue 12, spec Phase 3): `WritePathStateMachineTests` (xUnit + CsCheck 4.8.0) drives the write-path command cores (set/delete/rename/change-scope/PATH add/remove) against `InMemoryScope` with synthetic protection predicates while a dictionary + broadcast-count model advances in lockstep - 1000 randomly generated operations per run, divergence shrunk to a minimal counterexample sequence. Pinned contracts: rename/change-scope write-verify-delete seam-op order (windowed assertion - the accepted mutation-round form), protected variable/PATH-entry rejection, and broadcast timing (exactly one broadcast per actual write, none on rejection; delete-of-absent still broadcasts, registry parity). A deliberately injected delete-then-write rename mutation fails within the 1000-iteration budget with a shrunk counterexample (mutation round documented in .scratch/architecture-recovery/reports/12-write-path-state-machine-tests.md); known excluded edge: self-rename (old == new) is outside the generated domain (pre-existing product decision, reported).

Apply/unapply run against `InMemoryScope` with backup preservation, single-broadcast timing (apply broadcasts only when the batch wrote something), system-scope routing (requires `ResolveProfile` to carry `Scope` - a silent reset to "user" fails the test), and a poisoned-store protection guard (`SaveProfilesRawForTests` bypasses `ValidateProfiles` the way a hand-edited profiles.json would; `ApplyProfile` must skip protected entries and broadcast nothing). `ValidateLaunchPreflight` covers the launch entry-point rejections without spawning a process; `SecretProviderManager.Decrypt` fail-closed routing is pinned for unknown providers and non-envelope garbage.

The `ValidateLaunchTarget` System32 guard compares against the resolved system folder - the prior doubled-separator verbatim literal never matched a real path (T04-SYS32-FIX), so the system32-hijacking refusal documented in hard-boundaries.md now actually fires.

Secret provider contract tests (architecture-recovery issue 10) pin all eight `ISecretProvider` implementations to one shared behavior contract: an abstract `SecretProviderContractTests` base (fail-closed decryption, round-trip, stable malformed-format error, plaintext-never-in-the-envelope — each expressed only through the `ISecretProviderHarness` seam), one sealed mount per provider, and a `SecretProviderContractComplianceTests` reflection gate that fails the build when an implementation lacks a mount. `DpapiCurrentUserContractTests` runs its backend-dependent assertions on the real local DPAPI backend (L0); the other seven mounts run the backend-independent assertions (fail-closed, malformed-format) and `Skip` the backend-dependent round-trip/plaintext assertions with the layer reason (L1/L2, see docs/architecture.md "Secret Provider Contract Test Suite").

IPC schema contract tests (architecture-recovery issue 08) pin the three IPC clients to the single Rust-owned schema:

- Authoritative schema: `IpcRequest`/`IpcResponse` in `service/src/ipc.rs`; golden files `docs/schemas/env-manager-service-ipc.schema.json` + `docs/schemas/ipc-samples.json` are exported from it (regenerate with `ENVMANAGER_REGENERATE_IPC_GOLDEN=1 cargo test -p env-manager-service ipc`).
- C# gateway: `src/ServiceIpc.cs` typed request/response + `ServiceIpcContractTests` xUnit suite (wire names, null-skip semantics, schema property coverage).
- TS GUI: `parseServiceResponse` (exported from `api.ts`) + `frontend/src/lib/ipc-schema-contract.test.ts` vitest suite over the golden samples.
- Tauri shell: `ipc_contract_tests` in `frontend/src-tauri/src/main.rs` pin the watchdog ping / GUI-exit shutdown pipe payloads.
- CI: `cargo test --locked` runs for `service` and `frontend/src-tauri` in the build.yml verify job. See docs/architecture.md "IPC Schema Contract (single source of truth)".

Differential oracle testing (architecture-recovery issue 11, spec Phase 3) pins InMemoryScope's fidelity to real Windows semantics: `DifferentialOracleTests` (xUnit, `tests/EnvManager.Engine.Tests/`) runs the same operation script against InMemoryScope and RegistryScope (real registry as oracle) and asserts, after every step, both the terminal state (raw unexpanded value + registry value kind per variable/scope) and the broadcast count agree. Semantic matrix: REG_EXPAND_SZ %VAR% preservation + upgrade-only kind policy, PATH 1024/~30000-char boundaries and the 32767 MaxLength rejection, empty-entry = current-directory semantics, `=`-in-name rejection before any seam call, and elevation-gated system-scope writes. The suite drives the real registry, so it is gated behind `EM_DIFFERENTIAL_ORACLE=1` (shows as Skip in a plain `dotnet test`) and mounts only inside `scripts/test-with-restore.ps1` (new "differential oracle parity" Run-Test block), which snapshots HKCU/HKLM first and reconciles both hives after - the CI windows-latest coverage arrives through the existing Pester integration step. A deliberately injected REG_SZ↔REG_EXPAND_SZ kind-promotion regression in InMemoryScope turned the suite red (4/11 failed with kind-drift diagnostics) and reverted green (red-first acceptance, report 11).
Secret provider L1 emulator matrix (architecture-recovery issue 15): the 7 backend-dependent contract assertions (round-trip / plaintext-never) now run against real local backends instead of static Skips. Each `*ContractTests.cs` mount gains `[SkippableFact]` `Category=L1` tests over a per-provider `*L1Harness` (neutral Seed/ReadRaw via the ticket-10 `ISecretProviderHarness` seam). Backends: Vault dev server (generic Testcontainers container, `hashicorp/vault:1.20.4` - no official .NET module exists), LocalStack (`localstack/localstack:4.4.0`, last token-free community image, reached through the new `AWS_ENDPOINT_URL_SECRETS_MANAGER` production seam in `AwsSecretsManagerProvider`), Lowkey Vault (`Testcontainers.LowkeyVault` 4.14.0, `nagyesta/lowkey-vault:4.0.0-ubi9-minimal`, driven through the new `IDENTITY_ENDPOINT`/`IDENTITY_HEADER` App-Service-convention seam in `AzureKeyVaultProvider` plus store-trusted self-signed certs), Windows Credential Manager + pwsh SecretStore (real local backends, non-interactive `Authentication=None` automation mode), sops+age (real pinned sops 3.13.3 / age 1.3.2 with a throwaway session keypair), and 1Password (real pinned op CLI 2.39.0 against the in-repo `OpConnectMock` Connect REST stub; the full Encrypt side stays Skip because `op item create` is refused over Connect by design - live-verified). Container/CLI backends are opt-in via `EM_L1_MATRIX=1` (plain `dotnet test` never pulls images or downloads binaries); tool backends run wherever the binaries exist. CI: new `verify-l1` job (ubuntu-latest, Docker preinstalled) runs the `Category=L1` filter with `EM_L1_MATRIX=1`; Windows dev hosts run the CredentialManager/SecretStore/sops smokes locally. OnePasswordProvider additionally gained the production Connect fixes: `--vault` is always passed (mandatory in Connect mode), `--format=json` on `item get` (mandatory in Connect mode), JSON-string unwrap of the field value, and `NO_PROXY=localhost,127.0.0.1,::1` so loopback targets bypass system proxies. See docs/architecture.md "Secret Provider L1 Emulator Matrix".

Metamorphic testing pilot (architecture-recovery issue 34, spec Phase 5): `MetamorphicPathTests` (xUnit, `tests/EnvManager.Engine.Tests/`) pins 8 oracle-free metamorphic relations over PATH normalization (`NormalizePathEntry`) and case-folding semantics - trailing-separator output-invariance for list/health/dedupe (MR-1/2/3), list-vs-health duplicate-classifier agreement (MR-4), PATH round-trip identity (MR-5), case-variant rename data preservation (MR-6), and normalization-aware duplicate folding for dedupe/add (MR-7/8). All relations run against `InMemoryScope` through the seam-parameterized cores (`PathAddCore`/`PathDedupeCore`/`PathRemoveCore`, historical inline bodies moved verbatim) with allow-all synthetic protection predicates. The pilot found 3 real defects (dedupe raw-string duplicate detection, case-only rename data destruction with --overwrite, path add raw-compare guard bypass) with 0 false positives (CI red run 33986994731 Failed 3/Passed 159 -> green run 33987712651); ROI verdict and expansion preconditions live in `.scratch/architecture-recovery/reports/34-metamorphic-testing-pilot.md`. Protected entries still never enter the dedupe set (the vitest source gate in `frontend/src/lib/review-regressions.test.ts` pins the normalized form).

Survivor triage kill tests (architecture-recovery issue 18): `MutationSurvivorTriageTests` + `MutationSurvivorTriageStdoutTests` (xUnit) kill the 13 weak-assertion Stryker baseline survivors through the existing seams plus a new `SetAppDataDirectoryForTests` redirect seam (protection JSON stores; mirrors `SetProfilesFilePathForTests`). The single equivalent survivor (`CollectInheritedSecretsFrom` cycle guard) is registered in `.scratch/architecture-recovery/reports/18-survivor-registry.json` with an LLM-detection reserve field. Stryker runs on demand via the `workflow_dispatch` `stryker` job in build.yml, which publishes per-module scores from `scripts/stryker-module-scores.mjs`; stryker-config.json thresholds/mutate scope remain locked.

Mutation testing gate (architecture-recovery issue 13): dotnet-stryker 4.16.0 (local tool manifest `.config/dotnet-tools.json`, run `dotnet tool restore` then `dotnet stryker` from the repo root) mutates only the four red-line files (rename / change-scope / profile apply-unapply / protection) with string/logical mutants ignored and thresholds high 85 / low 70 / break 60. It is a local/PR-assist gate, deliberately NOT a CI hard gate (v5/dotnet10 pipeline friction; MS guidance against chasing 100% scores). Baseline: 76/94 killed (80.85% of tested mutants; raw Stryker score 37.07% counts NoCoverage as failures). Survivor classification and the red-line kill-rate mapping live in `.scratch/architecture-recovery/reports/13-mutation-testing-gate.md`.

Method-level cognitive complexity guard (architecture-recovery issue 29): `scripts/cognitive-complexity.mjs` (zero-dependency Node ESM) implements a Sonar-style cognitive complexity rule per method over `src/*.cs` (SonarSource 2021 spec, C# subset; threshold 15 = SonarWay S3776 default). Modes: default report (always exit 0, the CI report step), `--gate` (blocks only NEW violations: over-threshold methods absent from the committed baseline, or baseline-clean methods that regressed above it - existing violations stay grandfathered per the ticket 18 tiering principle), `--update-baseline`, `--selftest` (21 golden snippets), `--demo-gate` (red -> grandfathered-green -> regression-red demonstration). The committed baseline `scripts/cognitive-complexity-baseline.json` pins all 304 methods (32 over-threshold entries tiered T1/T2/T3 in `.scratch/architecture-recovery/reports/29-cognitive-complexity-guard.md` with the remediation schedule tied to tickets 26/27). The build.yml verify job runs report -> artifact upload (`cognitive-complexity-report`) -> `--gate`; renaming an over-threshold method requires `--update-baseline` in the same PR with reviewer-approved rationale.

```bash
Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process - Force
node scripts/build.mjs --arch x64
# Or per-architecture: --arch x86, --arch arm64
# Skip stages: --skip-gui, --skip-msi, --skip-cli
```

Verify: `release/portable/env-manager.exe`, `release/portable/env-manager-cli.exe`, `release/cli-only/env-manager-cli.exe`, `release/portable/Env-Manager_portable_X.Y.Z_x64.zip`, `release/cli-only/Env-Manager_cli-only_X.Y.Z_x64.zip`, `release/msi/Env Manager_X.Y.Z_x64.msi` (no locale suffix). The `release/` directory is gitignored - artifacts are for local testing only, not committed to git.

## Mandatory Git Push After Code Changes (Provenance)

After code changes, commit and push to GitHub (`git push origin main`). Authentication uses the global SSH config (`git@github-Xxx91n:...`), not PAT over HTTPS. The push requirement still applies: local-only commits are invisible to other agents. If a push fails, keep the branch pushable (clean tree, fast-forwardable) and retry next opportunity—never `git reset --hard`.

## Documentation Maintenance

When the project changes, update files in the same commit:

| Event | Files to update |
|-------|----------------|
| New CLI command | AGENTS.md (quick reference), docs/cli-commands.md, README.md, docs/i18n/README.zh_CN.md, docs/architecture.md alignment table |
| Changed command args | AGENTS.md, docs/cli-commands.md, README.md, docs/i18n/README.zh_CN.md |
| New GUI feature | AGENTS.md, docs/architecture.md alignment table, README.md, docs/i18n/README.zh_CN.md, all 10 app translation JSON files |
| New debug log point | docs/build-and-release.md (Logging section) |
| Dependency update | docs/build-and-release.md, AGENTS.md if it affects build/architecture |
| Build change | AGENTS.md, docs/build-and-release.md, README.md, docs/i18n/README.zh_CN.md |
| Directory structure change | AGENTS.md |
| CodeGraph index change | docs/build-and-release.md (CodeGraph section) |
| Code change (any) | Run `node scripts/build.mjs --arch x64`, verify release/ artifacts |
| New test file | AGENTS.md (test inventory), docs if it documents new behavior |
| Architecture/IPC/security change | docs/architecture.md, docs/backup-and-profiles.md, AGENTS.md hard boundaries |

A commit that does not update AGENTS.md (and the relevant `docs/` file) when the project has changed is considered incomplete.

## How to Add a New CLI Command

1. Add a `case` in `src/Program.cs` `Main()` switch statement.
2. Implement the command method in the matching command-domain module under `src/` (one module per domain; create a new `src/<Domain>Command.cs` partial-class file for a new domain).
3. Update `ShowHelp()` with usage text.
4. Add to the command table in [docs/cli-commands.md](docs/cli-commands.md) and the quick reference in AGENTS.md.
5. If write command: add to `WRITE_COMMANDS` in `frontend/src-tauri/src/main.rs`. If read command: add to `READ_COMMANDS`.
6. Update `ALLOWED_COMMANDS` in `main.rs`.
7. Update `README.md` and `docs/i18n/README.zh_CN.md`.
8. Add the API function in `frontend/src/lib/api.ts` and the GUI surface in the appropriate `.svelte` component.
9. Add i18n strings to all 10 translation files.
10. Update the alignment table in [docs/architecture.md](docs/architecture.md).
11. Add test coverage.
12. Run `node scripts/build.mjs --arch x64` and verify release artifacts.

## Detailed Reference Index

Topic-to-file index in [docs/agents/reference-index.md](docs/agents/reference-index.md). Key references:

| Topic | File |
|-------|------|
| Full CLI command table | [docs/cli-commands.md](docs/cli-commands.md) |
| Architecture, IPC, race conditions, GUI/CLI alignment | [docs/architecture.md](docs/architecture.md) |
| Build system, release steps, CodeGraph | [docs/build-and-release.md](docs/build-and-release.md) |
| Backup/profile JSON format, safety contracts | [docs/backup-and-profiles.md](docs/backup-and-profiles.md) |
| Secrets architecture roadmap, Phase A-E | [docs/secret-architecture-blueprint.md](docs/secret-architecture-blueprint.md) |
| Secret providers setup guide (all 8 providers) | [docs/secret-providers-guide.md](docs/secret-providers-guide.md) |
| CLI-level agent guide (distributed with binary) | [AGENTS.cli.md](AGENTS.cli.md) |
