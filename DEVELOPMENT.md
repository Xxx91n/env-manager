# Development Guide

## Project Structure

- **Program.cs** - CLI implementation (C# .NET 10)
- **frontend/** - GUI application (TypeScript + Svelte + Tauri)
  - `src/` - Svelte components and TypeScript
  - `src-tauri/` - Rust Tauri backend
  - `package.json` - Frontend dependencies
- **bin/Release/net10.0/** - Compiled CLI binary

## Prerequisites

- .NET 10 SDK
- Node.js 20+
- Rust toolchain (for Tauri builds)
- Git

## Building

### CLI Only

```bash
dotnet build -c Release
# Binary: bin/Release/net10.0/env-manager.exe
```

### GUI (Development Mode)

```bash
cd frontend
npm install
npm run tauri-dev
```

This opens the development window with hot-reload enabled.

### GUI (Production Build)

```bash
cd frontend
npm install
npm run tauri-build
# Output: src-tauri/target/release/env-manager.exe or .msi
```

## Testing

### CLI Tests

```bash
# List all variables
.\bin\Release\net10.0\env-manager.exe list

# Backup
.\bin\Release\net10.0\env-manager.exe backup --output backup.json

# Restore from backup
.\bin\Release\net10.0\env-manager.exe restore backup.json
```

### GUI Tests

1. Run `npm run tauri-dev` in `frontend/` directory
2. Test variable listing and filtering
3. Test variable creation/editing
4. Test backup/restore functionality

## Code Organization

### CLI (Program.cs)

- **EnvVariable** - Data model for a single variable
- **BackupData** - Format for JSON exports
- **ListEnvironment()** - Display all variables
- **GetVariable()** - Retrieve single variable
- **SetVariable()** - Create/update variable
- **DeleteVariable()** - Remove variable
- **CreateBackup()** - Export to JSON
- **RestoreBackup()** - Import from JSON
- **DiffBackups()** - Compare two exports
- **MergeBackups()** - Combine backups
- **ValidateBackup()** - Verify JSON format

### GUI (frontend/src/)

- **App.svelte** - Root component with header
- **lib/stores.ts** - Svelte stores for reactive state
- **lib/api.ts** - IPC bridge to CLI (run_cli command)
- **lib/components/Variables.svelte** - Main table with search/filter
- **lib/components/EditDialog.svelte** - Modal for create/edit
- **lib/components/BackupDialog.svelte** - Backup/restore interface

### Tauri Backend (frontend/src-tauri/)

- **src/main.rs** - Tauri application entry
- **run_cli()** - Command that spawns CLI executable and captures output
- **Cargo.toml** - Rust dependencies
- **tauri.conf.json** - Window config, security settings

## Development Workflow

1. Make changes to CLI or GUI code
2. Build/test locally:
   - CLI: `dotnet build -c Release`
   - GUI: `npm run tauri-dev`
3. Commit with conventional message:
   - `feat(cli): add new command`
   - `fix(gui): correct dialog layout`
   - `docs: update README`
4. Push to GitHub (triggers CI)
5. When ready: tag release as `v0.x.0`

## Common Tasks

### Add a CLI Command

1. Edit `Program.cs` Main() switch statement
2. Add handler method
3. Update ShowHelp() text
4. Test with `env-manager.exe <command>`

### Update GUI Component

1. Edit `.svelte` file in `frontend/src/`
2. Test with `npm run tauri-dev`
3. Verify API bridge calls (if needed)

### Debug IPC Issues

- Check `frontend/src-tauri/src/main.rs run_cli()` for path resolution
- Ensure CLI binary exists at expected location
- Look for error messages in Tauri console

## Release Process

1. Bump version in all relevant files:
   - `env-manager.csproj` - SDK version
   - `frontend/package.json` - Version
   - `frontend/src-tauri/tauri.conf.json` - Version
2. Create release commit: `chore: release v0.x.0`
3. Tag: `git tag v0.x.0 && git push --tags`
4. GitHub Actions auto-builds and creates release

## Troubleshooting

### "CLI binary not found" in GUI

The GUI expects the CLI at a specific path. Ensure:
- CLI is built: `dotnet build -c Release`
- Path resolution in `run_cli()` matches your build location

### Tauri build fails

- Ensure Rust toolchain is installed
- Run `cargo update` in `frontend/src-tauri/`
- Check permissions in Windows (may need admin for first build)

### Registry access denied

Some operations require elevation:
- Running with admin account
- Modifying system variables (vs user-only)
- The CLI shows clear errors for permission issues
