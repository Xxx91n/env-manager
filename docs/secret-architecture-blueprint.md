# Env Manager - Secrets Architecture Blueprint

- Version: 1.0 (2026-07-26)
- Status: Strategic audit + multi-phase roadmap
- Owner: Env Manager core team
- Audience: project maintainers, downstream agents, deployers
- Source research: D:\NDM\机密env研究.md, project hard-boundary ledger (AGENTS.md v0.7.5), community reference repos (hashicorp/vault 36k stars, getsops/sops 22k, external-secrets/external-secrets 6.7k), PowerShell SecretManagement/SecretStore upstream README.

This is a strategic blueprint. It is not a sprint plan. Each phase names the problem, the proposed architectural pivot, the user-visible contract, and the AGENTS.md hard boundary it adds or revises. Implementation work is tracked separately against the bullet list at the end.

---

## Abstract

Env Manager currently ships 8 secret providers (DPAPI currentUser, Windows Credential Manager, PowerShell SecretManagement, HashiCorp Vault KV2, SOPS, Azure Key Vault, 1Password, AWS Secrets Manager) wired behind a single `ISecretProvider` five-method interface (`Name`/`Encrypt`/`Decrypt`/`CanRotate`/`Rotate`/`Delete`). Secrets are scoped to **Launch (local) profiles only**; the GUI and CLI both reject Global secrets at the entry point; `IsProfileApplicable` doubly refuses any profile containing `SecretVariables` from touching the user registry; `profile launch` decrypts the secret in the launcher process and spikes the child env block with the plaintext. Provider activation is preflight-checked at config time (v0.7.5), and the pwsh provider auto-probes its module + vault before any `Set-Secret`.

This is a defensible v0.7.x baseline, but it is **fundamentally still a single-machine, single-process-time, single-user secret vault**. The community reference projects all converged on a different shape, and the secrets domain has shifted underneath us. This blueprint reconciles our position with that shift and lays out a phased path to an industrial-grade secrets surface without abandoning the current investment.

---

## Comparison with community reference projects

Three reference projects dominate the secrets-management design space and all three converge on the same shape:

| Project | Shape | The pivot Env Manager is missing |
| --- | --- | --- |
| `hashicorp/vault` (36k stars) | Server authority + lease + audit + dynamic secret generation | We let the caller pull a secret and then never know when it expires; no lease, no revocation. |
| `getsops/sops` (22k stars) | Encrypt-in-place, decrypt-as-needed, GitOps-native | We collaborate on plaintext-in-process but never encrypt the source artifact at rest in the repo. |
| `external-secrets/external-secrets` (6.7k stars) | External authority + refresh policy (Periodic / CreatedOnce / OnChange) + reconciliation loop | There is no reconciliation loop in Env Manager. A secret pulled once is stuck in the process env block for that process lifetime. |

The Microsoft PowerShell SecretManagement upstream note (see upstream README, January 2025 feature-complete declaration) is the strongest external signal: the maintainers are no longer adding features because the secrets domain has moved to FIDO2 passkeys, hardware security keys, Microsoft Entra ID, and federated credential systems. Our pwsh provider must therefore be maintained as **Best-Effort extension vault**, not as the long-term primary.

Commercial password-manager CLIs (`op`, `bw`) speak the secrets language well enough to consume but do not give us a refresh contract; they assume a CLI invocation per secret read and expose no streaming/lease primitive.

---

## Current capabilities (as of v0.7.5)

What the project already does well. This is the inventory to preserve while we add capability; do not silently regress any of these.

