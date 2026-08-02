# 0001: Secret Architecture Revision (v0.8.0 to v1.0.0)

The env-manager secret architecture blueprint (`docs/secret-architecture-blueprint.md`, v1.0 dated 2026-07-26) proposed a linear Phase A-E roadmap. A grill-with-docs session on 2026-08-02 (see `CONTEXT.md` Decisions A1-A11 and Risk Matrix) revised four of the five phases based on external research and codebase cross-validation. This ADR records the non-obvious, hard-to-reverse trade-offs.

## Status: accepted

## Context

The blueprint's Phase C (reconcile loop), Phase D (passkey identity layer), and Phase E (wrap-key escrow) were each drawn from larger industrial reference projects (External Secrets Operator, Vault Agent, SOPS); grill surfaced that several underlying assumptions do not match this project's constraints — single-machine Windows 11 standalone (no domain), single-user developer release, 20-100 mount workload, no central operator.

## Decision

**A5 (revised)**: Phase C is NOT a process-internal tokio task and NOT a standalone `secrets-agent.exe`. It is a Windows system service (NT SERVICE\\<svc-name> virtual service account) registered via MSI. The "指向和指南" framing from the original A5 answer is withdrawn in favour of a service-managed lifecycle for providers that natively need refresh.

**A7**: `env-manager-service.exe` is a new Rust binary with three runtime modes (`--mode=service|background|cli`), IPC via named pipes \\Global\\pipe\\EnvManager.Service, communicating with `env-manager-cli.exe` (thin gateway) and the Tauri GUI (UI only). The pattern is borrowed from `D:/Aworker/photo` but its permission/idempotency model is substantively different because env-manager's refresh loop must be crash-safe and cross-user-safe while photo's worker is stateless per-job.

**A8**: Service identity is `NT SERVICE\\EnvManagerService`. This is the documented Microsoft Learn least-privilege virtual service account for standalone Win11 (gMSA/sMSA require AD). Provider feasibility audit: 5 of 8 providers (Vault KV2, SOPS, Azure Key Vault SP-env, 1Password service-account-token, AWS Secrets Manager) work under this identity; 3 (DPAPI-CurrentUser, Credential Manager, PowerShell SecretManagement) are user-bound and MUST be `CreatedOnce` (fetch-on-launch only). This is the binding constraint the A5/A6 reversal imposes.

**A9**: Phase D is NOT passkey/Windows Hello. Windows Hello requires session 0 desktop UI which the service cannot surface. The securest option compatible with A8 is **certificate-based bootstrap** at `Cert:\\LocalMachine\\My` with a private-key ACL keyed to the per-service SID and marked non-exportable. This eliminates static cloud-credential files on disk while staying in the service identity framework. Vault (AppRole/client cert), Azure (SP cert auth) ship in v0.9.5; AWS IAM Roles Anywhere is documented but deferred.

**A10**: Phase E drops wrap-key escrow. The escrow solved a recovery problem A5/A6/A8 proved empty: for 5/8 cloud providers, the backend IS the recovery source (re-enroll cert at cloud, re-fetch); for 3/8 user-bound, loss is by-design (A5). Three concrete replacements: unified audit ledger `audit-ledger.jsonl` (append-only, hash-chained GUID eventId, 100MB rotation), Mount survival kit export (AES-GCM machine+user bound archive of audit + re-enroll token + cert thumbprint — NOT plaintext), GUI "recover from ledger" UX.

**A11**: Phase B and Phase C merge into a single v0.9.0 release. Four-version cadence replaces the five-phase blueprint: v0.8.0 (A), v0.9.0 (B+C), v0.9.5 (D), v1.0.0 (E).

## Considered, Rejected

- Phase C as Tauri in-process tokio task: rejected. GUI close = reconcile stops. Vault leases expire overnight when GUI is not open.
- Phase C as separate secrets-agent.exe: rejected. Doubles the binary footprint; does not match the constraints once A8 caps the service identity.
- Phase D as Windows Hello passkey: rejected. Session 0 service cannot show biometric UI.
- Phase D as delete entirely (keep static credentials on disk): rejected. Disk imaging / backup leakage / ransomware reading the static file is a real surface.
- Phase E wrap-key escrow: rejected. Escrow holder becomes a single-point-of-compromise third party. Recovery role is provably empty under A5/A6/A8.
- credential-manager / pwsh SecretManagement under service: deferred to v0.9.x if re-store-to-service-identity path adds value; v0.9.0 keeps them CreatedOnce by default.

## Consequences

- `env-manager-service.exe` is a third binary in the release package alongside the existing CLI and Tauri GUI.
- MSI installer gains three new responsibilities: `sc.exe create` with `NT SERVICE\\EnvManagerService` account, ACL `%ProgramData%\\EnvManager\\secretMount.json` to per-service SID, install cert into `Cert:\\LocalMachine\\My` with non-exportable private key ACL.
- Phase A schema v2 must include `refreshPolicy` / `refreshIntervalSeconds` / `bootstrapCertThumbprint` fields from day one (default `CreatedOnce` / null / null), so v0.8.0 releases without service-bound periods and Phase B+C activates the same fields later without re-migrating.
- Release gate gains 11 risk domains (see `CONTEXT.md`).
- This ADR supersedes the phase-C/D/E descriptions in `docs/secret-architecture-blueprint.md` (the blueprint is updated in the same commit to reference this ADR instead of duplicating the decision).
