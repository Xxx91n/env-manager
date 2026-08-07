# Contributing to Env Manager

Thanks for your interest in contributing! This project is a Windows environment variable manager with a multi-language stack (C# .NET 10, TypeScript/Svelte 4, Rust/Tauri 2.0).

## Prerequisites

- Windows 10 22H2+ or Windows 11 (x64, x86, or ARM64)
- .NET 10 SDK
- Node.js 20+ and npm
- Rust (stable toolchain with MSVC target)
- WebView2 Runtime (preinstalled on Windows 11, available on Windows 10 21H2+)

## Development Setup

```bash
# Clone
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager

# Install frontend dependencies
cd frontend && npm ci && cd ..

# Build CLI + GUI + Service (host architecture)
node scripts/build.mjs

# Or target a specific architecture
node scripts/build.mjs --arch x64
```

## Project Structure

- `Program.cs` / `*.cs` -- C# .NET 10 CLI backend (env-manager-cli.exe)
- `frontend/src/` -- Svelte 4 + TypeScript frontend (WebView2)
- `frontend/src-tauri/src/` -- Rust Tauri shell (env-manager.exe)
- `service/` -- Rust standalone service binary (env-manager-service.exe)
- `scripts/` -- Build orchestrator, test harnesses, migration scripts
- `docs/` -- Architecture docs, CLI reference, secret provider guide

## Testing

```bash
# Frontend unit tests (Vitest)
cd frontend && npx vitest run && cd ..

# Pester integration tests (PowerShell)
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/run-ci-tests.ps1

# Inheritance protection test
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-inheritance-protection.ps1

# Registry-safe CLI smoke test (backs up + restores registry)
pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-with-restore.ps1
```

> [!WARNING]
> Never run raw registry-mutating CLI commands against the host without `test-with-restore.ps1`. It snapshots and restores HKCU\Environment and HKLM\...\Environment around every test run.

## Code Style

- C#: Follow existing file-scoped namespaces and naming. Run `dotnet build` to verify.
- TypeScript/Svelte: Follow existing formatting. Run `npx vitest run` to verify.
- Rust: Follow `cargo fmt` and `cargo clippy` conventions.

## Pull Request Process

1. Create a branch from `main` (prefix `codex/` for AI-assisted work, or `feat/`/`fix/` for manual work)
2. Make your changes with focused, reviewable commits
3. Ensure all tests pass:
   ```bash
   node scripts/build.mjs --arch x64
   cd frontend && npx vitest run && cd ..
   pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/run-ci-tests.ps1
   ```
4. Update `AGENTS.md` if your change introduces a new hard boundary or architectural invariant
5. Update `CHANGELOG.md` under the `[Unreleased]` section
6. Open a PR with the template filled out

## Hard Boundaries

Before contributing, read `AGENTS.md` carefully. It contains 40+ hard boundaries (red lines) that must never be violated: protected variables, cross-process mutex, verified registry writes, DPAPI secrets, Launch profile isolation, secret provider activation preflight, and more. Changes that violate a hard boundary will be rejected.

## Reporting Issues

Use GitHub Issues with the appropriate template (Bug Report or Feature Request). For security vulnerabilities, see `SECURITY.md` -- do NOT open a public issue for security-sensitive reports.

## Code of Conduct

This project follows the Contributor Covenant Code of Conduct (see `.github/CODE_OF_CONDUCT.md`). By participating, you agree to abide by its terms.