1. **Zero-knowledge at rest**: profile `secretVariables` are stored only as envelope `{ provider, version, targetName, createdAt/encrypted }`. Plaintext never lands in `profiles.json`, the registry, or logs. Audit records use `<redacted>`/`<encrypted>` markers.
2. **Launch-only contract**: secrets live only on Launch profiles, which `env_clear + inject` into a child process. They are never written to the registry, never broadcast `WMSETTINGCHANGE`.
3. **Cross-process serialization**: write operations acquire `Local\EnvManager.RegistryMutation` mutex + Rust `CLI_RWLOCK` + frontend serialization chain. v0.7.5 added provider-change confirmation modal + activation preflight so silent cross-provider migration is impossible.
4. **Triple-guarded protected invariants**: built-in system variables and PATH entries from `protection.defaults.json`, custom user locks, all gated at every entry point (toggle/delete/set/rename/change-scope/path remove/path rename).
5. **Per-provider RAII**: network providers (Vault/Azure/AWS) enforce HTTPS, 10-15s timeouts, and fail-closed on 403/404. Subprocess providers (sops/op/pwsh) apply `CREATE_NO_WINDOW` and 30s timeouts. AWS SigV4 is implemented in-process without AWS SDK dependency. Azure token caching has 5-minute expiry buffer.
6. **Recoverability**: rotation/re-export/re-import all record audit entries with counts but never values. Live test harness (test-with-restore.ps1) snapshots HKCU+HKLM and reconciles drift, motivated by a real prior incident where a test clobbered the user system PATH.
7. **CLI activation preflight (v0.7.5)**: `SetActiveProvider` runs a sentinel Encrypt/Decrypt/Delete round-trip on the candidate provider before committing the config, so a missing module/credential surfaces at config time rather than at the next add-secret.
8. **pwsh module + vault auto-registration (v0.7.5)**: `PowerShellSecretManagementProvider.EnsureSecretManagementAvailable` probes `Get-Module -ListAvailable Microsoft.SecretManagement` and `EnsureVaultRegistered` auto-`Register-SecretVault EnvManager -ModuleName Microsoft.SecretStore -AllowClobber`. stderr is unwrapped by `StripClixml` (parses `<S S="Error">`, restores `_x001B_/_x000D_/_x000A_/_x0009_` escapes, strips ANSI sequences).

---

## Current limitations

Mirror the v0.7.5 capability list. Each limitation here motivates a phase later in this document.

### L1: Secret lives for exactly one process lifetime

`profile launch` decrypts each secret into the child process env block and the child either outlives the secret's validity or dies first. There is no Periodic refresh, no OnChange reconciliation, no lease to revoke. If the credential is rotated upstream (e.g. AWS IAM key rotated) the process keeps running with a stale secret until it is restarted.

### L2: Single-machine binding is implicit, not negotiated

DPAPI-CurrentUser is bound to the Windows user SID; CredMan is the same. Network providers (Vault/Azure/AWS) carry the user's token at the moment of `profile launch`. There is no concept of a device authority, no `device-id`-scoped secret, no enclave attestation. A user who copies their `profiles.json` to a second machine can still use the DPAPI envelope but `Decrypt` fails for crypt32 reasons; the platform should make this explicit rather than let users discover it at decrypt time.

### L3: Plaintext lives in process memory for the launcher's lifetime

Plaintext is loaded into the launcher process memory to spike the env block for the child. The child then inherits the plaintext into its own memory for its own lifetime. There is no SecureZeroMemory, no `CryptMemzero`, no `RestrictedToken` to constrain the child. .NET does not pin strings, so the GC may keep copies alive arbitrarily long.

### L4: No streaming / fetch-on-demand / dynamic secret

Vault supports dynamic secrets (database credentials generated on demand, revoked after lease expiry). AWS Secrets Manager supports rotation Lambda and pending/current/staged. Azure Key Vault has secret versions. We never use any of those - we treat every secret as a static `name -> value` pair fetched once. This wastes the capability that justified adopting those providers in the first place.

### L5: Secret metadata is not first-class

`SecretEnvelope.TargetName` is the only field that pins the external secret's location. There is no `ExpiresAt`, no `RenewedAt`, no `SourceType` ("static" vs "dynamic"), no `RotationPolicy`, no `LastAccessedAt`. The CLI audit captures the operation name + counts but not the per-secret lifecycle. A user recovering from a leaked credential has no way to ask "which profile last accessed this credential and when".

### L6: No separation between Secret (a network-fetched value) and Credential (an OS-bound identity)

The 8 providers are unified under one interface but they speak different domains. DPAPI/CredMan/PS SecretStore are **secret stores** (you push a value in). Vault KV2 / Azure Key Vault / 1Password / AWS Secrets Manager / SOPS are **secret backends** (you fetch a value they own). SOPS is its own category again - the "value" is the encrypted artifact in the repo. Forcing them into one interface means each provider ends up pretending to be a write-target when some are read-only by design. The interface should not collapse those.

### L7: Per-profile secret provenance, not per-secret provenance

