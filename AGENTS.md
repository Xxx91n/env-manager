# Env Manager - Project Specification

This document is the single source of truth for the Env Manager project. All developers, AI agents, and LLMs must follow this specification. When any project feature or structure changes, this document must be updated immediately in the same commit.

---

## Project Overview

- **Name**: Env Manager
- **Version**: 0.5.0
- **License**: MIT
- **Repository**: https://github.com/Xxx91n/env-manager
- **Languages**: C# (.NET 10), TypeScript, Svelte, Rust
- **Goal**: A modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support, inspired by Microsoft PowerToys environment variable editor but standalone and agent-friendly.

---

## Architecture

The application has three layers:

1. **CLI backend** (`Program.cs`) - C# .NET 10 console application that reads/writes the Windows Registry directly. Compiles to `env-manager-cli.exe`. Handles all variable CRUD, backup/restore, diff/merge operations.
2. **Tauri shell** (`frontend/src-tauri/`) - Rust application that embeds the CLI as a bundled resource. Spawns CLI subprocesses for each operation and returns structured JSON responses to the frontend via Tauri IPC commands.
3. **Svelte frontend** (`frontend/src/`) - TypeScript + Svelte 4 + TailwindCSS UI rendered in a WebView2 window. Communicates with the Rust layer exclusively through `invoke('run_cli', ...)`.

The GUI does NOT depend on a local web server. In development, Vite serves the frontend at `localhost:5173`. In production, Tauri embeds the built static assets via its `tauri://` custom protocol - no server, no network.

---

## Project Structure

```
env-manager/
├── Program.cs                         # C# CLI implementation
├── env-manager.csproj                 # .NET 10 project (AssemblyName: env-manager-cli)
├── LICENSE                            # MIT
├── README.md                          # English documentation
├── README_CN.md                       # Chinese documentation
├── AGENTS.md                          # This file (project-level, for repo developers/agents)
AGENTS.cli.md                       # CLI-level agent guide (distributed with CLI binary)
├── .gitignore
│
├── frontend/                          # Tauri GUI application
│   ├── index.html
│   ├── package.json
│   ├── vite.config.ts
│   ├── svelte.config.js
│   ├── tailwind.config.js
│   ├── postcss.config.js
│   ├── tsconfig.json
│   ├── playwright.config.ts
│   ├── scripts/
│   │   ├── prebuild.mjs              # Builds CLI, copies to src-tauri/bin/
│   │   ├── tauri-build.mjs           # Wraps tauri build, auto-detects host triple
│   │   └── build-all.ps1             # Consolidated build: release/portable + release/msi
│   ├── src/
│   │   ├── main.ts                   # App entry, initializes i18n
│   │   ├── App.svelte                # Root component
│   │   ├── App.test.ts
│   │   └── lib/
│   │       ├── api.ts                # Tauri IPC bridge, CLI invocation
│   │       ├── stores.ts             # Svelte reactive stores
│   │       ├── i18n.ts               # Internationalization setup
│   │       ├── components/
│   │       │   ├── Variables.svelte  # Variable list, search, filter
│   │       │   ├── EditDialog.svelte # Create/edit variable dialog
│   │       │   ├── BackupDialog.svelte # Backup/export dialog
│   │       │   ├── ProfileDialog.svelte # Profile management dialog
│   │       │   ├── PathEditor.svelte   # PATH variable list editor
│   │       │   └── SettingsDialog.svelte # Settings (language, theme)
│   │       └── translations/
│   │           ├── en.json
│   │           ├── zh.json
│   │           ├── ja.json
│   │           ├── ko.json
│   │           ├── de.json
│   │           ├── fr.json
│   │           ├── es.json
│   │           ├── pt.json
│   │           ├── ru.json
│   │           └── ar.json
│   ├── src-tauri/
│   │   ├── src/main.rs               # Tauri commands: run_cli, cli_diagnostics
│   │   ├── Cargo.toml
│   │   ├── Cargo.lock
│   │   ├── tauri.conf.json           # Bundle config, resources mapping
│   │   ├── build.rs
│   │   ├── capabilities/default.json
│   │   ├── icons/                    # Application icons
│   │   └── bin/                      # CLI files copied by prebuild (gitignored)
│   └── tests/
│       ├── setup.ts                  # Vitest global mocks
│       └── e2e/app.spec.ts
│
├── release/                           # Build output (gitignored)
│   ├── portable/                     # GUI exe + CLI files, flat layout
│   └── msi/                          # MSI installer
│
├── bin/                               # CLI build output (gitignored)
├── obj/                               # CLI build intermediates (gitignored)
└── dist/                              # Frontend build output (gitignored)
```

