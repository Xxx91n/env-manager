# Changelog

All notable changes to Env Manager will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added
- ADR 0002: Service Watchdog & Heartbeat (two-layer watchdog: SCM recovery + GUI 30s ping)
- ADR 0003: Version Single Source & CHANGELOG (build.mjs version sync)
- Grill plan: Industrial lifecycle control + pipeline control + package management + doc alignment
- CONTEXT.md: Service Lifecycle domain terms (Watchdog, Heartbeat, SCM Recovery, IPC Connection Health)
- CLI pipe connect retry (3x with 1s backoff) — prevents transient pipe-busy false negatives
- Service ping heartbeat enrichment (uptime_seconds, reconcile_last_run_at, mount health summary)

### Changed
- build.mjs: reads <Version> from env-manager.csproj, syncs to frontend/package.json before every build

## [0.9.5] - 2026-08-02

### Added
- Phase D: cert_bootstrap.rs — Vault AppRole/client cert, Azure SP cert bootstrap
- Service: cert_bootstrap eliminates VAULT_TOKEN and AZURE_CLIENT_SECRET via short-lived token exchange

## [0.9.3] - 2026-08-05

### Fixed
- GUI exit (tray quit / ExitRequested) no longer kills background service
- stop_service Tauri command is the only path that kills the service process

## [0.9.2] - 2026-08-04

### Fixed
- Service CancellationToken + CLI pipe fix
- Log alignment: service logs to env-manager-service.log, frontend to env-manager.log
- Atomic write for gui-settings.json (write_atomic: temp + fsync + rename)
- Process leak: SERVICE_PID tracking + kill+wait anti-zombie
- Tray i18n: pre-load locale messages before localeStore.set to fix English fallback on tray menu
- HistoryPage column resize: rAF-based smooth drag

### Added
- AuditPage.svelte: audit ledger panel moved from Settings to main navigation
- Service manager page in GUI (Start/Stop/Ping/Reload)
- 10-locale i18n for service and audit keys

## [0.9.0] - 2026-08-02

### Added
- Phase B+C: env-manager-service.exe Rust binary (service crate)
- Named pipe IPC (\\.\pipe\EnvManager.Service / .Background)
- CLI service subcommand (thin IPC gateway)
- Reconcile loop: tokio interval 300s periodic full-scan
- WiX installer: ServiceInstall + ServiceConfig DelayedAutoStart
- SecretMount schema v2 (separate secretMount.json file)
- One-shot migration from inline envelopes to mount references

## [0.8.0] - 2026-07-26

### Added
- Phase A: SecretMount schema v2 + one-shot migration
- C# AtomicWriteProfiles with fsync (Flush(true) before File.Move)
- New SecretMount fields: refreshPolicy, refreshIntervalSeconds, bootstrapCertThumbprint

## [0.7.15] - 2026-07-25

### Added
- Build-time CLI version verification (build.mjs probes spawned CLI matches csproj version)
- findCliOutput() prefers net10.0-windows directory

### Fixed
- Stale v0.3.0 CLI binary deployment (lacked path/profile/history/protection commands)

## [0.7.14] - 2026-07-24

### Added
- Cross-platform build orchestrator (scripts/build.mjs) — Windows/Linux/macOS, no hardcoded paths
- Multi-architecture support: x64, x86, arm64
- CI release workflow: workflow_dispatch only (manual trigger, no auto-trigger)
- ZIP artifacts: Env-Manager_{portable|cli-only}_version_arch.zip

## [0.7.13] - 2026-07-23

### Fixed
- history delete classified as WRITE (was READ) — prevents concurrent audit.json mutation race

## [0.7.12] - 2026-07-22

### Added
- pwsh async pipe drain (BeginOutputReadLine/BeginErrorReadLine before WaitForExit) — fixes deadlock
- Locale pre-await + tray i18n race fix (loadLocaleMessages before localeStore.set)
- Staged PATH move + Apply (accumulate local, commit via Apply button)
- Secret reveal / provider-set audit records
- Toggle switch right-bias visual fix
- addCliToPath fail-fast 30s timeout

### Fixed
- Named pipe deadlock: pwsh blocks writing to full pipe while we wait for exit
- Tray menu showing English on Chinese startup

