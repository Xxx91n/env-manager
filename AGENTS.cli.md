# Env Manager CLI - Agent Guide

> This file is distributed alongside the CLI binary (`env-manager-cli.exe`).
> AI agents and LLMs should read this file after invoking the CLI to understand
> its contract, safety boundaries, and integration patterns.
>
> To display this file from the CLI: `env-manager-cli agents`
> To get the file path: `env-manager-cli agents --path`
> To get a one-line machine-readable spec: `env-manager-cli agents --summary`
> To get a structured JSON spec (version + full command table): `env-manager-cli agents --json`

---

## Overview

Env Manager CLI is a Windows environment variable manager that reads and writes
the Windows Registry directly. It provides CRUD operations, rename, scope-change,
backup/restore, profile management, PATH editing, toggle (enable/disable),
audit history, bulk import/export, variable expansion, and protected-variable
safeguards.

- **Binary**: `env-manager-cli.exe`
- **Runtime**: .NET 10
- **Scopes**: user (`HKEY_CURRENT_USER\Environment`, no elevation) and system (`HKEY_LOCAL_MACHINE\...\Environment`, requires administrator)
- **Output**: stdout = JSON on success, stderr = error text on failure
- **Exit codes**: 0 = success, 1 = failure

## CLI Commands

All commands: `env-manager-cli <command> [args] [--scope user|system] [--debug]`

| Command | Usage | Description |
|---------|-------|-------------|
| `list` | `list` | List all variables (user + system) as JSON array |
| `get` | `get <name>` | Get single variable as JSON object |
| `set` | `set <name> <value> [--scope user\|system] [--overwrite]` | Set variable; explicit overwrite required for conflicts |
| `rename` | `rename <old> <new> [--scope] [--overwrite]` | Atomically rename and verify before deleting source (transactional) |
| `change-scope` | `change-scope <name> <new-scope> [--scope] [--overwrite]` | Move variable user<->system atomically (transactional) |
| `delete` | `delete <name> [--scope user\|system]` | Delete variable |
| `toggle` | `toggle <name> [--scope user\|system]` | Enable/disable variable (backs up raw value + registry kind) |
| `backup` | `backup [--output <file>]` | Backup all variables to JSON |
| `restore` | `restore <file> [--scope user\|system]` | Restore from JSON |
| `diff` | `diff <old> <new>` | Compare two backup files |
| `merge` | `merge <old> <new> --output <file>` | Merge two backups |
| `validate` | `validate <file>` | Validate backup file format |
| `profile` | `profile <subcommand>` | Manage variable profiles |
| `path` | `path <subcommand>` | Edit PATH as a list |
| `history` | `history list [--limit N]\|undo <id>\|delete <id>\|clear` | Audit log, guarded rollback, record purge |
| `bulk` | `bulk import\|export <file>` | JSON/.env/CSV interchange with --dry-run |
| `expand` | `expand <value>` | Resolve nested %VAR% references |
| `protection` | `protection <subcommand>` | List and manage protected vars + PATH entries |
| `update` | `update check` | Check GitHub releases for newer version (network) |
| `agents` | `agents [--path\|--json\|--summary]` | Output this guide / machine-readable spec |
| `help` | `help` | Show help text |

### Profile Subcommands

`profile list | create <name> [--type global|launch] [--target <exe>] [--args <args>] [--cwd <dir>] | delete <name> | show <name> [--reveal] | apply <name> | unapply <name> | rename <old> <new> | add-var <name> <var> <val> [--scope user|system] | remove-var <name> <var> | edit-var <name> <old> <new> <val> | add-path <name> <dir> [--scope user|system] | remove-path <name> <dir> | set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] | launch <name> [-- <extra-args>] | status <name> | inherits <name> --parents <p1> [<p2>...] | add-secret <name> <var> <value> | edit-secret <name> <old> <new> <value> | remove-secret <name> <var> | reveal-secret <name> <var> | secret-provider list | secret-provider set <name> | secret-provider rotate | export-secrets <name> <file> | import-secrets <name> <file>`

Key profile concepts:

- **Global profile**: variables are written to the user or system registry on `apply` and broadcast `WM_SETTINGCHANGE`. `unapply` restores the prior values backed up as `<name>_PowerToys_<profileName>`.
- **Launch profile**: variables are NEVER written to the registry. `profile launch` spawns the target executable with `env_clear + env(k, v)` so the variables only live in that child process. This is the only path that decrypts secrets at runtime. Apply/ApplyAll on a Launch profile is rejected at entry point.
- **Per-variable/per-PATH scope**: since v0.7.1 `profile add-var` and `profile add-path` accept `--scope user|system` and store it per-entry in `profiles.json`. `ProfileApply` routes each variable/PATH entry to HKCU or HKLM accordingly. Default is `user` for backward compatibility.
- **Inheritance**: a profile MAY inherit from other profiles via `profile inherits`. Cycles and self-inheritance are rejected (DFS over the existing chain). A Global profile cannot inherit a Launch profile, and a Launch profile cannot inherit a Launch profile that already carries secrets. Resolution is cycle-safe and wrapped in try/catch, so a poisoned `profiles.json` cannot brick the command.
- **Multiple profiles active simultaneously**: applying a profile does NOT unapply others. Each profile backs up originals independently. Overlapping variables require reverse-order unapply.

