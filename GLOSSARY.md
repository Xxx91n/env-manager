# Env Manager — Glossary

Canonical domain vocabulary for this repository: terms plus their _Avoid_ pairs. Nothing else belongs here — decisions, mechanisms, and gate descriptions live in code comments.

## Secrets domain

**Secret Envelope**:
The on-disk JSON container in a profile's `secretVariables` array: `provider`, `version`, `createdAt`, `ciphertext` (DPAPI base64), `targetName`. Schema v1.
_Avoid_: secret blob, encrypted value, secret record

**Secret Mount**:
The schema v2 unit in `secretMount.json`, referenced by id from `profiles.secretMountRefs`; adds `refreshPolicy`, `refreshIntervalSeconds`, `lastRotatedAt`, `expiresAt` to the envelope fields. Replaces the direct `secretVariables` array.
_Avoid_: mount point, secret reference, secret link

**Mount Refresh Policy**:
Field on a `SecretMount`. `Periodic` = the service refreshes on `refreshIntervalSeconds`; the fetch-on-launch policy is serialized as `CreatedOnly` (term: `CreatedOnce`) and the service ignores it, fetching the value on launch only. Default `CreatedOnly`.
_Avoid_: refresh mode, lease mode

**Provider**:
An `ISecretProvider` implementation (8 total: dpapi-current-user, credential-manager, powershell-secretmanagement, vault-kv2, sops, azure-keyvault, 1password, aws-secretsmanager) with five verbs: Name/Encrypt/Decrypt/CanRotate/Rotate/Delete.
_Avoid_: backend, vault (ambiguous with HashiCorp Vault), store

**SecretStore**:
The two-layer domain port: `ISecretStore` in `EnvManager.Secrets.Core` declares Mount/Reveal/Rotate/Export/Import; `SecretProviderManager` is its only implementation. It sits above the transport-level `Provider` contract, so SDK types never cross the adapter boundary.
_Avoid_: secret manager, provider router, secret backend

**Provider Error Family**:
The typed `SecretProviderException` family: a base class carrying Provider/Operation/MountId plus six sealed subtypes (AuthFailed, NotFound, PermissionDenied, Unavailable, Timeout, InvalidEnvelope). Every adapter maps raw transport failures onto it, and every message is scrubbed in the base constructor.
_Avoid_: error codes, raw SDK exception, exception passthrough

**Capability Descriptor**:
Declared per-provider metadata on `ISecretProvider`: `RefreshCapable`, `CertAuthRequired`, `RequiresNetwork`, `Available`. `Available` is a cheap declared self-check (config or resolved binary, no I/O).
_Avoid_: hardcoded Periodic refusal, capability flags keyed on provider names

**Secrets Namespace**:
The `src/Secrets/` bounded context: Core (envelope, mounts, port, JSON contexts), Providers (the 8 adapters), Manager (routing and rotation). Outside the context only Core and Manager may be consumed.
_Avoid_: secrets folder, secrets module, secrets layer

**Audit Ledger**:
The append-only, hash-chained ledger at `%ProgramData%\EnvManager\audit-ledger.jsonl`, verified by `audit verify --strict`. It is the authoritative audit trail; `audit.json` survives only for backward-compatible undo. Not an event store, never replayed as one.
_Avoid_: event store, event log, audit log (ambiguous with audit.json), blockchain

**Reconcile Loop**:
The service-side loop that refreshes `Periodic` mounts, default off. Only `CanRotate=true` providers carry a lease concept.
_Avoid_: refresh daemon, sync loop, agent process

**Capability-Scoped Agentic Surface**:
The `agentCapabilities` whitelist on `secret-providers.json`; empty (the default) means all-allowed. Opt-in deployments use it to reject parallel set/delete calls from LLM agents.
_Avoid_: agent gate, capability filter, agent whitelist

## Profile domain

**Launch Profile**:
A profile whose variables are injected into a child process via `env_clear + env(k,v)`; never written to the registry, never broadcasts `WM_SETTINGCHANGE`. The only profile type that may carry secrets.
_Avoid_: local profile (ambiguous with local-scope PATH entries), child profile

**Global Profile**:
A profile applied to the HKCU/HKLM registry on `profile apply`. Cannot carry secrets (`IsProfileApplicable` rejects them) and may inherit from other Global profiles.
_Avoid_: system profile, applied profile

**Profile Aggregate Root**:
`ProfileData` in `src/Models.cs`; every setter is private, so external mutation goes through domain methods (AddVariable, SetLaunchTarget, AddPathEntry, AddSecretVariable, …). `SecretMount` stays its own aggregate — a profile references mounts only by `mount:<id>` strings.
_Avoid_: profile model, DTO with public setters, inline secret collection on profile

**Version Single Source**:
`env-manager.csproj` `<Version>` is the only version source; `build.mjs` syncs it into `frontend/package.json`. Never maintain two copies by hand.
_Avoid_: duplicated version literals

## Process and IPC

**Runtime Mode**:
The mode `env-manager-service.exe` starts in: `service` (SCM-managed, machine boot, non-interactive), `background` (user-launched, attached to the GUI session), `cli` (one-shot gateway call).
_Avoid_: launch mode, process mode

