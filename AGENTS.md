# Env Manager — Agent Instructions

Windows environment-variable manager: a C# .NET 10 CLI that reads and writes the registry directly, a Tauri/Rust shell, a Svelte 4 GUI, and an optional Rust service that manages secret-mount lifecycles. Apache-2.0, repository `Xxx91n/env-manager`.

## Run it
- Full build: `node scripts/build.mjs --arch x64` (`--skip-gui` / `--skip-msi` / `--skip-cli` to segment); artifacts land in the gitignored `release/`.
- Tests: `scripts/test-all.ps1` — runs both suites, keeps the full log, prints exit codes only.
- Docs-only change: `powershell -NoProfile -File scripts/check-docs.ps1` — bare-id gate, `git diff --check`, cited file/`Class.Member` resolution, stale-claim guard. Source changes still run `scripts/test-all.ps1`.
- Registry-mutating smoke tests go through `scripts/test-with-restore.ps1`. Running a write command raw is a red line.

## Don't touch
- `src/Audit/` and `src/Secrets/` are bounded contexts: cross-context code consumes only `Secrets.Core` and `Secrets.Manager`. `release/`, `bin/`, `obj/` are artifacts.
- Three top rules (read `docs/agents/hard-boundaries.md` before changing code): protected variables and PATH entries are never written or deleted; every write takes all three locks (mutex + `CLI_RWLOCK` + `writeChain`); rename/scope-change writes and verifies the target before deleting the source, and TxR/TxF is banned.
- Service startup order and thread model (`service/src/scm.rs`, `service/src/main.rs`): `StartServiceCtrlDispatcherW` runs once on the process's initial thread and `service_main` builds the tokio runtime on the SCM thread; `run_server` creates the named-pipe listener, reports `SERVICE_RUNNING`, and only then starts the reconcile loop. Moving slow work ahead of the RUNNING report brings back the boot-time 1053 failure.
- Stop converges what it interrupts (`service/src/ipc.rs`, `service/src/reconcile.rs`): the accept loop joins its in-flight connection handlers, and a delegated CLI rotate child is terminated *and reaped* inside that window before `SERVICE_STOPPED` (#153). A lost rotation is recovered by replay, never by waiting the child out - the same window answers `SERVICE_ACCEPT_SHUTDOWN`.

## Non-obvious traps
- `env-manager.csproj` holds the only version; `build.mjs` syncs it to `frontend/package.json`.
- Changing `installer.wxs` must pass three real acceptance layers: WiX structural compilation (`scripts/build.mjs` compiles `installer.wxs` into the MSI, so structure errors fail the build — but only when WiX 3.14 is present: set the `WIX` env var or install WiX 3.14; without it, build.mjs logs "WiX tools not found, skipping MSI" and skips the MSI, leaving structure errors hidden on a local build), the five-cell silent MSI install/upgrade matrix (`scripts/test-msi-upgrade-matrix.ps1` and the `msi-upgrade-matrix` job in `.github/workflows/build.yml`), and the four-step silent msiexec runbook in `docs/build-and-release.md`.
- `GLOSSARY.md` owns vocabulary; a README change is mirrored into the 9 locale files in the same section.
- Version control is `but` (GitButler). No push, merge, tag, release or secret change without an explicit instruction.

## Agent skills

### Issue tracker

Issues live as GitHub issues on `Xxx91n/env-manager`, driven by the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical triage roles; label strings equal the role names (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/issue-tracker.md` §Triage labels.

### Domain docs

Single-context: root `GLOSSARY.md` for vocabulary; decisions live as comments at the code they constrain. See `docs/agents/domain.md`.
