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
- **Version**: 0.7.1
- **License**: Apache-2.0
- **Repository**: https://github.com/Xxx91n/env-manager
- **Languages**: C# (.NET 10), TypeScript, Svelte 4, Rust
- **Goal**: A modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support, inspired by Microsoft PowerToys environment variable editor but standalone and agent-friendly.

## Architecture

Three layers:
1. **CLI backend** (`Program.cs`) - C# .NET 10 console app, reads/writes Windows Registry directly, compiles to `env-manager-cli.exe`.
2. **Tauri shell** (`frontend/src-tauri/`) - Rust app, embeds CLI as bundled resource, spawns CLI subprocesses, returns JSON via Tauri IPC.
3. **Svelte frontend** (`frontend/src/`) - TypeScript + Svelte 4 + TailwindCSS in WebView2. Talks to Rust only via `invoke('run_cli', ...)`.

The GUI has NO local web server. Dev: Vite at `localhost:5173`. Production: Tauri embeds static assets via its `tauri://` custom protocol.

See [docs/architecture.md](docs/architecture.md) for IPC bridge, race condition prevention, system tray, toast, caching, auto-update, security hardening, modal dialog system, rename/change-scope contracts, profile audit history, and the GUI/CLI alignment table.

## Project Structure

```
env-manager/
+- Program.cs                  # C# CLI implementation
+- env-manager.csproj          # .NET 10 project (AssemblyName: env-manager-cli)
+- AGENTS.md                   # This file (project-level operating instructions)
+- AGENTS.cli.md               # CLI-level agent guide (distributed with CLI binary)
+- README.md / README_CN.md    # English / Chinese documentation
+- docs/                       # Detailed reference (cli-commands, architecture, build-and-release, backup-and-profiles)
+- frontend/                   # Tauri GUI application (src/, src-tauri/, tests/)
+- release/                    # Build output (gitignored): portable/, cli-only/, msi/
+- bin/ obj/ dist/             # Intermediate build output (gitignored)
```

## CLI Command Quick Reference

Full table, scope, debug, error handling, profiles, toggle, path editor, path resolution: see [docs/cli-commands.md](docs/cli-commands.md).

Read-only (concurrent-safe, read-locked): `list`, `get`, `backup`, `diff`, `validate`, `agents`, `profile list/show/status, launch`, `path list, path health (no --fix)`, `path dedupe --dry-run`, `history list`, `bulk export`, `expand`, `protection list`, `update check`.

Write (serialized, write-locked): `set`, `rename`, `change-scope`, `delete`, `toggle`, `restore`, `merge`, `profile create/delete/apply/unapply/add-var/remove-var/edit-var/rename, set-launch, add-secret/edit-secret/remove-secret`, `path add/remove/move-up/move-down/rename/dedupe, path health --fix`, `history undo/delete`, `bulk import`, `protection add-path/remove-path/add-var/remove-var`.

All commands: `env-manager-cli <command> [arguments] [--flags]`. `--debug`/`-d` anywhere enables verbose stderr. `--scope user|system` (default user). Exit 0/1.

## Hard Boundaries (Red Lines)

These invariants must never be violated by any code change:

