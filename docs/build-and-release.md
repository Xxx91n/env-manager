# Build and Release

## Prerequisites

- .NET 10 SDK
- Node.js 18+ with npm
- Rust toolchain (rustc + cargo). Either GNU (`stable-x86_64-pc-windows-gnu`) or MSVC (`stable-x86_64-pc-windows-msvc`) target works. The build system auto-detects the host triple via `rustc -vV` and passes `--target <triple>` to `tauri build`. It does not hardcode any specific target or linker path.

## Build CLI only

```powershell
# Development build (fast, no RID)
dotnet build -c Release
# Output: bin/Release/net10.0-windows/env-manager-cli.exe

# Release build (arch-specific, used by build.mjs)
dotnet publish -c Release -r win-x64 --no-self-contained -p:PublishSingleFile=true
# Output: bin/Release/net10.0-windows/win-x64/env-manager-cli.exe
```

## C# engine unit tests (xUnit)

```bash
dotnet test tests/EnvManager.Engine.Tests/EnvManager.Engine.Tests.csproj
```

- Test project: `tests/EnvManager.Engine.Tests/` (xUnit 2.9.3, Microsoft.NET.Test.Sdk 17.14.1, xunit.runner.visualstudio 3.1.5).
- Covers the pure-logic engine domains: argument tokenizing (`LenientArgs.Tokenize`), exception message scrubbing (`ScrubExceptionMessage`), PATH entry normalization/dedupe (`NormalizePathEntry`).
- Tests are pure: no real registry access and no machine environment dependency (env vars used by tests are Process-scoped and cleared in-test).
- CI: the `build.yml` `verify` job runs this suite after `Build CLI` and gates pull requests.
- Isolation: `env-manager.csproj` excludes `tests/**` from its compile glob (`Compile Remove`) and grants `InternalsVisibleTo` only to `EnvManager.Engine.Tests`; the release artifact list is unchanged.

## Mutation testing (local gate, architecture-recovery issue 13)

Stryker.NET runs a mutation-analysis gate over the four red-line files (rename / change-scope / profile apply-unapply / protection). It is a **local/PR-assist gate, not a CI hard gate**: the v5/dotnet10 CI pipeline friction is unresolved upstream, and the MS-official guidance is not to chase a 100% mutation score.

```bash
# One-time tool install (pinned by .config/dotnet-tools.json)
dotnet tool restore

# Run the gate from the repo root (config: stryker-config.json)
dotnet stryker
```

Config contract (stryker-config.json): `mutate` is pinned to src/VariableRename.cs, src/VariableChangeScope.cs, src/ProfileEffective.cs, src/ProtectionCommand.cs; `ignore-mutations` excludes string/logical mutants; thresholds high 85 / low 70 / break 60 (exit code 2 below 60 is the gate firing); reporters html + progress (HTML report under StrykerOutput/, self-gitignored).

Current baseline (2026-09-03): 94 mutants tested, 76 killed / 18 survived; raw Stryker score 37.07% (NoCoverage mutants count as failures) vs 80.85% over tested mutants only. The survived-mutant classification (2 equivalent, 16 missing-assertion) lives in .scratch/architecture-recovery/reports/13-mutation-testing-gate.md.

CI short run (issue 18): the build.yml `stryker` job runs on `workflow_dispatch` and publishes the HTML report plus per-module scores from `scripts/stryker-module-scores.mjs` (accepts the HTML report or a raw mutation-report.json). Current triage baseline (2026-09-05, issue 18): 96 tested, 14 survivors - 13 weak-assertion (kill tests in MutationSurvivorTriageTests/MutationSurvivorTriageStdoutTests) + 1 registered equivalent; registry at .scratch/architecture-recovery/reports/18-survivor-registry.json.

## Build GUI (development with hot reload)

```powershell
cd frontend
npm install
npm run tauri-dev
# Opens a Tauri window with Vite dev server at localhost:5173
```

## Build GUI (production)

The `tauri-build` npm script runs `scripts/tauri-build.mjs`, which auto-detects the Rust host triple via `rustc -vV` and passes `--target <triple>` to `tauri build`. This ensures the Tauri bundler looks in the same output directory where cargo actually places the binary (`target/<triple>/release/`), which is critical on hosts whose default triple is not the plain host (e.g. `x86_64-pc-windows-gnu` with MinGW).

