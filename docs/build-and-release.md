# Build and Release

## Prerequisites

- .NET 10 SDK
- Node.js 18+ with npm
- Rust toolchain (rustc + cargo). Either GNU (`stable-x86_64-pc-windows-gnu`) or MSVC (`stable-x86_64-pc-windows-msvc`) target works. The build system auto-detects the host triple via `rustc -vV` and passes `--target <triple>` to `tauri build`. It does not hardcode any specific target or linker path.

## Build CLI only

```powershell
dotnet build -c Release
# Output: bin/Release/net10.0-windows/env-manager-cli.exe
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

## Build everything (consolidated output)

```powershell
cd frontend
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-all.ps1
# Output:
#   release/portable/  - env-manager.exe + env-manager-cli.exe + DLLs (flat)
#   release/cli-only/  - env-manager-cli.exe + DLLs + AGENTS.cli.md (no GUI)
#   release/msi/       - Env Manager_X.Y.Z_x64.msi
```

## Intermediate Build Artifacts

The `bin/Release/` directory (specifically `bin/Release/net10.0-windows/`) is an intermediate build output from `dotnet build`. It is NOT the final distribution. Its role:

1. `dotnet build -c Release` produces CLI DLLs and exe here
2. `frontend/scripts/prebuild.mjs` copies from here to `frontend/src-tauri/bin/` for Tauri resource bundling
3. `frontend/scripts/build-all.ps1` copies from here to `release/portable/` for the portable distribution

This directory is gitignored and should not be manually distributed. It is regenerated on every build. The TFM output path may be `net10.0` or `net10.0-windows` depending on the .NET SDK version; the build scripts auto-detect which exists.

## Build output layout

The `release/` directory is the canonical output for distribution:

- `release/portable/` contains the GUI executable (`env-manager.exe`) and all CLI runtime files side-by-side. This is the portable distribution - no installation needed, just run the exe.
- `release/cli-only/` contains only the CLI binary and its runtime dependencies (DLLs, JSON config, `AGENTS.cli.md`) - no GUI. Standalone CLI mode: the CLI detects whether a GUI exe (`env-manager.exe`) exists alongside it; if not, it operates in standalone CLI mode.
- `release/msi/` contains the Windows MSI installer. When installed, the CLI exe and its DLLs are bundled as Tauri resources and resolved at runtime via `BaseDirectory::Resource`.

The MSI filename is `Env Manager_X.Y.Z_x64.msi` - no locale suffix. Verify after every build that no `_en-US` or other locale suffix appears in the MSI filename.

## Mandatory Build After Code Changes

**Every commit that modifies CLI, GUI, or build code MUST produce compiled artifacts in `release/` before pushing.**

Run the consolidated build after any code change:

```powershell
cd frontend
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-all.ps1
```

Verify the output:
- `release/portable/env-manager.exe` - GUI executable
- `release/portable/env-manager-cli.exe` - CLI backend
- `release/cli-only/env-manager-cli.exe` - standalone CLI
- `release/msi/Env Manager_X.Y.Z_x64.msi` - MSI installer (no locale suffix)

A commit that does not produce working `release/` artifacts is considered incomplete. The `release/` directory is gitignored - artifacts are for local testing only, not committed to git.

Before building, stop any running instances: `Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process -Force`.

### Live CLI smoke test with registry backup/restore

When validating the published CLI binary, run the registry-safe live test harness **before** making any release commit:

```powershell
# After build-all.ps1 has produced release/cli-only/env-manager-cli.exe:
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-with-restore.ps1
```

The harness implements the two-pronged gate described in AGENTS.md: it backs up `HKCU\Environment` to a `.reg` file before tests, runs a smoke suite restricted to `EM_TEST_`-prefixed user variables + ephemeral profiles, then verifies registry drift is zero (a leftover `EM_TEST_` key or a value change on a pre-existing key triggers restore). On green-all and zero drift, the backup auto-deletes. On any failure, it restores via `reg import` and broadcasts `WM_SETTINGCHANGE`, keeps the backup in `.test-backups/` for forensics, and exits non-zero.

`.test-backups/` is gitignored.

## How to Release

1. Update version in `env-manager.csproj`, `frontend/package.json`, `frontend/src-tauri/tauri.conf.json`, `frontend/src-tauri/Cargo.toml`
2. Update `README.md` and `README_CN.md` if features changed
3. Update `AGENTS.md` if structure or commands changed
4. Run `powershell -NoProfile -ExecutionPolicy Bypass -File frontend/scripts/build-all.ps1`
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

The Tauri shell uses `tauri-plugin-log` at `Info` level. All `run_cli` calls log:
- Command and args on entry
- Exit code, stdout/stderr length, and elapsed time on completion
- stderr content if non-empty
- Spawn failures with error details

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
- `runCommand()` in `api.ts` logs all CLI invocations with timing (ms) and success/error status
- GUI buttons (nav tabs, refresh) are disabled via `disabled={$isWriteInProgress}` during write operations to prevent UI race conditions

The `isWriteInProgress` store drives button-disable behavior: when a write operation starts, the store is set to `true`, and all navigation buttons and refresh buttons get `disabled` styling (`opacity-50`, `cursor-not-allowed`). When the write completes, the store returns to `false`, re-enabling buttons.
