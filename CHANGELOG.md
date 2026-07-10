# Changelog

All notable changes to this project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- Initial GitHub Actions CI/CD workflow with security scanning
- Tauri 2.0 GUI foundation with TypeScript/Svelte
- MSI installer generation capability
- Comprehensive security audit (Semgrep)

### Changed
- Improved .gitignore to exclude OMX workflow artifacts
- Enhanced tauri.conf.json with proper bundle configuration

### Fixed
- Frontend build output path configuration
- Tauri dependency feature flags

## [0.3.0] - 2026-07-10

### Added
- Tauri GUI application scaffolding
- TypeScript + Svelte frontend components
- EditDialog, Variables list, BackupDialog components
- TailwindCSS styling framework
- E2E test foundation

### Changed
- Upgraded to Tauri 2.0
- Improved CLI/GUI IPC bridge design

## [0.2.0] - 2026-07-10

### Added
- Backup/restore functionality with JSON format
- Diff command for comparing backup files
- Merge command for combining backups
- Validate command for backup format verification
- Comprehensive input validation and error handling
- Security audit with Semgrep (0 findings)

### Documentation
- README.md and README_CN.md with full feature parity
- DEVELOPMENT.md with setup instructions
- SECURITY_AUDIT.md with comprehensive audit report
- AGENTS.md with project specifications

## [0.1.0] - 2026-07-10

### Added
- Initial project setup with Phase 1 CLI
- Core commands: list, get, set, delete, backup, restore
- Registry API integration for user/system scopes
- Spectre.Console for formatted output
- .NET 10 single executable (15MB)
- MIT License
- GitHub Actions basic workflow

### Features
- User-scope environment variable management (no admin required)
- System-scope management (requires administrator privileges)
- JSON backup format with timestamp and version tracking
- Comprehensive error handling and validation
- Windows Registry API integration

[Unreleased]: https://github.com/Xxx91n/env-manager/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/Xxx91n/env-manager/releases/tag/v0.3.0
[0.2.0]: https://github.com/Xxx91n/env-manager/releases/tag/v0.2.0
[0.1.0]: https://github.com/Xxx91n/env-manager/releases/tag/v0.1.0