---

## Build System

### Prerequisites

- .NET 10 SDK
- Node.js 18+ with npm
- Rust toolchain (rustc + cargo). Either GNU (stable-x86_64-pc-windows-gnu) or MSVC (stable-x86_64-pc-windows-msvc) target works. The build system auto-detects the host triple and does not hardcode any specific target or linker path.

### Build CLI only

```powershell
dotnet build -c Release
# Output: bin/Release/net10.0-windows/env-manager-cli.exe
```

### Build GUI (development with hot reload)

```powershell
cd frontend
npm install
npm run tauri-dev
# Opens a Tauri window with Vite dev server at localhost:5173
```

### Build GUI (production)

The `tauri-build` npm script runs `scripts/tauri-build.mjs`, which auto-detects the Rust host triple via `rustc -vV` and passes `--target <triple>` to `tauri build`. This ensures the Tauri bundler looks in the same output directory where cargo actually places the binary (`target/<triple>/release/`), which is critical on hosts whose default triple is not the plain host (e.g. `x86_64-pc-windows-gnu` with MinGW).

```powershell
cd frontend
npm run tauri-build
# Compiles Rust, bundles frontend, produces:
#   frontend/src-tauri/target/<triple>/release/env-manager.exe
#   frontend/src-tauri/target/<triple>/release/bundle/msi/*.msi
```

### Build everything (consolidated output)

```powershell
cd frontend
powershell -ExecutionPolicy Bypass -File scripts/build-all.ps1
# Output:
#   release/portable/  - env-manager.exe + env-manager-cli.exe + DLLs (flat)
#   release/msi/       - Env Manager_X.Y.Z_x64_en-US.msi
```

### Intermediate Build Artifacts

The `bin/Release/` directory (specifically `bin/Release/net10.0-windows/`) is an intermediate build output from `dotnet build`. It is NOT the final distribution. Its role:

1. `dotnet build -c Release` produces CLI DLLs and exe here
2. `frontend/scripts/prebuild.mjs` copies from here to `frontend/src-tauri/bin/` for Tauri resource bundling
3. `frontend/scripts/build-all.ps1` copies from here to `release/portable/` for the portable distribution

This directory is gitignored and should not be manually distributed. It is regenerated on every build. The TFM output path may be `net10.0` or `net10.0-windows` depending on the .NET SDK version; the build scripts auto-detect which exists.

### Build output layout

The `release/` directory is the canonical output for distribution:

- `release/portable/` contains the GUI executable (`env-manager.exe`) and all CLI runtime files side-by-side. This is the portable distribution - no installation needed, just run the exe.
- `release/msi/` contains the Windows MSI installer. When installed, the CLI exe and its DLLs are bundled as Tauri resources and resolved at runtime via `BaseDirectory::Resource`.

---

## CLI Command Specification

All commands follow: `env-manager-cli <command> [arguments] [--flags]`

