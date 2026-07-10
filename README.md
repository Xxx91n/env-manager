# Env Manager

A modern, lightweight Windows environment variable manager with a fast CLI and elegant desktop GUI.

Inspired by Microsoft PowerToys. Engineered for efficiency. Open source and MIT licensed.

---

## Features

### CLI

- List all variables from user and system scopes
- Get, set, and delete individual variables with scope control
- Backup and restore environment snapshots in JSON format
- Diff and merge multiple backups to track changes
- Beautiful output with Spectre.Console tables
- Fast and native performance with .NET 10

### GUI (Phase 2)

- Real-time environment variable editor
- Search and filter across all scopes
- Backup management with export and import
- Responsive design with dark mode support
- IPC synchronization between CLI and desktop app
- Built with Tauri for lightweight native performance

---

## Quick Start

### Installation

**Option 1: Download Binary**

Download from GitHub Releases:
https://github.com/Xxx91n/env-manager/releases

Copy to your PATH or use directly:
```powershell
.\\env-manager.exe list
```

**Option 2: Build from Source**

```powershell
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager/backend
dotnet build -c Release

# Binary at: bin/Release/net10.0/env-manager.exe
```

### CLI Usage

```powershell
# List all variables
env-manager list

# Get a specific variable
env-manager get PATH

# Set a variable (user scope)
env-manager set MY_VAR "my_value"

# Set system variable (requires elevation)
env-manager set MY_VAR "my_value" --scope system

# Delete a variable
env-manager delete MY_VAR

# Backup current state
env-manager backup --output my_backup.json

# Restore from backup
env-manager restore my_backup.json

# Compare two backups
env-manager diff old_backup.json new_backup.json

# Merge backups
env-manager merge old.json new.json --output merged.json

# Show help
env-manager help
```

---

## Project Phases

### Phase 1: CLI Backend (Complete)

- Fast CRUD operations on environment variables
- User and system scope support
- Minimal dependencies (Spectre.Console only)
- Direct Windows Registry API access
- Fully tested on Windows 10 and 11

### Phase 2: Desktop GUI (In Development)

- Tauri-based cross-platform application
- TypeScript and Svelte frontend
- Real-time environment variable editor
- Backup export and import UI
- IPC bridge to CLI

### Phase 3: Polish and Distribution (Planned)

- MSI installer for easy installation
- GitHub Releases with auto-download functionality
- Auto-update mechanism
- Complete documentation and guides

---

## Architecture

### Backend (C# .NET 10)

Program.cs contains:
- Registry API wrapper
- CRUD operations for variables
- Backup and restore logic
- CLI command routing
- Error handling

**Technology Stack**:
- Language: C# .NET 10
- Registry Access: Microsoft.Win32.Registry (built-in)
- CLI Output: Spectre.Console
- Delivery: Single executable, approximately 15MB with runtime

### Frontend (Tauri + TypeScript/Svelte)

```
src/
├── App.svelte              (Root component)
├── lib/
│   ├── api.ts              (IPC bridge to CLI)
│   ├── components/         (UI components)
│   └── stores/             (State management)
└── styles/                 (TailwindCSS)
```

**Technology Stack**:
- Framework: Tauri 2.0
- UI: TypeScript and Svelte
- Styling: TailwindCSS
- Build: Vite

### Data Format

**Backup JSON**:
```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\\\Windows\\\\System32;...",
      "scope": "user"
    },
    {
      "name": "JAVA_HOME",
      "value": "D:\\\\jdk17\\\\",
      "scope": "system"
    }
  ]
}
```

---

## Comparison with Alternatives

| Feature | Env Manager | PowerToys | setx (native) |
|---------|-------------|----------|---------------|
| GUI | Planned | Yes | No |
| CLI | Yes | No | Yes (limited) |
| Backup/Restore | Yes | No | No |
| Diff/Merge | Yes | No | No |
| Open Source | Yes (MIT) | Yes (MIT) | Yes |
| .NET Lightweight | Yes | No (C++) | N/A |
| Cross-Scope | Yes | Yes | Yes (limited) |

---

## Development

### Setup (Windows)

**Prerequisites**:
- .NET 10 SDK
- Node.js 18 or higher (for GUI)
- Tauri CLI
- Git

**Clone and Build**:
```powershell
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager

# Build CLI
cd backend
dotnet build -c Release
cd ..

# Build GUI (Phase 2)
cd frontend
npm install
tauri dev
```

### Project Structure

```
env-manager/
├── backend/                # C# CLI
│   ├── Program.cs
│   ├── env-manager.csproj
│   ├── bin/Release/
│   └── obj/
│
├── frontend/               # Tauri GUI (Phase 2)
│   ├── src/
│   ├── package.json
│   └── tauri.conf.json
│
├── docs/
│   ├── README.md           (this file)
│   ├── README_CN.md        (Chinese version)
│   ├── ARCHITECTURE.md
│   └── DEVELOPMENT.md
│
├── .github/workflows/      # CI/CD
├── AGENTS.md               # Development guidelines
├── LICENSE                 # MIT license
└── .gitignore
```

### Testing

**CLI Smoke Test**:
```powershell
.\\backend\\bin\\Release\\net10.0\\env-manager.exe list
```

**Backup Test**:
```powershell
.\\backend\\bin\\Release\\net10.0\\env-manager.exe backup --output test.json
.\\backend\\bin\\Release\\net10.0\\env-manager.exe validate test.json
```

### Contribution

We welcome contributions. See DEVELOPMENT.md for setup and workflow.

**Code Style**:
- C#: .editorconfig enforced
- TypeScript: ESLint strict mode
- Commits: Conventional Commits format

---

## License

MIT License. Copyright (c) 2026 Env Manager Contributors.

See LICENSE for full text.

---

## Documentation

- README.md (English, this file)
- README_CN.md (Chinese translation)
- AGENTS.md (Development guidelines)
- DEVELOPMENT.md (Developer setup)
- ARCHITECTURE.md (System design)
- CHANGELOG.md (Release notes)

---

## Community

- Report bugs on GitHub Issues
- Ask questions on GitHub Discussions
- Submit PRs with improvements
- Share ideas via Issues (tag as enhancement)

---

## Roadmap

- v0.2 (Phase 2): Tauri GUI with backup commands
- v0.3 (Phase 3): MSI installer and auto-update
- v1.0: Stable release with Windows Store availability
- v1.1 and beyond: Community-driven enhancements

---

## Why Env Manager?

- Modern: Built with latest .NET and Tauri technologies
- Fast: Native performance with single executable
- Lightweight: Approximately 15MB CLI with minimal dependencies
- Safe: Direct Registry access with comprehensive error handling
- Polished: Clean CLI output and elegant GUI design
- Free: Open source with MIT license
- Community-Driven: Your contributions shape the future

---

Repository: https://github.com/Xxx91n/env-manager