### Path Subcommands

`path list [--scope] | add <dir> [--scope] [--index N] | remove <dir> [--scope] | move-up <index> [--scope] | move-down <index> [--scope] | rename <old> <new> [--scope] | dedupe [--dry-run] [--scope] | health [--fix] [--scope]`

- `path health` reports duplicate and non-existent entries. With `--fix` it removes duplicates; without it stays read-only.
- `path dedupe --dry-run` is read-only; `path dedupe` without `--dry-run` mutates the registry PATH.
- Protected PATH entries cannot be removed or renamed, but reordering (`move-up`/`move-down`) is allowed.

### Protection Subcommands

`protection list | add-var <name> [--scope user|system] | remove-var <name> [--scope] | add-path <dir> [--scope] | remove-path <dir> [--scope]`

Custom protected variables (in `%LOCALAPPDATA%\EnvManager\protected-vars.json`) cannot be set, deleted, toggled, renamed, scope-changed, or added to a profile until unlocked. Built-in protected variables (`builtin-protected-vars.json`) are seeded from an embedded resource and cannot be unlocked at all. Built-in protected PATH entries are seeded identically.

## Output Format

### list (JSON array)
```json
[{"name":"PATH","value":"C:\\Windows","scope":"user","isDisabled":false}]
```

### get (JSON object)
```json
{"name":"PATH","value":"C:\\Windows","scope":"user","isDisabled":false}
```

### set / delete / toggle / rename / change-scope
- Success: JSON or text message on stdout
- Failure: error message on stderr, exit code 1

### toggle
```json
{"name":"MY_VAR","scope":"user","isDisabled":true}
```

### agents --json
```json
{"name":"env-manager-cli","version":"<assembly>","commands":[...],"scopes":["user","system"],"safety":{...},"integration":{...}}
```

## Scope

- `user`: `HKEY_CURRENT_USER\Environment` - no elevation needed
- `system`: `HKEY_LOCAL_MACHINE\...\Environment` - requires administrator and is rejected if the calling process is not elevated
- Default scope is always `user` for agent workflows

## Variable Toggle

Disabling a variable backs up its raw value and original registry value kind to `<name>_EnvManager_disabled` in the same scope, then deletes the original. `list` and `get <name>` show the original name with `isDisabled=true` when only that backup exists; the internal backup name itself is never addressable. Re-enabling restores and verifies the exact value plus kind before deleting the backup.

Safety: if both original and backup exist, toggle refuses the recovery conflict without changing either value. Protected variables must be unlocked before enable or disable.

## Profiles

Profiles are stored at `%LOCALAPPDATA%\EnvManager\profiles.json`. When a Global profile is applied, original variable values are backed up as `<name>_PowerToys_<profileName>` and `WM_SETTINGCHANGE` is broadcast once at the end. `unapply` restores originals. Per-variable scope (`user` or `system`) controls which registry hive receives each entry on apply.

Launch profiles are local-only: `profile launch <name> [-- <extra-args>]` spawns the configured target with a closed environment block containing only the profile variables (including decrypted secrets). The child process inherits the profile; nothing is written to the registry and no `WM_SETTINGCHANGE` is broadcast.

## Audit History

Every mutating CLI command writes a record to `%LOCALAPPDATA%\EnvManager\audit.json` (capped at 2,000 entries). Profile mutations carry `Scope = "profile"`. `history list` outputs records with timestamp, command, affected scope, variable name, and old/new values. `history undo <id>` performs a guarded rollback using an allow-list of known subcommands plus Id-based conflict detection; unknown `profile <x>` subcommands emit an error and `return false` (never silently succeed). Use `--force` to override staleness guard ONLY when the caller explicitly accepts that intermediate state may have changed.

## Bulk Import / Export

`bulk export <file>` writes user+system variables to JSON/.env/CSV. `bulk import <file>` applies them; `--dry-run` shows conflicts without writing. Without `--dry-run`, the import FAILS on conflicts unless `--overwrite` is passed. Always dry-run first in agent workflows.

## Protection

Protected variables and PATH entries cannot be mutated until unlocked. Built-in defaults (PATH, SystemRoot, APPDATA, CommonProgramW6432, PROGRAMW6432, etc.) are seeded from the embedded `protection.defaults.json` and are NOT unlockable. Custom user-locked variables (via `protection add-var` or the GUI lock button) are stored separately in `protected-vars.json` and CAN be unlocked by removing them from the protected list.