- **Protected variables**: built-in protected system variables are seeded from embedded `protection.defaults.json` into `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json`. They cannot be set, deleted, toggled, renamed, scope-changed, or added to a profile at system scope. Custom user-locked variables (`protected-vars.json`) cannot be toggled, edited, or deleted until unlocked. See `IsProtectedVariable`.
- **Protected PATH entries**: built-in defaults are seeded from embedded `protection.defaults.json` into `builtin-protected-paths.json`. Built-in and custom locked entries (`protected-paths.json`) cannot be removed or edited via `path remove`/`rename`. See `IsProtectedPathEntry`. Reordering (`move-up`/`move-down`) is allowed because it is non-destructive.
- **Cross-process mutex**: all write operations acquire `Local\EnvManager.RegistryMutation` mutex. Plus Rust `CLI_RWLOCK` write lock. Plus frontend `writeChain` serialization. Three layers, never bypass.
- **Variable rename/scope-change contract**: `rename` writes+verifies target before deleting source. `change-scope` writes new scope, verifies, deletes source, relocates `_EnvManager_disabled` backup. Both reject protected `oldName`/`newName`/source-scope/target-scope at entry point. Never delete-then-set for renames.
- **GUI EditDialog 3-way save ordering**: `rename(old scope)` -> `changeScope(overwrite flag, never hardcoded true)` -> `setVariable(value, overwrite flag)`. The `--overwrite` flag flows only from explicit user confirmation of the conflict modal, never an injected synthetic `true`.
- **Profile audit**: `TryUndoProfileAudit` uses allow-list of known subcommands plus Id-based conflict detection; unknown `profile <x>` subcommands emit error and `return false` (never silently succeed); try/catch fallback returns false so `--force` contract works.
- **v0.7.0 Launch profiles + DPAPI secrets**: a Launch profile is NEVER written to the registry, NEVER broadcasts `WM_SETTINGCHANGE`. `profile launch` spawns the target with `env_clear` + inject, classified as read in Rust `is_read_only`. `profile set-launch` is a write (mutates profiles.json, holds `CLI_RWLOCK` write lock). `ValidateLaunchTarget` rejects `\Windows\System32` targets and non-executable extensions. Profile names are globally unique because all CLI profile commands are name-addressed.
- **v0.7.0 DPAPI secrets (hard boundary)**: secret variable values are DPAPI-CurrentUser encrypted on disk (base64 of `CryptProtectData` output, scope CurrentUser). Plaintext lives only in transient CLI/launcher process memory - never written to `profiles.json`, the registry, or logs. `profile reveal-secret` is the ONLY stdout-plaintext path and is DPAPI-bound to the current user (cannot be decrypted by another user or machine). `profile launch` decrypts secrets in the launcher process; if decryption fails the launch is refused (never silently inject ciphertext garbage). Audit records the variable NAME plus `<redacted>`/`<encrypted>` markers - never the plaintext or ciphertext. `add-secret`/`edit-secret` require the profile to be unapplied (same as other profile mutations).
- **v0.7.0 Secrets never applied to registry (hard boundary)**: `IsProfileApplicable` rejects any profile containing a secret variable from being applied to the user registry. Secrets on a Global profile can be stored but NEVER applied - applying would write DPAPI ciphertext garbage to the registry, violating the plaintext-never-persisted invariant. Secrets are meaningful only on Launch profiles (env_clear + inject + decrypt-in-process). `ProfileEditSecret` rejects renaming a secret into a protected system variable name (same entry-point invariant as `ProfileAddSecret`).
- **v0.8 Versioned Secret Envelopes (hard boundary)**: All secret values written to profiles.json are wrapped in a JSON envelope { provider, version, createdAt, ciphertext } for dpapi-current-user or { provider, version, targetName } for credential-manager. SecretProviderManager routes to the active provider declared in secret-providers.json. Unknown providers are fail-closed. Bare pre-v0.8 DPAPI base64 blobs are auto-detected and decrypted via DpapiCurrentUserProvider. CredentialManagerProvider uses advapi32.dll CredWriteW/CredReadW/CredDeleteW with CRED_TYPE_GENERIC; the credential blob is DPAPI-encrypted before CredMan storage. Profile stores only the CRED target name. CLI command profile secret-provider list (read) and set (write) manages the active provider. Audit records provider name, never plaintext or ciphertext.
- **v0.8 Phase 3 Secret Rotation/Export/Import (hard boundary)**: `profile secret-provider rotate` re-encrypts all secrets across all profiles with the active provider. If any secret fails to decrypt (wrong provider, deleted CredMan entry), the rotation skips it and increments the failed count - the failed secret is NOT deleted. The `profile export-secrets` command exports secrets to a DPAPI-CurrentUser-encrypted backup file (portable within the same user account). The `profile import-secrets` command imports secrets from an encrypted backup; each imported secret is verified by trial-decryption before being written to the profile. Import requires the profile to be unapplied. Rotation/export/import all generate audit entries with operation name, file path, and counts - never the secret values. Rotation = write (mutates profiles.json); export = read; import = write.
- **v0.8 Phase 4-5 Additional Secret Providers (hard boundary)**: Two new ISecretProvider implementations are registered in SecretProviderManager:

