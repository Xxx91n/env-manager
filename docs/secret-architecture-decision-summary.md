# Secret Architecture Decision Summary (v0.8.0 → v1.0.0)

This is the single-page distillation of the design review session (2026-08-02). Full rationale lives in `CONTEXT.md` (Decisions A1-A11 + Risk Matrix). Authoritative ADR: `docs/adr/0001-secret-architecture-revision.md`. This file replaces the phase descriptions in `docs/secret-architecture-blueprint.md` (blueprint retains context and rejected-alternative notes).

## Four-version cadence

| Version | Phase | Ship criteria |
|---|---|---|
| v0.8.0 | A | SecretMount schema v2 + one-shot migration + C# fsync + new fields nullable (refreshPolicy, refreshIntervalSeconds, bootstrapCertThumbprint). audit.json unchanged. |
| v0.9.0 | B+C merged | env-manager-service.exe Rust binary, NT SERVICE\EnvManagerService, Named Pipe IPC, CLI service gateway subcommand, periodic full-scan reconcile, GUI as control panel, three-level capability whitelist. |
| v0.9.5 | D | Cert bootstrap: Vault AppRole/client cert, Azure SP cert. AWS Roles Anywhere documented but deferred. Env-var fallback retained. |
| v1.0.0 | E | Unified audit-ledger.jsonl (append-only, hash-chained, 100MB rotation), Mount survival kit export, GUI recover-from-ledger, audit.json retired after migration. |

## Binding decisions

1. **Migration strategy (A2)**: one-shot atomic write, delete old `secretVariables` field. No dual-write period. Rollback via `.env_bak` (existing hard boundary).
2. **File topology (A3)**: `secretMount.json` and `profiles.json` separate files, each atomic (temp + fsync + rename). Write order mount-first, profile-second; delete reverse. C# `AtomicWriteProfiles` gains fsync to align with Rust `write_atomic`.
3. **SecretMount schema (A3/A6/A8)** fields `{ id, provider, name, targetName, scope, refreshPolicy: CreatedOnce|Periodic, refreshIntervalSeconds, lastRotatedAt, lastFetchedAt, expiresAt, createdAt, schemaVersion: 2, bootstrapCertThumbprint }`. Default CreatedOnce/null.
4. **Capability whitelist (A4)**: three-level combo `{cmd, scope?, mountId?}` (omitted = wildcard). Enforced in Rust `run_cli` IPC choke point. Default empty = all-allowed (today's behavior). Soft gate: rejection emits constraint-guidance message referencing `AGENTS.cli.md` so cooperating LLM agents self-correct. Not a hard wall against direct exe invocation.
5. **Reconcile loop (A8)**: `tokio::time::interval(300s)` periodic full-scan. Idempotent per-item handler: read observed, fetch desired, write only if diff. Crash resume on next start by reading file state. No SQLite queue, no file watcher, no WAL.
6. **Service identity (A8)**: `NT SERVICE\EnvManagerService` virtual service account. Per-service SID ACL on `%ProgramData%\EnvManager\secretMount.json` (machine-level mounts) and the audit ledger. User-bound mounts (DPAPI/CredMan/pwsh SecretStore) stay in `%LOCALAPPDATA%\EnvManager\` and are NEVER touched by the service.
7. **Provider refresh feasibility (A8)**: Vault KV2 / SOPS / Azure SP-env / 1Password service-account-token / AWS SigV4 are Periodic-capable. DPAPI / Credential Manager / PowerShell SecretStore are NOT — GUI refuses to set them to Periodic with i18n error pointing to `docs/secret-providers-guide.md`.
8. **Cert bootstrap (A9)**: Vault via AppRole or TLS client cert; Azure via SP cert auth; cert stored `Cert:\LocalMachine\My` with non-exportable private key ACL'd to per-service SID. AWS Roles Anywhere documented, deferred. GUI is one-time interactive enroll (no per-launch Windows Hello).
9. **Audit ledger (A10)**: `audit-ledger.jsonl` append-only, hash-chained (sha256(prev||cur)), GUID eventId (concurrent writes safe), 100MB rotation via atomic rename. Tamper detection on parse.
10. **Drop wrap-key escrow (A10)**: replaced by cloud-provider-native re-enroll + Mount survival kit export (AES-GCM encrypted archive, NOT plaintext) + GUI recover-from-ledger UX. No central escrow authority.
11. **Drop passkey/Windows Hello identity layer (A9)**: session 0 service cannot surface biometric UI. Cert auth covers the static-credential-elimination goal.

## Release-gate risk matrix (11 domains)

Detailed failure modes + regression tests live in `CONTEXT.md` Risk Matrix. Summary:

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
11. IPC endpoint name (Global\\pipe\ scope, RDP per-session CLI routing, machine rename no-op, locale no-op)

## Threat model — explicit exclusions

- **Local-admin attacker**: out of scope. NTFS ACLs, cert store ACLs, and named pipe ACLs all fall to a local admin. The release targets a single-user dev machine where the user IS the admin; this is documented in AGENTS.md hard boundary.
- **Per-user-bound provider recovery**: out of scope for machine service. DPAPI/CredMan/pwsh SecretStore mounts lost by profile corruption are recreated by the user with fresh values (A5 "指向和引导" principle).
- **AWS IAM Roles Anywhere**: documented in `docs/secret-providers-guide.md` but not shipped in v0.9.5 — deferred to v1.0.x if real demand surfaces.
