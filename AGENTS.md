# Env Manager — Agent Instructions

Windows environment-variable manager: a C# .NET 10 command-line tool that reads and writes the registry directly, a Tauri/Rust shell, a Svelte 5 GUI, and an optional Rust service that keeps secret mounts fresh. Apache-2.0, repository `Xxx91n/env-manager`.

## Run it
- Full build: `node scripts/build.mjs --arch x64` (`--skip-gui` / `--skip-msi` / `--skip-cli` to segment); artifacts land in the gitignored `release/`.
- Tests: `scripts/test-all.ps1` — runs both suites, keeps the full log, prints exit codes only.
- Docs-only change: `powershell -NoProfile -File scripts/check-docs.ps1` — bare-id gate, `git diff --check`, cited file/`Class.Member` resolution, stale-claim guard, the glossary's avoid words, and translation structure consistency. Source changes still run `scripts/test-all.ps1`.
- Registry-mutating smoke tests go through `scripts/test-with-restore.ps1`. Running a write command raw is a red line.

## Don't touch
- `src/Audit/` and `src/Secrets/` are bounded contexts: cross-context code consumes only `Secrets.Core` and `Secrets.Manager`. `release/`, `bin/`, `obj/` are artifacts.
- Three top rules (read `docs/agents/hard-boundaries.md` before changing code): protected variables and PATH entries are never written or deleted; every write takes all three locks (mutex + `CLI_RWLOCK` + `writeChain`); rename/scope-change writes and verifies the target before deleting the source, and TxR/TxF is banned.
- Service startup order and thread model (`service/src/scm.rs`, `service/src/main.rs`): `StartServiceCtrlDispatcherW` runs once on the process's initial thread and `service_main` builds the tokio runtime on the SCM thread; `run_server` creates the named-pipe listener, reports `SERVICE_RUNNING`, and only then starts the refresh loop (`reconcile_loop`). Moving slow work ahead of the RUNNING report brings back the boot-time 1053 failure.
- Stop converges what it interrupts (`service/src/ipc.rs`, `service/src/reconcile.rs`): the accept loop joins its in-flight request handlers, and a delegated CLI rotate child is terminated *and reaped* inside that window before `SERVICE_STOPPED` (#153). A lost rotation is recovered by replay, never by waiting the child out - the same window answers `SERVICE_ACCEPT_SHUTDOWN`. The child also runs in a per-delegation kill-on-close job object (`service/src/job_object.rs`), so the provider helpers it started die with it; a job that cannot be created or assigned, and a helper started before the assignment, are recorded exceptions, not promises (#160). The plaintext those helpers write does not survive the stop either: the service owns one sops scratch root per delegation and deletes it after the child is reaped, retrying briefly and logging any failure (#177).

## Non-obvious traps
- `env-manager.csproj` holds the app version; release-please keeps `frontend/package.json` and `frontend/src-tauri/tauri.conf.json` in step with it, and `build.mjs` aborts on a stale CLI binary. The two Rust crate manifests still say `0.12.0`; that drift is tracked separately.
- Changing `installer.wxs` must pass three real acceptance layers: WiX structural compilation (`scripts/build.mjs` compiles `installer.wxs` into the MSI, so structure errors fail the build — but only when WiX 3.14 is present: set the `WIX` env var or install WiX 3.14; without it, build.mjs logs "WiX tools not found, skipping MSI" and skips the MSI, leaving structure errors hidden on a local build), the five-cell silent MSI install/upgrade matrix (`scripts/test-msi-upgrade-matrix.ps1` and the `msi-upgrade-matrix` job in `.github/workflows/build.yml`), and the four-step silent msiexec runbook in `docs/build-and-release.md`.
- `GLOSSARY.md` owns vocabulary. The nine locale READMEs follow the English original and are updated by their own tickets.
- Version control is `but` (GitButler). No push, merge, tag, release or secret change without an explicit instruction.

## Agent skills

### Issue tracker

Issues live as GitHub issues on `Xxx91n/env-manager`, driven by the `gh` CLI. See `docs/agents/issue-tracker.md`.

### Triage labels

Five canonical triage roles; label strings equal the role names (`needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`). See `docs/agents/issue-tracker.md` §Triage labels.

### Domain docs

Single-context: root `GLOSSARY.md` for vocabulary; decisions live as comments at the code they constrain. See `docs/agents/domain.md`.