## Update Check

`update check` fetches the latest GitHub release tag from `api.github.com/repos/Xxx91n/env-manager/releases/latest` and compares it against the running assembly version. Network access required; timeout 10 seconds. Fails closed (returns no-update) on network error so an offline host never false-positives "update available".

## Debug Mode

Pass `--debug` or `-d` to enable verbose stderr logging with timestamps. Logs record command names and argument counts only - never argument values, because values may contain credentials or secret material.

## Error Handling

- stderr: error messages (human-readable)
- stdout: success data (JSON)
- Exit code: 0 success, 1 failure
- `UnauthorizedAccessException`: handled for system scope without elevation (error to stderr, exit 1)

## Security Boundaries

- **Input validation**: variable names max 255 chars, rejects `=`, NUL, CR, LF, TAB. Path fragments reject `;`, null bytes, CR, LF.
- **Value length**: max 32767 bytes
- **Path traversal**: backup files must have `.json` extension
- **System directory writes**: writes to `\Windows`, `\Program Files`, `\Program Files (x86)` blocked
- **Backup file size**: 50 MB maximum
- **No plaintext credential storage**: secret values are encrypted via DPAPI-CurrentUser (default provider) or one of the registered external secret providers (see below). Plaintext lives only in transient process memory; `profiles.json` stores ciphertext or an envelope reference, never plaintext.
- **Network access**: limited to (a) `update check` to the GitHub releases API and (b) secret providers that talk to external vaults (Vault, Azure Key Vault, AWS Secrets Manager, 1Password). All TLS is mandatory (HTTPS) for non-localhost endpoints; localhost-only http is permitted only for 127.0.0.1/localhost/[::1]. All external calls have a 10-15s timeout and fail closed.
- **Audit integrity**: every mutating command records its operation. Unknown profile subcommands fail loud; `--force` is the only override and it still records the attempt.

## v0.8 Secret Providers

Secret variables (added via `profile add-secret` on a LAUNCH profile only) are routed to the active provider declared in `%LOCALAPPDATA%\EnvManager\secret-providers.json`. List and switch providers via `profile secret-provider list` and `profile secret-provider set <name>`. `set` runs a no-op Encrypt/Decrypt/Delete probe before committing the config switch - a provider that fails its probe (pwsh missing module, Vault no `VAULT_ADDR`, cloud credentials missing, network down) is REJECTED at config time with an actionable message, never silently swapped.

Registered providers:

| Name | Storage | Auth | Network |
|------|---------|------|---------|
| `dpapi-current-user` | DPAPI-CurrentUser ciphertext in `profiles.json` (JSON envelope) | Windows user account | No |
| `credential-manager` | Windows Credential Manager (advapi32); blob DPAPI-encrypted before CredMan store | Windows user account | No |
| `powershell-secretmanagement` | Delegates to PowerShell `Set-Secret`/`Get-Secret` via `pwsh -EncodedCommand` with CREATE_NO_WINDOW. Vault auto-registered as `EnvManager`. | PowerShell + SecretManagement/SecretStore modules | No |
| `vault-kv2` | HashiCorp Vault KV v2 HTTP API | `VAULT_TOKEN` env var (never persisted) | Yes, HTTPS mandatory |
| `sops` | SOPS encrypted JSON envelope; supports Age, PGP, AWS KMS, Azure KV, GCP KMS, Vault | Provider-specific env vars | Yes for cloud KMS |
| `azure-keyvault` | Azure Key Vault REST API (PUT/GET /secrets/<name>) | Managed Identity (IMDS) or Service Principal (`AZURE_CLIENT_ID`/`AZURE_CLIENT_SECRET`/`AZURE_TENANT_ID`) | Yes, HTTPS only |
| `1password` | 1Password CLI (`op`) vault item | `OP_ACCOUNT` or `OP_SERVICE_ACCOUNT_TOKEN` | Yes |
| `aws-secretsmanager` | AWS Secrets Manager REST API with SigV4 signed requests | `AWS_ACCESS_KEY_ID`/`AWS_SECRET_ACCESS_KEY`/`AWS_SESSION_TOKEN` | Yes, HTTPS only |

**Agent safety with secrets**:

- Do NOT pipe `profile reveal-secret` output into logs, commits, agent traces, or chat. Plaintext is user-bound (DPAPI) or external-vault-bound; another user or machine cannot decrypt it.
- For workflows that only need to spawn a target, prefer `profile launch` (decrypts in-process, never touches stdout).
- Secrets can ONLY live on a Launch profile. `profile add-secret` and `profile edit-secret` reject non-launch profiles at entry point.
- Secrets on a profile CANNOT be applied to the registry (Global apply path); `IsProfileApplicable` rejects any profile containing a secret, including inherited secrets. A Global profile that inherits a Launch profile with secrets is also rejected.
- All secret mutations require the profile to be unapplied. Switching the active provider does not silently re-encrypt existing secrets; they keep their original provider's decryption until rotated via `profile secret-provider rotate`.