| Command | Usage | Description |
|---------|-------|-------------|
| `list` | `list` | List all variables (user + system) |
| `get` | `get <name>` | Get variable value |
| `set` | `set <name> <value> [--scope user\|system]` | Set variable (default: user) |
| `delete` | `delete <name> [--scope user\|system]` | Delete variable (default: user) |
| `toggle` | `toggle <name> [--scope user\|system]` | Enable/disable variable (backs up value, default: user) |
| `backup` | `backup [--output <file>]` | Backup all variables to JSON |
| `restore` | `restore <file> [--scope user\|system]` | Restore variables from JSON |
| `diff` | `diff <old> <new>` | Compare two backup files |
| `merge` | `merge <old> <new> --output <file>` | Merge two backup files |
| `validate` | `validate <file>` | Validate backup file format |
| `help` | `help` | Show help text |
| `agents` | `agents [--path\|--json\|--summary]` | Output CLI AGENTS.md spec. --path: file only. --json: machine-readable JSON. --summary: brief |
| `profile list` | `profile list` | List all profiles (JSON) |
| `profile create` | `profile create <name>` | Create a new empty profile |
| `profile delete` | `profile delete <name>` | Delete a profile |
| `profile show` | `profile show <name>` | Show profile details (JSON) |
| `profile apply` | `profile apply <name>` | Apply a profile (backs up existing user vars) |
| `profile unapply` | `profile unapply <name>` | Unapply a profile (restores backed-up user vars) |
| `profile add-var` | `profile add-var <profile> <name> <val>` | Add a variable to a profile |
| `profile remove-var` | `profile remove-var <profile> <name>` | Remove a variable from a profile |
| `profile edit-var` | `profile edit-var <profile> <old> <new> <val>` | Edit a variable in a profile |
| `profile status` | `profile status <name>` | Check profile application status (JSON) |
| `path list` | `path list [--scope]` | List PATH entries (JSON) |
| `path add` | `path add <dir> [--scope] [--index N]` | Add directory to PATH |
| `path remove` | `path remove <dir> [--scope]` | Remove directory from PATH |
| `path move-up` | `path move-up <index> [--scope]` | Move PATH entry up |
| `path move-down` | `path move-down <index> [--scope]` | Move PATH entry down |
| `path rename` | `path rename <old> <new> [--scope]` | Rename a PATH entry |

### Debug Mode

Pass `--debug` (or `-d`) anywhere in the args to enable verbose stderr logging:

```powershell
env-manager-cli list --debug
env-manager-cli set MY_VAR value --scope user --debug
```

Debug output goes to stderr with timestamps: `[debug] HH:mm:ss.fff message`. This does not affect stdout JSON output. The GUI's Rust layer captures and logs all stderr to the Tauri log.

### Scope

- `user`: `HKEY_CURRENT_USER\Environment` (no elevation required)
- `system`: `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` (requires administrator)

### Error handling

- Errors go to stderr, success output to stdout
- Exit code: 0 = success, 1 = failure
- The GUI catches CLI errors and displays them as transient toasts (auto-dismiss after 3s),
  not as persistent banners. This prevents duplicate error display when both the CLI
  stderr and the GUI error store would show the same message.

- Errors go to stderr, success output to stdout
- Exit code: 0 = success, 1 = failure

### Profiles

Profiles are sets of preconfigured variables that can be applied/unapplied as a group. When applied, original values of affected user variables are backed up. Unapplying restores originals. Profiles only affect user scope.

- Profile storage: `%LOCALAPPDATA%\EnvManager\profiles.json`
- Profile variables override user variables when applied
- Original values backed up before apply, restored on unapply
- Profiles only affect user scope
- `profile status` checks if a profile is correctly applied (mirrors PowerToys `IsCorrectlyApplied()`)
- `IsProfileApplicable()` validation runs before apply - rejects profiles with invalid variable names (>255 chars, contains `=`)
- Variable name validation: user-scope names limited to 255 chars (registry limit), rejects `=` in names
- Values containing `%` are stored as `REG_EXPAND_SZ` (matches Windows default editor behavior)
- List-type variables (`PATH`, `PATHEXT`, `PSMODULEPATH`, `_NT_SYMBOL_PATH`, etc.) are detected for list-style editing

### Variable Toggle (Enable/Disable)

Variables can be toggled on/off without deleting them. When disabled:
- The original value is backed up to a registry key named `<name>_EnvManager_disabled`
- The original variable is deleted from the active environment
- The `list` command shows disabled variables with `isDisabled: true` and their backed-up value
- Re-enabling restores the original value and deletes the backup key

This mirrors PowerToys' approach of preserving variable data while deactivating it.

**Safety**: The toggle operation verifies backup write success before deleting the original. If the backup fails, the original variable is preserved unchanged.

**GUI optimistic update**: The GUI uses an optimistic UI pattern for toggle --
the slider flips immediately on click, before the CLI response arrives. If the
CLI operation fails, the slider reverts to its previous state. This gives instant
visual feedback without the jarring full-list refresh that previously occurred.

**Delete cleanup**: When `delete` is called on a disabled variable, the CLI also
removes the corresponding `_EnvManager_disabled` backup key and any
`_EnvManager_backup_*` profile backup keys for that variable name. This prevents
orphaned registry entries from accumulating.

