# Env Manager
> **Internal development process record — not user documentation.**
> Architectural decisions are distilled into `docs/adr/`. Domain glossary
> terms in `## Language` are canonical vocabulary; other sections are
> session records.

Env Manager is a Windows environment variable manager with a CLI backend (C# .NET 10), a Tauri/Rust shell, and a Svelte 4 frontend. This CONTEXT.md captures the domain language specific to the secrets architecture, resolved during the grill-with-docs session on the Phase A–E roadmap (docs/secret-architecture-blueprint.md).

## Language

**Secret Envelope**:
The on-disk JSON container written into a profile's `secretVariables` array. Fields: `provider`, `version`, `createdAt`, `ciphertext` (DPAPI base64 blob), `targetName` (CredMan target / Vault path / cloud secret ID). Schema v1.
_Avoid_: secret blob, encrypted value, secret record

**Secret Mount**:
The Phase A v0.8.0 schema v2 unit. Lives in `secretMount.json`, referenced by id from `profiles.secretMountRefs`. Adds `refreshPolicy`, `refreshIntervalSeconds`, `lastRotatedAt`, `expiresAt` to the envelope fields. Replaces the direct `secretVariables` array on profiles.
_Avoid_: mount point, secret reference, secret link

**SecretStore**:
The Phase B v0.8.5 controller class behind `ISecretProvider`. Owns provider registry, mount metadata, health surface. Replaces direct `SecretProviderManager` routing for mount-aware operations.
_Avoid_: secret manager, provider router, secret backend

**Launch Profile**:
A profile whose variables are injected into a child process via `env_clear + env(k,v)`. Never written to registry, never broadcasts `WM_SETTINGCHANGE`. The only profile type that may carry secrets.
_Avoid_: local profile (ambiguous — "local" means local-scope PATH entry too), child profile

**Global Profile**:
A profile whose variables are applied to HKCU/HKLM registry on `profile apply`. Cannot carry secrets (IsProfileApplicable rejects). May inherit from other Global profiles.
_Avoid_: system profile, applied profile

**Provider**:
An `ISecretProvider` implementation (8 total: dpapi-current-user, credential-manager, powershell-secretmanagement, vault-kv2, sops, azure-keyvault, 1password, aws-secretsmanager). Five-method interface: Name/Encrypt/Decrypt/CanRotate/Rotate/Delete. Stays as-is through all phases.
_Avoid_: backend, vault (ambiguous with HashiCorp Vault), store

**Reconcile Loop**:
The Phase C v0.9.0 optional `secrets-agent.exe` background process that refreshes `Periodic` mounts on interval. Default off. Only `CanRotate=true` providers get a lease concept.
_Avoid_: refresh daemon, sync loop, agent process

**Capability-Scoped Agentic Surface**:
The Phase B v0.8.5 `agentCapabilities` whitelist on `secret-providers.json`. Default empty = all-allowed (today's behaviour). Opt-in deployments can reject parallel set/delete calls from LLM agents.
_Avoid_: agent gate, capability filter, agent whitelist

## Decisions

> **Process record — decisions are sedimented into ADRs.**
> See [docs/adr/](docs/adr/) for accepted architecture decisions.
> The original design review session decisions (A1-A11) and risk matrix
> are archived in [docs/history/design-review-research.md](docs/history/design-review-research.md).

Key decisions: [ADR 0001](docs/adr/0001-secret-architecture-revision.md) (Secret Architecture Revision)
covers Phase A-E trade-offs (A5/A7/A8/A9 revised). ADR 0002-0007 cover
service watchdog, version single-source, release readiness, sensitive data
redaction, GUI z-index layering, and form control color-scheme override. ADR 0008 covers
public release and mirror topologies. [ADR 0009](docs/adr/0009-stay-on-tauri-bundled-wix-v3.md)
covers packaging: stay on Tauri-bundled WiX v3.14.1 and repair installer.wxs semantics
(MajorUpgrade schedule + util:ServiceConfig + WIX_UPGRADE_DETECTED service stop).
[ADR 0010](docs/adr/0010-msi-hygiene-guid-pinning-eol-test-pyramid.md) covers MSI hygiene:
per-component RemoveFile residue wipe, pinned GUIDs on the three binaries, WiX EOL supply-chain
watchdog, line-ending normalization chore, and the GUI test pyramid (Vitest + WDIO + MSI matrix).
[ADR 0011](docs/adr/0011-msi-ui-wixui-installdir-desktop-shortcut.md) covers MSI UI:
WixUI_InstallDir with en-US localization, optional desktop shortcut, ARPPRODUCTICON,
MSI string-gate tests, and ICE suppression list. Cherry-picked from f073733.


**ADR-0011 Decision 2 follow-up ponytail debt (added 2026-08-23 maintenance pass):** the InstallDir-dialog desktop-shortcut **checkbox UI** is deliberately not implemented in the v0.9.29 MSI — only the `INSTALLDESKTOPSHORTCUT` public property + component Condition ship. Rationale: minimizing WixUI Extension surface (frozen WiX v3.14.1, EOL); a dialog fork is a high-risk change touching localized UI strings. If GUI surfacing becomes a requirement, implement it as a vendored `WixUI_InstallDir` fragment copy with renamed dialog IDs (e.g. `EnvMgrInstallDirDlg`) and a `<UI>` element in `frontend/scripts/`, never by mutating `WixUIExtension.dll` state.
watchdog covers WixUI_InstallDir localization asset availability.

[ADR 0010 amendment (issue 04)](docs/adr/0010-msi-hygiene-guid-pinning-eol-test-pyramid.md) extends Decision 6: the test pyramid now covers the C# engine via the IEnvironmentScope seam (xUnit lane against InMemoryScope; registry adapter stays apex-smoke-only).

[ADR 0012](docs/adr/0012-msi-uninstall-shortcut-logs-localappdata.md) covers MSI lifecycle: in-INSTALLDIR uninstall shortcut, log directory externalization to LocalAppData, legacy INSTALLDIR logs backstop, and 14-day log retention sweep.

[ADR 0013](docs/adr/0013-resvg-js-for-svg-rendering.md) covers README asset rendering: use @resvg/resvg-js as committed npm devDependency instead of native rsvg-convert (Windows stdout corruption, no winget package). resvg does not support animation; the hero-motion.json per-frame composition architecture handles this.

## Language (continued)

**Mount Refresh Policy**:
Field on a `SecretMount` controlling whether the service reconciles it. `Periodic` = service refreshes on `refreshIntervalSeconds`; `CreatedOnce` = service ignores (fetch-on-launch only, the "point and guide" path for mounts whose backend manages its own lifecycle). Default `CreatedOnce`.
_Avoid_: refresh mode, lease mode

**Runtime Mode**:
The mode the `env-manager-service.exe` binary is started in: `service` (SCM-managed, non-interactive, machine boot), `background` (user-launched, attached to GUI session), `cli` (one-shot CLI gateway call). Resolved from `--mode=<x>` argv or SCM service args. Pattern 1:1 imported from `D:/Aworker/photo` (`RuntimeModeResolver` + `WorkerEntryGuard`).
_Avoid_: launch mode, process mode

**A7 (service binary entry surface, photo pattern)**: `env-manager-service.exe` is an independent Rust binary with three RuntimeModes refactored from the `D:/Aworker/photo` reference architecture (see `PhotoPrivacy.Worker/RuntimeModeResolver.cs`, `WorkerIpcContracts.cs`, `WorkerIpcEndpointNames.cs`):
- `--mode=service`: registered as Windows service via SCM, runs at machine boot, non-interactive. Named pipe endpoint name: `EnvManager.Service` (mirrors photo's `PhotoPrivacyCleaner.Service`).
- `--mode=background`: user-launched, attached to GUI session. Named pipe: `EnvManager.Background`.
- `--mode=cli`: CLI gateway — `env-manager-cli.exe` (existing C# .NET 10 binary) connects to whichever pipe is live and issues one request, then exits. The CLI is a thin gateway, NOT the reconcile loop owner.
- GUI (`env-manager.exe` Tauri) calls the CLI, CLI calls the service via named pipe. Single entry funnel — same as photo's pattern (`Ui → Cli → Worker`).
- IPC protocol mirrors photo's `WorkerIpcRequest/Response`: `EnvManagerIpcRequest{method, id, args}` / `EnvManagerIpcResponse{ok, data, message, id}`. Methods include `Ping`, `GetStatus`, `RefreshMount <id>`, `RotateMount <id>`, `GetMountHealth`, `Shutdown`, `ReloadConfig`. No method carries plaintext; only mount ids, status, and timestamps.
- Photograph's `WorkerEntryGuard.ShouldRejectDirectLaunch` pattern is imported: if a user double-clicks the service binary with no `--mode` flag and the session is interactive, it refuses to run as a foreground service; instead it prints "use the GUI / CLI" and exits. The service MUST NOT be a foreground user app.
- C# CLI gains a new `service` subcommand: `env-manager-cli service status` / `service refresh <mountId>` / `service rotate <mountId>` / `service health`. These are thin IPC gateways, not direct provider calls — the service is the single reconcile authority so concurrent CLI + service Decrypt calls cannot race (the Q7 race concern is closed by funnelling all refresh through the service).

## Service Lifecycle Terms (v0.9.6)

- **Watchdog**: 周期性健康检查线程，检测服务进程存活状态，连续失败后触发自动重启。GUI 内嵌，30s 间隔，2 次连续失败后触发 start_service。
- **Heartbeat**: 服务端 ping 响应携带的 uptime + reconcile 状态，用于区分 "busy" vs "deadlocked"。包含 uptime_seconds, reconcile_last_run_at, reconcile_next_run_at, mount_count, healthy_mounts。
- **SCM Recovery**: Windows Service Control Manager 原生的失败恢复机制，通过 sc failure 配置 3 次重启/60s 间隔。Service mode 专用，与 GUI watchdog 互为 defense-in-depth。
- **Service Lifecycle**: 服务进程从 spawn -> run -> crash -> restart -> shutdown 的完整生命周期。两条恢复路径: SCM (Service mode) 和 GUI watchdog (Background mode)。
- **IPC Connection Health**: named pipe 连接健康度，由 ping success/failure 序列判定。CLI 端 3 次 retry with 1s backoff，GUI 端 watchdog 30s 周期检测。
- **Version Single Source**: env-manager.csproj <Version> 是唯一版本源，build.mjs 负责同步到 frontend/package.json。不手动维护两处版本号。
- **CHANGELOG**: 手动维护的变更日志，遵循 Keep a Changelog 格式 (Added/Changed/Deprecated/Removed/Fixed/Security)。release.yml 嵌入对应版本段落到 GitHub Release body。

## Logging & Disaster Recovery Terms (v0.9.8+)

Resolved during grill-with-docs session 2026-08-07 (decisions A1-A6, see .codex-tmp/grill-plan-logs-dr.md).

- **Unified Logging Backend**: `tracing` + `tracing-subscriber` + `tracing-appender` as the single logging crate across Rust GUI shell and Rust service, replacing `tauri-plugin-log` + `log`. C# CLI retains `DebugLog` to stderr (short-lived subprocess, host captures). Per-module filtering via `RUST_LOG` EnvFilter.
- **Log Rotation**: `tracing-appender` `Rotation::Daily` + `max_log_files(10)` + 50MB per-file size cap + 7-day retention. Total worst-case ~350MB. Service heartbeat (reconcile loop 300s, watchdog 30s) logs at `debug`; only state changes log at `info`.
- **Log File Layout**: GUI and Service share the same directory (`logs/` adjacent to exe for portable, `%LOCALAPPDATA%\EnvManager\logs\` for MSI) but separate filenames: `env-manager.log` vs `env-manager-service.log`.
- **Service State Enum**: `not_installed` | `not_running` | `running_healthy` | `unresponsive`. CLI `service status` returns structured JSON `{state, pid?, last_heartbeat?}`. Pipe existence check (`CreateFile` fail = `not_running`) precedes IPC ping (timeout = `unresponsive`).
- **Log Level by State**: `not_installed` / `not_running` = `debug` (expected常态); `unresponsive` = `warn`. Replaces flat WARN noise for expected-not-running probe results.
- **Request ID**: 8-char hex generated by Rust `run_cli` at entry, propagated to C# CLI via `ENVMANAGER_REQUEST_ID` env var, to Service IPC via JSON body `"request_id"`, and to frontend via `frontendLog` param. `grep <request_id>` across all three log files traces one full transaction end-to-end.
- **Versioned Schema Migration**: Explicit `schema_version` field in `profiles.json` and `secretMount.json` + migration registry (`v1->v2 migrate()`, ...) replacing inline one-shot `MigrateSecretsToMounts`. Startup runs migrations in version order.
- **Full-State Export/Import**: DPAPI-CurrentUser-encrypted archive containing `profiles.json` + `secretMount.json` + `protected-vars.json` + `protected-paths.json` + `builtin-protected-vars.json` + `builtin-protected-paths.json` + `gui-settings.json` + `audit.json`. CLI: `env-manager export-state` / `import-state`. GUI: disaster recovery entry point.
- **Audit Ledger Unification (Phase E)**: `audit.json` (CLI-level) migrates to `audit-ledger.jsonl` (append-only hash-chained, 100MB rotation). `export_survival_kit` gets DPAPI encryption via `audit encrypt-file` subprocess. Migration is one-way; after migration `audit.json` is retired.

## Release Readiness Terms (v0.9.12+)

Resolved during grill-with-docs session 2026-08-08 (decisions A1-A5, GitHub public release readiness).

- **Release Phase 1 (Local)**: All artifacts producible within the local codebase without external service dependencies. Includes community health files (CONTRIBUTING, SECURITY, CODE_OF_CONDUCT, Issue/PR templates), README rewrite per standard-readme spec, Tauri capability audit, CSP hardening, Named Pipe DACL hardening, and README unsigned-code warning. No remote pushes required.
_Avoid_: pre-release, local-only phase, stage one

- **Release Phase 2 (Remote)**: Artifacts requiring external service interaction after the GitHub repository is public. Includes winget manifest submission to `microsoft/winget-pkgs`, code signing (OV/EV Authenticode certificate), and Tauri updater plugin integration. Blocked until repository visibility switches from private to public.
_Avoid_: post-release, remote phase, stage two

- **Community Health Files**: The standard set of GitHub repository files that signal project maturity and enable safe external contribution: `CONTRIBUTING.md`, `SECURITY.md`, `CODE_OF_CONDUCT.md`, `.github/ISSUE_TEMPLATE/` (bug report + feature request forms), `.github/PULL_REQUEST_TEMPLATE.md`. All UTF-8 without BOM, placed under `.github/`.
_Avoid_: community templates, repo metadata, social files

- **Standard README Compliance**: README.md adheres to the `standard-readme` npm specification: required sections in fixed order (Title, Short Description, Table of Contents, Security, Install, Usage, Extra Sections, API, Maintainers, Contributing, License), optional sections (Banner, Badges, Long Description, Background, Thanks). i18n file naming: `README.md` is English (sole authority), translations live under `docs/i18n/README.<locale>.md` (zh_CN is the complete reference).
_Avoid_: readme spec, readme standard

- **Tauri Capability Coverage**: Every custom Tauri IPC command (`run_cli`, `read_gui_setting`, `write_gui_setting`, `frontend_log`) must be explicitly listed in `frontend/src-tauri/capabilities/default.json` permissions. Tauri 2.0 deny-by-default model means unlisted commands are inaccessible from the frontend. Audit verifies no command is silently unguarded.
_Avoid_: IPC whitelist, permission audit, capability gap

- **CSP Hardening**: Content Security Policy in `tauri.conf.json` `app.security.csp`. Production target removes `'unsafe-inline'` from `style-src` if Svelte 4 scoped styles render correctly without it. Current: `default-src 'self'; img-src 'self' data:; style-src 'self' 'unsafe-inline'; script-src 'self'`. Hardened target: remove `'unsafe-inline'` from style-src.
_Avoid_: content security, CSP policy, style policy

- **Named Pipe DACL**: Explicit Discretionary Access Control List on `\\.\pipe\EnvManager.Service` and `\\.\pipe\EnvManager.Background` named pipe endpoints. Restricts pipe access to the current user SID only. Complements existing `PIPE_FIRST_PIPE_INSTANCE` anti-squatting flag. Implemented via `SECURITY_ATTRIBUTES` with explicit DACL in Rust `ipc.rs`.
_Avoid_: pipe ACL, pipe permissions, pipe security descriptor

- **Code Signing (Phase 2)**: Authenticode signing of `env-manager.exe`, `env-manager-cli.exe`, `env-manager-service.exe`, and MSI installer with an OV (Organization Validation) code signing certificate. Eliminates SmartScreen "unrecognized app" warning after reputation builds. EV certificates no longer guarantee instant SmartScreen bypass as of 2025. Deferred to Phase 2.
_Avoid_: cert signing, Authenticode, executable signing

- **winget Distribution (Phase 2)**: Submission of a YAML manifest to `microsoft/winget-pkgs` repository via PR, enabling `winget install EnvManager` installation. Manifest includes publisher, package name, version, installer URLs (MSI per architecture), and SHA256 hashes. Blocked until GitHub repository is public and release artifacts have stable URLs.
_Avoid_: winget manifest, package manager submission, Windows Package Manager

- **Tauri Updater (Phase 2)**: Integration of `tauri-plugin-updater` for signed auto-update: app checks a remote HTTPS manifest on startup, downloads a signed update bundle, verifies with embedded public key, then restarts to apply. Requires signing key management (private key protected, public key embedded in app). Deferred to Phase 2.
_Avoid_: auto-update, update plugin, signed update

**ScrubExceptionMessage**:
The v0.9.12 C# helper that masks 22 secret-bearing patterns from exception messages before they reach stderr or logs. Applied at all 7 x.Message leak sites in Program.cs. Bounded to 512 chars, best-effort pattern matching. Mirrors scrub_stderr in Rust.
_Avoid_: error scrubber, message filter, log sanitizer

**SecretString (C#)**:
A
ef struct wrapping a decrypted secret value. Zeroes the underlying char[] on Dispose() to minimize plaintext lifetime in heap memory. Used at ProfileRevealSecret and ProfileLaunch decrypt sites. The Rust equivalent uses the secrecy crate's SecretString with zeroize on drop.
_Avoid_: secure string, encrypted wrapper, secret holder

**Redaction Vocabulary**:
The canonical set of 22 secret-bearing string patterns recognized by all three tiers. Patterns: Bearer, token=, Token=, password=, Password=, setx, OP_SERVICE_ACCOUNT_TOKEN=, VAULT_TOKEN=, AWS_SECRET_ACCESS_KEY=, AWS_SESSION_TOKEN=, client_secret=, connection_string=, subscription_key=, api_key=, apikey=, client_id=, tenant_id=, access_token=, refresh_token=, Authorization:, X-Vault-Token:, x-api-key:. A new pattern must be added to all three code sites simultaneously.
_Avoid_: mask list, secret patterns, redaction rules

## GUI Layer Terms (v0.9.26)

Resolved during grill-with-docs session 2026-08-19 (5 GUI bug fixes, see .codex-tmp/grill-plan-path-move-dead-zindex-radio-input.md).

- **Staged Move**: The PathEditor reorder pattern where move-up/move-down clicks accumulate adjacent swaps locally on `stagedEntries` (new array ref per swap) without per-click IPC. The Apply button commits the whole sequence via `applyStagedMoves()` which re-reads live registry order and calls `movePathEntryUp` repeatedly. `ensureStagedActive()` is the single init entry that copies `entries.slice()` into `stagedEntries` and sets `stagedActive=true`. Guard order matters: the boundary guard MUST run after `ensureStagedActive()`, not before, or it reads an empty array and blocks the only init path.
_Avoid_: staged reorder, pending moves, buffer reorder

- **Live Dead Count**: The reactive derived value `displayEntries.filter(e => !e.exists && !e.isProtected).length` that drives the "Remove dead entries" button `disabled` state. It is the single source of truth for dead-entry visibility; the confirm handler MUST read the same value, not `healthSummary` (which is only filled by the manual health-check click and resets to null on every refresh).
_Avoid_: dead count, health dead, stale dead count

- **z-index Layering**: The GUI overlay stacking convention: titlebar `z-index: 50` (navbar level), modal dialogs and toast notifications `z-[100]`. The titlebar stays above scrolling content but never suppresses overlays. See ADR 0006.
_Avoid_: z-index scale, overlay stack, layer order

- **Form Control color-scheme Override**: The element-level `color-scheme: light` on `input, select, textarea` that overrides the `:root` `color-scheme: light dark` (v0.9.24 scrollbar hard boundary). Binds form control fill/outline to app `data-theme` instead of Windows `prefers-color-scheme`. Paired with `accent-color: hsl(var(--primary))` for radio/checkbox fill and `outline: none` on focus (Tailwind `focus:ring` takes over). See ADR 0007.
_Avoid_: form theme binding, control theme override, light-only controls


## Public Release & Mirror Terms (v0.9.26+)

- **Public Visibility Gate**: `gitleaks git .` clean (exit 0) required before flipping repo public; PAT mode retired in favor of global SSH.
- **Release Gate**: The phrase "开始发布" is the explicit user authorization required before creating any git tag, GitHub Release, or external channel submission (winget/Scoop/Chocolatey).
- **Mirror Topology**: GitHub is canonical; GitLab + Codeberg are read-only mirrors updated by `qte77/gha-github-mirror-action` on push to `main`.
- **README-i18n root**: Root README.md is the English landing page; translations live under `docs/i18n/README.<locale>.md` with an `<!-- README-I18N:START/END -->` switcher block.
## MSI Packaging & Toolchain Terms (v0.9.27)

Resolved during grill-with-docs session 2026-08-22 on MSI install-hang fix on frozen WiX v3.

- **Tauri-Bundled WiX**: The WiX v3.14.1 RTM toolchain pinned inside `tauri-bundler` (`WIX_URL` points at `wix314-binaries.zip`, cached under `~/.cache/tauri/WixTools314`); the project deliberately stays on this frozen chain because Tauri v2 has officially declined to migrate (issue #10348). MajorUpgrade semantics, ICE rules, and ServiceInstall behaviour are bounded by WiX v3 only.
_Avoid_: modern WiX toolchain, v5+ toolchain

- **WixUtilExtension-ServiceConfig**: The transactional inline replacement for the deprecated `sc.exe failure/config` custom-action pair. Nested inside `ServiceInstall`, runs inside the MSI transaction with native rollback.
_Avoid_: sc.exe CA, deferred sc.exe

- **Upgrade-Stage Service Stop**: The pattern of placing a separate `ServiceControl` in its own component, gated on `WIX_UPGRADE_DETECTED`, with only `Stop="uninstall"` (no Start/Remove), so the old version's service is stopped before its files are removed. Decouples "stop the previous version" from "install the new version".
_Avoid_: inline upgrade stop, mixed-purpose service control

- **MajorUpgrade Schedule (afterInstallExecute)**: The explicit schedule where `RemoveExistingProducts` is deferred until after `InstallExecute`, preventing file-in-use reboot prompts during silent install. Replaces the WiX v3 default `afterInstallValidate` for service-shipping MSI projects.
_Avoid_: default schedule, afterInstallValidate

- **ServiceInstall Vital Flag**: `ServiceInstall ... Vital="yes"` so a failed service install rolls the transaction back rather than leaving a half-installed product. Pair with `ServiceControl ... Wait="yes"` (30-second hard cap per MSDN).
_Avoid_: Vital="no", Wait="no"

- **Per-Component RemoveFile**: `<RemoveFile Name="*.*" On="uninstall"/>` placed inside each file component removes *all* files the component ever dropped in that directory on uninstall. Idempotent (file absent = no-op), transactional via MSI engine, junction-safe by construction (no recursion). This is the inverse of relying on GUID-matched component tracking — it is *what makes unstable Heat-generated GUIDs survivable*.
  _Avoid_: util:RemoveFolderEx (CVE-2024-29188 junction traversal, transactional-rollback burden, unnecessary power here).

- **Component GUID Stability (`Guid="*"`)**: In WiX v3, `Component/@Guid="*"` generates a *deterministic* GUID via RFC 4122 v3 over (install directory + KeyPath filename). It is stable across rebuilds *unless* (a) the file is renamed, (b) its install directory changes, (c) Win64-ness switches `ProgramFilesFolder` ↔ `ProgramFiles64Folder`. Components keyed on file path cannot be renamed without an ORPHAN component (old GUID never matches, new GUID never cleans). Pin explicit GUIDs only on the three components most likely to change identity (GuiExecutable, CliExecutable, ServiceExecutable); leave utility/registry/shortcut components on `*` — matches Tauri's own `main.wxs` pattern.
  _Avoid_: `Guid="*"` on components whose file the project may one day rename (any new .exe / .dll).

- **Line-Ending Normalization**: A single `chore(repo)` commit running `git add --renormalize .` against the root, *plus* a `.editorconfig` at the root with `end_of_line = lf`. `.gitattributes` is already in place (`*.md text eol=lf` etc.); renormalize makes the index match it. `core.autocrlf` is a local trap, never a fix; VS Code `files.eol = auto` is the default Windows trap. Cured once, governed forever by the two repo-level files.
  _Avoid_: dos2unix, git filter-repo, per-branch rebase-fixes.

- **GUI Test Pyramid (1.0 gate)**: Vitest + mockIPC for IPC contract unit tests (existing 37 files stay). WebdriverIO + `@wdio/tauri-service` for 8-12 critical-flow e2e on Windows CI (no xvfb needed on windows-latest). MSI upgrade matrix (default-path + custom-path, empty-install + with-prior-install) runs in GitHub Actions via `tauri-action`. Coverage gates: CLI ≥ 80%, Rust service ≥ 80%, frontend unit ≥ 70%, e2e flow-count only (no line-coverage target — registry writes are mock-only in unit tests by design). Playwright stays for browser-mode layout/a11y, never drives Tauri IPC.
  _Avoid_: Playwright-as-e2e-driver for Tauri (no CDP on macOS/Linux WebViews), chasing e2e line coverage, mocking the registry-write layer.

- **WiX v3 EOL Supply-Chain Watch**: Weekly GitHub Actions workflow runs on `schedule: cron: '0 0 * * 1'` + `workflow_dispatch`. Fails the run and files an issue if any of: (a) `wixtoolset/wix3` publishes a release newer than 3.14.1, (b) a new GHSA is filed against `wixtoolset`, (c) Tauri dev-branch `crates/tauri-bundler/src/bundle/windows/msi/mod.rs` stops pointing `WIX_URL` at `wix3141rtm/wix314-binaries.zip`. Zero runtime cost, detects silent upstream drift before it contaminates the next release build.
  _Avoid_: vendoring the 26MB WixTools314 zip into git LFS.
- **ICE Suppression List**: An inline-commented array in build.mjs (`ICE_SUPPRESSIONS`) naming every suppressed ICE with its one-line rationale (e.g., `// ICE91 perMachine benign warning`). Survives extension by appending `// <ICE_ID> <reason>`. Aligns with WiX v3 industry practice (VSCodium, PowerToys).
_Avoid_: blanket `-sice` suppression without per-ICE comments.

- **MSI UI Dialog Set**: The chosen WixUI_InstallDir dialog sequence for Env Manager: Welcome → InstallDir (with optional desktop shortcut checkbox) → VerifyReady → Finish. No License page (Apache-2.0, no consent requirement). `WIXUI_INSTALLDIR` must point to an all-uppercase Directory Id (`INSTALLDIR`) — ICE does not validate this pair, and missing it causes runtime error 2819.
- **Desktop Shortcut Checkbox**: A `VerifyReadyDlg`-pattern checkbox on the InstallDir dialog bound to `INSTALLDESKTOPSHORTCUT=1|0`. When 1, MSI creates a Desktop folder shortcut; when 0, only Start Menu shortcut is installed. Follows the WiX v3 `CustomizeDlg` pattern.
- **ARPPRODUCTICON**: MSI property pointing to the product's icon resource (`ProductIcon`) shown in Windows Control Panel "Programs and Features". References the icon.ico from `frontend/src-tauri/icons/` (cherry-picked from f073733).
- **Icon Cherry-Pick (f073733)**: The frontend/src-tauri/icons emerald icon family (icon.ico, icon.png, 32x32.png, 128x128.png, 128x128@2x.png) plus docs/assets/brand/hero.svg, cherry-picked from main to codex/v1.0.0 as commit fb164b9. The icon.ico is the ARPPRODUCTICON source.
- **WiX UI Extension Integration**: Adding WixUI_InstallDir requires `-ext WixUIExtension.dll` on both candle and light. The extension does NOT include .wxl files — `sdk\wixui\WixUI_en-US.wxl` must exist physically under the WiX toolchain directory or light will fail with LGHT0103.
- **MSI String-Gate Tests**: Three canonical assertions on installer.wxs source: (1) `WIXUI_INSTALLDIR=INSTALLDIR`, (2) `<UIRef Id="WixUI_InstallDir"/>`, (3) `-ext WixUIExtension.dll` present in build.mjs candle/light args. These close the ICE blind spot that build+validate green but runtime error 2819.
- **ICE Suppression List**: An inline-commented array in build.mjs naming every suppressed ICE with its rationale (e.g., ICE91 perMachine benign warning). Survives extensions by adding `// <ICE_ID> <reason>` lines.
_Avoid_: blanket -sice suppression without per-ICE comments.

## MSI Lifecycle & Logging Terms (v0.9.30)

Resolved during grill-with-docs session 2026-08-24 on two user-reported MSI bugs (no uninstall entry; logs residue in INSTALLDIR).

- **In-INSTALLDIR Uninstall Shortcut**: A WiX non-advertised Shortcut component inside `DirectoryRef Id="INSTALLDIR"` targeting `[System64Folder]msiexec.exe /x [ProductCode]`, paired with a `RegistryValue KeyPath` under **HKMU** (HKCU would trip ICE37/ICE57 on per-machine installs). This is the FireGiant How-To / Rob Mensching canonical pattern. Safe under MajorUpgrade because `[ProductCode]` evaluates at *runtime* against the currently installed product, not at build time.
  _Avoid_: Start Menu uninstall entry (less discoverable); appwiz.cpl shortcut (just opens the ARP list, not a real uninstall); ARP-only (fails user expectation).

- **Log Directory Externalization**: GUI logs live at `%LOCALAPPDATA%\<App>\logs` (PowerToys, 1Password 8, Chrome, tauri-plugin-log v2 defaults; VS Code/Discord still on Roaming are legacy). Service logs (`env-manager-service.exe`) continue under `%ProgramData%\<App>\` because service accounts have no meaningful LocalAppData. MSI never touches LocalAppData on uninstall (industry default = keep user logs; Chrome offers it as opt-in only).
  _Avoid_: writing logs inside INSTALLDIR (Program Files is read-only for standard users; MSI treats runtime files as orphans; never upgraded cleanly).

- **Legacy INSTALLDIR Logs Backstop**: A single paired `CreateFolder` + `RemoveFile Name="*.log*"` + `RemoveFolder` component inside a dedicated `LogsDir` directory reference. Closes pre-v0.9.30 installs' residue without touching other components. Explicitly NOT `util:RemoveFolderEx` (CVE-2024-29188 junction traversal when per-machine MSI touches per-user data; needs WiX ≥ 3.14.1).
  _Avoid_: bare `CreateFolder` component (wixsharp #1114 — removes empty skeleton but not the parent dir); `*.*` wildcard on INSTALLDIR itself (risky on shared Program Files).

- **Date-Based Log Retention (14-day sweep)**: GUI startup spawns a fire-and-forget thread walking `%LOCALAPPDATA%\<App>\logs` and removing files whose `metadata.modified() < now - 14 days`. Daily rotation already bounds per-run volume; the sweep keeps total directory age bounded. Aligns with 1Password 8 exactly. Per-file **size** cap is explicitly *rejected*: tracing-appender doesn't support it natively, switching to tauri-plugin-log (40KB KeepOne) is a larger stack change for marginal benefit.
  _Avoid_: rewriting to tauri-plugin-log for retention alone; default tracing-appender `rolling::daily` without any retention sweep (industry default logs-out-of-folder, unbounded growth).
## README Display Terms (v0.9.30+)

Resolved during readme-display-grill session 2026-08-29 (follow-up branch:
rsvg-convert dependency formalization + i18n native-quality translation).

- **rsvg-convert Wrapper**: A Node.js CLI wrapper script (`scripts/rsvg-convert.js`)
  using `@resvg/resvg-js` (napi-rs prebuilt) that provides `rsvg-convert`
  CLI compatibility for Python skill scripts. Replaces the native
  `rsvg-convert` binary on Windows where it has known stdout binary
  corruption issues (librsvg#676, #812). Discovered by `shutil.which()`
  in `render_motion_gif.py`. See ADR-0013.
  _Avoid_: installing native `rsvg-convert` via winget/MSYS2/choco on
  Windows; placing the wrapper in `.codex-tmp/` (ephemeral, gitignored,
  not reproducible).

- **i18n README Drift Check**: A CI guard (`scripts/check-readme-i18n.ps1`)
  that verifies structural consistency between the English README (sole
  authority) and all 9 locale files: H2 heading count match, i18n switcher
  block present, no inline version strings, hero.gif + demo.gif referenced,
  For AI Agents section present. Exits non-zero on drift.
  _Avoid_: relying on human review for locale consistency; omitting the
  negative-test injection before shipping.

- **atomcode-assisted Translation**: Using the `atomcode` CLI with `[fast]`
  mode hint to produce native-quality translations of README content from
  English to locale languages (ja, ko, de, fr, es, pt, ru, ar). Technical
  terms (Env Manager, CLI, GUI, Tauri, DPAPI, PATH, etc.) are preserved
  untranslated; only prose and section headings are translated.
  _Avoid_: mechanical/copy-based translation without native-quality review;
  translating technical identifiers and command names.

- **hero-motion.json Motion Spec**: A JSON specification describing
  per-frame animation of SVG layers for GitHub-safe GIF output. Defines
  width, fps, duration, color/dither settings, reveal clips, and layer
  enter/exit transitions. Rendered via `render_motion_gif.py` (Python +
  Pillow + ffmpeg + rsvg-convert wrapper) into `hero.gif`. resvg core
  does not support SMIL animation; the per-frame composition architecture
  is the correct static-renderer path.
  _Avoid_: animated SVG with SMIL/CSS (GitHub camo proxy breaks it);
  hardcoding version strings in hero.svg (use shields.io dynamic badge).