```powershell
cd frontend
npm run tauri-build
# Compiles Rust, bundles frontend, produces:
#   frontend/src-tauri/target/<triple>/release/env-manager.exe
#   frontend/src-tauri/target/<triple>/release/bundle/msi/*.msi
```

## Build everything (cross-platform, multi-architecture)

The primary build orchestrator is `scripts/build.mjs` - a Node.js ESM script that works on Windows, Linux, and macOS with no hardcoded paths. It auto-discovers the project root relative to its own location.

```bash
# Build for host architecture (auto-detected)
node scripts/build.mjs

# Build for specific architecture
node scripts/build.mjs --arch x64    # x64 (amd64)
node scripts/build.mjs --arch x86    # x86 (32-bit)
node scripts/build.mjs --arch arm64  # ARM64

# Skip specific stages
node scripts/build.mjs --skip-cli    # Skip C# CLI build
node scripts/build.mjs --skip-gui   # Skip Tauri GUI build
node scripts/build.mjs --skip-msi    # Skip MSI installer build
```

Output layout (all under `release/`):
- `release/portable/` - env-manager.exe + env-manager-cli.exe + DLLs (flat, no install needed)
- `release/cli-only/` - env-manager-cli.exe + DLLs + AGENTS.cli.md (no GUI, standalone CLI)
- `release/msi/` - Env Manager_X.Y.Z_<arch>.msi (Windows only, requires WiX)
- `release/portable/Env-Manager_portable_X.Y.Z_<arch>.zip` - portable ZIP archive (inside portable/ subdir)
- `release/cli-only/Env-Manager_cli-only_X.Y.Z_<arch>.zip` - CLI-only ZIP archive (inside cli-only/ subdir)

Architecture naming: `x64` (not amd64), `x86` (not x32), `arm64`. These map to .NET RIDs (`win-x64`/`win-x86`/`win-arm64`) and Rust triples (`x86_64-pc-windows-msvc`/`i686-pc-windows-msvc`/`aarch64-pc-windows-msvc`).

The legacy `frontend/scripts/build-all.ps1` is preserved as a backward-compatible wrapper that delegates to `scripts/build.mjs`.

## Intermediate Build Artifacts

The `bin/Release/` directory (specifically `bin/Release/net10.0-windows/`) is an intermediate build output from `dotnet build`. It is NOT the final distribution. Its role:

1. `dotnet build -c Release` produces CLI DLLs and exe here
2. `frontend/scripts/prebuild.mjs` copies from here to `frontend/src-tauri/bin/` for Tauri resource bundling
3. `scripts/build.mjs` copies from here to `release/portable/` and `release/cli-only/` for the portable and CLI-only distributions

This directory is gitignored and should not be manually distributed. It is regenerated on every build. The TFM output path may be `net10.0` or `net10.0-windows` depending on the .NET SDK version; the build scripts auto-detect which exists.

## Build output layout

The `release/` directory is the canonical output for distribution:

- `release/portable/` contains the GUI executable (`env-manager.exe`) and all CLI runtime files side-by-side. This is the portable distribution - no installation needed, just run the exe.
- `release/cli-only/` contains only the CLI binary and its runtime dependencies (DLLs, JSON config, `AGENTS.cli.md`) - no GUI. Standalone CLI mode: the CLI detects whether a GUI exe (`env-manager.exe`) exists alongside it; if not, it operates in standalone CLI mode.
- `release/msi/` contains the Windows MSI installer. When installed, the CLI exe and its DLLs are bundled as Tauri resources and resolved at runtime via `BaseDirectory::Resource`.

The MSI filename is `Env Manager_X.Y.Z_x64.msi` - no locale suffix. Verify after every build that no `_en-US` or other locale suffix appears in the MSI filename.

## Mandatory Build After Code Changes

**Every commit that modifies CLI, GUI, or build code MUST produce compiled artifacts in `release/` before pushing.**

Run the cross-platform build after any code change:

```bash
# From project root - build for host arch (default x64)
node scripts/build.mjs --arch x64

# Never use --skip-cli/--skip-gui/--skip-service after code changes (see below).
```