- **PowerShellSecretManagementProvider** (name: powershell-secretmanagement): delegates encryption to PowerShell Set-Secret and decryption to Get-Secret via a hosted pwsh process with CREATE_NO_WINDOW. The profile stores only the vault name and secret name in the envelope TargetName; the actual secret value lives in the PowerShell SecretManagement vault. Requires PowerShell 7 + Microsoft.SecretManagement + Microsoft.SecretStore modules installed. Fail-closed if pwsh is not found or exits non-zero. The 30-second process timeout prevents indefinite hangs.

- **VaultKV2Provider** (name: vault-kv2): reads/writes secrets via the Vault HTTP API (POST /v1/secret/data/<path> and GET /v1/secret/data/<path>). The profile stores only the mount path, secret path, and key name in the envelope TargetName. Token is read from VAULT_TOKEN env var (never persisted). TLS is mandatory: VAULT_ADDR must use https:// for non-localhost addresses; http:// is only permitted for 127.0.0.1/localhost/[::1]. Fail-closed on network errors, 403/404, or timeout (10s). The CLI does not cache decrypted material beyond the launcher process lifetime.



- **v0.7.2 Phase 6-7 SOPS + Azure Key Vault (hard boundary)**: Two new ISecretProvider implementations are registered in SecretProviderManager:

- **SopsProvider** (name: sops): shells out to a verified sops binary (-e/-d) under CREATE_NO_WINDOW with 30s timeout. The profile stores the full sops-encrypted JSON as the envelope ciphertext field. Supports Age, PGP, AWS KMS, Azure Key Vault, GCP KMS, and HashiCorp Vault decryptors via sops env vars. Binary is discovered via SOPS_PATH env var, PATH search, or common install locations. Fail-closed if sops binary is missing or non-functional. Temp files are created in a per-operation isolated directory and securely cleaned up in a finally block. The envelope is self-contained; Delete is a no-op.

- **AzureKeyVaultProvider** (name: azure-keyvault): calls Azure Key Vault REST API (PUT/GET /secrets/<name>?api-version=7.4). Profile stores only vault URI + secret name as TargetName (format: vaultUri|secretName). TLS mandatory (HTTPS only). Token obtained via managed identity (IMDS 169.254.169.254) or service principal (AZURE_CLIENT_ID/AZURE_CLIENT_SECRET/AZURE_TENANT_ID). Token cached in process memory only with 5-minute expiry buffer. 15s HTTP timeout. Fail-closed on 403/404. Supports rotation. Delete issues a soft-delete. Secret names sanitized to alphanumeric + hyphens, max 127 chars.


