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
- **Version**: 0.9.26
- **License**: Apache-2.0
- **Repository**: https://github.com/Xxx91n/env-manager
- **Languages**: C# (.NET 10), TypeScript, Svelte 4, Rust
- **Goal**: A modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support, inspired by Microsoft PowerToys environment variable editor but standalone and agent-friendly.

## Architecture

Four layers:
1. **CLI backend** (`Program.cs`) - C# .NET 10 console app, reads/writes Windows Registry directly, compiles to `env-manager-cli.exe`.
2. **Tauri shell** (`frontend/src-tauri/`) - Rust app, embeds CLI as bundled resource, spawns CLI subprocesses, returns JSON via Tauri IPC.
3. **Svelte frontend** (`frontend/src/`) - TypeScript + Svelte 4 + TailwindCSS in WebView2. Talks to Rust only via `invoke('run_cli', ...)`.
4. **Service crate** (`service/`) - Rust standalone binary (`env-manager-service.exe`), manages secret mount lifecycle via named pipe IPC. Optional: runs as Windows service (`--mode=service`) or background process (`--mode=background`). The CLI `service` subcommand is a thin IPC gateway to this binary. See ADR 0001 and `docs/secret-architecture-blueprint.md` for the design review roadmap.

The GUI has NO local web server. Dev: Vite at `localhost:5173`. Production: Tauri embeds static assets via its `tauri://` custom protocol.

See [docs/architecture.md](docs/architecture.md) for IPC bridge, race condition prevention, system tray, toast, caching, auto-update, security hardening, modal dialog system, rename/change-scope contracts, profile audit history, and the GUI/CLI alignment table. See [docs/secret-architecture-decision-summary.md](docs/secret-architecture-decision-summary.md) for the Phase A-E secret architecture roadmap and ADR 0001.

## Project Structure

```
env-manager/
+- Program.cs                  # C# CLI implementation
+- env-manager.csproj          # .NET 10 project (AssemblyName: env-manager-cli)
+- AGENTS.md                   # This file (project-level operating instructions)
+- AGENTS.cli.md               # CLI-level agent guide (distributed with CLI binary)
+- README.md / docs/i18n/      # English landing README + localized translations (docs/i18n/README.<locale>.md; zh_CN is the complete reference, others track it)
+- CONTEXT.md                  # Internal development process record (design review session decisions A1-A11 + Risk Matrix)
+- docs/                       # User documentation (cli-commands, architecture, build-and-release, backup-and-profiles, secret-architecture-blueprint, secret-providers-guide, adr/)
+- docs/agents/                # Agent-specific reference (issue-tracker, domain)
+- docs/history/              # Process artifacts (ui-audit, session records)
+- scripts/                    # Build orchestrator (build.mjs), test harness, migration scripts, snapshot scripts
+- service/                    # Rust service crate (env-manager-service.exe, named pipe IPC, reconcile loop, audit ledger)
+- frontend/                   # Tauri GUI application (src/, src-tauri/, tests/)
+- release/                    # Build output (gitignored): portable/, cli-only/, msi/
+- bin/ obj/ dist/             # Intermediate build output (gitignored)
```

## CLI Command Quick Reference

Full table, scope, debug, error handling, profiles, toggle, path editor, path resolution: see [docs/cli-commands.md](docs/cli-commands.md).

Read-only (concurrent-safe, read-locked): `list`, `get`, `backup`, `diff`, `validate`, `agents`, `profile list/show/status, launch, secret-provider list, export-secrets, reveal-secret`, `path list, path health (no --fix)`, `path dedupe --dry-run`, `history list`, `bulk export`, `expand`, `protection list`, `audit list`, `update check`, `service status/health/ping`. `export-state`, `audit verify-ledger/migrate-audit/export-survival-kit --dry-run`.

Write (serialized, write-locked): `set`, `rename`, `change-scope`, `delete`, `toggle`, `restore`, `merge`, `profile create/delete/apply/unapply/add-var/remove-var/edit-var/rename, set-launch, add-secret/edit-secret/remove-secret, secret-provider set/rotate, import-secrets`, `path add/remove/move-up/move-down/rename/dedupe, path health --fix`, `history undo/delete`, `bulk import`, `audit encrypt-file`, `protection add-path/remove-path/add-var/remove-var`, `service refresh/rotate/reload/shutdown`. `import-state`, `audit migrate-audit/recover-from-ledger`.

All commands: `env-manager-cli <command> [arguments] [--flags]`. `--debug`/`-d` anywhere enables verbose stderr. `--scope user|system` (default user). Exit 0/1.

## Hard Boundaries (Red Lines)

All invariants that must never be violated are in [docs/agents/hard-boundaries.md](docs/agents/hard-boundaries.md) (~108 KiB, 279 lines). Read it before any code change.

**Top-level constraints (most critical):**
- **Protected variables/PATH**: built-in protected entries cannot be set/deleted/toggled/renamed/scope-changed. See `IsProtectedVariable` / `IsProtectedPathEntry`.
- **Cross-process mutex**: all writes acquire `Local\EnvManager.RegistryMutation` mutex + Rust `CLI_RWLOCK` write lock + frontend `writeChain` serialization. Three layers, never bypass.
- **Rename/scope-change contract**: write+verify target before deleting source. Never delete-then-set.
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

1. Add a `case` in `Program.cs` `Main()` switch statement.
2. Implement the command method.
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