### Path Editor

PATH variable edited as a list of directory entries. Entries can be added, removed, and reordered. Supports both user and system scopes.

---

## IPC Bridge

The Rust layer (`main.rs`) exposes two Tauri commands:

- `run_cli(command: String, args: Vec<String>) -> CliResponse` - Spawns the CLI subprocess, returns `{ success, data, error }`.
- `cli_diagnostics() -> serde_json::Value` - Returns resolved CLI path, GUI exe directory, and CWD for debugging.

### Race Condition Prevention

The Rust IPC layer uses a `static CLI_RWLOCK: RwLock<()>` to implement read/write lock separation:

- **Read commands** (`list`, `get`, `backup`, `diff`, `validate`, `agents`, `profile list/show/status`, `path list`) acquire a **read lock** that allows concurrent execution. Multiple read operations can run in parallel without blocking each other.
- **Write commands** (`set`, `delete`, `toggle`, `restore`, `merge`, `profile create/delete/apply/unapply/add-var/remove-var/edit-var`, `path add/remove/move-up/move-down/rename`) acquire a **write lock** that is exclusive. Only one write can run at a time, and no read can interleave with a write.

This means:
- Concurrent reads (e.g. loading variables list + loading profiles) run in parallel, improving responsiveness.
- All mutations are serialized, preventing race conditions where a read could see a partial mutation.

The frontend also serializes write operations via a `writeChain` promise in `api.ts`. This ensures that even if a user double-clicks a button, the write operations execute in order rather than racing. Read operations (`runRead()`) are not serialized on the frontend side, allowing them to fire concurrently.

The `is_read_only()` function in `main.rs` determines the lock type by inspecting both the command and its first argument (subcommand for `profile` and `path`).

### System Tray

The GUI creates a system tray icon on startup. Features:
- Closing the main window hides it to tray instead of exiting
- Double-clicking the tray icon restores the window
- Right-click context menu: Show, Quit
- The tray tooltip is "Env Manager"

This is implemented in `main.rs` using Tauri 2's `tray::TrayIconBuilder`.

**i18n Sync**: The tray menu text and tooltip are dynamically updated when the user
changes the GUI language. The frontend calls `updateTrayLocale(showText, quitText, tooltip)`
which rebuilds the tray menu with translated strings. This ensures the right-click
context menu matches the GUI locale.

### Internal Modal Dialog System

The GUI uses an internal Svelte store-based modal system instead of browser `confirm()`/`alert()`. The `modal` writable store in `stores.ts` holds the current `ModalConfig`. The `ConfirmDialog.svelte` component renders globally in `App.svelte`. All confirmation dialogs (delete variable, delete profile, etc.) use `showModal()` from `stores.ts`.

### CLI path resolution order:
1. Tauri resource directory (`BaseDirectory::Resource`) - production MSI install
2. Adjacent to GUI exe - portable distribution
3. Dev mode relative paths - `../../../../bin/Release/net10.0/`
4. Current working directory
5. PATH fallback (`where env-manager-cli.exe`)

---

## i18n (Internationalization)

The GUI supports 10 languages: English (en), Chinese (zh), Japanese (ja), Korean (ko), German (de), French (fr), Spanish (es), Portuguese (pt), Russian (ru), Arabic (ar).

### Rule: i18n sync is mandatory

When adding any new user-facing string (button label, message, dialog text, error), you must:

**ICU MessageFormat caveat**: The translation engine is `svelte-i18n` which uses
`IntlMessageFormat` (ICU MessageFormat). In ICU, single quotes `'` are escape
characters. **Never wrap a `{placeholder}` in single quotes** in translation
strings -- `'{name}'` produces the literal text `{name}`, not the interpolated value.
Use bare `{name}` or use double single quotes `''` to emit a literal quote.

When adding any new user-facing string (button label, message, dialog text, error), you must:

1. Add the key to `frontend/src/lib/translations/en.json` (the reference)
2. Add the same key with translated value to ALL other translation files in `frontend/src/lib/translations/`
3. Use `$t('key')` in Svelte components - never hardcode display text
4. Register any new locale in `frontend/src/lib/i18n.ts` (both `register()` call and `supportedLocales` array)

