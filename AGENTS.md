# Development Guidelines & Agent Operations

This document defines the development standards, architecture decisions, and operational procedures for the Env Manager project.

## Project Specification

**Project**: Env Manager  
**Scope**: Windows environment variable manager with CLI and desktop GUI  
**Language**: C# (.NET 10), TypeScript + Svelte  
**License**: MIT  
**Status**: Phase 1 Complete | Phase 2-3 In Development  

## Architecture & Design Decisions

### Backend Architecture

**Technology Stack**:
- Runtime: .NET 10
- Registry Access: Microsoft.Win32.Registry (built-in)
- CLI Output: Spectre.Console
- Delivery: Single executable (~15MB)

**Design Pattern**: Command-based architecture with direct Registry operations.

**Why C# over Rust**:
1. Native Windows Registry API support
2. Eliminates linker chain complexity
3. .NET 10 provides excellent performance
4. Faster CLI prototyping cycle

**Registry Access Pattern**:
```
User Environment:   HKCU\Environment
System Environment: HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment
```

### Frontend Architecture (Phase 2)

**Technology Stack**:
- Framework: Tauri 2.0
- UI: TypeScript + Svelte
- Styling: TailwindCSS
- Build: Vite
- State: Svelte stores

**IPC Bridge Strategy**:
- GUI invokes CLI via `tauri::Command` spawning `env-manager.exe`
- Communication through stdout parsing and JSON intermediates
- Error handling with process exit codes
- Async/await for non-blocking UI

**UI Components**:
- Environment variable table with pagination
- Search and filter by name/value
- Create/Edit/Delete dialogs
- Backup export/import interface
- Scope selector (user/system)
- Status notifications and error messages

### Data Format

**Backup JSON Structure**:
```json
{
  "timestamp": "ISO8601",
  "version": "1.0.0",
  "variables": [
    {
      "name": "VARIABLE_NAME",
      "value": "value",
      "scope": "user|system"
    }
  ]
}
```

**Invariants**:
- Timestamp is required for audit trails
- Version enables migration logic
- Scope field distinguishes user/system variables

## Implementation Guidelines

### Code Standards

**C# Backend**:
- Minimum .NET 10
- Async/await for all I/O operations
- Explicit types (no `var` in public APIs)
- Using statements for resource disposal
- XML documentation for public members
- Unit tests for Registry operations

**TypeScript/Svelte Frontend**:
- ESLint strict mode
- TypeScript strict null checks
- Reactive bindings for state management
- Error boundaries for stability
- Component-based organization
- TailwindCSS utility classes

**Code Quality Metrics**:
- Target: 80%+ test coverage for critical paths
- No console.log in production code
- All error paths explicitly handled
- No TODO comments without GitHub issues

### Testing Strategy

