# Env Manager

Modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support. Inspired by Microsoft PowerToys, built standalone for speed and simplicity.

**[简体中文](README_CN.md)** | **English**

---

## Features

### CLI Mode
- 18 commands for complete environment variable management
- Simultaneous profiles with inheritance, conflict previews, PATH fragments, and safe reverse-order rollback
- PATH editor with duplicate and missing-directory diagnostics
- **Launch profiles**: create a Global or Launch profile directly in the GUI or with one CLI transaction. Launch profiles start the selected executable with an isolated env block (`env_clear` + inject), never write the registry, and never broadcast `WM_SETTINGCHANGE`. Profile names are globally unique so name-addressed CLI commands remain unambiguous.
- **v0.6.0 PATH health**: `path health [--fix] [--dry-run]` detects duplicates AND dead (non-existent) PATH entries in one command; `--fix` safely removes non-protected entries; protected entries always preserved.
- **v0.7.0 DPAPI secrets**: `profile add-secret`/`edit-secret`/`remove-secret`/`reveal-secret` - per-profile variable values encrypted with Windows DPAPI CurrentUser. Plaintext lives only in transient process memory; `profile launch` decrypts at spawn time; `reveal-secret` is the only stdout-plaintext path. Audit records NAME only.
- **v0.7.0 GUI**: PATH health badges (healthy/dead/duplicate/duplicate+dead) + Remove Dead bulk action; Launch profile type badge + Launch button + Create bar type selector + native file picker; variable search highlight + `%VAR%` expansion preview; `.env`/CSV bulk import/export in Settings (native file picker).
- **v0.7.0 PATH health GUI**: one-click health check shows per-row color-coded status; `--fix` remove-dead is gated behind a confirmation modal (protected entries never removed).
- User and System scope support
- JSON backup/restore with diff/merge, audited history and guarded undo
- Bulk `.env`, CSV, and JSON import/export with dry-run conflict previews
- No admin required for user scope
- Single 158KB executable, no runtime dependency

### GUI Mode
- Native desktop app built with Tauri 2.0 (WebView2)
- Real-time variable list with highlighted search matches, scope filtering, and expanded `%VAR%` previews
- Inline add, edit, delete with confirmation
- Backup and restore through the UI
- 10-language internationalization: English, Chinese, Japanese, Korean, German, French, Spanish, Portuguese, Russian, Arabic

---

## Quick Start

### Download