## GUI Integration

This CLI is the backend for the Env Manager GUI (Tauri + Svelte). The GUI spawns this CLI as a subprocess via a Rust IPC layer. State protection is layered:

1. **CLI mutex**: all write operations acquire the `Local\EnvManager.RegistryMutation` Windows named mutex. Concurrent CLI writes serialize on this.
2. **Rust `CLI_RWLOCK`**: the IPC layer separates read concurrency from write exclusivity at the process level.
3. **Frontend `writeChain`**: the GUI serializes write IPC calls so the user cannot race two write commands through the UI.

A single GUI instance is enforced by `tauri-plugin-single-instance`: a second launch of `env-manager.exe` restores and focuses the existing window instead of opening a duplicate, so the GUI never has two process-level state machines racing against the same registry. CLI scripts invoked from a terminal or by an agent still coordinate through the mutex - they are NOT blocked by the GUI, but writes are serialized.

When the GUI is used, it refreshes its view after every mutation. CLI-only usage must manually verify state after mutations (`list`, `get`, `profile status`, `path list`).

## Agent Integration Tips

1. **Discover the contract first**: run `agents --json` (structured) or `agents --summary` (one-line) before issuing commands, so your decoder knows the version and command set.
2. **Always parse stdout as JSON** for `list`, `get`, `agents --json`.
3. **Check stderr** for errors on exit code 1.
4. **Use --debug** for diagnostics when troubleshooting.
5. **Backup before risky operations**: run `backup` before `restore`, `apply`, `change-scope`, or `bulk import`.
6. **Verify after mutations**: run `list` after `set`/`delete`/`toggle`/`rename`/`change-scope`.
7. **Prefer user scope** unless system-wide changes are explicitly needed and the process has elevation.
8. **Profile apply/unapply is atomic per-profile**: the CLI batches all variable changes and broadcasts `WM_SETTINGCHANGE` once at the end.
9. **Multiple profiles can be active simultaneously**: applying a profile does NOT unapply other active profiles. Overlapping variables require reverse-order unapply.
10. **Protected variables are blocked from profiles and direct mutation**: both `IsProfileApplicable()` and `ProfileAddVar()` reject variables in the protected list. PATH entries are guarded symmetrically.
11. **Launch profiles are local-only**: never call `profile apply` on a Launch profile; use `profile launch` instead.

## Concurrency and Destructive-Action Rules

- All CLI writes are serialized by the `Local\EnvManager.RegistryMutation` mutex plus the IPC write lock. Do not add external parallel write loops; they will block on the mutex anyway.
- Read operations may run concurrently. Run mutations sequentially and verify with `list`, `get`, `profile status`, or `path list`.
- Never use `--overwrite`, `--force`, system scope, `restore`, or `bulk import` unless the caller explicitly accepts the destructive effect.
- Run `bulk import` once with `--dry-run`; only rerun with `--overwrite` after reviewing conflicts.
- Profiles with overlapping variables must be unapplied in reverse application order.
- Profile inheritance must remain acyclic and respect Global/Launch topology. Active profiles cannot change inheritance or PATH fragments while applied.
- Logs intentionally omit argument values. Do not add environment values to logs because they may contain credentials or secret material.

## Data Locations

| Path | Purpose |
|------|---------|
| `%LOCALAPPDATA%\EnvManager\profiles.json` | Profile definitions, per-var scopes, secret envelopes, inheritance graph |
| `%LOCALAPPDATA%\EnvManager\audit.json` | Audit history (capped 2,000 entries) |
| `%LOCALAPPDATA%\EnvManager\protected-vars.json` | User-managed protected variable list |
| `%LOCALAPPDATA%\EnvManager\protected-paths.json` | User-managed protected PATH entry list |
| `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json` | Built-in protected variables (seeded from CLI resource) |
| `%LOCALAPPDATA%\EnvManager\builtin-protected-paths.json` | Built-in protected PATH entries (seeded from CLI resource) |
| `%LOCALAPPDATA%\EnvManager\secret-providers.json` | Active secret provider config |

The user JSON folder is shared by both the portable and MSI installations of Env Manager, so configuration follows the Windows user account rather than the binary install location.

---

When this guide is out of date relative to the CLI surface (a new subcommand, a renamed flag, a removed safeguard), trust the actual binary: `agents --json` reports the authoritative versioned command table. Update this file in the same commit that changes the CLI command surface so the next agent and the next reader stay aligned.