Verify the output:
- `release/portable/env-manager.exe` - GUI executable
- `release/portable/env-manager-cli.exe` - CLI backend
- `release/cli-only/env-manager-cli.exe` - standalone CLI
- `release/portable/Env-Manager_portable_X.Y.Z_x64.zip` - portable ZIP
- `release/cli-only/Env-Manager_cli-only_X.Y.Z_x64.zip` - CLI-only ZIP
- `release/msi/Env Manager_X.Y.Z_x64.msi` - MSI installer (no locale suffix, Windows only)

A commit that does not produce working `release/` artifacts is considered incomplete. The `release/` directory is gitignored - artifacts are for local testing only, not committed to git.

Before building, stop any running instances: `Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process -Force`.

### Compile-Before-Package Enforcement (Hard Boundary)

The build orchestrator (`scripts/build.mjs`) enforces a strict compile-then-package sequence:

1. **Step 1**: `dotnet build -c Release -r <RID>` compiles the C# CLI, then verifies the deployed `env-manager-cli.exe` version matches `env-manager.csproj` `<Version>`. A mismatch (stale binary) throws `CLI version mismatch` and aborts the build before any packaging occurs.
2. **Step 2**: `npm run build` + `tauri build` compiles the Tauri GUI (Rust + Svelte frontend).
3. **Step 2b**: `cargo build` compiles the `env-manager-service` Rust binary.
4. **Step 3-4**: Assembles portable/cli-only staging dirs from the freshly compiled output, builds MSI via WiX.
5. **Step 5**: Creates ZIP archives from the staging dirs.

**The `--skip-cli`, `--skip-gui`, and `--skip-service` flags are for incremental packaging ONLY** — for example, re-running the MSI build after a WiX template change without touching CLI/GUI code. If any source file (`*.cs`, `*.rs`, `*.svelte`, `*.ts`, `*.css`) changed since the last successful `build.mjs` run, ALL compile steps must run. Using skip flags to bypass compilation after a code change produces stale binaries in `release/` and is a hard boundary violation.

The CLI version verification guard (AGENTS.md hard boundary v0.7.15) is the automated backstop: if a stale `bin/Release/net10.0-windows/env-manager-cli.exe` is detected (version mismatch against csproj), `build.mjs` throws and aborts before any packaging occurs. This prevents the incident where the deployed CLI was a months-old v0.3.0 artifact while the GUI showed "Unknown command: history/protection/path/profile" for every page.

### Live CLI smoke test with registry backup/restore

When validating the published CLI binary, run the registry-safe live test harness **before** making any release commit:
> The harness captures exact registry state (all value names, unexpanded values, and registry value kinds) for HKCU plus any accessible HKLM system hive, and snapshots test-owned Env Manager internal configuration. On a test failure or any detected drift it reconciles the exact pre-test state, including removal of values introduced during testing, then verifies both registry hives and internal configuration before broadcasting `WM_SETTINGCHANGE`. Backups are cleaned up on a clean run, retained with `-KeepBackup`, and always kept on failure for forensics in `.test-backups/`. Never run raw `env-manager-cli set ... --scope system` against the real registry without this wrapper - this is a project hard boundary (see AGENTS.md).

```powershell
# After build.mjs has produced release/cli-only/env-manager-cli.exe:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-with-restore.ps1
```

The harness uses an exact transaction: it snapshots all HKCU values plus accessible HKLM values and test-owned internal configuration, runs only isolated `EM_TEST_` variables and timestamped profiles, then restores internal configuration and compares the exact snapshots. A failure or drift triggers value-by-value reconciliation, including deletion of newly introduced registry values, followed by rollback verification and `WM_SETTINGCHANGE`. Green runs remove temporary backups unless `-KeepBackup` is set; failed runs retain them for forensics and exit non-zero.

`.test-backups/` is gitignored.

### Test residue hygiene (self-check and user self-clean)

The harness registers every registry value it writes under the `EM_TEST_` prefix and enforces a **residue-zero assertion** in its reconciliation block (architecture-recovery issue 22):

- The pre/post snapshot diff of a run may reference only harness-registered `EM_TEST_*` value names. Any name outside the registered set is foreign drift: it is reported separately as `registry-foreign-drift`, reconciled, and fails the run.
- After compensatory reconciliation the diff must be empty; surviving names are listed in a `RESIDUE-ZERO assertion failed` warning and the run exits non-zero.
- Pre-existing `EM_TEST_*` values (written before the run, present in both snapshots) are only logged as an informational note at snapshot time; the harness never deletes values it did not write.