- **`RunChangeScope` ambiguous-scope rejection**: when a variable exists in both user and system scope, the caller must specify `--scope` explicitly; auto-detection is rejected (no silent pick of user).
- **`path dedupe` HashSet isolation**: the dedupe `seen` HashSet only records non-protected entries, so protected entries are never treated as duplicates of themselves or each other.
- **Backup file validation**: `.json` extension required; writes to `\Windows`, `\Program Files`, `\Program Files (x86)` blocked; 50 MB cap.
- **Disabled-variable restoration**: `toggle` writes a same-scope backup with the original raw registry value and `RegistryValueKind`. Re-enable verifies both exactly before deleting the backup; if both original and backup exist, it refuses the conflict. `list` and `get` project a backup-only disabled variable through the same contract for both user and system scope. Names ending in `_EnvManager_disabled` are internal and cannot be created, addressed, renamed, deleted, or moved directly.
- **Profile creation**: profile names are globally unique because commands are name-addressed. `profile create <name> --type launch --target <exe> [--args <args>] [--cwd <dir>]` validates and persists Launch profiles in one write transaction. Never create a Global profile and convert it in a later operation.
- **Frontend cache consistency**: variable and PATH reads use bounded 5-second caches with generation-aware single-flight reads. A successful write advances the generation; older reads must not refill caches or replace newer visible state. The frontend write busy store is reference-counted until the serialized queue drains.
- **Trailing-backslash + quote argv recovery**: .NET argv tokenization can fold a quoted value ending with `\` together with following args. `LenientArgs.WasArgsCorruptedByTrailingBackslashQuote` detects the signature (an arg element containing both a quote and an embedded long-option/separator marker such as ` --scope `, ` --overwrite`, ` --index `, or standalone ` -- `); only then `LenientArgs.Tokenize` re-scans `Environment.CommandLine` with quotes as terminators and backslashes literal. Recovery replaces known-corrupted argv even when its token count changes. A standalone `--` stays intact for `profile launch <name> -- <extra-args>`. Clean argv from the Tauri/Rust `Command::arg()` path is never re-tokenized. See `ArgTokenizer.cs`.
- **Verified registry writes**: `SetVariable` captures the prior raw value and registry kind, writes, then verifies the exact persisted raw value and kind before reporting success. If verification or writing fails, it restores the prior value (or deletes a newly-created value) and verifies that restoration. PATH mutations must use `SetPathEntries` and therefore this transaction; never add a direct PATH registry write path.
- **Rust IPC input validation**: command whitelist, max 64 args, max 32,767 chars per arg, null bytes and control characters rejected.
- **Audit history**: `%LOCALAPPDATA%\EnvManager\audit.json`, capped at 2,000 entries. Undo refuses stale changes unless `--force` explicit. Profile entries carry `Scope = "profile"` and route to `TryUndoProfileAudit`.
- **Live test harness (hard boundary after incident)**: any local CLI smoke test against the REAL registry MUST use `scripts/test-with-restore.ps1`. Before testing it snapshots every value name, unexpanded value, and `RegistryValueKind` in `HKCU\Environment` plus accessible `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`, and byte-snapshots test-owned Env Manager internal configuration (`profiles.json`, `audit.json`, and protection JSON files). It compares exact before/after state, restores internal configuration after the suite, and on any failure or drift reconciles every registry value (including deleting newly introduced values) before broadcasting `WM_SETTINGCHANGE`. HKLM is skipped, with an explicit warning, only when an administrator-accessible snapshot cannot be created. The harness retains `.reg` and `.json` artifacts on failure or `-KeepBackup`, otherwise removes them on a clean run. Never run raw registry-mutating CLI smoke commands against the host. Backups live in `.test-backups/` (gitignored).
- **Per-session host snapshot (hard boundary after incident)**: before any local dev session that touches the CLI or build, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/snapshot-host-env.ps1` once. It exports `HKCU\Environment` and `HKLM\...\Environment` to `.env_bak/<UTC-timestamp>/` and copies Env Manager's internal configs from `%LOCALAPPDATA%\EnvManager\` (profiles.json, audit.json, protected-vars.json, protected-paths.json, builtin-protected-vars.json, builtin-protected-paths.json) into `.env_bak/<UTC-timestamp>/internal-configs/`. `.env_bak/` is gitignored and NOT auto-cleaned. This is the per-session forensic complement to `test-with-restore.ps1` (which does per-run backup+restore). The prior incident (test harness only backed up HKCU, so a RunSet regression clobbered the user system PATH) motivated both guards.

