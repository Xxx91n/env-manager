# Env Manager - Project Specification

This document is the single source of truth for the Env Manager project. All developers, AI agents, and LLMs must follow this specification. When any project feature or structure changes, this document must be updated immediately in the same commit.

---

## Project Overview

- **Name**: Env Manager
- **Version**: 0.4.0
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
├── AGENTS.md                          # This file
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
│   │       │   └── BackupDialog.svelte # Backup/export dialog
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
# Output: bin/Release/net10.0/env-manager-cli.exe
```

### Build GUI (development with hot reload)

```powershell
cd frontend
npm install
npm run tauri-dev
# Opens a Tauri window with Vite dev server at localhost:5173
```

### Build GUI (production)

```powershell
cd frontend
npm run tauri-build
# Compiles Rust, bundles frontend, produces:
#   frontend/src-tauri/target/release/env-manager.exe (host default)
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
| `backup` | `backup [--output <file>]` | Backup all variables to JSON |
| `restore` | `restore <file> [--scope user\|system]` | Restore variables from JSON |
| `diff` | `diff <old> <new>` | Compare two backup files |
| `merge` | `merge <old> <new> --output <file>` | Merge two backup files |
| `validate` | `validate <file>` | Validate backup file format |
| `help` | `help` | Show help text |
| `profile list` | `profile list` | List all profiles (JSON) |
| `profile create` | `profile create <name>` | Create a new empty profile |
| `profile delete` | `profile delete <name>` | Delete a profile |
| `profile show` | `profile show <name>` | Show profile details (JSON) |
| `profile apply` | `profile apply <name>` | Apply a profile (backs up existing user vars) |
| `profile unapply` | `profile unapply <name>` | Unapply a profile (restores backed-up user vars) |
| `profile add-var` | `profile add-var <profile> <name> <val>` | Add a variable to a profile |
| `profile remove-var` | `profile remove-var <profile> <name>` | Remove a variable from a profile |
| `path list` | `path list [--scope]` | List PATH entries (JSON) |
| `path add` | `path add <dir> [--scope] [--index N]` | Add directory to PATH |
| `path remove` | `path remove <dir> [--scope]` | Remove directory from PATH |
| `path move-up` | `path move-up <index> [--scope]` | Move PATH entry up |
| `path move-down` | `path move-down <index> [--scope]` | Move PATH entry down |

### Scope

- `user`: `HKEY_CURRENT_USER\Environment` (no elevation required)
- `system`: `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` (requires administrator)

### Error handling

### Profiles

Profiles are sets of preconfigured variables that can be applied/unapplied as a group. When applied, original values of affected user variables are backed up. Unapplying restores originals. Profiles only affect user scope.

- Profile storage: `%LOCALAPPDATA%\EnvManager\profiles.json`
- Profile variables override user variables when applied
- Original values backed up before apply, restored on unapply
- Profiles only affect user scope

### Path Editor

PATH variable edited as a list of directory entries. Entries can be added, removed, and reordered. Supports both user and system scopes.

- Errors go to stderr, success output to stdout
- Exit code: 0 = success, 1 = failure

---

## IPC Bridge

The Rust layer (`main.rs`) exposes two Tauri commands:

- `run_cli(command: String, args: Vec<String>) -> CliResponse` - Spawns the CLI subprocess, returns `{ success, data, error }`.
- `cli_diagnostics() -> serde_json::Value` - Returns resolved CLI path, GUI exe directory, and CWD for debugging.

CLI path resolution order:
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

1. Add the key to `frontend/src/lib/translations/en.json` (the reference)
2. Add the same key with translated value to ALL other translation files in `frontend/src/lib/translations/`
3. Use `$t('key')` in Svelte components - never hardcode display text
4. Register any new locale in `frontend/src/lib/i18n.ts` (both `register()` call and `supportedLocales` array)

The default locale (en) is loaded synchronously via `addMessages()` to ensure the UI renders immediately under Tauri's custom protocol. Other locales load lazily via dynamic import.

---

## Backup JSON Format

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

---

## Coding Standards

### C#

- 4-space indentation
- Max line length: 120 characters
- Use `using` statements for Registry keys
- Catch specific exceptions, never empty catch blocks
- Explicit types on public API, `var` for locals

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

---

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

## Documentation Maintenance

| Event | Files to update |
|-------|----------------|
| New CLI command | AGENTS.md, README.md, README_CN.md |
| Changed command args | AGENTS.md, README.md, README_CN.md |
| New GUI feature | AGENTS.md, README.md, README_CN.md, all translation files |
| Dependency update | AGENTS.md |
| Build change | AGENTS.md, README.md, README_CN.md |
| Directory structure change | AGENTS.md |

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