The default locale (en) is loaded synchronously via `addMessages()` to ensure the UI renders immediately under Tauri's custom protocol. Other locales load lazily via dynamic import.

---

## Backup JSON Format

```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\Windows\\System32;...",
      "scope": "user"
    }
  ]
}
```

- `timestamp`: RFC3339 / ISO 8601, UTC
- `version`: Semantic version (currently "1.0.0")
- `variables`: Array of `{ name, value, scope }`, may be empty
- `scope`: Must be "user" or "system"

## Profile JSON Format

Profiles are stored at `%LOCALAPPDATA%\EnvManager\profiles.json`:

```json
[
  {
    "id": "uuid-string",
    "name": "dev-profile",
    "isEnabled": false,
    "variables": [
      { "name": "JAVA_HOME", "value": "C:\\Program Files\\Java\\jdk-21" }
    ]
  }
]
```

- `id`: Unique identifier (GUID)
- `name`: Profile name (unique)
- `isEnabled`: Whether the profile is currently applied
- `variables`: Array of `{ name, value }` pairs
- When applied, original user variable values are backed up as `name_EnvManager_backup_<profileName>`

---

## Testing

### Test framework

Frontend unit tests use Vitest with jsdom environment. Tests live alongside source files as `*.test.ts`. The test setup file at `frontend/tests/setup.ts` provides global mocks for `@tauri-apps/api/core` `invoke` and `svelte-i18n`.

```powershell
cd frontend
npx vitest run          # Run all tests once
npm run test:ui         # Interactive test UI
npm run test:coverage   # With coverage report
npm run test:e2e        # Playwright E2E tests
```

### Mandatory testing rules

1. **Before every commit**: run `npx vitest run` from `frontend/` and ensure all tests pass. A commit with failing tests is considered incomplete.
2. **New feature = new tests**: any new CLI command, GUI component, store, API function, or i18n key must have corresponding unit test coverage added in the same commit.
3. **i18n key completeness**: `src/lib/translations.test.ts` validates that every key in `en.json` exists in all 9 non-English translation files with non-empty values. Adding a key to `en.json` without adding it to all other files will fail the test.
4. **Build verification after code changes**: after modifying code on the local machine, you must compile the release build to verify the full pipeline works. Run `powershell -ExecutionPolicy Bypass -File frontend/scripts/build-all.ps1` and verify `release/portable/env-manager.exe` launches successfully. Do not commit code that breaks the build.
5. **No emoji in tests**: test names and assertions follow the same no-emoji rule as the rest of the project.

### Test file inventory

| File | Coverage |
|------|----------|
| `src/App.test.ts` | Root component rendering, navigation |
| `src/lib/stores.test.ts` | All Svelte stores (variables, loading, error, scope filter, search, settings) |
| `src/lib/api.test.ts` | Tauri IPC bridge, all CLI command invocations, response parsing |
| `src/lib/i18n.test.ts` | Locale registration, default locale, setup function, localStorage persistence |
| `src/lib/translations.test.ts` | Translation key completeness across all 10 locales |
| `src/lib/race.test.ts` | CLI/GUI race condition prevention, toggle safety, rapid toggle serialization |
| `src/lib/sync.test.ts` | CLI/GUI state synchronization, mutation triggers refresh, error store lifecycle |
| `src/lib/debug.test.ts` | Debug logging system, log entry management, 200-entry cap (memory leak prevention), isWriteInProgress tracking |

---

## CodeGraph

