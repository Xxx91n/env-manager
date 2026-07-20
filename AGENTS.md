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

## Project Overview

- **Name**: Env Manager
- **Version**: 0.7.0
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

- **Protected variables**: built-in protected system variables (loaded from `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json`) cannot be set, deleted, toggled, renamed, scope-changed, or added to a profile at system scope. Custom user-locked variables (`protected-vars.json`) cannot be toggled, edited, or deleted until unlocked. See `IsProtectedVariable`.
- **Protected PATH entries**: built-in protected PATH entries (`builtin-protected-paths.json`) and custom locked entries (`protected-paths.json`) cannot be removed or edited via `path remove`/`rename`. See `IsProtectedPathEntry`. Reordering (`move-up`/`move-down`) is allowed - reordering is not destructive.
- **Cross-process mutex**: all write operations acquire `Local\EnvManager.RegistryMutation` mutex. Plus Rust `CLI_RWLOCK` write lock. Plus frontend `writeChain` serialization. Three layers, never bypass.
- **Variable rename/scope-change contract**: `rename` writes+verifies target before deleting source. `change-scope` writes new scope, verifies, deletes source, relocates `_EnvManager_disabled` backup. Both reject protected `oldName`/`newName`/source-scope/target-scope at entry point. Never delete-then-set for renames.
- **GUI EditDialog 3-way save ordering**: `rename(old scope)` -> `changeScope(overwrite flag, never hardcoded true)` -> `setVariable(value, overwrite flag)`. The `--overwrite` flag flows only from explicit user confirmation of the conflict modal, never an injected synthetic `true`.
- **Profile audit**: `TryUndoProfileAudit` uses allow-list of known subcommands plus Id-based conflict detection; unknown `profile <x>` subcommands emit error and `return false` (never silently succeed); try/catch fallback returns false so `--force` contract works.
- **v0.7.0 Launch profiles + DPAPI secrets**: a Launch profile is NEVER written to the registry, NEVER broadcasts `WM_SETTINGCHANGE`. `profile launch` spawns the target with `env_clear` + inject, classified as read in Rust `is_read_only`. `profile set-launch` is a write (mutates profiles.json, holds `CLI_RWLOCK` write lock). `ValidateLaunchTarget` rejects `\Windows\System32` targets and non-executable extensions. Global and Launch profile name namespaces are independent (cross-type name collision allowed).
- **v0.7.0 DPAPI secrets (hard boundary)**: secret variable values are DPAPI-CurrentUser encrypted on disk (base64 of `CryptProtectData` output, scope CurrentUser). Plaintext lives only in transient CLI/launcher process memory - never written to `profiles.json`, the registry, or logs. `profile reveal-secret` is the ONLY stdout-plaintext path and is DPAPI-bound to the current user (cannot be decrypted by another user or machine). `profile launch` decrypts secrets in the launcher process; if decryption fails the launch is refused (never silently inject ciphertext garbage). Audit records the variable NAME plus `<redacted>`/`<encrypted>` markers - never the plaintext or ciphertext. `add-secret`/`edit-secret` require the profile to be unapplied (same as other profile mutations).
- **v0.7.0 Secrets never applied to registry (hard boundary)**: `IsProfileApplicable` rejects any profile containing a secret variable from being applied to the user registry. Secrets on a Global profile can be stored but NEVER applied - applying would write DPAPI ciphertext garbage to the registry, violating the plaintext-never-persisted invariant. Secrets are meaningful only on Launch profiles (env_clear + inject + decrypt-in-process). `ProfileEditSecret` rejects renaming a secret into a protected system variable name (same entry-point invariant as `ProfileAddSecret`).
- **`RunChangeScope` ambiguous-scope rejection**: when a variable exists in both user and system scope, the caller must specify `--scope` explicitly; auto-detection is rejected (no silent pick of user).
- **`path dedupe` HashSet isolation**: the dedupe `seen` HashSet only records non-protected entries, so protected entries are never treated as duplicates of themselves or each other.
- **Backup file validation**: `.json` extension required; writes to `\Windows`, `\Program Files`, `\Program Files (x86)` blocked; 50 MB cap.
- **Trailing-backslash + quote argv recovery**: .NET argv tokenizer folds a quoted value ending with `\` together with following args. `LenientArgs.WasArgsCorruptedByTrailingBackslashQuote` detects this (signature: an arg element containing both a quote and an embedded flag literal like ` --scope `, ` --overwrite`, ` --index `); only then `LenientArgs.Tokenize` re-scans `Environment.CommandLine` (quote always terminator, backslashes literal, `--` honored). Clean argv from the Tauri/Rust `Command::arg()` path is never re-tokenized. See `ArgTokenizer.cs`.
- **Rust IPC input validation**: command whitelist, max 64 args, max 32,767 chars per arg, null bytes and control characters rejected.
- **Audit history**: `%LOCALAPPDATA%\EnvManager\audit.json`, capped at 2,000 entries. Undo refuses stale changes unless `--force` explicit. Profile entries carry `Scope = "profile"` and route to `TryUndoProfileAudit`.
- **Live test harness (hard boundary after incident)**: any local CLI smoke test against the REAL registry MUST be run via `scripts/test-with-restore.ps1`. That harness backs up BOTH `HKCU\Environment` and `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` before each run, and restores BOTH on any verification drift or test failure. Never run raw `env-manager-cli set ... --scope system` against the real registry for testing without this wrapper. The HKLM backup is best-effort (admin may be required); if the export fails the harness warns but continues, and on restore it warns "skipping system-hive restore" if no HKLM backup exists. Both `.reg` and `.json` snapshot files are cleaned up on a clean run, retained when `-KeepBackup` is passed, and ALWAYS retained on failure for forensics. Backups live in `.test-backups/` (gitignored).
- **Per-session host snapshot (hard boundary after incident)**: before any local dev session that touches the CLI or build, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/snapshot-host-env.ps1` once. It exports `HKCU\Environment` and `HKLM\...\Environment` to `.env_bak/<UTC-timestamp>/` and copies Env Manager's internal configs from `%LOCALAPPDATA%\EnvManager\` (profiles.json, audit.json, protected-vars.json, protected-paths.json, builtin-protected-vars.json, builtin-protected-paths.json) into `.env_bak/<UTC-timestamp>/internal-configs/`. `.env_bak/` is gitignored and NOT auto-cleaned. This is the per-session forensic complement to `test-with-restore.ps1` (which does per-run backup+restore). The prior incident (test harness only backed up HKCU, so a RunSet regression clobbered the user system PATH) motivated both guards.
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
| `src/lib/profile-drag.test.ts` | Profile drag-to-reorder, performReorder, applyStoredOrder, localStorage persistence |
| `src/lib/multi-profile.test.ts` | Single-profile policy, backup/restore, protected variable rejection |
| `src/lib/change-scope-protection-profile.test.ts` | change-scope CLI args, protected-variable rejection, profile audit record + undo |
| `src/lib/path-dedupe.test.ts` | dedupePathEntries CLI args (dry-run, scope), result shape, failure propagation |
| `src/lib/review-regressions.test.ts` | Code-review invariants: EditDialog 3-way save ordering, toggle on protected, path dedupe protected isolation, change-scope ambiguous-scope rejection, profile audit fail-loud |
| `src/lib/quoting.test.ts` | GUI argv safety: trailing-backslash/quote values stay independent array elements (no merging with --scope/--index/--overwrite) |
| `src/lib/v0.6-launch-health.test.ts` | v0.6.0 profileSetLaunch/profileLaunch/pathHealth API arg construction, fix/dry-run routing, read vs write classification |
| `src/lib/v0.7-secrets.test.ts` | v0.7 profileAddSecret/EditSecret/RemoveSecret/RevealSecret CLI args (path, write classification), secretVariables type surface, design invariants |
| `src/lib/path-badge-exclusivity.test.ts` | PathEditor badge exclusivity after health check (single duplicate badge regression, all boolean combinations) |
| `src/lib/history-col-resize.test.ts` | HistoryPage column resize persistence, defaults, corrupt-storage fallback, clamp range |
| `scripts/test-with-restore.ps1` | Live CLI smoke harness: backup `HKCU\Environment` -> set/rename/profile/secrets tests -> verify registry drift -> conditional restore. Touch-only-EM_TEST_-keys invariant. |
| `scripts/snapshot-host-env.ps1` | Per-session forensic snapshot: export HKCU+HKLM Environment hives + copy EnvManager internal configs to `.env_bak/<timestamp>/` (not auto-cleaned) |

## Coding Standards

- **C#**: 4-space indent, 120 char max line, `using` for Registry keys, catch specific exceptions (no empty catch), explicit types on public API, `var` for locals.
- **TypeScript/Svelte**: 2-space indent, strict mode, no implicit any, JSDoc on exports, `$:` reactive syntax, props validation.
- **Rust**: 4-space indent, `log` crate macros for diagnostics, `#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]` to hide console in release.
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