Rotation/re-export/re-import operate on whole profiles, so a single leaked secret cannot be rewrapped without touching every other secret in the same profile. Industrial deployments want per-secret operations.

### L8: No governance/listing/health API

`profile secret-provider list` names providers and (un)availability but does not surface "total secrets / per-provider / per-profile / health". A blue-team operator has no in-process surface to inventory, a maintainer has no surface to find stale envelopes. The audit file is operation-log-shaped, not state-shaped.

### L9: Recovery economics

`profile export-secrets` produces a DPAPI-CurrentUser-encrypted backup - portable **within the same Windows user account** only. Restore verifies by trial-decryption but does not catch the common failure mode where the Windows user profile was rebuilt and the DPAPI master key is now unreachable. There is no escrow / recovery wallet / cross-machine wrap.

### L10: Agentic surface is over-trusted

The `agents` JSON command exposes the write/read classification. The design trusts that the agent reads that contract. An LLM-driven agent that ignores it and fires a `set` / `delete` in parallel with the GUI has no guard beyond the mutex, and a `toggle` against a system variable is contained only by the in-process isProtected guard. The agent path needs a capability-based (not command-string-based) authorization layer.

---

## Strategic pivot

The architectural decision this blueprint commits to. Each subsection names the direction, the rejection it implies, and the reason.

### P1: Decouple "provider" from "secret mount"

Today `Profile` owns a `secretVariables: List<string>` whose names index into `Profile.Variables` whose values are provider envelopes, and the active provider is a single key stored in `secret-providers.json`. We propose a new `SecretMount` abstraction owned by a `SecretStore` (see P3) rather than by the Profile. A Profile references a SecretMount by id, not by value. A SecretMount has its own refresh policy, lease, and provenance. The Profile becomes a co-orchestrator, not a vault. This removes L5/L7.

Rejected alternative: keep secrets inside the Profile but add the metadata fields. We considered this and decided against it because SecretMount needs to be referenced by multiple Profiles (promote cross-profile reuse) and needs its own lifecycle that does not require un-applying every Profile that refers to it.

### P2: External authority + reconciliation loop, modeled on External Secrets Operator

A Secrets coordinator (see P3) runs as a background service (`env-manager-service.exe` with `--mode=service` or `--mode=background`) that periodically reconciles each SecretMount against its provider, refreshes the envelope if needed, records the audit, and surfaces new state to subscribers (the GUI, the CLI on next read). The reconcile contract is `spec.refreshPolicy: Periodic | CreatedOnce | OnChange` with `spec.refreshIntervalSeconds` default 300. This removes L1/L4.

Rejected alternative: on-demand fetch only, no reconcile loop. This is the current behaviour and it leaks stale credentials. On-demand only belongs at the very-low-end of provider sophistication (DPAPI) and is the wrong default for the network providers.

### P3: Replace single `secret-providers.json` with a `SecretStore` controller

A new `SecretStore` owns (a) the provider registry (still in-process, no IPC), (b) the per-secret mounts, (c) the reconcile scheduler, (d) the health surface. The CLI's `profile secret-provider set/list` becomes `secret-store set-default-provider/list`. `profile add-secret/edit-secret/remove-secret/reveal-secret` keep their names but re-route internally to the SecretStore. The Audit file acquires a SecretStore shaped sub-tree in addition to the existing Profile audit. This removes L6/L8.

Rejected alternative: keep `profile secret-provider` as the user-facing command shape. We considered this and decided against because the user mental model is "I have a SecretStore with N providers and N mounts; I tie mounts to launch profiles". Keeping the provider-management under `profile` obscures that.

### P4: Passkey / hardware-bridge as a first-class identity layer, not a provider

A passkey (Windows Hello / FIDO2 / TPM-backed) is **not** another secret to be encrypted; it is the device-side assertion of identity. We propose a separate `IIdentityProvider` interface that emits attestation assertions used by network providers (Vault/Azure/AWS) to negotiate auth without static env vars. The first implementation is Windows Hello passkey as a 2FA alternative to `AZURE_CLIENT_SECRET`. This is also exactly the direction Microsoft's upstream note pointed at for PowerShell SecretManagement. This removes L2.

Rejected alternative: treat passkey as a provider. We considered this and decided against because passkey is identity, not data; collapsing them into `ISecretProvider` would repeat the L6 mistake with worse consequences.

