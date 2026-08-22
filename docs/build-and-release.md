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

## How to Release

1. Update version in `env-manager.csproj`, `frontend/package.json`, `frontend/src-tauri/tauri.conf.json`, `frontend/src-tauri/Cargo.toml`
2. Update `README.md` and `README_CN.md` if features changed
3. Update `AGENTS.md` if structure or commands changed
4. Run `node scripts/build.mjs --arch x64` (or per-arch: `--arch x86`, `--arch arm64`)
5. Verify `release/portable/env-manager.exe` launches and shows variables
6. Verify MSI installs and the app works
7. Commit: `chore: release vX.Y.Z`
8. Tag: `git tag vX.Y.Z`
9. Push: `git push origin main --tags`

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
| archiver 7.x | ZIP archive creation for portable/CLI-only builds |

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

### release.yml (Manual release)
Triggered manually via GitHub Actions `workflow_dispatch` with a version input. Builds x64, x86, and arm64 packages in parallel, then creates a GitHub Release with all artifacts:
- Portable ZIPs (per arch)
- CLI-only ZIPs (per arch)
- MSI installers (per arch, Windows only)

The release workflow does NOT auto-trigger on tags or pushes - it must be manually dispatched.

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

## Public Flip and Mirror Sync (v0.9.26+)

1. Run `gitleaks git .` (must exit 0) before flipping the repository to public.
2. Flip via GH CLI: `gh repo edit Xxx91n/env-manager --visibility public`.
3. Add GitHub Actions variables `GITLAB_USER` and `CODEBERG_USER`, plus secrets `GITLAB_TOKEN` and `CODEBERG_TOKEN` (write scope, minimal expiry).
4. `.github/workflows/mirror.yml` runs on every push to `main` and keeps GitLab / Codeberg in sync; do not push to mirrors manually.
5. release-please (`release-please.yml`) drives CHANGELOG / version PRs after the public flip; this repository does not yet contain git tags, so release-please is configured in advance but the first release remains gated behind the "开始发布" user confirmation.
6. Provenance attestation (`actions/attest-build-provenance`) is pre-wired in `build.yml` release job so future tagged releases carry SLSA L2 provenance automatically.
7. Tauri updater: `tauri signer generate` executed once; public key committed in `frontend/src-tauri/tauri.conf.json` under `plugins.updater.pubkey`; private key kept only in GitHub Secrets (`TAURI_SIGNING_PRIVATE_KEY`, `TAURI_SIGNING_PRIVATE_KEY_PASSWORD`). Reference: ADR 0008.
