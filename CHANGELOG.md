# Changelog

All notable changes to Env Manager will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.10.0](https://github.com/Xxx91n/env-manager/compare/v0.9.30...v0.10.0) (2026-09-05)


### Features

* **brand:** add animated hero GIF + demo GIF + hero motion spec ([61ee5a2](https://github.com/Xxx91n/env-manager/commit/61ee5a2c5943a5c6d49dc509ac6f5347901a63db))
* **ci:** isolate CI user-state via ENVMANAGER_LOCALAPPDATA redirect seam (issue 24) ([c6d4dc8](https://github.com/Xxx91n/env-manager/commit/c6d4dc8ef8f46465ddc398f0993695dea426606f))
* **preflight:** two-tier profile validation with warn tier, --strict, and exit code 2 (issue 19) ([58072a0](https://github.com/Xxx91n/env-manager/commit/58072a08101ddaa0ca1f254317cc7c48db3fa284))
* **scripts:** add check-readme-i18n.ps1 and record-demo.py helper scripts ([528d651](https://github.com/Xxx91n/env-manager/commit/528d651455938a02aca5d05bd6d013ec53a760f7))
* **scripts:** add rsvg-convert wrapper scripts (JS + cmd) using @resvg/resvg-js ([c7d7750](https://github.com/Xxx91n/env-manager/commit/c7d7750ee140a71712d0ff11a8c66091a74fc07e))


### Bug Fixes

* **ci:** add missing cargo test --locked steps to build.yml verify job ([afa2ae3](https://github.com/Xxx91n/env-manager/commit/afa2ae3a6613e9a5c4d1f51275f2086acc7858ee))
* **ci:** compare PATH semantically (entry list, case-sensitive) instead of byte-exact - CLI normalizes empty segments on write, CI image PATH is dirty by construction ([152a951](https://github.com/Xxx91n/env-manager/commit/152a951cd5661a18b5cd9ee9506e22f1cacc8b4d))
* **ci:** doc-sync expects docs/i18n/README.zh_CN.md (README_CN.md superseded by i18n suite); local AGENTS.md has no stale refs ([45a5ba3](https://github.com/Xxx91n/env-manager/commit/45a5ba3bb15d615729d8eb07cdc46f8a1add6598))
* **ci:** drop unused HITS var in wix-watchdog (actionlint SC2034 - HITS computed but never read) ([cad1905](https://github.com/Xxx91n/env-manager/commit/cad19056764ded4174aa784ef2761bdef6424bc6))
* **ci:** MSI smoke probes installed CLI with bare args (build.mjs version-probe protocol); CLI has no --version flag ([fc53ad2](https://github.com/Xxx91n/env-manager/commit/fc53ad23ab2bfd8fa6525eb1bd29db9e1396daf6))
* **ci:** package job installs WiX 3.14 to %LOCALAPPDATA%\tauri\WixTools314 (windows-2025 image no longer bundles it); build.mjs copies loose exes beside ZIPs to match release layout contract ([d082a9c](https://github.com/Xxx91n/env-manager/commit/d082a9c78aebb6b5dc732e98388280a06847ae8c))
* **ci:** pass -CliExe pointing to Release binary so test-with-restore.ps1 finds CLI on CI (was defaulting to release/cli-only which CI never builds) ([0d3cb9c](https://github.com/Xxx91n/env-manager/commit/0d3cb9c5cf21201f402dc7590cee52a197bbea8c))
* **ci:** PATH snapshot compares entries semantically (case+order), folding empty-segment noise the CLI normalizes on write on dirty CI images ([516dcbf](https://github.com/Xxx91n/env-manager/commit/516dcbf322b0810de1a8cd65066482fd8a9b5b6a))
* **ci:** pin WIX env to extracted WixTools314 path (windows-2025 image preset WIX shadows it with a removed dir) ([c0b9e69](https://github.com/Xxx91n/env-manager/commit/c0b9e69dfac07b2808260ccba9bf7392251f39fe))
* **ci:** replace rustsec/audit-check (v2.0.0 'Unexpected end of JSON input' bug) with taiki-e/install-action + cargo audit on both workspaces ([40ac008](https://github.com/Xxx91n/env-manager/commit/40ac008dbd39c8767fe0660fa7adde9848946976))
* **ci:** restore all workflow failures - build.yml YAML corruption, mirror action input rename, release-please dead SHA ([e8ad626](https://github.com/Xxx91n/env-manager/commit/e8ad626201d0c3b03bf5ed92b75cc6f9c605cc01))
* **ci:** run root npm ci in package job so scripts/build.mjs finds archiver (ERR_MODULE_NOT_FOUND) ([5cfb7a9](https://github.com/Xxx91n/env-manager/commit/5cfb7a90da81bfd4af20116096d8db44753a9cf0))
* **ci:** seed frontendDist placeholder for generate_context! in verify (issue 17) ([96abc6b](https://github.com/Xxx91n/env-manager/commit/96abc6bcc5aaaf473b11279fc9e4cc3616ec37c5))
* **deps:** archiver v8 is ESM with named exports - use ZipArchive class (fixes 'does not provide an export named default' in package job) ([ff63983](https://github.com/Xxx91n/env-manager/commit/ff63983bfce7854f54fd7f3d5d2a4525175bc921))
* **deps:** regenerate lock with npm 10 (CI Node 20=npm 10; npm 11-generated lock failed npm 10 ci peer validation on esbuild@0.28.2) ([e05e6a4](https://github.com/Xxx91n/env-manager/commit/e05e6a41b573d6f940045f10c20e9ea53f606ef0))
* **deps:** revert bits-ui to 0.22.0 (v2 peer-requires Svelte 5; project on Svelte 4 chain) ([b114ed4](https://github.com/Xxx91n/env-manager/commit/b114ed481bd847bb116be8e4de56401fddf2e6c3))
* **engine:** route test-with-restore launch target away from System32 and sync docs after enforced guard (issue 04 B1) ([828af15](https://github.com/Xxx91n/env-manager/commit/828af152b5196e34a4b73fd13baa252b0a99e153))
* **frontend:** regenerate package-lock.json against registry.npmjs.org; pin registry via .npmrc (npmmirror cache caused version-graph mismatch breaking npm ci in CI) ([b079c39](https://github.com/Xxx91n/env-manager/commit/b079c39cea5de17a0154476405c0bac14c346f3f))
* **frontend:** sync package-lock.json with package.json (npm ci EUSAGE in CI) ([4a9f43a](https://github.com/Xxx91n/env-manager/commit/4a9f43a245793287a4c5f265bb91d7fb29767f5d))
* **l1:** harness PutSecretValue ClientRequestToken; vault bounded health poll replaces query-string wait (issue 15) ([61891ad](https://github.com/Xxx91n/env-manager/commit/61891ad520cdeffcfe16523e7a1c9cb14875ab72))
* **l1:** SigV4 ClientRequestToken idempotency token; vault dev-server -dev command (issue 15) ([112dd0d](https://github.com/Xxx91n/env-manager/commit/112dd0d93d9b2e084de33dea7835f5759dc441f4))
* **l1:** wire Azure vault URI env, SigV4 header validation bypass, SecretStore round-trip gate, serialize container fixtures; record 1password linux stack overflow (issue 15) ([a478b9d](https://github.com/Xxx91n/env-manager/commit/a478b9dbd7aec46d6d08c60cdf6317c30810c21c))
* **preflight:** make %VAR% defined-check hermetic via process env (issue 19 rework 2) ([b64b6fd](https://github.com/Xxx91n/env-manager/commit/b64b6fdfdcfae055bf160a1d02796d15bf8b8c52))
* **profile:** recognize --help/-h/-?//? in profile create name position as help request (issue 20) ([4f6f7db](https://github.com/Xxx91n/env-manager/commit/4f6f7dba20ce93eaa9557e2c6832ea138317ca40))
* **test:** preserve CLI stderr on failure and stamp round-trip name (issue 22+24) ([3890e6a](https://github.com/Xxx91n/env-manager/commit/3890e6a01551860b898bc77150e527c6f0fdfb12))
* **test:** replace hardcoded D:/Aworker absolute paths with repo-relative resolve() in secret tests (CI runner has no D:/Aworker) ([fc8ada3](https://github.com/Xxx91n/env-manager/commit/fc8ada3ba3be814ea0f2bd20729e578d00ece10e))
* **test:** use unique fixture path C:\EM_TEST_TRAILING_BACKSLASH\ in trailing-backslash case (PS7 install dir may preexist in CI runner user PATH, breaking snapshot equality) ([1c6ef82](https://github.com/Xxx91n/env-manager/commit/1c6ef82e37906df461a92ffdb2d25df51ad4cadf))

## [Unreleased]

### Added
- CI hardening (P0): actionlint gate, dependency-review on PRs, Dependabot for GitHub Actions/cargo/npm, `timeout-minutes` on all jobs, `Swatinem/rust-cache` in verify + package jobs
- CI hardening (P1): RustSec audit (src-tauri + service), NuGet vulnerability audit, MSI quiet install/run/uninstall smoke test, release version-consistency gate (tag vs csproj vs tauri.conf vs package.json)

### Fixed
- CI: replace `rustsec/audit-check` (v2.0.0 JSON bug) with `taiki-e/install-action` + `cargo audit`
- CI: MSI smoke probes installed CLI with bare args per build.mjs version-probe protocol
- Tests: replace hardcoded `D:/Aworker` paths with repo-relative resolve; fix trailing-backslash PATH round-trip in snapshot tests
- Frontend: regenerate `package-lock.json` against registry.npmjs.org (npmmirror cache broke `npm ci` in CI)
- Deps: archiver v8 (ESM named exports — use `ZipArchive` class)

## [0.9.30] - 2026-08-27

### Added
- MSI: uninstall shortcut next to the EXE; GUI logs externalized to `%LOCALAPPDATA%\EnvManager\logs`; clean uninstall removes the entire install dir (ADR 0012)
- Brand icons applied to exe, MSI, and installer
- SignPath signing prep: code-signing policy page and artifact metadata alignment

### Fixed
- Release published to GitHub with MSI, portable zip, CLI-only zip, and `SHA256SUMS.txt`

## [0.9.29] - 2026-08-23

### Added
- MSI UI: custom install directory (`WixUI_InstallDir`), `ARPPRODUCTICON`, optional desktop shortcut property (ADR 0011)

## [0.9.28] - 2026-08-23

### Added
- MSI hygiene: per-component `RemoveFile` residue wipe, pinned GUIDs on the three binaries, WiX EOL supply-chain watchdog (ADR 0010)

## [0.9.27] - 2026-08-22

### Fixed
- MSI: replace `sc.exe` custom actions with WiX `util:ServiceConfig` for service stop/config (ADR 0009)

## [0.9.26] - 2026-08-19

### Fixed
- GUI: 5 bugs — path move-down guard, dead-entries source column, z-index layering, radio color-scheme, input focus outline

### Added
- ADR 0006: GUI z-index layering contract
- ADR 0007: Form control color-scheme override

## [0.9.25] - 2026-08-17

### Fixed
- GUI: notes reactive store + WAI-ARIA tooltip, path move new-array reference + focus/highlight, dead-entries reactive disabled state, dark destructive red-400 alignment

## [0.9.24] - 2026-08-16

### Fixed
- GUI: 5 bugs — CSS-only tab indicator, dark mode flash fix, color-scheme, destructive contrast, titlebar sticky

### Added
- 5 new hard boundaries; supersede stale v0.9.20 indicator boundary

## [0.9.23] - 2026-08-16

### Fixed
- GUI: 5 bugs — titlebar bottom truncation, notes async reactivity, path move index, dark mode white flash, dark destructive red

## [0.9.22] - 2026-08-15

### Fixed
- GUI: titlebar fixed + smooth scroll + dark mode toggle + font slider + notes fix + path move bidirectional
- i18n: add missing settings.lightMode to all 10 locales

## [0.9.21] - 2026-08-15

### Added
- UI-AUDIT.md v0.9.21 audit round — 6-color theme + custom titlebar + CSS containment

### Fixed
- themeStyle allow-list was stale — blocked non-slate themes on restart
- titlebar button tooltips + dead i18n key cleanup

## [0.9.20] - 2026-08-15

### Added
- UI design system — dual-axis theme tokens + lucide icons + 275 dark: class migration

### Fixed
- 5 UI bugs (theme style toggle/i18n/layout/jank/indicator) + regression tests
- dedup CSS classes, tokenize 180 residual gray/hardcoded colors

## [0.9.19] - 2026-08-14

### Added
- SWR caching + listProfiles cache + Tab preload + ProtectionPage concurrent

### Fixed
- tab indicator uses transform:translateX instead of left, rAF instead of setTimeout

## [0.9.18] - 2026-08-14

### Fixed
- service rotate CREATE_NO_WINDOW + refresh timestamp format + WebView2 context menu disabled

## [0.9.17] - 2026-08-14

### Fixed
- ProfilePage pathScopes badge + PathEditor reverse index + IPC mount_id snake_case

## [0.9.16] - 2026-08-14

### Added
- ProfileShow source-aware output + path scope badge + ServicePage i18n

## [0.9.14] - 2026-08-13

### Added
- InputDialog replaces window.prompt + sticky-note annotations + Material 3 ARIA tablist

### Fixed
- ProfilePage addVar inputs, note hover preview, tab indicator initial width
- add-var inputs use bind:value (Svelte 4 two-way binding) + tab indicator transition suppression
- i18n locale loader uses static import map instead of runtime path concat
- tauri.conf.json version sync to match csproj/package.json

### Added
- AGENTS.md hard boundary: PATH scope-aware apply/unapply

## [0.9.13] - 2026-08-12

### Added
- Process security hardening: process_guard.rs (crash dialogs, binary hash verify, pagefile protection, process mitigations, module enumeration, authenticode stub)
- 12 unit tests in process_guard::tests

## [0.9.12] - 2026-08-08

### Added
- GitHub release readiness Phase 1 — community health files + README rewrite + CSP hardening + Named Pipe DACL hardening
- Sensitive data redaction architecture — 22-pattern unified scrubbing across CLI/GUI/service tiers

### Fixed
- i18n settings.drTitle path mismatch + .NET 10 runtime fencing
- embed .NET 10 runtime detection in Tauri Rust shell

## [0.9.11] - 2026-08-07

### Fixed
- audit security hardening — atomic writes, path fix, transactional import, lock classification

### Added
- Final alignment — docs sync, security audit, CLI command ref update

## [0.9.10] - 2026-08-07

### Added
- Phase E audit ledger unification — migrate, verify, export, recover

## [0.9.9] - 2026-08-07

### Added
- Versioned schema migration + full-state export/import + GUI DR entry

## [0.9.8] - 2026-08-07

### Added
- Industrial logging backend — tracing+tracing-appender, request_id, service status enum

## [0.9.7] - 2026-08-07

### Fixed
- Service probe fast-fail — 18s to 2s when service down

## [0.9.6] - 2026-08-06

### Added
- ADR 0002: Service Watchdog & Heartbeat (two-layer watchdog: SCM recovery + GUI 30s ping)
- ADR 0003: Version Single Source & CHANGELOG (build.mjs version sync)
- Grill plan: Industrial lifecycle control + pipeline control + package management + doc alignment
- CONTEXT.md: Service Lifecycle domain terms (Watchdog, Heartbeat, SCM Recovery, IPC Connection Health)
- CLI pipe connect retry (3x with 1s backoff) — prevents transient pipe-busy false negatives
- Service ping heartbeat enrichment (uptime_seconds, reconcile_last_run_at, mount health summary)

### Changed
- build.mjs: reads <Version> from env-manager.csproj, syncs to frontend/package.json before every build

### Fixed
- watchdog activation + doc-sync regex + build.yml EOF
- service read/write lock classification — fix GUI page block

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