### P5: Per-secret rotation policy driven by the SecretStore, not by `Rotate-all`

`profile secret-provider rotate` re-encrypts everything today. We propose per-secret `Rotate(id, newProvider? = keep)` with an on-disk `secretMount.json` (per-mount metadata) capturing `lastRotatedAt` and `nextRotationDueAt`. The reconcile loop drives rotation based on policy without user intervention. Auto-rotation applies only to providers that declare `CanRotate=true` (Vault KV2, Azure, AWS, 1Password do). This removes L7/L9.

### P6: Capability-scoped agentic surface

CLI commands gaining a new `--capability` mental layer alongside the existing write/read classification. Capabilities are named (`registry.write.user`, `registry.write.system`, `secret.read`, `secret.write`, `secret.rotate`, `profile.apply`, `profile.launch`). The `agents` JSON already describes commands; we add a per-command `capabilities` array. The configuration `secret-providers.json` is extended with `agentCapabilities` whitelist. The frontend still works without any caps (back-compat) but a deployment that ships `agentCapabilities` enforces them at the CLI entry point. This removes L10.

Rejected alternative: command-string allow-list. Already what AGENTS.md documents and already defeated by the catastrophic incidents catalogued in v0.7.1; we need semantic cap, not syntactic allow-list.

---

## Architecture delta - what stays

- `ISecretProvider` five-method interface stays **as-is**. Every provider implementation we have is reusable; only the registry layer above them changes.
- Launch profile `env_clear + inject` semantics stay. The launch path is the proven surface and we keep its contract forever.
- `IsProfileApplicable` refusing profiles with `SecretVariables` stays. With the new `SecretStore`, this rule relaxes to `IsProfileApplicable` rejecting profiles with **unresolved mounts** instead - which means the same invariant survives the migration.
- `profile launch` continues to be the only path that emits plaintext to a child env block.
- The pwsh provider's `EnsureSecretManagementAvailable` + `EnsureVaultRegistered` + `StripClixml` preflight chain stays as the canonical pattern future providers will imitate.
- The host-incident-shaped test harness (`test-with-restore.ps1`, `snapshot-host-env.ps1`) stays. No phase may weaken it.
- The AGENTS.md hard-boundary ledger format stays. We extend it, we do not rewrite it.

---

## Architecture delta - what changes

- The on-disk schema introduces `secretMount.json` alongside `profiles.json`. The Migration reads existing `SecretVariables` envelopes, attaches default `refreshPolicy: CreatedOnce` and `lastRotatedAt: updatedAt-via-audit`, and writes the new file in one transaction. Rollback = delete the file.
- `secret-providers.json` learns `agentCapabilities` and `defaultRefreshPolicy`. Backward-compatible (defaults filled in when missing).
- `SecretEnvelope` grows from `{ provider, version, targetName, createdAt }` to `{ provider, version, targetName, createdAt, expiresAt, rotationPolicyId, schemaVersion: 2 }`. v0.8 envelopes have only the first four; the new fields are auto-prefilled on next read.
- New CLI commands: `mount list/show/add/remove/refresh/rotate/set-policy`, `secret-store list-providers/set-default/health/audit`. Secret-oriented `profile *` commands become aliases for back-compat.
- The agent JSON under `agents --json` adds the `capabilities` array per command and a new `authZ` field defined per the project's `agentCapabilities` config.
- A new optional background reconcile service is shipped as a portable `env-manager-service.exe` that the GUI starts on demand and Windows Service Manager can install. This service is gated by a Settings toggle - off by default in v0.8, on by opt-in.

---

## Phased roadmap — SUPERSEDED

> **This section is superseded by ADR 0001 and the Decision Summary.**
> The grill-with-docs session on 2026-08-02 revised four of the five phases after external research and codebase cross-validation. The authoritative decisions now live in:
>
> - **ADR**: [docs/adr/0001-secret-architecture-revision.md](adr/0001-secret-architecture-revision.md)
> - **Decision Summary**: [docs/secret-architecture-decision-summary.md](secret-architecture-decision-summary.md)
> - **Full interview context + risk matrix**: [CONTEXT.md](../CONTEXT.md)

### What changed and why

