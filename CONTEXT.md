# Env Manager

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

**A2 (migration strategy)**: One-shot atomic write, delete old `secretVariables` field. No dual-write period. No existing production users to preserve. Rollback via `.env_bak` backup (already a hard boundary).

**A3 (file topology + fsync)**: `secretMount.json` and `profiles.json` are separate files, each written atomically (temp + fsync + rename). Write order fixed: mount-first, profile-second (forward reference safety). Delete order reverse: profile-first (remove refs), mount-second. Phase A MUST also add fsync to C# `AtomicWriteProfiles` to match Rust `write_atomic` (v0.7.9 hard boundary). C# gap discovered during grill: `ProfileStorage.AtomicWriteProfiles` uses temp + `File.Move` but no `Flush(true)`/`sync_all` before rename.

**A4 (capability whitelist granularity + agent gate surface)**: Three-level combo granularity: `{ cmd, scope?, mountId? }` — omitted fields = wildcard. Default whitelist entries only fill to command level. Today's behavior (all allowed) = empty array. Gate enforcement is in the Rust `run_cli` IPC layer (single choke point for all agent calls through the GUI/plugin path). BUT per user feedback: most agents consuming env-manager read `AGENTS.cli.md` first, so the gate must be a SOFT guide, not a silent block. When a capability check fails, the CLI must immediately emit a constraint-guidance prompt visible to the agent (not just a reject error) — so the model sees why it was blocked and can self-correct. This means: (1) Rust layer emits a structured rejection message with the missing capability and the fix hint; (2) AGENTS.cli.md gains a "capability contract" section listing the allowed cmd/scope/mountId combos; (3) the rejection message references AGENTS.cli.md by name so the agent knows where to look. Direct exe bypass is NOT blocked (no agent identity proof mechanism) — the gate is advisory guidance for cooperative agents, not a hard wall against adversarial shell access.

**A5 (Phase C reconcile loop — REJECTED, "指向和引导" model)**: Env Manager's role is "point and guide", not continuous lifecycle management. Secrets are set once; Env Manager fetches plaintext at `profile launch` time, injects into the child process env block, and the CLI exits. No background reconcile, no `secrets-agent.exe`, no `tokio::spawn` background task, no FileSystemWatcher, no `OnChange` refresh policy. Backend lifecycle (lease renewal, rotation, expiry) is the backend's own problem — Vault manages its own leases, Azure manages its own secret expiry, AWS manages its own rotation Lambda. Env Manager does not compete with these backends; it only reads at launch time. This eliminates `refreshPolicy: Periodic` entirely from the Phase A `SecretMount` schema. `refreshPolicy` collapses to a single value: `CreatedOnce` (fetch-on-launch, no scheduled refresh). The `refreshIntervalSeconds` field is also dropped. The mount metadata still tracks `lastFetchedAt` and `lastRotatedAt` for audit visibility, but these are passive observations, not active refresh triggers. Confirmed against `Program.cs ProfileLaunch`: `EnvironmentVariables.Clear()` → decrypt → inject → `Process.Start` → return. The CLI process exits immediately after `Process.Start`; it does not watch the child, does not hold the env block, does not re-fetch. Phase C as written in the blueprint (`secrets-agent.exe`, 300s interval, Periodic/CreatedOnce/OnChange split) is **dead on arrival** — it solves a problem this project does not have.

