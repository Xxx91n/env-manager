# Env Manager _(env-manager)_

Modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support. Inspired by Microsoft PowerToys, built standalone for speed and simplicity.

**[简体中文](README_CN.md)** | **English**

<!-- screenshot: main-ui -- replace with screenshot of main variable list view -->
<!-- screenshot: profile-editor -- replace with screenshot of profile editor with secret provider selector -->
<!-- screenshot: service-manager -- replace with screenshot of service management panel -->

---

## Security

> [!WARNING]
> Env Manager is **not code-signed**. Windows SmartScreen may show an "unrecognized app" warning on first launch. Click "More info" then "Run anyway" to proceed. Code signing is planned for a future release.

Protected variables and PATH entries are disabled before deletion, with exact registry value-kind verification on restore. Secret values are encrypted via provider-specific mechanisms (DPAPI, CredMan, Vault, SOPS, Azure KV, 1Password, AWS SM) — plaintext never persists to disk or logs. Named pipe IPC uses anti-squatting flags and input validation (64 arg max, 32767 char cap, null byte rejection). See [SECURITY.md](SECURITY.md) for vulnerability reporting.

## Table of Contents

- [Security](#security)
- [Background](#background)
- [Install](#install)
- [Usage](#usage)
- [Features](#features)
- [Architecture](#architecture)
- [Secret Providers](#secret-providers)
- [Service Mode](#service-mode)
- [Maintainers](#maintainers)
- [Contributing](#contributing)
- [License](#license)

## Background

The built-in Windows environment variable editor is clunky and error-prone. Env Manager provides a modern, fast alternative with a C# CLI for scripting and automation, plus a native Tauri/Svelte GUI for interactive editing. It adds profile inheritance, PATH health diagnostics, 8 secret provider backends, Launch profile isolation, a standalone secret-lifecycle service, audit ledger, and 10-language i18n — capabilities that go beyond PowerToys, RapidEE, and other alternatives.

## Install

### Portable

Download from [GitHub Releases](https://github.com/Xxx91n/env-manager/releases). Extract the ZIP and run `env-manager.exe` directly. No installation needed.

### Prerequisites

> [!IMPORTANT]
> **Portable** and **CLI-Only** builds are framework-dependent: they require the **.NET 10 Desktop Runtime** to be installed on the target machine.
>
> Download the matching runtime for your architecture from the official .NET download page: <https://dotnet.microsoft.com/download/dotnet/10.0>
>
> | Build | Architecture | .NET 10 Runtime |
> |------|-------------|-----------------|
> | Portable / CLI-Only | x64 | .NET 10 Desktop Runtime x64 |
> | Portable / CLI-Only | x86 | .NET 10 Desktop Runtime x86 |
> | Portable / CLI-Only | ARM64 | .NET 10 Desktop Runtime ARM64 |
>
> The **MSI installer** checks for .NET 10 at install time and prompts automatically.
>
> **WebView2 Runtime** (for GUI) is preinstalled on Windows 11 and available for Windows 10 21H2+ from <https://developer.microsoft.com/microsoft-edge/webview/>.

For optional external secret-provider tools (SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7), see the [Secret Providers Guide](docs/secret-providers-guide.md) for download links and setup instructions.

### MSI Installer

Run the `.msi` file. Creates Start Menu shortcuts automatically. Available in x64, x86, and ARM64.

### CLI-Only

Download the CLI-only ZIP for headless or scripting use: `env-manager-cli.exe` plus `.dll` files. No GUI, no WebView2 dependency.

### winget

> [!NOTE]
> winget distribution is planned but not yet available. Track via GitHub Issues for updates.

### From Source

```bash
git clone https://github.com/Xxx91n/env-manager.git
cd env-manager
cd frontend && npm ci && cd ..
node scripts/build.mjs --arch x64
```

Requires .NET 10 SDK, Node.js 20+, Rust stable with MSVC target. See [docs/build-and-release.md](docs/build-and-release.md) for details.

## Usage

### CLI

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

# PATH health check
env-manager-cli.exe path health

# Create a Launch profile and launch with isolated env
env-manager-cli.exe profile create dev --type launch --target python.exe
env-manager-cli.exe profile add-secret dev API_KEY "sk-xxx"
env-manager-cli.exe profile launch dev

# Service control
env-manager-cli.exe service status
env-manager-cli.exe service ping

# State export/import for disaster recovery
env-manager-cli.exe export-state --output state.dpapi
env-manager-cli.exe import-state --input state.dpapi

# Audit ledger
env-manager-cli.exe audit migrate-audit
env-manager-cli.exe audit verify-ledger
```

See [docs/cli-commands.md](docs/cli-commands.md) for the full command reference.

### GUI

Run `env-manager.exe`. The GUI provides real-time variable list with search, scope filtering, inline edit, PATH editor with drag-and-drop reordering, profile management, secret provider selection, service control panel, audit history, and 10-language i18n.

## Features

### CLI Mode

- 18+ commands for complete environment variable management
- Profiles with inheritance, conflict previews, PATH fragments, and safe reverse-order rollback
- PATH editor with duplicate and missing-directory diagnostics
- **Launch profiles**: isolated env block (`env_clear` + inject), never write registry, never broadcast `WM_SETTINGCHANGE`
- **PATH health**: `path health [--fix] [--dry-run]` detects duplicates and dead entries
- **Secret providers**: 8 backends with activation preflight and inline error guidance
- **Backup/restore**: JSON backup, diff, merge, validate, audited history with guarded undo
- **Bulk import/export**: `.env`, CSV, JSON with dry-run conflict previews
- **State export/import**: `export-state`/`import-state` — DPAPI-encrypted full-state archive for disaster recovery
- **Audit ledger**: `audit migrate-audit` / `verify-ledger` / `export-survival-kit` / `recover-from-ledger`
- **Service control**: `service status` / `ping` / `refresh` / `rotate` / `reload` / `shutdown`
- **v0.9.8 Industrial logging**: `tracing` + `tracing-appender` backend with daily rotation, 7-day retention, cross-process `request_id` for CLI/Rust/Service debug correlation
- **v0.9.9 Schema migration**: `SchemaMigration.cs` registry-based sequential migration framework (profiles v0->v1->v2)
- **v0.9.10 Audit ledger**: `AuditLedgerMigration.cs` implements `audit migrate-audit` (audit.json to `audit-ledger.jsonl` hash-chained), `verify-ledger` (SHA256 chain tamper detection), `export-survival-kit`, `recover-from-ledger`
- User and System scope support, no admin required for user scope

### GUI Mode

- Native desktop app built with Tauri 2.0 (WebView2)
- Real-time variable list with highlighted search, scope filtering, `%VAR%` expansion preview
- PATH editor with staged move (Apply button), health badges, Remove Dead bulk action
- Profile management: Global and Launch types, inheritance, secret variables, provider selector with inline error banner
- Secret provider selector with activation preflight and inline amber error banner
- Service management panel (Ping/Reload/Shutdown + mount health list)
- Audit history viewer with full-command-level operation labels
- Settings: dark mode, font scale, CLI-in-PATH toggle, DR export/import, i18n locale
- Edge-style true-overlay scrollbar (floats over content, zero layout space)
- 10-language internationalization: English, 简体中文, 日本語, 한국어, Deutsch, Français, Español, Português, Русский, العربية

## Architecture

Four layers:

1. **CLI backend** (`Program.cs`) — C# .NET 10 console app, reads/writes Windows Registry directly, compiles to `env-manager-cli.exe`
2. **Tauri shell** (`frontend/src-tauri/`) — Rust app, embeds CLI as bundled resource, spawns CLI subprocesses, returns JSON via Tauri IPC
3. **Svelte frontend** (`frontend/src/`) — TypeScript + Svelte 4 + TailwindCSS in WebView2, talks to Rust only via `invoke('run_cli', ...)`
4. **Service crate** (`service/`) — Rust standalone binary (`env-manager-service.exe`), manages secret mount lifecycle via named pipe IPC

See [docs/architecture.md](docs/architecture.md) for IPC bridge, race condition prevention, system tray, caching, and security hardening.

## Secret Providers

8 provider backends with activation preflight — failures surface as inline amber banners directly in the profile editor:

| Provider | Auth Method | Periodic Refresh | Docs |
|---|---|---|---|
| DPAPI CurrentUser | Windows DPAPI | No (per-user) | [Guide](docs/secret-providers-guide.md) |
| Windows Credential Manager | CredMan + DPAPI | No (per-user) | [Guide](docs/secret-providers-guide.md) |
| PowerShell SecretManagement | SecretStore vault | Best-effort | [Guide](docs/secret-providers-guide.md) |
| HashiCorp Vault KV v2 | VAULT_TOKEN / AppRole cert | Yes | [Guide](docs/secret-providers-guide.md) |
| SOPS | Age / PGP / KMS | Yes | [Guide](docs/secret-providers-guide.md) |
| Azure Key Vault | SP cert / managed identity | Yes | [Guide](docs/secret-providers-guide.md) |
| 1Password CLI | OP_SERVICE_ACCOUNT_TOKEN | Yes | [Guide](docs/secret-providers-guide.md) |
| AWS Secrets Manager | SigV4 + access keys | Yes | [Guide](docs/secret-providers-guide.md) |

See [docs/secret-providers-guide.md](docs/secret-providers-guide.md) for per-provider prerequisites, one-time setup, and activation error fix steps.

## Service Mode

`env-manager-service.exe` is a standalone Rust binary managing secret mount lifecycle via named pipe IPC:

- **RuntimeMode**: Service (SCM-managed, machine boot), Background (user-launched), Cli (one-shot gateway)
- **Reconcile loop**: 300s periodic full-scan, idempotent per-item handler, 30s first-tick delay
- **Cert bootstrap** (Phase D): Vault AppRole and Azure SP certificate-based auth eliminates long-lived tokens
- **Audit ledger** (Phase E): append-only hash-chained `audit-ledger.jsonl` with 100MB rotation and tamper detection
- **IPC**: Anti-squatting pipe flag, 65536-byte request cap, newline-delimited JSON protocol
- **v0.9.6 Watchdog**: two-layer recovery — SCM auto-restart (Service mode) + GUI 30s ping watchdog (Background mode)
- **v0.9.7 Fast-fail**: service probe fast-fail in 2s when service is down (was 18s)

See [docs/secret-architecture-blueprint.md](docs/secret-architecture-blueprint.md) and [docs/secret-architecture-decision-summary.md](docs/secret-architecture-decision-summary.md) for the Phase A-E roadmap.

## Maintainers

[@Xxx91n](https://github.com/Xxx91n)

## Contributing

Contributions are welcome! See [CONTRIBUTING.md](.github/CONTRIBUTING.md) for development setup, testing, and PR process. For bug reports and feature requests, use the [Issue templates](https://github.com/Xxx91n/env-manager/issues). For security reports, see [SECURITY.md](SECURITY.md).

## License

Apache-2.0 — see [LICENSE](LICENSE).