The original Phase A-E roadmap below assumed an `secrets-agent.exe` reconcile process, a passkey/Windows Hello identity layer, and a wrap-key escrow recovery mechanism. The grill session proved each of these assumptions does not match the project's actual constraints (single-machine standalone Win11, single-user developer release, 20-100 mount workload, no central operator, session-0 service cannot surface biometric UI). The revised four-version cadence is:

| Version | Phase | Ship criteria |
| --- | --- | --- |
| v0.8.0 | A | SecretMount schema v2 + one-shot migration + C# fsync + new nullable fields (refreshPolicy, refreshIntervalSeconds, bootstrapCertThumbprint). audit.json unchanged. |
| v0.9.0 | B+C merged | env-manager-service.exe Rust binary, NT SERVICE\EnvManagerService, Named Pipe IPC, CLI service gateway subcommand, periodic full-scan reconcile, GUI as control panel, three-level capability whitelist. |
| v0.9.5 | D | Cert bootstrap: Vault AppRole/client cert, Azure SP cert. AWS Roles Anywhere documented but deferred. Env-var fallback retained. |
| v1.0.0 | E | Unified audit-ledger.jsonl (append-only, hash-chained, 100MB rotation), Mount survival kit export, GUI recover-from-ledger, audit.json retired after migration. |

### Key reversals from the original blueprint

- **Phase C**: the original `secrets-agent.exe` standalone process is rejected. It is now a Windows system service (`env-manager-service.exe` with `--mode=service`) under `NT SERVICE\EnvManagerService` virtual service account. In-process tokio task inside the GUI is also rejected (GUI close = reconcile stops). See ADR 0001 A5/A6/A7/A8.
- **Phase D**: the original passkey/Windows Hello identity layer is rejected. Session 0 service cannot show biometric UI. It is replaced by certificate-based bootstrap at `Cert:\LocalMachine\My` with non-exportable private key ACL'd to per-service SID. See ADR 0001 A9.
- **Phase E**: the original wrap-key escrow is rejected. The escrow solved a recovery problem that A5/A6/A8 proved empty (for cloud providers the backend IS the recovery source; for user-bound providers loss is by-design). It is replaced by unified audit-ledger + Mount survival kit export + GUI recover-from-ledger UX. See ADR 0001 A10.
- **Phase B and Phase C merged into a single v0.9.0 release** (user chose Option 2). See ADR 0001 A11.

### Release-gate risk matrix (11 domains)

The original blueprint had an anti-rejection checklist as a per-phase gate. The revised roadmap replaces it with an 11-domain release-gate risk matrix:

1. Windows service lifecycle (SCM timeout, boot ordering, stop-during-reconcile)
2. Named pipe IPC security (DACL, impersonation, squatting, stale connection)
3. Periodic reconcile loop (lease TTL < tick, 429 throttle, clock skew, thundering herd, partial tick)
4. Certificate lifecycle (expiry, ACL reset by GPO, Mimikatz CNG export, Windows-upgrade loss)
5. Audit ledger tamper-resistance (hash-chain, unauthorized actor, rollover race, replay)
6. Schema migration (crash orphan, rollback script completeness, service race, per-user)
7. Existing codebase regressions (DPAPI `_EnvManager_disabled` orphan, mutex vs write_atomic race, frontend cache staleness)
8. Cert-enroll cancellation mid-flight (3 sub-states, temp dir cleanup, cloud revoke path)
9. MSI major-upgrade outage (child survives, schema mismatch, mid-write profiles.json)
10. Memory pressure / startup timeout (SCM event 7009/7000, OOM-by-mount skipping, deferred cold-start full scan)
11. IPC endpoint name (Global\pipe\ scope, RDP per-session CLI routing, machine rename no-op, locale no-op)

Detailed failure modes + regression tests for each domain live in [CONTEXT.md](../CONTEXT.md) Risk Matrix.

### Original phase descriptions (historical, for reference only)

The text below this paragraph preserved the original Phase A-E descriptions for historical reference. They are NOT the current plan. Do not implement against them. Follow ADR 0001 instead.

<details>
<summary>Original Phase A-E (collapsed — superseded by ADR 0001)</summary>

## Phased roadmap

The phases are commensurate with the current v0.7.5 baseline. Each phase is shippable on its own. No phase breaks backward compatibility with the prior one without a documented in-place migration.

### Phase A (v0.8.0): Secret mounts, schema v2, audit enrichment

