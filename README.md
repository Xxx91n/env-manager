<div align="center">

<picture>
  <source media="(prefers-color-scheme: dark)" srcset="docs/assets/logo-dark-theme.png">
  <source media="(prefers-color-scheme: light)" srcset="docs/assets/logo-light-theme.png">
  <img src="docs/assets/logo.png" alt="Env Manager logo" width="120" height="120">
</picture>

<p align="center">
  <img src="docs/assets/brand/hero.gif" alt="Env Manager hero: terminal prompt with three-peak crest in Dev/Staging/Prod color tokens" width="100%">
</p>

# Env Manager

A Windows environment-variable manager with two front ends — a C# CLI built for scripts and AI agents, and a Tauri GUI for interactive editing — over one set of registry contracts.

**"Adapts seamlessly to every environment."**

[![Release](https://img.shields.io/github/v/release/Xxx91n/env-manager)](https://github.com/Xxx91n/env-manager/releases)
[![License](https://img.shields.io/badge/License-Apache--2.0-yellow.svg)](LICENSE)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%2B-brightgreen?logo=windows&logoColor=white)](#install)
[![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)](#prerequisites)
[![Tauri](https://img.shields.io/badge/Tauri-2-24C8D8?logo=tauri&logoColor=white)](#architecture)

[Install](#install) · [Features](#features) · [CLI Reference](docs/cli-commands.md) · [Architecture](#architecture) · [Changelog](CHANGELOG.md)

<!-- README-I18N:START -->
**Languages:** **English** · [简体中文](docs/i18n/README.zh_CN.md) · [日本語](docs/i18n/README.ja.md) · [한국어](docs/i18n/README.ko.md) · [Deutsch](docs/i18n/README.de.md) · [Français](docs/i18n/README.fr.md) · [Español](docs/i18n/README.es.md) · [Português](docs/i18n/README.pt.md) · [Русский](docs/i18n/README.ru.md) · [العربية](docs/i18n/README.ar.md)
<!-- README-I18N:END -->

</div>

## Showcase

### GUI

| | | |
|:-:|:-:|:-:|
| ![Variables view](docs/assets/gallery/01-variables.webp)<br>**Variables** — search, scope filter, per-row actions | ![System scope](docs/assets/gallery/02-variables-system.webp)<br>**System scope** — protected entries locked | ![PATH Editor](docs/assets/gallery/03-path-editor.webp)<br>**PATH Editor** — dead & duplicate detection |
| ![Profiles](docs/assets/gallery/04-profiles.webp)<br>**Profiles** — apply / launch / inherit | ![Audit Ledger](docs/assets/gallery/07-audit.webp)<br>**Audit Ledger** — hash-chained, verifiable | ![Service Manager](docs/assets/gallery/08-service.webp)<br>**Service Manager** — secret mounts |

### CLI

<p align="center">
  <img src="docs/assets/demo.gif" alt="Env Manager CLI demo: agents, path health, and get commands" width="100%">
</p>

| | |
|:-:|:-:|
| ![CLI terminal](docs/assets/gallery/12-cli-terminal.webp)<br>**JSON-first output** — `agents --json` emits the full command spec | The CLI is a first-class surface: 18+ commands, every one scripted and JSON-consumable. The agent manual ships inside the binary — run `env-manager-cli agents` to print it. Read-only commands above use simulated terminal output; regenerate with `node scripts/demo/capture.mjs`. |

## Features

- **Dual front ends** — `env-manager-cli` for scripting and CI, `env-manager.exe` (Tauri 2 + Svelte) for interactive editing. Both route through the same registry contracts.
- **Protected by default** — system variables and PATH entries cannot be deleted or renamed; every write is serialized through three layers (mutex + write lock + verify-before-swap).
- **Profiles** — global profiles apply to the registry; launch profiles inject an isolated env block into one process without touching the registry. Inheritance, conflict preview, reverse-order rollback.
- **8 secret providers** — DPAPI, Credential Manager, SecretStore, HashiCorp Vault, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager. The secret verbs persist only ciphertext envelopes or `mount:<id>` references — the generic `add-var`/`edit-var` gap can still write plaintext to the registry; the full boundary is in [Security](#security) and [docs/plaintext-sinks.md](docs/plaintext-sinks.md).
- **PATH health** — `path health` detects duplicates and dead entries; `--fix` rewrites, `--dry-run` reports.
- **Audit ledger** — append-only, SHA256 hash-chained history with rollback and disaster-recovery export.

## For AI agents

```bash
env-manager-cli agents            # print the embedded agent manual
env-manager-cli agents --path     # file path of the manual (AGENTS.cli.md)
env-manager-cli agents --summary  # one-line machine-readable spec
env-manager-cli agents --json     # full command table as structured JSON
```

- [AGENTS.md](AGENTS.md) — repository-level agent instructions (architecture, hard boundaries, testing).
- [AGENTS.cli.md](AGENTS.cli.md) — ships inside the CLI binary, so an agent can discover the contract at runtime.
- **Capability-scoped surface** — an opt-in `agentCapabilities` whitelist on `secret-providers.json` lets deployments reject parallel set/delete calls from agents.

## Security

> [!WARNING]
> Current builds are **not code-signed**. Windows SmartScreen may show an "unrecognized app" warning on first launch — click "More info" then "Run anyway". We have applied for free open-source code signing via the SignPath Foundation; once approved, all release artifacts (MSI + EXE) will be signed. See [Code signing policy](docs/code-signing-policy.md).

Protected variables and PATH entries are disabled before deletion, with exact registry value-kind verification on restore. Named pipe IPC uses anti-squatting flags and input validation (64 arg max, 32767 char cap, null byte rejection). See [SECURITY.md](SECURITY.md) for vulnerability reporting.

**Threat model.** The adversary we do *not* defend against is any process running inside your own Windows user session — it can already call `profile reveal-secret`, read a launched child's environment block, or catch the transient temp files the providers produce. What the design *does* bound: DPAPI-CurrentUser ciphertext is undecryptable to another user or machine, the secret verbs cannot land a secret in the registry (the generic-verb gap above aside), and the only sinks *meant* to carry a secret value are the designed ones documented in the plaintext-boundary matrix linked below.

**Plaintext boundary.** The full sink-by-sink matrix — where plaintext can exist, for how long, and who cleans it up — lives in [docs/plaintext-sinks.md](docs/plaintext-sinks.md); the Plaintext lifecycle section of `docs/architecture.md` is the authoritative per-surface allowlist. Memory zeroing and log scrubbing are best-effort hygiene, not guarantees; `tests/canary-redaction.Tests.ps1` scans 7 output sinks for a planted canary and the shared contract suite asserts `plaintext-never-in-envelope` on all 8 providers.

## Install

### MSI Installer

Download the MSI from [GitHub Releases](https://github.com/Xxx91n/env-manager/releases) and run it. Creates Start Menu shortcuts automatically. Available in x64, x86, and ARM64.

### Portable

Download the portable ZIP from [GitHub Releases](https://github.com/Xxx91n/env-manager/releases). Extract and run `env-manager.exe` directly. No installation needed.

### CLI-Only

Download the CLI-only ZIP for headless or scripting use: `env-manager-cli.exe` plus `.dll` files. No GUI, no WebView2 dependency.

### Prerequisites

> [!IMPORTANT]
> **Portable** and **CLI-Only** builds are framework-dependent: they require the **.NET 10 Desktop Runtime** on the target machine — download it from the official [.NET download page](https://dotnet.microsoft.com/download/dotnet/10.0). The **MSI installer** does not check for or install .NET — install the runtime yourself before first launch.
>
> **WebView2 Runtime** (for the GUI) is preinstalled on Windows 11 and available for Windows 10 21H2+ from [Microsoft](https://developer.microsoft.com/microsoft-edge/webview/).

For optional external secret-provider tools (SOPS, 1Password CLI, Vault CLI, AWS CLI, Azure CLI, PowerShell 7), see the [Secret Providers Guide](docs/secret-providers-guide.md).

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

Requires .NET 10 SDK, Node.js 18+, Rust stable with MSVC or GNU target. See [docs/build-and-release.md](docs/build-and-release.md) for details.

## Usage

### CLI

```bash
# List / get / set
env-manager-cli.exe list
env-manager-cli.exe get PATH
env-manager-cli.exe set JAVA_HOME "D:\jdk17" --scope system

# Create a Launch profile and launch with isolated env
env-manager-cli.exe profile create dev --type launch --target python.exe
env-manager-cli.exe profile add-secret dev API_KEY "sk-xxx"
env-manager-cli.exe profile launch dev

# PATH health
env-manager-cli.exe path health

# Backup / restore
env-manager-cli.exe backup --output backup.json
env-manager-cli.exe restore backup.json

# Disaster recovery + tamper-evident audit
env-manager-cli.exe export-state --output state.dpapi
env-manager-cli.exe audit verify --strict

# Service control
env-manager-cli.exe service status
```

See [docs/cli-commands.md](docs/cli-commands.md) for the full command reference.

### GUI

Run `env-manager.exe`. The GUI provides a real-time variable list with search, scope filtering, inline edit, a PATH editor with drag-and-drop reordering, profile management, secret provider selection, service control panel, audit history, and 10-language i18n.

## Architecture

```mermaid
flowchart LR
    GUI[Tauri GUI - Svelte] -->|named pipe IPC| SVC[env-manager-service Rust]
    GUI --> CLI[env-manager-cli C#]
    SVC --> CLI
    CLI --> REG[(Registry HKCU/HKLM)]
    CLI --> SEC[Secret Providers]
```

- **CLI**: C# .NET 10 single-file executable — the coordination layer and registry gateway.
- **Service**: standalone Rust binary managing secret mount lifecycle via named pipe IPC.
- **GUI**: Tauri 2 + Svelte 5 frontend using the same IPC contracts.

## Secret providers

8 provider backends with activation preflight — failures surface as inline amber banners directly in the profile editor:

| Provider | Auth Method | Periodic Refresh | Docs |
|---|---|---|---|
| DPAPI CurrentUser | Windows DPAPI | No (per-user) | [Guide](docs/secret-providers-guide.md) |
| Windows Credential Manager | CredMan + DPAPI | No (per-user) | [Guide](docs/secret-providers-guide.md) |
| PowerShell SecretManagement | SecretStore vault | No (CreatedOnly) | [Guide](docs/secret-providers-guide.md) |
| HashiCorp Vault KV v2 | VAULT_TOKEN / AppRole cert | Yes | [Guide](docs/secret-providers-guide.md) |
| SOPS | Age / PGP / KMS | No (CreatedOnly) | [Guide](docs/secret-providers-guide.md) |
| Azure Key Vault | SP cert / managed identity | Yes | [Guide](docs/secret-providers-guide.md) |
| 1Password CLI | OP_SERVICE_ACCOUNT_TOKEN | Yes | [Guide](docs/secret-providers-guide.md) |
| AWS Secrets Manager | SigV4 + access keys | Yes | [Guide](docs/secret-providers-guide.md) |

See [docs/secret-providers-guide.md](docs/secret-providers-guide.md) for per-provider prerequisites, one-time setup, and activation error fix steps.

## Service mode

`env-manager-service.exe` is a standalone Rust binary managing secret mount lifecycle via named pipe IPC:

- **RuntimeMode**: Service (SCM-managed, machine boot), Background (user-launched), Cli (one-shot gateway)
- **Reconcile loop**: 300s periodic full-scan, idempotent per-item handler, 30s first-tick delay
- **Audit ledger**: append-only hash-chained `audit-ledger.jsonl` with 100MB rotation and tamper detection
- **IPC**: anti-squatting pipe flag, 65536-byte request cap, newline-delimited JSON protocol
- **Watchdog**: two-layer recovery — SCM auto-restart (Service mode) + GUI 30s ping watchdog (Background mode)

## Documentation

| Document | Content |
|---|---|
| [CHANGELOG.md](CHANGELOG.md) | Version history (keepachangelog format) |
| [docs/cli-commands.md](docs/cli-commands.md) | Full CLI command reference |
| [docs/architecture.md](docs/architecture.md) | Deep architecture |
| [docs/plaintext-sinks.md](docs/plaintext-sinks.md) | Where secret plaintext can exist |
| [docs/backup-and-profiles.md](docs/backup-and-profiles.md) | Backup, restore, profile semantics |
| [docs/secret-providers-guide.md](docs/secret-providers-guide.md) | Secret provider setup |
| [docs/build-and-release.md](docs/build-and-release.md) | Build and release process |
| [AGENTS.md](AGENTS.md) / [AGENTS.cli.md](AGENTS.cli.md) | Agent-facing guides |

## Maintainers

[@Xxx91n](https://github.com/Xxx91n)

## Contributing

Issues and PRs welcome. Read [AGENTS.md](AGENTS.md) first for architecture boundaries and the test policy.

## License

Apache-2.0 © 2026 Env Manager Contributors. See [LICENSE](LICENSE).