- **v0.7.1 Incident: CommonProgramW6432 disabled via toggle (2026-07-22)**: `CommonProgramW6432` was a built-in Windows REG_EXPAND_SZ system variable (value `C:\Program Files\Common Files`) but was missing from `protection.defaults.json`, so the `toggle` command could rename it to `CommonProgramW6432_EnvManager_disabled` and delete the original. The user noticed `opencode` and `npm` broke because system PATH resolution depends on this family of variables. Root cause: `protection.defaults.json` listed `CommonProgramFiles` and `CommonProgramFiles(x86)` but omitted `CommonProgramW6432` and `ProgramW6432`. Recovery: wrote `CommonProgramW6432` back to HKLM with the exact original value and REG_EXPAND_SZ kind, verified, deleted the `_EnvManager_disabled` backup, broadcast WM_SETTINGCHANGE. Post-incident fix: added `COMMONPROGRAMW6432` and `PROGRAMW6432` to `protection.defaults.json` and re-seeded `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json`. All future built-in Windows variables that the OS derives from `ProgramFilesDir`/`ProgramFilesDir(x86)`/`CommonProgramFilesDir`/`CommonW6432` must be added to the protection list before any toggle/delete/set/rename/change-scope path can touch them.
- **Logs never record environment values** - CLI/Rust log command names and argument counts only. Values may contain credentials.

### Agent Safety Guidelines

1. Always use `--scope user` for non-interactive workflows. System scope requires elevation and may fail silently.
2. Call `agents --json` first to discover the full command contract, safety boundaries, and async support.
3. Read commands are safe to batch concurrently. Write commands are serialized - do not fire multiple writes in parallel.
4. Never delete critical system variables. Always backup first.
5. Profile names: 1-255 chars, no null bytes, newlines, carriage returns. Variable names in profiles: no `=`.
6. Backup files: `.json` extension, not in system directories, under 50 MB.

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

```powershell
cd frontend
npx vitest run          # Run all tests once
npm run test:ui         # Interactive test UI
npm run test:coverage   # With coverage report
npm run test:e2e        # Playwright E2E tests
```