Problem solved: L3, L5, L7, L9.

- Add `SecretMount` type. Profiles hold `secretMountRefs: List<string>` (mount ids) instead of `secretVariables: List<string>`.
- Add `secretMount.json` schema: `{ id, provider, name, targetName, scope, refreshPolicy: "CreatedOnce", refreshIntervalSeconds: null, lastRotatedAt, expiresAt, createdAt, createdAt, schemaVersion: 2 }`.
- Auto-migrate existing `secretVariables` envelopes into mounts in one atomic write.
- New CLI: `mount list / show / refresh / rotate / set-policy`.
- The audit file gains a SecretMount-state sub-tree beside the existing Profile/Registry audit: per-mount `id`, `lastFetchedAt`, `lastRotatedAt`, `provider`, `name`, never the value.
- AGENTS.md: add hard boundary `v0.8.0 SecretMount schema v2 (hard boundary)` describing the migration.
- Tests: migration reversibility (rollback = delete `secretMount.json`), mount list/show CLI surface, audit enrichment produces zero new plaintext leaks.
- Exit criteria: a single secret can be rotated without touching other secrets in the same profile; audit can answer "last fetched, last rotated, last accessed" without value leakage.

### Phase B (v0.8.5): SecretStore controller + capability-scoped agentic surface

Problem solved: L6, L8, L10.

