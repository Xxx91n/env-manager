# Changelog

All notable changes to Env Manager will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.13.0](https://github.com/Xxx91n/env-manager/compare/v0.12.1...v0.13.0) (2026-10-27)


### Features

* **installer:** add a desktop shortcut check box to the MSI wizard ([#136](https://github.com/Xxx91n/env-manager/issues/136)) ([59fad51](https://github.com/Xxx91n/env-manager/commit/59fad519fb52f51bc6f539a36cbcabb2ff6a7644))


### Bug Fixes

* **build:** 补回删裸号时留下的孤儿冒号 ([c9c2ee8](https://github.com/Xxx91n/env-manager/commit/c9c2ee824d55fa488ed9de1f9e8182b95759cc14))
* **ci:** dependabot 安全更新组补 patterns 以过线上 schema 校验 ([#115](https://github.com/Xxx91n/env-manager/issues/115)) ([64df60a](https://github.com/Xxx91n/env-manager/commit/64df60a7605585d8a5f94cb3619236ee8feed49c))
* **ci:** npm audit 门禁按 OpenVEX 豁免口径放行 ([ef64d7f](https://github.com/Xxx91n/env-manager/commit/ef64d7f8081900d21a4e73bd16e8cab901f1fa1d))
* **ci:** npm audit 门禁按 VEX 口径放行（[#105](https://github.com/Xxx91n/env-manager/issues/105)） ([61c7465](https://github.com/Xxx91n/env-manager/commit/61c7465af9e37032205cdd23794cab99fc8b6d33))
* **ci:** release-please 的 tag 自动触发资产构建 ([deba936](https://github.com/Xxx91n/env-manager/commit/deba93653bfa3b6531149a72240387fdff7f65c0))
* **ci:** release-please 的 tag 自动触发资产构建 ([63c8f83](https://github.com/Xxx91n/env-manager/commit/63c8f83ac5ebc73efd2aacc226afb36925706e50))
* **ci:** 修好从来无法解析的 dependabot 自动合并工作流 ([1de9c58](https://github.com/Xxx91n/env-manager/commit/1de9c584763468b4eb755342a79438df7cfdd12c))
* **ci:** 修正 build.yml stryker job 过时的'必红'注释（[#102](https://github.com/Xxx91n/env-manager/issues/102)） ([f1bbb78](https://github.com/Xxx91n/env-manager/commit/f1bbb78b28940df129f09c39cbc360b813fa2d4e))
* **ci:** 修正 build.yml stryker job 过时的'必红'注释（[#102](https://github.com/Xxx91n/env-manager/issues/102)） ([a9eb697](https://github.com/Xxx91n/env-manager/commit/a9eb6972ad0c344f1c4b0990fff6ec1de88f9ac2))
* **ci:** 裸号扫描兼容两段版本号，并加可失败的自检门禁 ([9e39ef5](https://github.com/Xxx91n/env-manager/commit/9e39ef569dbf5dd9d3541a4ca6325d7bd6d8813a))
* **ci:** 裸号门禁认得复数形态，并清掉它一直看不见的四处残留 ([9530e59](https://github.com/Xxx91n/env-manager/commit/9530e596164c03ccc20ef6c2945dc9b5e392c7cc))
* **ci:** 让 dependabot 自动合并门真正轮询并合入 minor/patch ([e70f3b0](https://github.com/Xxx91n/env-manager/commit/e70f3b0c58eec3de309e63a7896e43498e70f60d))
* **release:** latest.json URL 指向 GitHub 归一后的资产名 ([4857c8f](https://github.com/Xxx91n/env-manager/commit/4857c8f55a04e99d3ea5e2fcbb7fb2f0820e300e))
* **release:** latest.json URL 指向 GitHub 归一后的资产名 ([aaf6507](https://github.com/Xxx91n/env-manager/commit/aaf6507d102ff1bdab200e0349f54d768390210d))
* **release:** 用 config 文件同步三个版本载体 ([2387748](https://github.com/Xxx91n/env-manager/commit/238774830405f079f4919239794afdba6dcabce7))
* **review:** 落实 CodeRabbit 的五条修正 ([9b88c01](https://github.com/Xxx91n/env-manager/commit/9b88c0100cd15f5c8b62b26b6c3cdb4e5e1c4dcb))
* **secrets:** 探测预算与真实调用对齐，超时不再冒充“二进制不可用” ([cc9eeba](https://github.com/Xxx91n/env-manager/commit/cc9eebae0e219493d376d3514d3d2aa419958591))

## [0.12.1](https://github.com/Xxx91n/env-manager/compare/v0.12.0...v0.12.1) (2026-09-14)


### Bug Fixes

* **ci:** collect stryker evidence even when break threshold trips (ticket 50) ([f736bc7](https://github.com/Xxx91n/env-manager/commit/f736bc7fda6dab211e0e421260bb4d2a62127e32))
* **ci:** early-exit non-Dependabot PRs and bound required-check wait in auto-merge gate (ticket 48) ([2893cf4](https://github.com/Xxx91n/env-manager/commit/2893cf4fce974676053357a9018ee9b5fe8387a1))
* **ci:** resolve service exe under cargo build.target triple dir (ticket 49) ([8c5940c](https://github.com/Xxx91n/env-manager/commit/8c5940ca72ebb91962549a1fe116f0a8f65d8cb2))
* **ci:** scope portable/cli-only artifacts to their zips - loose runtime files leaked into v0.12.0 release assets (review finding) ([a97b451](https://github.com/Xxx91n/env-manager/commit/a97b451c47a81039240b3323800f4df9babf66e1))

## [Unreleased]

### Fixed
- sops 明文临时目录由服务拥有并回收：停服务打断 rotate 后，服务在被终止的 helper 释放句柄的窗口内删除整棵临时目录树（有限重试），不再无声遗留 `env-manager-sops-*`；回退与删除失败均留痕（#177）

## [0.12.0] - 2026-09-09

Release-infrastructure milestone (architecture-recovery Round 7, tickets 44-45): first release cut from the tag-driven CI pipeline, with release-please taking over subsequent version bumps.

### Added
- Release infrastructure (ticket 44): tag-triggered `build.yml` release job — updater triple (`.msi` + `.msi.zip` + `.msi.zip.sig`), `latest.json` assembled by `scripts/gen-latest-json.mjs` (fail-closed on missing signature), SLSA L2 build provenance attestation
- Tauri updater wiring (ticket 37): `tauri-plugin-updater` with silent update check and in-app download-and-install
- Release automation takeover (ticket 45): `release-please.yml` manifest config — plain `vX.Y.Z` tags (`include-component-in-tag: false`), `.release-please-manifest.json` pinning the released version, `extra-files` syncing the version carriers checked by the release job's version gate; legacy manual `release.yml` retired
- v0.12.0 tag + GitHub Release with the four updater asset classes (.msi, .msi.zip, .msi.zip.sig, latest.json)
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