## [0.7.11] - 2026-07-21

### Fixed
- PowerShell SecretManagement canonical module name (Microsoft.PowerShell.SecretStore)
- SOPS env var SOPS_AGE_RECIPIENTS (plural) + no-BOM file write
- AWS SigV4 request headers on HttpRequestMessage (not HttpContent)
- Rust non-zero exit stderr_hint log (scrub secrets, 512 char bound)

## [0.7.10] - 2026-07-20

### Added
- Secret provider errors shown inline (amber banner under provider select)
- i18n locale race eliminated (await applyPersistedLocale before App mount)

## [0.7.9] - 2026-07-19

### Fixed
- setupI18n MUST NOT read localStorage on startup (portable Tauri localStorage unreliable)
- applyPersistedLocale empty-durable stays at default, never seeds from localStorage
- write_gui_setting atomic + fsync + rename (prevents torn writes)
- frontend_log IPC for diagnostic tracing

## [0.7.8] - 2026-07-18

### Added
- Secret provider activation error i18n keys (sopsConfig, opAccounts)
- Secret provider activation error inline display
- Edge-style true-overlay scrollbar (no layout-width track)

### Fixed
- Locale persistence startup read-order (no sync seed before durable IPC read)

## [0.7.7] - 2026-07-17

### Added
- Single-instance GUI (tauri-plugin-single-instance)
- Settings persistence via Rust IPC (read_gui_setting / write_gui_setting)
- History action column reactive $t (pass $t as argument, not close over $store)
- Launch badge i18n (profiles.typeLaunch key)
- Inheritance chain secret propagation (CollectInheritedSecrets)
- Global-inherits-Launch topology rejection (at set time, not just apply time)
- Live CLI inheritance-protection integration test

### Fixed
- Dark mode and font scale not read back on startup (onMount now reads from localStorage)
- Settings Dialog writing to localStorage but App never reading back

## [0.7.6] - 2026-07-16

### Added
- Secret provider activation error i18n (localizeError with provider-specific keys)
- History operation label i18n call-site fix (entry.command, not split)

### Fixed
- HistoryPage falling back to English on locale switch ($t inside function body not reactive)

## [0.7.5] - 2026-07-15

### Added
- PowerShell SecretManagement preflight (EnsureSecretManagementAvailable + EnsureVaultRegistered)
- CLIXML stderr stripper
- Secret provider activation preflight (probe before commit)
- Non-launch profile secret rejection (CLI side)
- Inheritance cycle / self-inheritance rejection
- Profile type filter (GUI segmented control)
- Add-var / add-path GUI input validation
- History page operation label full-command-first lookup
- GUI DPI-aware glyph rendering

## [0.7.4] - 2026-07-14

### Added
- Provider-change confirmation modal (prevent silent re-encryption-provider swap)
- PowerShell -EncodedCommand (eliminate shell quoting)
- Regular/secret variable display split in ProfilePage
- Global profiles cannot hold secrets (decision)
- Launch-target / Vault CLI error i18n (localizeError)
- Microsoft YaHei UI font (CJK Windows)
- HistoryPage column-resize highlight fix (col-resizing class pin)
- Clone-from-existing combobox (self-rendered, not native select)

## [0.7.3] - 2026-07-13

### Added
- 1Password provider (op binary, CREATE_NO_WINDOW, 30s timeout)
- AWS Secrets Manager provider (SigV4 signed requests)
- GUI Secret Provider Selector (dynamic from CLI output, no hardcoded list)

## [0.7.2] - 2026-07-12

### Added
- SOPS provider (age/PGP/AWS KMS/Azure/GCP/Vault decryptors)
- Azure Key Vault provider (REST API, managed identity / SP auth)

## [0.7.1] - 2026-07-11

### Added
- Launch profiles + DPAPI secrets (profile launch spawns target with env_clear + inject)
- Profile variable/PATH scope selector (--scope user|system)
- Profile creation with --type launch --target <exe>

### Fixed
- CommonProgramW6432 protection gap (was missing from protection.defaults.json)

## [0.7.0] - 2026-07-10

### Added
- DPAPI-CurrentUser encryption for secret values on disk
- Secrets never applied to registry (IsProfileApplicable rejects secret-bearing profiles)
- Profile audit history