- Introduce `SecretStore` controller class behind `ISecretProvider`. Owns provider registry, mount metadata, health surface.
- New CLI: `secret-store list-providers / set-default / health / audit`. `profile secret-provider` commands become aliases for back-compat.
- `agents --json` adds `capabilities` array per command + top-level `authZ` config.
- `secret-providers.json` gains `agentCapabilities` whitelist (default empty = all allowed, matching today's behaviour).
- AGENTS.md: add hard boundary `v0.8.5 Capability-scoped agentic surface (hard boundary)`.
- Tests: capability rejection, secret-store health surfaces every provider + mount count, audit inversibility.
- Exit criteria: a deployment can ship `agentCapabilities` whitelist and reject parallel `set`/`delete` calls from LLM agents on the same machine; blue-team can inventory all secrets from the CLI in 30 seconds.

### Phase C (v0.9.0): Optional reconcile loop (`env-manager-service.exe`)

Problem solved: L1, L4 (partial).

- Ship `env-manager-service.exe` as a separate binary bundled in `release/portable/` and `release/cli-only/`. GUI Settings toggle opt-in.
- Default reconcile interval 300s. Only providers with `CanRotate=true` get a lease concept (`VaultKV2Provider`'s 1000s lease, Azure's 90-day secret expiry, AWS rotation Lambda's `pending/current/staged`).
- Per-mount `refreshPolicy: Periodic | CreatedOnce | OnChange`. `OnChange` requires file-system watch on `secretMount.json`.
- AGENTS.md: add hard boundary `v0.9.0 Optional reconcile loop (hard boundary)` describing the no-drift contract and the fail-closed behaviour when `env-manager-service.exe` is missing.
- Tests: reconcile loop updates envelope without dropping semantics; `CreatedOnce` does not refresh on schedule; `Periodic` honour `interval`; crash recovery resumes reconcile.
- Exit criteria: a Vault KV2 secret rotated upstream surfaces in the next process launch within `refreshIntervalSeconds`; the loop never writes stale plaintext to the registry.

### Phase D (v0.9.5): Passkey / hardware-bridge identity layer

Problem solved: L2.

- Add `IIdentityProvider` interface. First implementation `WindowsHelloIdentityProvider` emitting a FIDO2 assertion usable by Vault / Azure / AWS auth.
- Replace `AZURE_CLIENT_SECRET` / `AWS_SECRET_ACCESS_KEY` static load with passkey-attested OAuth 2.0 token negotiation. Tokens cached in-memory only with 5-minute buffer, matching today's Azure provider.
- AGENTS.md: add hard boundary `v0.9.5 Windows Hello passkey identity (hard boundary)` describing the device-binding contract, the cross-machine refusal, and the lockout policy.
- Tests: passkey attestation round-trips; SP-bound auth fails closed when Windows Hello is locked; cross-machine assertion rejected.

### Phase E (v1.0.0): Industrial governance blueprint

Problem solved: L9, L10 (fully), L8 (fully).

- Schema v3 reconciles all audit streams (Profile / Registry / SecretMount / SecretStore / Identity) into a single WAL-backed ledger with retention policy.
- Backup escrow supports WrappingKey (age-encrypted) so that an encrypted-secrets backup can be opened on a second machine by a key that lives in the user's `pass`/GPG ring, not just by DPAPI bound to the original Windows SID. `profile export-secrets` accepts `--wrapping-key <path-to-age-recipient-file>`.
- AGENTS.md becomes the source of truth across CLI + GUI + background service + identity layer; the agent guide is generalized to cover multi-process concurrency at the SecretStore level, not just per-process RegistryMutation.
- Exit criteria: a user can recover their secrets on a freshly installed Windows account after their original account was rebuilt, via the age-wrapping key; an LLM agent with the default `agentCapabilities` whitelist cannot perform any destructive secret operation even if it manages to invoke the CLI directly.

---

## Anti-rejection checklist (every phase gate)

 noop before the next phase is merged.

1. Live test harness (`test-with-restore.ps1`) snapshots HKCU + accessible HKLM, byte-snapshots EnvManager internal configs, restores on drift. Snapshot must include `secretMount.json` after Phase A.
2. Per-session host snapshot (`snapshot-host-env.ps1`) still runs before any dev session that touches the CLI.
3. Existing tests pass without modification unless the test file documented the breaking change in the AGENTS.md hard-boundaries ledger.
4. Build artifacts land in `release/portable`, `release/cli-only`, `release/msi` per the AGENTS.md mandatory build-after-code-changes rule.
5. AGENTS.md hard-boundaries ledger updated in the same commit; README + README_CN updated for any user-visible CLI or GUI change.
6. i18n: every new user-facing string lands in all 10 locales.
7. CodeGraph index rebuilt after any code change.
8. No new dependencies without explicit user request.
9. No plaintext secret in logs. Ever.
10. No emoji in source / tests / docs / commits.

Single TODO row in the per-phase ledger:

```
Phase A: SecretMount schema v2 + secretMount.json + mount CLI + audit enrich   - not started
Phase B: SecretStore controller + capability agentic surface                    - not started
Phase C: env-manager-service.exe optional reconcile loop                       - DONE (v0.9.0)
Phase D: WindowsHelloIdentityProvider (passkey)                                  - not started
Phase E: Schema v3 unified audit ledger + wrapping-key escrow                    - not started
```

---


</details>
## Risks and counter-decisions

Risk-aware record of the dangerous calls we considered and rejected, so future maintainers know why the proposed structure was chosen.

- Risk: introducing `env-manager-service.exe` violates the "single-cli, single-GUI, single-process" emphasis users relied on. Counter: the agent is opt-in via Settings; default off in v0.8 / v0.9; on-by-opt-in in v0.9.5. Users who want the static-fetch-on-launch behaviour continue to get it for free.
- Risk: the `SecretMount` schema migration is a one-shot write that can clobber profiles. Counter: atomic write + back-up of `profiles.json` to `.env_bak/<timestamp>/` before migration; the existing per-session forensic snapshot already covers this.
- Risk: capability-gating the agent surface may break existing automation scripts that fire parallel writes. Counter: default empty `agentCapabilities` = all-allowed; the gate is opt-in. A deployment that depends on parallel writes keeps today's behaviour.
- Risk: passkey introduction may exclude users without Windows Hello capable hardware. Counter: capability fallback to today's static env-var token path; the passkey path is opt-in.
- Risk: integrating with the Microsoft SecretManagement upstream note's "feature-complete" status means the pwsh provider will continue to receive critical security fixes but not new features; we own the long-term maintenance burden. Counter: we keep the current implementation, mark the provider option as `Best-Effort extension vault` in the GUI, and Phase E's wrapping-key escrow makes the pwsh vault replaceable without data loss.

---

Protocol to amend this blueprint

1. Edit this file (precise diff) in the same commit that ships the phase A/B/C/D/E code.
2. Cite the phase exit criteria met.
3. Update AGENTS.md hard-boundary ledger for any new invariant introduced.
4. Re-run the full CI test suite + `node scripts/build.mjs --arch x64` before merging the phase commit.

This blueprint is not a contract; it is a map. Any future contributor who finds the map no longer matches the territory should amend the map, not the territory.