To audit a machine for residue at any time, run the read-only self-check (exit 0 = no residue, exit 1 = residue found):

```powershell
pwsh -NoProfile -File scripts/check-test-residue.ps1
```

It lists harness-prefix registry values under `HKCU\Environment` and the HKLM system environment key, plus `EM_TEST_*` profiles in `%LOCALAPPDATA%\EnvManager\profiles.json`. It never mutates anything.

**User self-clean.** Removing residue (for example the legacy `EM_TEST_DST=v1` value) is a deliberate user-side operation the harness never performs on values it did not write. Preferred path, because the CLI broadcasts `WM_SETTINGCHANGE` so running processes pick up the change:

```powershell
env-manager-cli delete EM_TEST_DST --scope user
env-manager-cli profile delete EM_TEST_PROFILE_20260101-000000   # for residue profiles
```

Native registry alternatives (target `HKEY_CURRENT_USER\Environment`; raw edits do not broadcast the change, so sign out/in or restart Explorer afterwards):

```powershell
Remove-ItemProperty -Path 'HKCU:\Environment' -Name 'EM_TEST_DST'
# or: reg delete "HKCU\Environment" /v EM_TEST_DST /f
```

## MSI Silent-Install Validation Runbook (v0.9.27+, ADR-0009)

Any change to `frontend/scripts/installer.wxs` MUST pass this local 4-step silent msiexec proof before commit. Runs on the build machine; takes under 2 minutes total when green.

Prereqs: elevated shell; no env-manager process running (`Get-Process -Name 'env-manager*' | Stop-Process -Force`); WiX logs land in `%TEMP%`.

```powershell
# Step 0 (RED, only once per regression): prove the broken baseline hangs
# Build the previous-tag MSI (e.g. v0.9.26 with the old installer.wxs) and run:
$elapsed = Measure-Command { msiexec /i "<prev>.msi" /quiet /norestart /L*v "$env:TEMP\msi-prev-install.log" }
if ($elapsed.TotalMilliseconds -gt 60000) { "BASELINE REPRODUCED (hang)" }   # expected for v0.9.26

# Step 1: silent install of the fixed MSI
$msi = "release/msi/Env Manager_0.9.27_x64.msi"
$elapsed = Measure-Command { msiexec /i $msi /quiet /norestart /L*v "$env:TEMP\msi-install.log" }
$elapsed.TotalMilliseconds -lt 10000  # expect ~1.1s; must be green
(Get-Service EnvManagerService).Status  # 'Stopped' is CORRECT (Start=auto -> next boot); 'Running' also acceptable

# Step 2: silent upgrade over the previous version
msiexec /i $msi /quiet /norestart /L*v "$env:TEMP\msi-upgrade.log"
$LASTEXITCODE -eq 0

# Step 3: silent uninstall
msiexec /x $msi /quiet /norestart /L*v "$env:TEMP\msi-uninstall.log"
$LASTEXITCODE -eq 0
Get-Service EnvManagerService -ErrorAction SilentlyContinue  # must be $null (SCM entry removed)
```

Failure triage: hang at `InstallFinalize` -> `StartServices` with lowest `ActionStart` op = service-start blocking regression (check `ServiceControl/@Start` is absent and `Wait="no"` in installer.wxs). See ADR-0009 for the validated root cause.

## How to Release

### Normal path — release-please single track (ticket 30, supersedes the manual flow)

1. Land conventional commits on main (`feat:`, `fix:`, `perf:`, ...). Nothing else to do by hand — versions are decided by release-please only.
2. release-please opens the Release PR (`chore(main): release X.Y.Z`). The PR bumps, in one commit:
   - `CHANGELOG.md` (auto-generated section)
   - `env-manager.csproj` `<Version>` (ADR 0003 single version source; annotated `<!-- x-release-please-version -->`, byte-identical update)
   - `frontend/src-tauri/tauri.conf.json` + `frontend/package.json` (extra-files jsonpath `$.version`)
   - `.release-please-manifest.json` (version ledger)
   - `frontend/src-tauri/Cargo.toml` and `frontend/package-lock.json` root version are NOT bumped by the PR (spike A6/A7, see `.scratch/architecture-recovery/reports/30-release-please-single-track.md`): Cargo.lock is committed and CI runs `cargo --locked`, and npm does not validate the lockfile root version at install time.