Mandatory rules:
1. **Before every commit**: `npx vitest run` from `frontend/`, all tests must pass.
2. **New feature = new tests**: new CLI command, GUI component, store, API function, or i18n key must add unit test coverage in the same commit.
3. **i18n key completeness**: `src/lib/translations.test.ts` validates every `en.json` key exists in all 9 non-English files with non-empty values.
4. **Build verification after code changes**: run `powershell -NoProfile -ExecutionPolicy Bypass -File frontend/scripts/build-all.ps1` and verify `release/portable/env-manager.exe` launches. Do not commit code that breaks the build. See [docs/build-and-release.md](docs/build-and-release.md).
5. **No emoji in tests** - same no-emoji rule as the rest of the project.
6. **Live CLI test harness (two-pronged gate)**: when validating the published CLI in `release/cli-only/`, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-with-restore.ps1`. This script backs up `HKCU\Environment` to a `.reg` file before tests, runs a smoke suite that touches only `EM_TEST_`-prefixed user variables and profiles, then verifies registry drift is zero (leftover `EM_TEST_` keys or modified pre-existing keys trigger restore). On any failure (test fails OR registry drift detected), it restores from the backup, broadcasts `WM_SETTINGCHANGE`, and exits non-zero while keeping the backup for forensics. On a clean run the backup is auto-deleted. Never run live CLI smoke tests against the real registry without this script (it is the only guard that prevents test-time mutation of the host machine).

Test file inventory:

| File | Coverage |
|------|----------|
| `src/App.test.ts` | Root component rendering, navigation |
| `src/lib/stores.test.ts` | Svelte stores (variables, loading, error, scope, search, settings) |
| `src/lib/api.test.ts` | Tauri IPC bridge, CLI command invocations, response parsing |
| `src/lib/i18n.test.ts` | Locale registration, default locale, localStorage persistence |
| `src/lib/translations.test.ts` | Translation key completeness across all 10 locales |
| `src/lib/race.test.ts` | CLI/GUI race condition prevention, toggle safety, rapid toggle serialization |
| `src/lib/sync.test.ts` | CLI/GUI state synchronization, mutation triggers refresh, error store lifecycle |
| `src/lib/debug.test.ts` | Debug logging, 200-entry cap, isWriteInProgress tracking |
| `src/lib/profile-drag.test.ts` | Pointer reorder, localStorage persistence, stale/duplicate order recovery, and large-list preservation |
| `src/lib/multi-profile.test.ts` | Single-profile policy, backup/restore, protected variable rejection |
| `src/lib/change-scope-protection-profile.test.ts` | change-scope CLI args, protected-variable rejection, profile audit record + undo |
| `src/lib/path-dedupe.test.ts` | dedupePathEntries CLI args (dry-run, scope), result shape, failure propagation |
| `src/lib/review-regressions.test.ts` | Code-review invariants: EditDialog ordering, protected-variable guards, PATH dedupe isolation, ambiguous scope rejection, profile audit fail-loud, transactional PATH writes, argv recovery, harness rollback verification, safe startup fallback, and console-free update checks |
| `src/lib/quoting.test.ts` | GUI argv safety: trailing-backslash/quote values stay independent array elements (no merging with --scope/--index/--overwrite) |
| `src/lib/v0.6-launch-health.test.ts` | v0.6.0 profileSetLaunch/profileLaunch/pathHealth API arg construction, fix/dry-run routing, read vs write classification |
| `src/lib/v0.7-secrets.test.ts` | v0.7 profileAddSecret/EditSecret/RemoveSecret/RevealSecret CLI args (path, write classification), secretVariables type surface, design invariants |
| `src/lib/path-badge-exclusivity.test.ts` | PathEditor badge exclusivity after health check (single duplicate badge regression, all boolean combinations) |
| `src/lib/components/Variables.test.ts` | Protected variable controls are disabled and cannot dispatch a toggle IPC call |
| `src/lib/history-col-resize.test.ts` | HistoryPage column resize persistence, defaults, corrupt-storage fallback, clamp range |
| `scripts/test-with-restore.ps1` | Live CLI smoke harness: exact HKCU plus accessible HKLM snapshots, disabled-variable raw-value/RegistryValueKind recovery, raw trailing-backslash command-line regression, exact drift verification, and transactional reconciliation on failure. |
| `scripts/snapshot-host-env.ps1` | Per-session forensic snapshot: export HKCU+HKLM Environment hives + copy EnvManager internal configs to `.env_bak/<timestamp>/` (not auto-cleaned) |

## Coding Standards

- **C#**: 4-space indent, 120 char max line, `using` for Registry keys, catch specific exceptions (no empty catch), explicit types on public API, `var` for locals.
- **TypeScript/Svelte**: 2-space indent, strict mode, no implicit any, JSDoc on exports, `$:` reactive syntax, props validation.
- **Rust**: 4-space indent, `log` crate macros for diagnostics, `#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]` to hide the release console. Every Windows-native child process spawned by the Tauri shell must apply `CREATE_NO_WINDOW`, including CLI execution, `where`, and update checks.
- **No emoji** in source, tests, docs, or commit messages.
- **Font size scaling**: 6 presets (85%-160%) in Settings, applied via `document.documentElement.style.fontSize = 13 * scale + px`, CSS rem units throughout, persisted as `fontScale` in localStorage.
- **Add CLI to PATH**: Settings dialog one-click button. Calls `cli_diagnostics` to resolve CLI path (no hardcoding), extracts dir, checks for duplicates in user PATH, adds via `path add`. Implemented as `addCliToPath()` in `api.ts`.

## File encoding

- All files: UTF-8 without BOM.
- Line endings: enforced by `.gitattributes` at repo root. Default text: LF on disk and in index. Windows-native scripts (`.bat`, `.ps1`, `.cmd`): CRLF. Binary assets (`png`, `ico`, `exe`, `dll`, `msi`, etc.): marked `binary`, never normalized.
- `core.autocrlf` is `false` at the repo level. Do not re-enable it.
- `frontend/node_modules/` is gitignored; never tracked. If tracked files appear: `git rm -r --cached frontend/node_modules` and commit.
- After any `.gitattributes` change: `git add --renormalize .` and commit the line-ending-only diff.
- `apply_patch` does byte-exact matching. If a patch fails for context that looks identical, suspect CRLF/LF mismatch on disk and re-inspect the target region before retrying. Never write a file with mixed line endings.