The project uses [CodeGraph](https://github.com/nicholasgriffintn/codegraph) for code navigation and indexing.

- Index is stored in `.codegraph/` (gitignored, regenerated per machine)
- Initialize: `codegraph init` (scans and indexes all source files)
- The index enables fast symbol lookup, reference finding, and dependency analysis
- Agents should use `codegraph` for code navigation when available, but it is not required for development
- The index is machine-local and never committed to git

## Coding Standards

### C#

- 4-space indentation
- Max line length: 120 characters
- Use `using` statements for Registry keys
- Catch specific exceptions, never empty catch blocks
- Explicit types on public API, `var` for locals

### Add CLI to PATH

The GUI Settings dialog includes a one-click "Add CLI to PATH" button that:
1. Calls `cli_diagnostics` to resolve the actual CLI executable path (no hardcoding)
2. Extracts the directory from the resolved path
3. Checks if the directory is already in user PATH (prevents duplicates/infinite loops)
4. If not present, adds it via `path add` CLI command
5. Reports success or the reason for skipping

This feature is implemented in `api.ts` as `addCliToPath()` and exposed in `SettingsDialog.svelte`.

### TypeScript / Svelte

- 2-space indentation
- Strict mode, no implicit any
- All exports documented with JSDoc
- Reactive statements use `$:` syntax
- Components use props validation

### Rust

- 4-space indentation
- Use `log` crate macros (`info!`, `warn!`, `error!`) for diagnostics
- `#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]` to hide console in release

### File encoding

- All files: UTF-8 without BOM
- Line endings: LF

---

## Commit Convention

Use Conventional Commits:

```
<type>(<scope>): <subject>

<body>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `perf`, `chore`

Scopes: `cli`, `gui`, `backup`, `registry`, `i18n`, `docs`, `build`

---

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
|-------|---------|
| tauri 2.0 | Desktop framework |
| serde / serde_json | Serialization |
| log | Logging |
| tauri-plugin-log | Tauri log integration |

---

## Security

- No credential storage - only manages environment variables
- Direct Registry API via `Microsoft.Win32.Registry`, no COM
- IPC isolation - CLI runs as a separate subprocess
- Input length validation (32767 byte limit on variable names/values)
- Permission separation: user scope needs no elevation, system scope requires administrator
- `UnauthorizedAccessException` handled explicitly for system scope without elevation
- Variable name validation: rejects empty names, names >255 chars (user scope), names containing `=`
- Path traversal protection: backup files must have `.json` extension, writes to system directories blocked
- Backup file size cap: 50 MB maximum to prevent DoS via large files
- CLI command whitelist in Rust IPC layer: only known commands can spawn subprocesses (list, get, set, delete, toggle, backup, restore, diff, merge, validate, profile, path, agents, help)
- Process isolation: CREATE_NO_WINDOW flag prevents console flicker and information leakage
- **Critical system variable protection**: system-scope modifications to PATH, PATHEXT, SystemRoot, windir, ComSpec, TEMP, TMP, USERPROFILE, SystemDrive, ProgramFiles, ProgramFiles(x86), ProgramData, HOMEDRIVE, HOMEPATH, NUMBER_OF_PROCESSORS, OS, PROCESSOR_* are blocked in SetVariable, DeleteVariable, and SetVariableWithoutNotify
- **Toggle backup name collision prevention**: variables whose name ends with `_EnvManager_disabled` cannot be toggled, preventing backup key confusion
- **Profile name validation**: rejects empty/whitespace names, names >255 chars, names with null/newline/carriage-return chars
- **Profile variable name validation**: rejects empty names, names >255 chars, names containing `=`
- **PathAdd directory validation**: rejects empty paths, null bytes, paths exceeding max length (for direct CLI usage)
- **PathRename injection prevention**: validates new directory for empty values, null bytes, duplicates, max length
- **Path total length validation**: SetPathEntries rejects PATH values exceeding 32767 chars before writing
- **DiffBackups/MergeBackups file size validation**: both input files checked against 50 MB cap before deserialization (OOM prevention)
- **ListEnvironment O(n) optimization**: GetValueNames() cached in a HashSet instead of called per-variable (was O(n^2))
- **BroadcastSettingChange timeout reduced**: 500ms instead of 1000ms to prevent CLI exit delays
- **RunToggle null-scope crash fix**: ParseScope null return now properly checked before dereference
- **Control character rejection** in Rust IPC layer: rejects args containing control characters (prevents terminal injection)
- **Read/write lock separation** in Rust IPC: read commands share a read lock (concurrent), write commands use an exclusive write lock
- **Frontend write serialization**: writeChain in api.ts serializes all write operations to prevent UI-level races (double-click, rapid actions)

### Agent Safety Guidelines

When an AI agent uses the CLI directly:

1. **Always use `--scope user`** for non-interactive workflows. System scope requires elevation and may fail silently.
2. **Call `agents --json` first** to discover the full command contract, safety boundaries, and async support per command.
3. **Read commands are safe to batch** (list, get, backup, diff, validate, agents, profile list/show/status, path list). They acquire a read lock and can run concurrently.
4. **Write commands are serialized**. Do not fire multiple write commands in parallel - they will queue and execute in order, which may cause unexpected delays.
5. **Never delete critical system variables**. The CLI blocks system-scope modifications to protected variables, but user-scope PATH deletion is allowed and could break the agent's own environment. Always backup first.
6. **Profile names must be 1-255 chars** with no null bytes, newlines, or carriage returns. Variable names in profiles must not contain `=`.
7. **Backup files must have `.json` extension** and cannot be in system directories. Files exceeding 50 MB are rejected.

---

## Agent Integration

### CLI Agents Command

The CLI exposes an `agents` command that outputs the CLI-level AGENTS.md specification:
- `env-manager-cli agents` - Outputs the full AGENTS.cli.md content to stdout
- `env-manager-cli agents --path` - Outputs the file path of AGENTS.cli.md

This follows the industry pattern where CLI tools expose a machine-readable specification
file that AI agents and LLMs can read to understand the tool's API, safety boundaries,
and integration patterns. After first invoking the CLI, agents should call `agents` to
discover the full contract.

### CLI-Level AGENTS.md

`AGENTS.cli.md` is distributed alongside the CLI binary in both portable and MSI
installations. It is bundled as a Tauri resource and resolved at runtime via
`AppContext.BaseDirectory`. The file contains:
- Command reference (all commands with examples)
- Output format specification (JSON schemas)
- Security boundaries and validation rules
- Error handling conventions
- Agent integration tips

### GUI Agents API

The frontend exposes `getCliAgentsSpec()` and `getCliAgentsPath()` in `api.ts`
for programmatic access to the CLI specification from the GUI.

## How to Add a New CLI Command

1. Add a `case` in `Program.cs` `Main()` switch statement
2. Implement the command method
3. Update `ShowHelp()` with usage text
4. Document the command in this file (CLI Command Specification table)
5. Update `README.md` and `README_CN.md`
6. Add integration test coverage

---

## How to Modify the GUI

1. Edit `.svelte` files in `frontend/src/lib/components/`
2. Run `npm run tauri-dev` for live preview
3. Add any new display strings to ALL translation files (see i18n section)
4. Update this file if the component structure changes
5. Add component tests

---

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
- `release/msi/Env Manager_X.Y.Z_x64.msi` - MSI installer

A commit that does not produce working `release/` artifacts is considered incomplete. The `release/` directory is gitignored - artifacts are for local testing only, not committed to git.

---

## How to Release

1. Update version in `env-manager.csproj`, `frontend/package.json`, `frontend/src-tauri/tauri.conf.json`, `frontend/src-tauri/Cargo.toml`
2. Update `README.md` and `README_CN.md` if features changed
3. Update this file if structure or commands changed
4. Run `powershell -ExecutionPolicy Bypass -File frontend/scripts/build-all.ps1`
5. Verify `release/portable/env-manager.exe` launches and shows variables
6. Verify MSI installs and the app works
7. Commit: `chore: release vX.Y.Z`
8. Tag: `git tag vX.Y.Z`
9. Push: `git push origin main --tags`

---

## GUI/CLI Alignment

**WARNING: When adding or changing GUI features, you MUST verify the CLI has matching support.**

The GUI communicates with the CLI exclusively through `invoke('run_cli', { command, args })`. Every GUI action maps to a CLI command. If a GUI feature is added without CLI support, it will fail at runtime with "Unknown command".

### Current Alignment Status (v0.5.0)

| GUI Feature | CLI Command | API Function | Aligned |
|---|---|---|---|
| List variables | `list` | `listVariables()` | Yes |
| Get variable | `get` | `getVariable()` | Yes |
| Set variable | `set` | `setVariable()` | Yes |
| Delete variable | `delete` | `deleteVariable()` | Yes |
| Toggle variable | `toggle` | `toggleVariable()` | Yes |
| Backup | `backup` | `createBackup()` | Yes |
| Restore | `restore` | `restoreBackup()` | Yes |
| Profile list | `profile list` | `listProfiles()` | Yes |
| Profile create | `profile create` | `createProfile()` | Yes |
| Profile delete | `profile delete` | `deleteProfile()` | Yes |
| Profile apply | `profile apply` | `applyProfile()` | Yes |
| Profile unapply | `profile unapply` | `unapplyProfile()` | Yes |
| Profile show | `profile show` | `showProfile()` | Yes |
| Profile add-var | `profile add-var` | `addProfileVar()` | Yes |
| Profile remove-var | `profile remove-var` | `removeProfileVar()` | Yes |
| Profile edit-var | `profile edit-var` | `editProfileVar()` | Yes |
| Profile status | `profile status` | `getProfileStatus()` | Yes |
| Path list | `path list` | `listPathEntries()` | Yes |
| Path add | `path add` | `addPathEntry()` | Yes |
| Path remove | `path remove` | `removePathEntry()` | Yes |
| Path move-up | `path move-up` | `movePathEntryUp()` | Yes |
| Path move-down | `path move-down` | `movePathEntryDown()` | Yes |
| Tray locale sync | `update_tray_locale` | `updateTrayLocale()` | Yes |
| CLI agents spec | `agents` | `getCliAgentsSpec()` | Yes |
| Add CLI to PATH | `path add` | `addCliToPath()` | Yes |
| Rename PATH entry | `path rename` | `renamePathEntry()` | Yes |
| Rename variable | `delete` + `set` | EditDialog (rename via delete+set) | Yes |

### Variable Rename (GUI)

The GUI EditDialog allows renaming a variable by editing the name field. When the name changes:
1. The old variable is deleted via `deleteVariable(originalName, scope)`
2. The new variable is set via `setVariable(newName, value, scope)`

This two-step approach is used because the Windows Registry does not support atomic key renaming. The operation is serialized through the frontend `writeChain` and Rust `RwLock` write lock, ensuring no race condition can interleave the delete and set.

**Security**: The rename operation inherits all security checks:
- Protected system variables cannot be renamed (same list as SetVariable/DeleteVariable)
- Variable name validation: no empty names, no `=`, max 255 chars (user scope)
- The CLI-side `set` and `delete` commands enforce the same guards

### Alignment Checklist

When adding a new GUI feature:
1. Add the CLI command in `Program.cs`
2. Add the API function in `frontend/src/lib/api.ts`
3. Add the command to `ALLOWED_COMMANDS` in `main.rs` (current: list, get, set, delete, toggle, backup, restore, diff, merge, validate, profile, path, agents, help)
4. Add UI in the appropriate `.svelte` component
5. Add i18n strings to ALL translation files
6. Update the alignment table above
7. Add test coverage

## Logging and Debugging

### CLI Logging

The CLI supports a `--debug` flag (passable anywhere in args) that enables verbose stderr output with timestamps. Debug log lines use the format `[debug] HH:mm:ss.fff message`. Key instrumented methods:

- `Main()` - logs all args
- `ListEnvironment()` - logs read operation
- `GetVariable()` - logs variable name
- `SetVariable()` - logs name and scope
- `DeleteVariable()` - logs name and scope
- `CreateBackup()` / `RestoreBackup()` - logs file paths
- `ApplyProfile()` / `UnapplyProfile()` - logs profile name
- `RunPathCommand()` - logs subcommand and args

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

## Documentation Maintenance

| Event | Files to update |
|-------|----------------|
| New CLI command | AGENTS.md, README.md, README_CN.md, GUI/CLI alignment table |
| Changed command args | AGENTS.md, README.md, README_CN.md |
| New GUI feature | AGENTS.md, README.md, README_CN.md, all translation files, GUI/CLI alignment table |
| New debug log point | AGENTS.md (Logging section) |
| Dependency update | AGENTS.md |
| Build change | AGENTS.md, README.md, README_CN.md |
| Directory structure change | AGENTS.md |
| CodeGraph index change | AGENTS.md (CodeGraph section) |
| Code change (any) | Run build-all.ps1, verify release/ artifacts |
| New test file | AGENTS.md (test inventory section) |

A commit that does not update AGENTS.md when the project has changed is considered incomplete.

---

## Performance Targets

| Metric | Target |
|--------|--------|
| CLI startup | < 200ms |
| GUI startup | < 1s |
| List load | < 100ms |
| Backup size | < 1MB (typically ~10KB) |
| CLI memory | < 50MB |
| GUI memory | < 150MB |

---

**Last updated**: 2026-07-12