**A5 (REVISED per A6 feedback — Env Manager DOES continuous lifecycle, via Windows system service)**: Previous A5 ("point and guide", no reconcile loop) is WITHDRAWN. User revisited: Env Manager needs continuous lifecycle management for completeness/security of secret support, but the architecture pivots to a *machine-level microservice* that embeds as a **Windows system service** rather than a per-process in-memory reconcile loop. The lifecycle owner is no longer the CLI transient process, and not a tokio task inside the GUI Tauri shell — it is an OS-managed service. This re-opens the need for periodic refresh, but routes the refresh through the service instead of a separate secrets-agent.exe or an in-process background thread. Key changes:
- `refreshPolicy: Periodic` re-enters the Phase A `SecretMount` schema; `CreatedOnce` stays as the default for mounts that don't need scheduled refresh.
- `refreshIntervalSeconds` re-enters, but its enforcement lives in the service layer, not in CLI/GUI.
- The service is the single reconcile loop: no second secrets-agent.exe, no GUI in-process timer; GUI is just a control panel for the service.
- This matches the industrial pattern (External Secrets Operator runs as a controller, not inside each app process; Vault Agent runs as a system service on the host).

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

## Research (pwm, 2026-08-02)

**Windows service identity (non-domain Win11)**: Microsoft Learn prefers sMSA / gMSA, but those require AD/domain-joined hosts. For standalone Win11 Pro (the project's primary deploy target), the documented fallback is `NT SERVICE\\<svc-name>` virtual account (per-service SID) plus explicit ACLs. LocalService/NetworkService/LocalSystem are all rejected by the documented guidance for least-privilege secret-access workloads (no DPAPI isolation, no scoped file ACL). Cited: https://learn.microsoft.com/en-us/entra/architecture/service-accounts-standalone-managed

**Idempotent reconcile pattern (industry)**: External Secrets Operator + Reloader + Doppler Agent all converge on: per-tick read observed state + desired state, write ONLY when desired != observed. Two ticks hitting the same mount are no-ops by construction (the second tick reads the SAME desired+vsn that the first tick just wrote, so the diff collapses). No double lease renewal, no side-effecting call when nothing changed. Cited: https://external-secrets.io/, https://github.com/stakater/Reloader

**Rust reconcile loop at desktop scale (20-100 mounts, 300s tick)**: Periodic full-scan with idempotent per-item handler is the established pattern; no external queue, no file-watcher, no SQLite WAL needed at this scale. `tokio::time::interval` loop reads `secretMount.json`, diffs per-mount `lastFetchedAt`/`lastRotatedAt` against `refreshIntervalSeconds`, fetches only changed mounts, writes back. Crash-recovery = on next restart the loop just resumes — the full-scan reconciles drift because it reads observed file state on disk. No persistent task queue needed because the work is idempotent and the state is the durable file.

**A8 (service identity + reconcile loop + feasibility audit)**: Three confirmed decisions under the user's "if it fits current architecture, fits all 8 secrets, fits all Windows PCs" gate:

**Identity**: `NT SERVICE\\EnvManagerService` virtual service account (per-service SID). Microsoft Learn: on non-domain standalone Win11 (Home/Pro/Enterprise) and Win10 22H2, NT SERVICE\<svc-name> is the documented least-privilege fallback (gMSA/sMSA require AD). Per-service SID ACL on `%ProgramData%\\EnvManager\\secretMount.json` only — no other path access. Created automatically by SCM at `sc.exe create` time; no manual LSA setup.

**Idempotent reconcile loop**: `tokio::time::interval(300s)` periodic full-scan. Each tick reads `%ProgramData%\\EnvManager\\secretMount.json` atomically (A3 fsync+rename), for each mount with `refreshPolicy == Periodic` and `now - lastFetchedAt >= refreshIntervalSeconds`: Decrypt/Rotate from the provider, write back lastFetchedAt/lastRotatedAt. Idempotence by construction: second tick after first has already written back new lastFetchedAt, diff collapses to no-op. No SQLite queue, no file watcher, no WAL — workload is 20-100 mounts, durable state IS the JSON file. Crash resume: on next service start, loop reads file state from disk and reconciles drift.

**Provider feasibility audit under NT SERVICE\<svc>**:
| Provider | Works as service? | Reason |
|---|---|---|
| dpapi-current-user | NO — MUST be CreatedOnce | DPAPI CurrentUser binds to interactive user master key; service identity has its own DPAPI scope, cannot decrypt user blobs |
| credential-manager | PARTIAL — MUST be CreatedOnce | CredMan vaults are per-logon-identity; service reads its own vault not the user's. Re-store script could close this later; for v0.9.0 keep CreatedOnce |
| powershell-secretmanagement | PARTIAL — BEST-EFFORT Periodic | SecretStore vault under NT SERVICE\<svc> profile is a separate vault from the interactive user. MOST CASES: user needs to Re-register the vault under the service identity, OR set CreatedOnce |
| vault-kv2 | YES | Pure HTTPS + VAULT_TOKEN env — canonical service-managed refresh |
| sops | YES | subprocess + SOPS_AGE_KEY env — machine-level |
| azure-keyvault | YES (SP env) | AZURE_CLIENT_ID/SECRET/TENANT env — IMDS path requires Azure MI provisioned VM (out of scope on standalone desktop) |
| 1password | YES | OP_SERVICE_ACCOUNT_TOKEN env — machine-level credential |
| aws-secretsmanager | YES | AWS_ACCESS_KEY_ID + secret env + SigV4 HTTPS — canonical machine auth |

**Consequence (the "fits all 8 secrets" gate)**: 5 of 8 providers fully support Periodic refresh as NT SERVICE. 3 (DPAPI, CredMan, Pwsh SecretManagement) are per-user-bound — these three MUST default to `refreshPolicy: CreatedOnce` and the GUI MUST refuse to set them to Periodic with an i18n error pointing to the docs ("this provider cannot be refreshed by the machine service; choose a cloud/HCP provider for periodic refresh, or keep this mount as fetch-on-launch"). This honours both A5 ("point and guide" for per-user-provider surfaces) AND A6 (machine-level service for backends that SHOULD have runtime lifecycle management).

**All-Windows gate**: NT SERVICE accounts exist on Win10 22H2+ and all Win11 editions per Microsoft Learn. No AD, no PowerShell step, no manual LSA secret needed. Installer sets service bin-path + ServiceArgs + ACL via sc.exe + icacls. Win11 Home included (Home ships SCM, just not some Group Policy surfaces).

**A9 (Phase D — REVISED, "securest option" gate)**: Phase D is NOT deleted (the static-credential-on-disk leakage surface is real: backups, disk imaging, ransomware reading the file). Phase D also CANNOT use Windows Hello passkey as the blueprint wrote — A8 service model runs in session 0 with no desktop, no biometric UI. The "securest option" that fits A8 is: replace static long-lived credentials with SHORT-LIVED CERTIFICATE-BASED auth, with the bootstrap cert stored in the SERVICE identity's certificate store (per-service, not per-user). Three provider-specific approaches confirmed feasible from research:

- **Vault KV2**: Vault Agent Auto-Auth via AppRole OR TLS cert auth. Service holds an AppRole role_id + secret_id (rotatable) OR a client cert in its own `Cert:\LocalMachine\My` store; auto-auth fetches short-lived Vault token on service start and on token TTL expiry. The static VAULT_TOKEN env var is ELIMINATED. Cite: developer.hashicorp.com/vault/docs/auth/approle.
- **Azure Key Vault**: service-principal certificate auth (NOT secret). Cert stored in `Cert:\LocalMachine\My` readable only by `NT SERVICE\\EnvManagerService` ACL. Service loads cert at start, does SP cert-based OAuth to Azure AD for short-lived token, token cached in-memory only with 5-min buffer (matches today's Azure provider token cache). The static AZURE_CLIENT_SECRET env var is ELIMINATED, AZURE_CLIENT_ID + AZURE_TENANT_ID + cert thumbprint replace it. Cite: learn.microsoft.com Azure AD app certificate authentication.
- **AWS Secrets Manager**: IAM Roles Anywhere with attestation cert. Host has an intermediate CA cert (user installs once); the service presents its cert to get short-lived AWS credentials. NO static AWS_ACCESS_KEY_ID file. Cite: docs.aws.amazon.com/iam Roles Anywhere.

Vault + Azure paths are documented as production-ready for session-0 services. AWS Roles Anywhere is documented for non-EC2 servers but less-common on standalone Win11 — Phase D ships Vault+Azure Paths only, AWS Roles Anywhere as a documented future addition (blueprint explicitly defers).

**Bootstrap credential storage**: per-service cert store `Cert:\LocalMachine\My` with private key ACL keyed to the per-service SID; the private key is NOT exportable. A backup of the cert+key is the user's responsibility and is normally unnecessary because the cert can be re-enrolled from the cloud (Azure AD app new cert, Vault new client cert). This eliminates the disk-file leakage surface for static credentials without violating the A8 service model.

**What stays static on disk**: provider-agnostic config (Vault URI, Azure Key Vault URI, AWS region) live in `service.config.json` accessible by the service identity. None of these are credentials; leaking them discloses infrastructure location, not access.

**GUI for Phase D**: NOT a per-launch Windows Hello dialog. It's a one-time certificate provisioning dialog in Settings: user picks Azure/Vault/SOPS-age/AWS path, dialog runs the enrollment flow ONCE under the user's interactive identity, then hands the resulting private key to the service identity's cert store (the cert enrolls in HKLM Cert store, service reads it in session 0 afterward). The user interactive flow is "enroll" not "authenticate per launch". After enrollment, no human interaction needed for the service to keep refreshing tokens — the cert is non-exportable, only the service SID can use it.

**A10 (Phase E — unified audit ledger + drop wrap-key escrow, replace with provider-native recovery)**: User accepted "unified audit + drop wrap-key escrow" but asked for a better replacement that fits A2-A9 fully. Research confirmed wrap-key escrow's recovery role is provably empty under A5/A6/A8:

- **5/8 cloud providers (Vault/Azure/AWS/1Password/SOPS)**: the secret material never lives locally to be lost. A8 service identity uses Phase D cert-based bootstrap, and if local state is destroyed (disk crash, OS reinstall, ransomware encrypted the install) the recovery flow is: install fresh env-manager MSI → re-enroll the cert at cloud (one-time interactive GUI step) → service re-fetches the secrets from the cloud backend. The audit ledger tells the operator which mounts to re-establish. No wrap-key escrow needed — the source of truth IS the cloud backend.
- **3/8 user-bound providers (DPAPI-CurrentUser/CredMan/PowerShell-SecretStore)**: per-user-bound, fetch-on-launch. If the user's master key is lost (profile corruption/Y-Key wiped), the mount is lost by design (A5 "point and guide" — Env Manager does not compete with the OS user-data-recovery feature; the user re-creates the mount with a fresh value). Wrap-key escrow would have allowed cross-user decryption — A8 explicitly removed cross-user read access via NT SERVICE identity.

**Three concrete replacements for wrap-key escrow**:

1. **Unified Audit Ledger as authoritative event source** (PRIMARY replacement). Today's audit.json + secretMount.json metadata + service reconcile log are three sources of truth. Phase E collapses them into a single append-only ledger `%ProgramData%\\EnvManager\\audit-ledger.jsonl` (JSON Lines, append-only, atomic per-line write). Every mount lifecycle event (create/refresh/rotate/delete/recover/launch) emits ONE line with: { eventId(uuid), timestamp, actor(CLI|GUI|service|launch), provider, mountId, profileName, action, observedBefore, observedAfter, reason }. A user running `env-manager-cli audit list --mount <id>` gets the entire lifecycle of that mount in chronological order. Recovery = replay the ledger to know what should exist.
2. **Mount Survival Kit export** (one-click per-mount export, user invokes from GUI Settings → Backup). NOT a backup of the plaintext. Exports: the audit-ledger events for that mount + a one-time re-enrollment token + the cert-thumbprint the service should present. The kit is itself an AES-GCM-encrypted archive (key derived from user-interactive Windows Hello, machine-bound) that only the same Win11 machine + same user can re-open — exactly like current `profile export-secrets` but bound to the audit-replay path, not to a wrap-key recovery server.
3. **Provider-native re-enroll UX in the GUI** (POST recovery gap). If local state is fully destroyed, the user installs env-manager on a fresh machine, opens GUI, picks "Recover mounts from audit ledger" → GUI prompts for the re-enrollment cert (Azure SP cert import / Vault client cert import / etc.) → emits an audit-ledger "recovery.open" event with the new machine GUID → service re-fetches each mount from its cloud backend.

**What does NOT exist anymore**: any path that allows a "trusted third-party wrap-key holder" to decrypt user secrets. There is no master wrap key, no escrow operator, no central recovery service. The audit ledger is the authoritative state; the cloud backend is the authoritative store; the user's re-enrollment cert is the only proof of authority.

**XML schema additions**: audit-ledger schema is JSONL (not a single JSON document), tail-appended, rotated at 100MB. Events are versioned by `ledgerSchemaVersion`. Event IDs are GUIDs (not sequential ints) so concurrent CLI + service writers can't collide.

**A11 (final version cadence — 4 phases, B+C merged into v0.9.0)**: User selected Option 2. New phase map supersedes blueprint's original A-E linear sequence:

| Version | Phase | Scope | Dependent on |
|---|---|---|---|
| v0.8.0 | Phase A | SecretMount schema v2, one-shot migration, C# fsync in AtomicWriteProfiles, fields `refreshPolicy`/`refreshIntervalSeconds`/`bootstrapCertThumbprint` ready in schema (even though service is not yet shipped), audit.json stays as-is this version | — |
| v0.9.0 | Phase B+C merged | SecretStore controller + three-level capability whitelist ({cmd, scope?, mountId?}) + CLI `service` subcommand thin gateway + Named Pipe IPC contracts (EnvManagerService / EnvManager.Background pipes, EnvManagerIpcRequest/Response) + `env-manager-service.exe` Rust binary with `--mode=service|background|cli` + `NT SERVICE\\EnvManagerService` virtual account registration via MSI + idempotent periodic full-scan reconcile loop + GUI as control panel (start/stop service, mount health view) | Phase A schema v2 |
| v0.9.5 | Phase D | Finite cert-bootstrap: Vault AppRole / client cert + Azure SP cert auth (cert in `Cert:\\LocalMachine\\My` keyed to NT SERVICE SID). AWS IAM Roles Anywhere deferred to future (documented in docs/secret-providers-guide.md). GUI one-time cert enroll UX. `bootstrapCertThumbprint` field activates. BEGIN to migrate from static env-var auth path (secrets file) to cert path; env-var path remains as fallback for users not enrolled | Phase B+C service identity in place |
| v1.0.0 | Phase E | Unified audit ledger `audit-ledger.jsonl` (append-only, GUID eventId, 100MB rotation) + Mount survival kit export (AES-GCM encrypted archive bound to user+machine) + GUI "recover mounts from audit ledger" UX + existing `audit.json` retired after migration script runs once | Phase A-D all shipped |

**Migration/deprecation chain**: v0.8.0 ships schema v2 + new fields nullable-default (CreatedOnce + null thumbprint). v0.9.0 turns those fields active when service is installed. v0.9.5 sets cert path as default for Vault/Azure new mounts (env-var fallback retained). v1.0.0 migrates existing `audit.json` entries into the new jsonl ledger in one script run, then `audit.json` becomes read-only (no new writes).

**Release-gate risk matrix (A12 preliminary, from pwm sonar research 2026-08-02)**:

Seven domains, each with 3-5 concrete failure modes and release-gate tests. These are NOT my own intuited list — they are the documented failure patterns for Windows service + named-pipe IPC + secret-refresh loop + cert bootstrap + append-only ledger + schema migration + existing-codebase DPAPI/PATH/cache.

DOMAIN 1: Windows service lifecycle
- F: SCM start timeout under heavy boot load → T: validate service start completes within SCM timeout at boot + elevated load
- F: Service crash loop after early unchecked exception → T: inject startup crash, verify fail-fast after N retries not infinite loop
- F: Out-of-order boot (dependent service not up yet) → T: declare service dependencies (RpcSs, Tcpip), verify SCM waits for deps
- F: Stop event during user mid-launch → T: send stop during reconcile tick, verify tick completes cleanly OR rolls back half-written mount metadata

DOMAIN 2: Named pipe IPC security
- F: Pipe DACL misconfig allowing cross-user read/write → T: verify (icacls on \.pipeEnvManager.*) denies non-authorized accounts at install
- F: Impersonation privilege escalation (client with SeImpersonate sets pipe to self-elevate) → T: verify SetImpersonationToken rejected or scrubbed
- F: Pipe squatting (attacker pre-creates pipe before service starts) → T: test service refuses to bind to pre-existing pipe OR recreates with mandatory DACL
- F: Stale connection read across reconnect → T: after client disconnect, next connection does not see residual bytes

DOMAIN 3: Periodic reconcile loop
- F: Vault KV2 lease TTL < 300s interval (lease expires before service tick) → T: simulate 200s lease + 300s tick and verify lease renewal DOES NOT expire; either tighten refreshIntervalSeconds < lease or fail-closed
- F: Cloud provider 429 rate-limited burst on herd (100 mounts all due at service restart) → T: inject 429 and verify exponential backoff (2^n up to 60s) + concurrent cap (max 5 concurrent provider calls)
- F: Clock skew between client and provider (provider NTP-synced, client drifted) → T: simulate 5min clock skew, verify refresh uses provider-reported lastRotatedAt not local now()
- F: Partial-tick failures (5 mounts refreshed, 3 errored, 2 skipped) → T: verify partial tick emits per-mount failure events to audit ledger; DOES NOT advance lastFetchedAt on failed mounts

DOMAIN 4: Certificate lifecycle
- F: Cert expires without rotation → T: rotate cert 30 days before expiry, verify service continues without restart
- F: GPO/admin re-enroll wipes private-key ACL → T: after ACL reset, service reports mount unreachable via audit + emits re-enroll-required user notification (does not silently fall back to static env var)
- F: Cert cloned to another machine via CNG raw export attack (Mimikatz) → T: verify boot manually tests private-key presence is per-machine bound (optional key attestation import); document in secret-providers-guide that local-admin attacker defeats cert binding by design
- F: Cert missing on service start after Windows upgrade / reset ⟶ T: verify service start-up emits "no-cert" event to audit and terminates cleanly without crashing loop

DOMAIN 5: Audit ledger tamper-resistance
- F: Append-only not integrity (silent insertion of forged events) → T: hash-chain verification: each line's sha256(prev || current) verified at file open; tampered file fails parse
- F: Unrecognized actor injecting events (unauthorized process writes to ledger.jsonl) → T: NTFS ACL on ledger file denies non-service non-CLI writers; audit has mandatory actor field validated against in-process identity
- F: 100MB rollover race (rotation truncating the active file while another writer writes) → T: rotation writes to new file + atomic rename; verify no events lost during continuous-write benchmark
- F: Replay attack using old eventId → T: eventId is monotonic GUID + per-machine boot counter; replayed lines fail parse if eventId observed twice

DOMAIN 6: One-shot schema migration
- F: Migration crash leaves profiles.json.secretVariables empty AND secretMount.json orphan → T: migration script writes secretMount.json FIRST (after .env_bak backup of profiles.json), then rewrites profiles.json secretVariables to []; on abort restore from .env_bak
- F: Rollback restores profiles.json but forgets secretMount.json → T: rollback script deletes secretMount.json explicitly; test fails if any mount entry remains after rollback
- F: Service starts reading secretMount.json BEFORE migration atomic-write commits → T: service refuses to start at v0.8.0 (service is not installed yet at v0.8.0); at v0.9.0 service install includes schema-version check, refuses if secretMount.json schemaVersion != 2
- F: Per-user profiles.json with secretVariables not migrated for non-default user → T: test migration runs per-user (%LOCALAPPDATA%\EnvManager\), not once per machine

DOMAIN 7: Existing codebase regressions
- F: DPAPI _EnvManager_disabled backup orphan after uninstall leaves dead registry entry → T: MSI uninstall removes any disabled backup entries; release-gate test runs fresh install + toggle multiple + uninstall + verify registry empty of _EnvManager_disabled
- F: Race between RegistryMutation mutex (C# side, 30s timeout) and write_atomic timing (service writing secretMount.json) → T: integration test spawns CLI set + service reconcile simultaneously, verifies no torn write in either file
- F: Frontend cache returns stale mount metadata within 5s window after service updates it → T: service emits event via Tauri event, GUI invalidates cache on event signal BEFORE next 5s poll

## Additional risk domains (A13 supplementary research, pwm sonar 2026-08-02)

User-identified gaps that I (the agent) had wrongly classified as "ops events not release-gate". User correction stands: all four are release-gate research.

DOMAIN 8: Cert-enroll cancellation mid-flight cleanup
- F: Cert issued in cloud, private key not yet installed in Cert:\LocalMachine\My → T: cancel-after-cloud-issue flow leaves cloud cert object orphaned (revoke or accept cloud object as no-op since it is unused; preference: revoke to avoid orphan)
- F: Private key installed, service not yet notified "ready" → T: abort path removes private key from cert store AND revokes cloud cert (NOT just removes binding — private key without an entry in service-store is an invisible secret sitting on disk)
- F: Service started using cert, user revokes it in cloud GUI manually → T: service detects on next reconcile tick (provider returns 401) and emits re-enroll-required audit OR reverts to env-var fallback path WITH i18n user-visible banner asking for re-enroll
- F: Enroll temp files / tokens / webhook subscriptions leftover → T: enroll temp dir under %ProgramData%\EnvManager\.enroll-tmp\ is wiped on every service start (regardless of any in-flight enrollment) ; no cross-restart session state survives in temp (state is ONLY in cert store + audit + secretMount.json)

DOMAIN 9: MSI in-place major-upgrade transient outage
- Documented MSI sequence: InstallValidate → StopServices → RemoveFiles(old) → InstallFiles(new) → StartServices (WiX standard actions). Cited: learn.microsoft.com MSI major upgrade sequence.
- F: Service stopped for upgrade while user has a launch profile child process running → C: child process (exe launched via profile launch earlier) SURVIVES — it has its own env_block, no parent dependency. SCM StopServices only signals our service's main thread; child is not killed. BUT: future CLI mount refresh via Named Pipe FAILS until StartServices returns. T: release test (1) starts profile launch, (2) starts MSI upgrade, (3) verifies child runs uninterrupted, (4) verifies CLI refresh returns "service restarting, retry in N seconds" error code instead of falling back to direct provider Decrypt.
- F: SecretMount.json schema version mismatch (old service stops, new MSI ships new schema) → T: schemaVersion field check at StartServices; if file is older schema, runs in-place MIGRATION first (Phase A logic) before reconcile loop starts; if migration fails the service refuses to start and MSI rollback fires
- F: profiles.json mid-write at StopServices time → T: write_atomic temp+fsync+rename (A3 fix) guarantees profiles.json is either full old or full new; StopServices is read-safe (service holds no long-lived write lock between ticks)

DOMAIN 10: Memory pressure / startup timeout under resource exhaustion
- Microsoft documented SCM timeout: default 30000ms, raises event 7000/7009. Cited: learn.microsoft.com windows/win32/services service timeout.
- F: Startup under 99% commit pressure takes > 30s; SCM marks service FAILED → T: service declares dependency on RpcSs + Tcpip (so boot ordering is稳定); Reduce cold-start work (full scan is deferred 30s after StartServices returns so SCM doesn't time out)
- F: OOM during first reconcile scan → T: each reconcile tick has bounded memory — never loads all mount plaintext at once; process one mount, clear plaintext, next mount. Test injects low-committed-memory scenario and verifies "tick fails-mount-by-mount-skipping-failed-not-crashing" semantics.
- Recovery: SCM auto-restart is relied on; service MUST never call panic! on OOM — graceful Tokio task spawn error caught, mount skipped, audit eventId emitted, tick continues.

DOMAIN 11: IPC endpoint name conflict — multi-session / TS / machine rename
- Pipe namespace note: Windows pipes live in \\.\pipe\<name> from session 0; per-session pipes use \\.\pipe\<name> from the user session (different session has different pipe table) OR "Global\\pipe\<name>" for session 0 only. Cited: learn.microsoft.com/windows/win32/ipc/named-pipes + stackoverflow 4303154.
- F: Service in session 0 creates "Global\\pipe\\EnvManager.Service"; CLI in user session looks for it under user session pipe (not Global prefix) → mismatch → CLI cannot find pipe → T: CLI ALWAYS searches both \Session\<sid>\pipe\ AND Global\\pipe\; service registers under Global\\pipe\ only; documented in AGENTS.md hard boundary
- F: RDP second user logs in, their env-manager GUI tries to use service which is per-machine (one service instance per machine) → C: this is BY DESIGN — service is machine-level; CLI of the second user connects to the same Global pipe, sends its own per-user CLI identity, service responds per requested mount. T: test spec'd — second RDP user CLI mount refresh must not see mount metadata of first user's user-bound (DPAPI/CredMan/pwsh SecretManagement) mounts; only machine-level (Vault/Azure/AWS/1Password/SOPS) mounts are shared.
- F: Machine rename after install → pipe name does NOT contain machine name (verified — Global\\pipe\\EnvManager.Service is machine-agnostic), so rename is a no-op for pipe routing. T: no-op regression: machine rename does not break service.
- F: Locale affects pipe name encoding (Unicode pipe names) → C: pipe name "EnvManager.Service" is ASCII-only, locale-independent. T: no-op regression test under zh-CN/ja-JP/de-DE culture.


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

- **Standard README Compliance**: README.md adheres to the `standard-readme` npm specification: required sections in fixed order (Title, Short Description, Table of Contents, Security, Install, Usage, Extra Sections, API, Maintainers, Contributing, License), optional sections (Banner, Badges, Long Description, Background, Thanks). Bilingual variant `README_CN.md` mirrors structure with translated headings. i18n file naming: `README.md` is English, `README_CN.md` is Chinese.
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
A ef struct wrapping a decrypted secret value. Zeroes the underlying char[] on Dispose() to minimize plaintext lifetime in heap memory. Used at ProfileRevealSecret and ProfileLaunch decrypt sites. The Rust equivalent uses the secrecy crate's SecretString with zeroize on drop.
_Avoid_: secure string, encrypted wrapper, secret holder

**Redaction Vocabulary**:
The canonical set of 22 secret-bearing string patterns recognized by all three tiers. Patterns: Bearer, token=, Token=, password=, Password=, setx, OP_SERVICE_ACCOUNT_TOKEN=, VAULT_TOKEN=, AWS_SECRET_ACCESS_KEY=, AWS_SESSION_TOKEN=, client_secret=, connection_string=, subscription_key=, api_key=, apikey=, client_id=, tenant_id=, access_token=, refresh_token=, Authorization:, X-Vault-Token:, x-api-key:. A new pattern must be added to all three code sites simultaneously.
_Avoid_: mask list, secret patterns, redaction rules