## Commit Convention

Conventional Commits:

```
<type>(<scope>): <subject>

<body>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `perf`, `chore`
Scopes: `cli`, `gui`, `backup`, `registry`, `i18n`, `docs`, `build`

## Mandatory Build After Code Changes

Every commit that modifies CLI, GUI, or build code MUST produce compiled artifacts in `release/` before pushing. See [docs/build-and-release.md](docs/build-and-release.md) for the full build procedure, prerequisites, output layout, and release steps.

```powershell
Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process -Force
cd frontend
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-all.ps1
```

Verify: `release/portable/env-manager.exe`, `release/portable/env-manager-cli.exe`, `release/cli-only/env-manager-cli.exe`, `release/msi/Env Manager_X.Y.Z_x64.msi` (no locale suffix). The `release/` directory is gitignored - artifacts are for local testing only, not committed to git.

## Documentation Maintenance

When the project changes, update files in the same commit:

| Event | Files to update |
|-------|----------------|
| New CLI command | AGENTS.md (quick reference), docs/cli-commands.md, README.md, README_CN.md, docs/architecture.md alignment table |
| Changed command args | AGENTS.md, docs/cli-commands.md, README.md, README_CN.md |
| New GUI feature | AGENTS.md, docs/architecture.md alignment table, README.md, README_CN.md, all 10 translation files |
| New debug log point | docs/build-and-release.md (Logging section) |
| Dependency update | docs/build-and-release.md, AGENTS.md if it affects build/architecture |
| Build change | AGENTS.md, docs/build-and-release.md, README.md, README_CN.md |
| Directory structure change | AGENTS.md |
| CodeGraph index change | docs/build-and-release.md (CodeGraph section) |
| Code change (any) | Run build-all.ps1, verify release/ artifacts |
| New test file | AGENTS.md (test inventory), docs if it documents new behavior |
| Architecture/IPC/security change | docs/architecture.md, docs/backup-and-profiles.md, AGENTS.md hard boundaries |

A commit that does not update AGENTS.md (and the relevant `docs/` file) when the project has changed is considered incomplete.

## How to Add a New CLI Command

1. Add a `case` in `Program.cs` `Main()` switch statement.
2. Implement the command method.
3. Update `ShowHelp()` with usage text.
4. Add to the command table in [docs/cli-commands.md](docs/cli-commands.md) and the quick reference in AGENTS.md.
5. If write command: add to `WRITE_COMMANDS` in `frontend/src-tauri/src/main.rs`. If read command: add to `READ_COMMANDS`.
6. Update `ALLOWED_COMMANDS` in `main.rs`.
7. Update `README.md` and `README_CN.md`.
8. Add the API function in `frontend/src/lib/api.ts` and the GUI surface in the appropriate `.svelte` component.
9. Add i18n strings to all 10 translation files.
10. Update the alignment table in [docs/architecture.md](docs/architecture.md).
11. Add test coverage.
12. Run `build-all.ps1` and verify release artifacts.

## Detailed Reference Index

| Topic | File |
|-------|------|
| Full CLI command table, scope, debug, error handling, profiles, toggle, path editor, path resolution | [docs/cli-commands.md](docs/cli-commands.md) |
| Architecture, IPC bridge, race condition prevention, system tray, toast, caching, auto-update, security hardening, modal dialog, rename/change-scope, profile audit history, GUI/CLI alignment table | [docs/architecture.md](docs/architecture.md) |
| Build system, prerequisites, output layout, mandatory build rules, release steps, dependencies, CodeGraph, performance targets, logging, debugging | [docs/build-and-release.md](docs/build-and-release.md) |
| Backup JSON format, profile JSON format, extended state and safety contracts, full security list, agent safety guidelines | [docs/backup-and-profiles.md](docs/backup-and-profiles.md) |
| CLI-level agent guide (distributed with CLI binary) | [AGENTS.cli.md](AGENTS.cli.md) |
