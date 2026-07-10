# Env Manager

Modern, lightweight Windows environment variable manager with CLI and GUI support.

**[简体中文](README_CN.md)** | **English**

## Quick Start

```bash
# Download
# From: https://github.com/Xxx91n/env-manager/releases

# List all variables
env-manager.exe list

# Get a variable
env-manager.exe get PATH

# Set a variable
env-manager.exe set MY_VAR "my_value"

# Delete a variable
env-manager.exe delete MY_VAR

# Backup all variables
env-manager.exe backup --output backup.json

# Restore from backup
env-manager.exe restore backup.json

# Compare backups
env-manager.exe diff old.json new.json

# Merge backups
env-manager.exe merge old.json new.json --output merged.json
```

## Features

### CLI Mode
- **9 commands** for complete environment variable management
- **User/System scope** - manage both Current User and System environment variables
- **Backup/Restore** - export and import as JSON files
- **Diff/Merge** - compare and merge backup files
- **Validation** - verify backup file format
- **No admin required** for user scope (system scope requires administrator)

### GUI Mode
- **Modern Tauri desktop application** - lightweight (40MB) and fast
- **Real-time variable list** - see all variables at a glance
- **Search/Filter** - find variables by name or value
- **Scope selector** - toggle between User and System scopes
- **Add/Edit/Delete** - manage variables directly in the GUI
- **Backup/Restore UI** - export and import backups through the interface

## Installation

### From Release (Recommended)

1. Download `env-manager-v0.3.0.exe` (CLI) or `env-manager-v0.3.0.msi` (GUI installer)
2. Run the installer or execute directly
3. CLI: Copy to a directory in PATH or run from anywhere
4. GUI: MSI installer creates Start Menu shortcuts

### From Source

```bash
# Requirements: .NET 10 SDK, Node.js 20+, Rust stable

# Build CLI
dotnet build -c Release
# Output: bin/Release/net10.0/env-manager.exe

# Build GUI
cd frontend
npm install
npm run tauri-build
# Output: dist/ (web assets) + MSI installer
```

## Commands Reference

| Command | Usage | Description |
|---------|-------|-------------|
| `list` | `env-manager list` | List all variables |
| `get` | `env-manager get NAME` | Get variable value |
| `set` | `env-manager set NAME VALUE [--scope user\|system]` | Create/update variable (default: user) |
| `delete` | `env-manager delete NAME [--scope user\|system]` | Remove variable (default: user) |
| `backup` | `env-manager backup [--output FILE]` | Export all variables to JSON |
| `restore` | `env-manager restore FILE [--scope user\|system]` | Import from JSON backup |
| `diff` | `env-manager diff OLD NEW` | Compare two backup files |
| `merge` | `env-manager merge OLD NEW --output FILE` | Merge two backup files |
| `validate` | `env-manager validate FILE` | Verify backup format |
| `help` | `env-manager help` | Show help |

## GUI Usage

Open the GUI in multiple ways:

1. **Start Menu** - After MSI installation, search for "Env Manager"
2. **Direct Launch** - Run the generated desktop shortcut
3. **Web Build** - Open `dist/index.html` in a browser during development

### GUI Features

- **Search bar** - filter variables by name or value in real-time
- **Scope dropdown** - switch between User / System / All scopes
- **Variable table** - organized columns: Name, Scope, Value, Actions
- **Add button** - open dialog to create new environment variable
- **Edit button** - modify existing variables (for the current scope)
- **Delete button** - remove variables with confirmation
- **Backup/Restore button** - export or import JSON backups

## Backup File Format