Get the latest release from [GitHub Releases](https://github.com/Xxx91n/env-manager/releases).

- **Portable**: Extract the ZIP and run `env-manager.exe` directly. No installation needed.
- **MSI Installer**: Run the `.msi` file. Creates Start Menu shortcuts automatically.

### CLI Usage

```bash
# List all variables
env-manager-cli.exe list

# Get a variable
env-manager-cli.exe get PATH

# Set a variable (user scope by default)
env-manager-cli.exe set MY_VAR "my_value"
env-manager-cli.exe set JAVA_HOME "D:\jdk17" --scope system

# Delete a variable
env-manager-cli.exe delete MY_VAR

# Backup all variables to JSON
env-manager-cli.exe backup --output backup.json

# Restore from backup
env-manager-cli.exe restore backup.json

# Compare two backups
env-manager-cli.exe diff old.json new.json

# Merge two backups
env-manager-cli.exe merge old.json new.json --output merged.json

# Validate a backup file
env-manager-cli.exe validate backup.json
```

# Profile management
env-manager-cli.exe profile list
env-manager-cli.exe profile create dev-profile
# Create an isolated profile for one executable
env-manager-cli.exe profile create tool-run --type launch --target "C:\Tools\tool.exe"
env-manager-cli.exe profile add-var dev-profile JAVA_HOME "D:\jdk17"env-manager-cli.exe profile add-var dev-profile JAVA_HOME "D:\jdk17"
# Scope: default user; --scope system routes to HKLM on apply
env-manager-cli.exe profile add-path dev-profile "C:\Tools\bin" --scope user
env-manager-cli.exe profile apply dev-profile
env-manager-cli.exe profile unapply dev-profile
env-manager-cli.exe profile delete dev-profile

# PATH editor
env-manager-cli.exe path list --scope user
env-manager-cli.exe path add "C:\MyTools\bin" --scope user
env-manager-cli.exe path move-up 2 --scope user
env-manager-cli.exe path remove "C:\OldTools\bin" --scope user

# Protection management (lock variables / PATH entries from modification)
env-manager-cli.exe protection list
env-manager-cli.exe protection add-var JAVA_HOME
env-manager-cli.exe protection remove-var JAVA_HOME
env-manager-cli.exe protection add-path "C:\MyTools\bin"
env-manager-cli.exe protection remove-path "C:\MyTools\bin"

### GUI Usage

Launch `env-manager.exe` from the portable package or Start Menu. The GUI communicates with the CLI backend through Tauri IPC, so both modes always operate on the same state.

---

## Installation

### From Release

1. Go to [Releases](https://github.com/Xxx91n/env-manager/releases)
2. Download the portable ZIP or MSI installer
3. Portable: extract and run `env-manager.exe`
4. MSI: run the installer, then launch from Start Menu

### From Source

**Prerequisites**: .NET 10 SDK, Node.js 18+, Rust toolchain (GNU or MSVC)

```bash
# Build CLI
dotnet build -c Release
# Output: bin/Release/net10.0/env-manager-cli.exe

# Build GUI (development with hot reload)
cd frontend
npm install
npm run tauri-dev

# Build everything for distribution
powershell -ExecutionPolicy Bypass -File scripts/build-all.ps1
# Output:
#   release/portable/  - GUI + CLI flat layout, ready to run
#   release/msi/       - Windows MSI installer
```

---

## Commands Reference

| Command | Usage | Description |
|---------|-------|-------------|
| `list` | `list` | List all variables (user and system) |
| `get` | `get <name>` | Get variable value |
| `set` | `set <name> <value> [--scope user\|system]` | Create or update variable (default: user) |
| `delete` | `delete <name> [--scope user\|system]` | Remove variable (default: user) |
| `backup` | `backup [--output <file>]` | Export all variables to JSON |
| `restore` | `restore <file> [--scope user\|system]` | Import variables from JSON |
| `diff` | `diff <old> <new>` | Compare two backup files |
| `merge` | `merge <old> <new> --output <file>` | Merge two backup files |
| `validate` | `validate <file>` | Verify backup file format |
| `help` | `help` | Show help text |
| `rename` | `rename <old> <new> [--scope] [--overwrite]` | Atomically rename a variable |
| `history` | `history list [--limit N]` / `history undo <id>` | Inspect or undo audited changes |
| `bulk` | `bulk import\|export <file> [--scope]` | Import/export JSON, .env, or CSV |
| `expand` | `expand <value>` | Resolve nested `%VARIABLE%` references |
| `profile preview` | `profile preview <name>` | Preview conflicts and PATH effects |
| `profile set-inherits` | `profile set-inherits <name> [parent ...]` | Configure acyclic profile inheritance |
| `profile add-path` | `profile add-path <name> <dir>` | Add a PATH fragment to a profile |
| `profile list` | `profile list` | List all profiles |
| `profile create` | `profile create <name> [--type global|launch] [--target <exe>]` | Create a Global or isolated Launch profile atomically |
| `profile apply` | `profile apply <name>` | Apply a profile (backs up existing vars) |
| `profile unapply` | `profile unapply <name>` | Unapply a profile (restores originals) |
| `profile add-var` | `profile add-var <profile> <name> <val>` | Add variable to profile |
| `profile remove-var` | `profile remove-var <profile> <name>` | Remove variable from profile |
| `path list` | `path list [--scope]` | List PATH entries |
| `path add` | `path add <dir> [--scope]` | Add directory to PATH |
| `path remove` | `path remove <dir> [--scope]` | Remove directory from PATH |
| `path move-up` | `path move-up <index> [--scope]` | Move PATH entry up |
| `path move-down` | `path move-down <index> [--scope]` | Move PATH entry down |

### Scope

- `user`: `HKEY_CURRENT_USER\Environment` (no elevation required)
- `system`: `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` (requires administrator)

---

## Backup File Format

```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\Windows\\System32;...",
      "scope": "user"
    },
    {
      "name": "JAVA_HOME",
      "value": "D:\\jdk17",
      "scope": "system"
    }
  ]
}
```

---

## System Requirements

- Windows 10 21H2 or later (Windows 11 recommended)
- For CLI standalone: .NET Runtime 10.0+
- For GUI: WebView2 runtime (pre-installed on Windows 11, available for Windows 10)

---

## Tech Stack

**Backend**: C# .NET 10, Spectre.Console, Microsoft.Win32.Registry
**Frontend**: Tauri 2.0, Svelte 4, TypeScript 5, TailwindCSS 3, Vite 5
**Native**: Rust, serde, tokio, tauri-plugin-log

---

## Project Structure

```
env-manager/
├── Program.cs                    # CLI implementation (C#)
├── env-manager.csproj            # .NET 10 project (AssemblyName: env-manager-cli)
├── frontend/                     # Tauri GUI application
│   ├── src/                      # Svelte frontend
│   │   ├── App.svelte            # Root component
│   │   ├── lib/
│   │   │   ├── api.ts            # Tauri IPC bridge
│   │   │   ├── stores.ts         # Svelte stores
│   │   │   ├── i18n.ts           # Internationalization
│   │   │   ├── components/       # UI components
│   │   │   └── translations/     # 10 language files
│   ├── src-tauri/                # Rust backend
│   │   ├── src/main.rs           # Tauri command handlers
│   │   ├── tauri.conf.json       # Bundle configuration
│   │   └── Cargo.toml            # Rust dependencies
│   └── scripts/
│       ├── prebuild.mjs          # Builds CLI, copies to src-tauri/bin/
│       └── build-all.ps1         # Consolidated build script
├── release/                      # Build output (gitignored)
│   ├── portable/                 # GUI + CLI flat package
│   └── msi/                      # MSI installer
├── AGENTS.md                     # Project specification
└── LICENSE                       # Apache-2.0
```

---

## Security

- No credential storage. Only manages environment variables.
- Direct Registry API via `Microsoft.Win32.Registry`, no COM.
- IPC isolation. CLI runs as a separate subprocess spawned by the GUI.
- Input validation with 32767-byte limit on variable names and values.
- Permission separation between user and system scopes.

---

## FAQ

**How do I manage system variables?**

Use the `--scope system` flag. This requires administrator privileges:

```bash
env-manager-cli.exe set SYSTEM_VAR "value" --scope system
```

**Can I backup and restore?**

Yes. The JSON format is human-readable and portable:

```bash
env-manager-cli.exe backup --output my-backup.json
env-manager-cli.exe restore my-backup.json
```

**Does the GUI need a web server?**

No. In production, Tauri embeds the frontend as static assets served via its `tauri://` custom protocol. No localhost server, no network dependency. During development, Vite provides hot reload at `localhost:5173`.

**How do I add a new language?**

Add a JSON file in `frontend/src/lib/translations/`, register it in `frontend/src/lib/i18n.ts`. See AGENTS.md for the full i18n workflow.

---

## Troubleshooting

| Issue | Solution |
|-------|----------|
| Access denied for system scope | Run as Administrator |
| Variable not appearing immediately | Restart the application |
| GUI shows blank screen | Ensure WebView2 is installed |
| Backup file invalid | Run `validate` command or check JSON syntax |

---

## Development

See [AGENTS.md](AGENTS.md) for the full project specification, including architecture, coding standards, build system, i18n rules, and release procedures.

### Quick Setup

```bash
dotnet build -c Release         # Build CLI
cd frontend && npm install      # Install GUI dependencies
npm run tauri-dev               # Launch GUI with hot reload
```

---

## License

Apache-2.0 - Use freely for personal and commercial projects. See [LICENSE](LICENSE).

---

### v0.7.1

- Fixed a Windows argv tokenizer hazard where a quoted PATH value ending with a trailing backslash (e.g. `"C:\Program Files\PowerShell\7\"`) swallowed the following `--scope` argument. The CLI now detects this signature and re-tokenizes lazily; clean argv from the GUI/Tauri path is never touched.
- Added a per-session host environment snapshot script (`scripts/snapshot-host-env.ps1`) and upgraded the live smoke harness to exact registry/configuration snapshots with verified rollback; this prevents test artifacts from silently altering existing variables.
- Redacted CLI output from Rust/frontend diagnostic logs, replaced the startup error DOM sink with a safe generic state, made registry writes verify-and-rollback before success is printed, and pinned GitHub Actions to immutable commit SHAs.

## Contributing

Open source project. For issues, feature requests, or pull requests, visit the [GitHub repository](https://github.com/Xxx91n/env-manager).

---

**Version**: 0.7.1 | **License**: Apache-2.0 | **Status**: Active Development


### Safety and Performance

Protected variables and PATH entries are disabled in the GUI before a command is issued, while the CLI enforces the same rule as the authority. Disabled variables retain their original registry value kind and are restored only after an exact verification. The GUI uses bounded 5-second, generation-safe caches and single-flight IPC reads: large variable sets avoid duplicate refresh processes without allowing an old response to overwrite newer state.