3. Review the PR (human-in-the-loop gate, ADR 0003) and merge.
4. The merge triggers release-please again, which creates tag `vX.Y.Z` and the GitHub Release with the CHANGELOG notes (via the `RELEASE_PLEASE_TOKEN` PAT — a `GITHUB_TOKEN`-created tag would never trigger build.yml, GitHub recursive protection).
5. The `vX.Y.Z` tag triggers build.yml (`tags: ['v*']`): the release job verifies tag == csproj == tauri.conf == package.json (version-consistency gate), builds x64/x86/arm64 artifacts, generates build provenance attestations, and uploads them to the release.
6. Evidence: the release-please run, the Release PR, the tag, and the build.yml tag run URLs are the release record.

PAT maintenance: `RELEASE_PLEASE_TOKEN` is a fine-grained PAT scoped to this repo with Contents: RW + Pull requests: RW (see the report for the full permission table). It expires — when release-please starts failing with 401/403, rotate the secret (GitHub → Settings → Secrets and variables → Actions) before the next release. The emergency path below is the fallback while the PAT is broken.

### Emergency path — manual release.yml (EMERGENCY ONLY)

`.github/workflows/release.yml` is degraded to an emergency-only track (ticket 30 checkpoint E). Use it only when the automated track is unavailable (expired/revoked PAT, release-please-action outage) or for a rebuild/re-publish of an already-decided version.

- It keeps the `workflow_dispatch` form (version input + create_release toggle).
- A fail-closed guard aborts the run if the target tag already exists, so it can never overtake a release-please tag.
- Manual version editing remains the operator's responsibility; run the version-consistency expectations of step 5 above by hand before dispatching.

## Dependencies

### C# (.NET)

| Package | Purpose |
|---------|---------|
| Spectre.Console 0.49.1 | CLI table formatting |

### npm

| Package | Purpose |
|---------|---------|
| @tauri-apps/api 2.x | Tauri IPC |
| @tauri-apps/cli 2.x | Tauri build tooling |
| svelte 4.x | UI framework |
| svelte-i18n 4.x | Internationalization |
| tailwindcss 3.x | CSS framework |
| vite 5.x | Build tool |
| typescript 5.x | Type checking |
| vitest 4.x | Unit test runner |
| jsdom 25.x | DOM environment for tests |
| @testing-library/svelte 5.x | Svelte component testing utilities |
| @playwright/test 1.x | E2E browser testing |
| archiver 8.x | ZIP archive creation (ESM named exports; use `new ZipArchive(...)`, no default export) |

### Cargo (Rust)

| Crate | Purpose |
|-------|-------|
| tauri 2.0 | Desktop framework |
| serde / serde_json | Serialization |
| log | Logging |
| tauri-plugin-log | Tauri log integration |

## CodeGraph