```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\Windows\\System32",
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

## System Requirements

- **OS**: Windows 10 21H2 or later (Windows 11 recommended)
- **CLI**: .NET Runtime 10.0+
- **GUI**: No additional runtime (Tauri bundles everything)

## Project Structure

```
env-manager/
├── Program.cs                    # CLI implementation (215 lines, C#)
├── env-manager.csproj            # .NET project configuration
├── bin/Release/net10.0/          # Compiled CLI binary
│   └── env-manager.exe
├── frontend/                      # GUI application (Tauri + TypeScript + Svelte)
│   ├── src/                      # Frontend components
│   │   ├── App.svelte            # Root component
│   │   └── lib/components/       # Reusable components
│   ├── src-tauri/                # Tauri backend (Rust)
│   │   └── src/main.rs           # IPC command handler
│   └── package.json              # Node.js dependencies
├── dist/                         # Built frontend assets
├── .github/workflows/            # GitHub Actions CI/CD
│   └── build.yml                 # Full pipeline
└── AGENTS.md                     # Project specification
```

## Development

See `AGENTS.md` for:
- Project specifications and architecture
- Development guidelines and standards
- CLI command implementation details
- GUI component structure
- CI/CD pipeline details
- Release and testing procedures

### Quick Development Setup

```bash
# Setup environment
dotnet --version          # Verify .NET 10
node --version            # Verify Node.js 20+

# CLI development
dotnet build -c Release

# GUI development (hot reload)
cd frontend
npm install
npm run tauri-dev

# Run tests
dotnet test
cd frontend && npm run test
```

## Security

- **No external dependencies** for core CLI functionality
- **Local Registry access only** - no remote data transmission
- **Input validation** - all user input validated (32KB Windows limit)
- **Scope isolation** - User and System scopes properly isolated
- **Security audit**: Semgrep passed ✅ (0 findings)

See `SECURITY_AUDIT.md` for detailed security analysis.

## Tech Stack

### Backend
- **Language**: C# .NET 10
- **Registry**: Microsoft.Win32.Registry (native Windows API)
- **CLI Output**: Spectre.Console (beautiful formatting)
- **Deployment**: Single 158KB executable

### Frontend
- **Framework**: Tauri 2.0 (lightweight desktop)
- **UI**: Svelte 4 (reactive components)
- **Language**: TypeScript 5 (type-safe)
- **Styling**: TailwindCSS 3 (atomic CSS)
- **Build**: Vite 5 (fast bundling)

## CI/CD Pipeline

GitHub Actions workflow: `.github/workflows/build.yml`

**Stages**:
1. **Lint** - Semgrep security scanning
2. **Build-CLI** - .NET compilation + artifact upload
3. **Build-GUI** - Tauri build + MSI generation
4. **Test** - Integration testing
5. **Release** - Auto-publish GitHub Release on version tags

**Triggers**:
- Push to main: Runs lint, build, test
- Push tag (v*): Runs full pipeline + releases

## FAQ

### How do I manage system variables?
Use the `--scope system` flag. This requires administrator privileges:

```bash
# Run as Administrator first
env-manager.exe set SYSTEM_VAR "value" --scope system
```

### Can I backup and restore?
Yes! JSON format is human-readable and portable:

```bash
env-manager.exe backup --output my-backup.json
# Edit my-backup.json if needed
env-manager.exe restore my-backup.json
```

### Does the GUI work without installing?
Yes, the web build can be opened directly:

```bash
start .\dist\index.html
```

During development with hot reload:
```bash
cd frontend && npm run tauri-dev
```

### How do I update?
Tauri GUI has built-in auto-update support. CLI: Download new .exe and replace.

## Troubleshooting

| Issue | Solution |
|-------|----------|
| "Access Denied" for system scope | Run as Administrator |
| Variable not appearing immediately | Restart the application (environment cache) |
| GUI won't start | Check browser console (F12) for errors |
| Backup file invalid | Verify JSON format manually or use `validate` command |

## License

MIT - Use freely for personal and commercial projects.

See [LICENSE](LICENSE) for details.

## Contributing

This is an open-source project. For issues, feature requests, or pull requests:

1. Check existing issues on GitHub
2. For bugs, provide:
   - Windows version
   - CLI/GUI version
   - Exact reproduction steps
   - Screenshots (for GUI)

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for version history and release notes.

## Contact & Support

- **Issues**: GitHub Issues
- **Discussions**: GitHub Discussions
- **Email**: Report security issues privately

---

**Version**: 0.3.0  
**License**: MIT  
**Status**: Production Ready