**Service State Enum**:
`not_installed` | `not_running` | `running_healthy` | `unresponsive`, returned by `service status`. Pipe existence is probed before the IPC ping, so a missing pipe reads as `not_running`.
_Avoid_: ad-hoc state strings

**Request ID**:
An 8-char hex id generated at the Rust `run_cli` entry, propagated to the C# CLI via `ENVMANAGER_REQUEST_ID`, to the service via the JSON body, and to the frontend via `frontendLog`. Grepping it across the three log files traces one transaction.
_Avoid_: trace id, correlation id

**Updater Port**:
The auto-update transport seam: tauri-plugin-updater over the `latest.json` endpoint plus a minisign pubkey; the legacy GitHub-API probe remains only as the browser fallback. The frontend consumes both through one `UpdateInfo` shape.
_Avoid_: version literal in the GUI, pending-update State plumbing

**Updater Manifest (latest.json)**:
The Tauri v2 document the GUI polls to discover and verify an available update; the endpoint and schema contract live in `docs/architecture.md` and `docs/build-and-release.md`.
_Avoid_: appcast, sparkle xml, inline version probe

## GUI

**Staged Move**:
The PathEditor reorder pattern: move-up/move-down accumulate adjacent swaps on `stagedEntries` (new array reference per swap) with no per-click IPC; Apply commits the sequence. The boundary guard must run after `ensureStagedActive()`, or it reads an empty array and blocks the only init path.
_Avoid_: staged reorder, pending moves, buffer reorder

**Live Dead Count**:
`displayEntries.filter(e => !e.exists && !e.isProtected).length`, which drives the "Remove dead entries" button's disabled state. The confirm handler must read this same value, never `healthSummary` (filled only by the manual health check, reset to null on refresh).
_Avoid_: dead count, health dead, stale dead count

**z-index Layering**:
Titlebar at `z-index: 50`, modal dialogs and toasts at `z-[100]`. The titlebar stays above scrolling content but never suppresses overlays.
_Avoid_: z-index scale, overlay stack, layer order

**Form Control color-scheme Override**:
The element-level `color-scheme: light` on `input, select, textarea` that binds form-control fill and outline to the app `data-theme` instead of Windows `prefers-color-scheme`, while scrollbars keep reading `:root`.
_Avoid_: form theme binding, light-only controls

## Safety and logging

**ScrubExceptionMessage**:
The C# helper in `src/CliRuntime.cs` that masks 22 secret-bearing patterns from exception messages before they reach stderr or logs; bounded to 512 chars. Rust mirrors it as `scrub_stderr`.
_Avoid_: error scrubber, message filter, log sanitizer

**SecretString**:
The wrapper around a decrypted secret value that zeroes its underlying `char[]` on `Dispose()`, shortening plaintext lifetime on the heap. The Rust equivalent uses the secrecy crate with zeroize on drop.
_Avoid_: secure string, encrypted wrapper, secret holder

**Redaction Vocabulary**:
The 22 secret-bearing patterns recognized by all three tiers (Bearer, token=, VAULT_TOKEN=, Authorization:, …). A new pattern must be added to all three code sites simultaneously.
_Avoid_: mask list, secret patterns, redaction rules

## Release and build

**Release Gate**:
The phrase "开始发布" is the explicit user authorization required before creating any tag, GitHub Release, or third-party channel submission.
_Avoid_: assuming release authority

**Mirror Topology**:
GitHub is canonical; GitLab and Codeberg are read-only mirrors updated by `qte77/gha-github-mirror-action` on push to `main`.
_Avoid_: treating a mirror as authoritative

**Tauri-Bundled WiX**:
The WiX v3.14.1 toolchain pinned inside `tauri-bundler` (`WIX_URL` → `wix314-binaries.zip`). Tauri v2 has declined to migrate (#10348), so MajorUpgrade semantics, ICE rules, and ServiceInstall behaviour are bounded by WiX v3.
_Avoid_: modern WiX toolchain, v5+ toolchain

**Canonical Marker / Sync Stamp**:
HTML comments at the top of each translated README: `<!-- canonical: ../../README.md -->` declares English the sole authority, `<!-- sync: <en-hash12> -->` records the English commit hash at translation time. A lagging stamp warns but does not fail.
_Avoid_: treating a locale file as independently editable, bumping the stamp without rechecking parity

## Media pipeline

**Factual Shot (事实画面)**:
Screen visuals depicting the real product. AI generation is banned; the sanctioned source here is a Playwright-recorded stub UI (real 0.12.x UI code plus canned data).
_Avoid_: recording the installed app (stale binary, real-path leakage), AI-substituting factual visuals

**Segment Contract**:
`$MPIPE_HOME/docs/segment-contract.md`, the producer interface for non-factual shots: comp → `npx hyperframes render` → `segments/sN.mp4`. The bottom 15% of the frame is the burned-caption safe zone.
_Avoid_: static cards posing as animation, CDN GSAP, silent lavfi fallback