The project uses [CodeGraph](https://github.com/nicholasgriffintn/codegraph) for code navigation and indexing.

- Index is stored in `.codegraph/` (gitignored, regenerated per machine)
- Initialize: `codegraph init` (scans and indexes all source files)
- The index enables fast symbol lookup, reference finding, and dependency analysis
- Agents should use `codegraph` for code navigation when available, but it is not required for development
- The index is machine-local and never committed to git

## CI/CD Workflows

### build.yml (CI verification)
Runs on push to main and pull requests. Verifies code quality (tests, lint, build) and builds x64 packages to verify the build pipeline.

The verify job stages the five `tauri.conf.json` `bundle.resources` (`env-manager-cli.exe`/`.dll`/`.runtimeconfig.json`/`.deps.json` plus `AGENTS.cli.md`) from the CLI build output (`bin/Release/net10.0-windows/`) and the repo root into `frontend/src-tauri/bin/` immediately after the "Build CLI" step and before any cargo compile (service crate tests, Tauri crate tests, cargo check). The step is fail-closed: any of the five missing fails the job. This mirrors the local staging done by `frontend/scripts/prebuild.mjs` during `npm run build`/`tauri build`; `scripts/build.mjs` responsibilities are unchanged.

### release-please.yml (single release track)
Runs on every push to main via googleapis/release-please-action (pinned SHA) using the repository manifest config (`release-please-config.json` + `.release-please-manifest.json`). Maintains the Release PR that bumps CHANGELOG.md, `env-manager.csproj`, `tauri.conf.json`, and `package.json`; on merge of that PR it creates the `vX.Y.Z` tag + GitHub Release, which triggers build.yml's tag-driven release job. Authenticates with the `RELEASE_PLEASE_TOKEN` secret (fine-grained PAT) because GITHUB_TOKEN events never trigger other workflows.

### release.yml (Emergency manual release)
EMERGENCY ONLY since ticket 30. Triggered manually via GitHub Actions `workflow_dispatch` with a version input. Builds x64, x86, and arm64 packages in parallel, then creates a GitHub Release with all artifacts:
- Portable ZIPs (per arch)
- CLI-only ZIPs (per arch)
- MSI installers (per arch, Windows only)

The release workflow does NOT auto-trigger on tags or pushes - it must be manually dispatched. A tag-exists precheck fails the run closed if the tag already exists, preventing it from overtaking the release-please track.

### CI user-state isolation and env-block snapshot semantics (architecture-recovery issue 24)

Integration tests in CI run with an **isolated user-state root**: the `Run Pester integration tests` step in `build.yml` sets `ENVMANAGER_LOCALAPPDATA` to a job-private directory under `runner.temp`, and every CLI user-state file (profiles.json, audit.json/audit.key, secretMount.json, secret-providers.json, provider-hash.json, protection JSON stores) resolves through that root instead of the runner's real `%LOCALAPPDATA%`. A follow-up step, `Assert user-state isolation (issue 24)`, verifies after the run (even on failure, `if: always()`) that the real `%LOCALAPPDATA%\EnvManager` directory was not created by the job and prints the redirected directory contents as redirect proof.

Why an explicit seam variable instead of setting `LOCALAPPDATA`: `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` does **not** honor a process-level `LOCALAPPDATA` override — the .NET shell-folder API expands the `Local AppData` value from the registry (`HKCU\...\Explorer\User Shell Folders`), not the process environment. `src/CliRuntime.cs` therefore exposes `LocalAppDataRoot`, which returns `ENVMANAGER_LOCALAPPDATA` when set to a non-empty value and falls back to `GetFolderPath` otherwise. The seam is cross-process (the Pester harness passes the variable to every CLI subprocess env block) and requires no in-process test redirect. When the variable is unset — every user and non-test CI path — behavior is byte-identical to the pre-issue-24 resolution. Unit-pinned by `LocalAppDataRedirectTests` (xUnit).

**Two-level isolation discipline** (research/round4-closeout-patterns.md section A):

1. **Machine-state writes** (registry `HKCU\Environment` / `HKLM\...\Session Manager\Environment`, `%ProgramData%`): tolerated on GitHub-hosted runners because every job starts on a fresh VM — machine state dies with the job. The registry side is additionally bounded by the `test-with-restore.ps1` snapshot/reconcile transaction and the issue-22 residue-zero assertion, so nothing survives the run anyway.
2. **User-state writes** (`%LOCALAPPDATA%` tree): NOT tolerated inside a job, because a job's later steps share the same user profile — an unredirected integration run could read a stale profiles.json written by an earlier step or leak test profiles into packaging steps. The `ENVMANAGER_LOCALAPPDATA` redirect removes the shared surface entirely.

**env-block snapshot semantics** (tests and CI steps MUST NOT assume cross-process real-time environment refresh): a Windows environment block is a snapshot copied at process creation; a child process inherits the env of its creator at spawn time, and a registry environment write plus `WM_SETTINGCHANGE` broadcast notifies only registered listeners (shell/Explorer), not arbitrary already-running processes. Consequences the test discipline encodes:

- A CLI subprocess never sees environment changes made by the harness after the subprocess was spawned; set variables BEFORE spawning.
- A process that itself writes the registry environment does not see its own write via its inherited env-block snapshot; refresh assertions must re-read the registry (the CLI does) or spawn a fresh child, never probe the writer's own env.
- Windows-latest hosted runners are always elevated, so elevation-gated system-scope behavior cannot be differentially verified in CI; that path is pinned at the seam level in xUnit instead.

## Performance Targets

| Metric | Target |
|--------|--------|
| CLI startup | < 200ms |
| GUI startup | < 1s |
| List load | < 100ms |
| Backup size | < 1MB (typically ~10KB) |
| CLI memory | < 50MB |
| GUI memory | < 150MB |

## Logging and Debugging

### Rust Logging

The Tauri shell uses `tauri-plugin-log` at `Info` level. All `run_cli` calls log metadata only:
- Command and argument count on entry
- Exit code, stdout/stderr length, and elapsed time on completion
- stderr presence and length if non-empty
- Spawn failure category only; command output is never persisted

CLI path resolution also logs which method succeeded (resource, adjacent, dev, cwd, PATH).

### Frontend Diagnostics

The `cli_diagnostics` Tauri command returns:
- `resolved_cli_path` - the CLI exe path that will be used
- `gui_exe_dir` - directory of the GUI exe
- `cwd` - current working directory

This is accessible via `getDiagnostics()` in `api.ts` and helps debug "CLI not found" errors.

### Frontend Debug System

The GUI has a frontend-level debug logging system:

- `debugLogs` store in `stores.ts` - holds up to 200 `DebugLogEntry` objects (capped to prevent memory leaks)
- `addDebugLog()` - adds entries with timestamp, level (info/warn/error/debug), message, and optional command name
- `clearDebugLogs()` - empties the log store
- `isWriteInProgress` store - `true` while a write operation is executing (set in `runWriteOperation()` in `api.ts`)
- `runCommand()` in `api.ts` logs command, timing, and status only; raw CLI errors are returned to the active UI but excluded from persisted debug logs
- GUI buttons (nav tabs, refresh) are disabled via `disabled={$isWriteInProgress}` during write operations to prevent UI race conditions

The `isWriteInProgress` store drives button-disable behavior: when a write operation starts, the store is set to `true`, and all navigation buttons and refresh buttons get `disabled` styling (`opacity-50`, `cursor-not-allowed`). When the write completes, the store returns to `false`, re-enabling buttons.

## Per-Session Host Environment Snapshot

Before any local development session that touches the CLI or build, run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/snapshot-host-env.ps1
```

This exports both `HKCU\Environment` and `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` to `<repo-root>/.env_bak/` with a UTC timestamp, and copies Env Manager's internal config files from `%LOCALAPPDATA%\EnvManager\` (profiles.json, audit.json, protected-vars.json, protected-paths.json, builtin-protected-vars.json, builtin-protected-paths.json) into `.env_bak/internal-configs/`. The `.env_bak/` directory is gitignored and is a forensic safety net: it is NOT auto-cleaned. Keep at most the last few snapshots manually.

The live CLI smoke test harness `scripts/test-with-restore.ps1` snapshots every value name, unexpanded value, and registry value kind in HKCU plus accessible HKLM, byte-snapshots the test-owned Env Manager internal configuration, verifies exact post-test equality, and transactionally reconciles both hives on any failure or drift. It restores internal configuration after the suite and exercises both the raw trailing-backslash PATH command-line regression and disabled-variable exact raw-value/RegistryValueKind recovery. This snapshot script is the per-session complement: run it once before you start work, so if any change mutates the host registry or internal configs, you have a rollback artifact.

## Public Flip and Mirror Sync (v0.9.26+)

1. Run `gitleaks git .` (must exit 0) before flipping the repository to public.
2. Flip via GH CLI: `gh repo edit Xxx91n/env-manager --visibility public`.
3. Add GitHub Actions variables `GITLAB_USER` and `CODEBERG_USER`, plus secrets `GITLAB_TOKEN` and `CODEBERG_TOKEN` (write scope, minimal expiry).
4. `.github/workflows/mirror.yml` runs on every push to `main` and keeps GitLab / Codeberg in sync; do not push to mirrors manually.
5. release-please (`release-please.yml`) drives CHANGELOG / version PRs after the public flip; this repository does not yet contain git tags, so release-please is configured in advance but the first release remains gated behind the "开始发布" user confirmation.
6. Provenance attestation (`actions/attest-build-provenance`) is pre-wired in `build.yml` release job so future tagged releases carry SLSA L2 provenance automatically.
7. Tauri updater: `tauri signer generate` executed once; public key committed in `frontend/src-tauri/tauri.conf.json` under `plugins.updater.pubkey`; private key kept only in GitHub Secrets (`TAURI_SIGNING_PRIVATE_KEY`, `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`). Reference: ADR 0008.
