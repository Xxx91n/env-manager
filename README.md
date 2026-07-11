# Env Manager

Modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support. Inspired by Microsoft PowerToys, built standalone for speed and simplicity.

**[简体中文](README_CN.md)** | **English**

---

## Features

### CLI Mode
- 10 commands for complete environment variable management
- User and System scope support
- JSON backup and restore with diff/merge
- No admin required for user scope
- Single 158KB executable, no runtime dependency

### GUI Mode
- Native desktop app built with Tauri 2.0 (WebView2)
- Real-time variable list with search and scope filtering
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

**Prerequisites**: .NET 10 SDK, Node.js 18+, Rust toolchain with `x86_64-pc-windows-gnu` target, MinGW-w64

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
└── LICENSE                       # MIT
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

MIT - Use freely for personal and commercial projects. See [LICENSE](LICENSE).

---

## Contributing

Open source project. For issues, feature requests, or pull requests, visit the [GitHub repository](https://github.com/Xxx91n/env-manager).

---

**Version**: 0.3.0 | **License**: MIT | **Status**: Active Development