**Unit Tests** (C#):
- Registry wrapper functions
- Backup/restore logic
- Variable validation
- Mock Windows Registry for CI

**Integration Tests**:
- CLI command execution with sample data
- Backup export and restore workflows
- Cross-scope operations
- File I/O operations

**End-to-End Tests** (Playwright):
- Component rendering
- User workflows (create, edit, delete)
- IPC communication between GUI and CLI
- Screenshot regression testing
- Windows 10/11 compatibility

**Compatibility Requirements**:
- Windows 10 21H2+
- Windows 11 all versions
- Both admin and non-admin execution paths
- System scope operations require elevation

### Commit Conventions

Format:
```
<type>(<scope>): <subject>

<body>

Closes #<issue>
```

**Types**: feat, fix, docs, style, refactor, test, chore, perf  
**Scopes**: cli, gui, backup, registry, build, docs, infra  

**Example**:
```
feat(backup): implement JSON export and restore

- Add backup command with automatic timestamping
- Implement restore with scope filtering
- Add validate command for backup integrity
- Support both user and system scopes

Closes #3
```

### File Organization

```
env-manager/
├── backend/
│   ├── Program.cs                 # CLI entry point and handlers
│   ├── env-manager.csproj         # Project configuration
│   ├── bin/Release/net10.0/       # Compiled binaries
│   └── obj/                       # Build artifacts (gitignored)
├── frontend/
│   ├── src/
│   │   ├── main.ts                # Tauri app entry
│   │   ├── App.svelte             # Root component
│   │   ├── lib/
│   │   │   ├── api.ts             # CLI IPC bridge
│   │   │   ├── components/        # Reusable UI components
│   │   │   └── stores/            # Svelte state stores
│   │   └── styles/                # Global stylesheets
│   ├── public/                    # Static assets
│   ├── package.json
│   ├── vite.config.ts
│   ├── tsconfig.json
│   └── tauri.conf.json            # Tauri configuration
├── docs/
│   ├── README.md                  # Main documentation (English)
│   ├── README_CN.md               # Chinese translation
│   ├── ARCHITECTURE.md            # System design details
│   ├── DEVELOPMENT.md             # Developer setup guide
│   └── CHANGELOG.md               # Release notes
├── .github/
│   ├── workflows/                 # CI/CD pipelines
│   └── ISSUE_TEMPLATE/            # Bug report templates
├── .gitignore                     # Standard .NET exclusions
├── AGENTS.md                      # This file
├── LICENSE                        # MIT license text
└── .omx/                          # OMX workflow state

```

## Development Workflow

### Setup Requirements

**Prerequisites**:
- .NET 10 SDK
- Node.js 18+ (for GUI)
- Tauri CLI
- Git
- For system scope operations: Administrator access

**Initial Setup**:
```powershell
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager

# Backend
cd backend
dotnet restore
dotnet build -c Release

# Frontend (Phase 2+)
cd ../frontend
npm install
npm run build
```

### Build & Release Process

**Development Build**:
```powershell
dotnet build -c Debug
```

**Release Build**:
```powershell
dotnet build -c Release
# Output: bin/Release/net10.0/env-manager.exe
```

**GUI Development**:
```powershell
cd frontend
tauri dev  # Hot reload enabled
```

### Testing Process

**Run Tests**:
```powershell
dotnet test
```

**Manual Testing Checklist**:
- [ ] List all variables (user and system)
- [ ] Get specific variable
- [ ] Set user variable
- [ ] Set system variable (requires admin)
- [ ] Delete variable
- [ ] Backup creation
- [ ] Backup restore
- [ ] Diff two backups
- [ ] Merge backups
- [ ] Help output formatting

### Code Review Checklist

Before merge, verify:
- [ ] No hardcoded paths or credentials
- [ ] Error handling for all Registry operations
- [ ] Unit tests added for new logic
- [ ] Code follows style guidelines
- [ ] Commit messages follow conventions
- [ ] No Breaking changes documented
- [ ] Documentation updated
- [ ] Performance impact assessed

## Known Constraints & Tradeoffs

### System Scope Limitations

- Requires administrator privileges for write operations
- Changes apply to system-wide environment
- User must restart applications to see changes

### Backup & Restore

- JSON format for portability but larger than binary
- No compression implemented (can be added if needed)
- Merge strategy is last-write-wins

### IPC Design

- CLI spawned as separate process to avoid blocking GUI
- stdout/stderr parsing for output
- JSON intermediates for complex data
- No shared memory due to privilege isolation

## Error Handling Strategy

**Registry Access Failures**:
- Catch UnauthorizedAccessException for elevation requirements
- Log operation details for debugging
- Present user-friendly error messages
- Suggest running as administrator for system scope

**File I/O**:
- Validate paths before access
- Handle missing files gracefully
- Preserve backup files on error
- Log stack traces for unexpected errors

**Data Validation**:
- Enforce name length limits (max 32767)
- Validate scope values
- Check value encoding compatibility
- Prevent null or empty critical fields

## Performance Considerations

- Registry operations are I/O bound
- GUI should spawn CLI processes asynchronously
- Large variable lists require pagination
- Backup diff should short-circuit on first change

## Security Guidelines

### Registry Access

- Use minimal required Registry scope
- Validate all user input before Registry operations
- Log sensitive operations for audit trail
- Never log variable values containing credentials

### IPC Communication

- Validate CLI output before parsing
- Use JSON schema validation
- Implement timeout for process execution
- Handle process crashes gracefully

### Dependency Management

- Verify all NuGet packages before adding
- Keep dependencies up-to-date
- Review breaking changes in updates
- Document dependency rationale

## Release Checklist

Before major release:
- [ ] All tests passing
- [ ] Code review completed
- [ ] Documentation updated
- [ ] Changelog entries written
- [ ] Version bumped
- [ ] Release notes prepared
- [ ] Binaries built and tested
- [ ] Tag created in Git
- [ ] Artifacts uploaded
- [ ] GitHub release published

## Common Tasks

### Adding a New CLI Command

1. Add handler function in Program.cs
2. Add case statement to main switch
3. Document usage in help text
4. Add unit tests
5. Update README with example
6. Commit with conventional format

### Adding GUI Feature

1. Create Svelte component in lib/components/
2. Implement IPC call in lib/api.ts
3. Add state to lib/stores/
4. Wire component into App.svelte
5. Style with TailwindCSS
6. Test IPC communication
7. Add E2E test

### Updating Dependencies

1. Review changelog for breaking changes
2. Update package.json/csproj
3. Run build and tests
4. Commit: chore(deps): update <package>
5. Document any migration steps required

## Decision Log

### Why Single-File CLI

- Simplifies deployment (copy .exe, done)
- Avoids DLL version conflicts
- Self-contained for packaging
- Future: split into service/library for GUI integration

### Why JSON for Backups

- Human-readable for debugging
- Portable across platforms
- Version-aware for migrations
- Git-friendly for version control
- Tooling support (jq, PowerShell)

### Why Tauri for GUI

- Lightweight (40MB vs 200MB Electron)
- Native Windows integration
- Strong TypeScript support
- Active ecosystem
- Security-first design

## Maintenance & Support

### Issue Triage

- Bug: Reproducible issue with clear steps
- Enhancement: Feature request or improvement
- Documentation: Docs clarity or completeness
- Question: Support or clarification needed

### Version Support

- Latest: Full support and updates
- N-1: Critical bug fixes only
- N-2 and older: No support

### Deprecation Policy

- 3 month notice period
- Clear migration path provided
- Documentation updated
- Alternative solutions documented

---

**Last Updated**: 2026-07-10  
**Maintained By**: Env Manager Development Team  
**Status**: Active Development
