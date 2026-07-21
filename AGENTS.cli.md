# Env Manager CLI - Agent Guide

> This file is distributed alongside the CLI binary (`env-manager-cli.exe`).
> AI agents and LLMs should read this file after invoking the CLI to understand
> its contract, safety boundaries, and integration patterns.
>
> To display this file from the CLI: `env-manager-cli agents`
> To get the file path: `env-manager-cli agents --path`

---

## Overview

Env Manager CLI is a Windows environment variable manager that reads and writes
the Windows Registry directly. It provides CRUD operations, backup/restore,
profile management, PATH editing, and variable toggle (enable/disable).

- **Binary**: `env-manager-cli.exe`
- **Runtime**: .NET 10
- **Scope**: User (no elevation) and System (requires administrator)
- **Output**: stdout = JSON on success, stderr = error text on failure
- **Exit codes**: 0 = success, 1 = failure

## CLI Commands

All commands: `env-manager-cli <command> [args] [--scope user|system] [--debug]`

| Command | Usage | Description |
|---------|-------|-------------|
| `list` | `list` | List all variables (user + system) as JSON array |
| `get` | `get <name>` | Get single variable as JSON object |
| `set` | `set <name> <value> [--scope user\|system] [--overwrite]` | Set variable; explicit overwrite required for conflicts |
| `rename` | `rename <old> <new> [--scope] [--overwrite]` | Atomically rename and verify before deleting source |
| `delete` | `delete <name> [--scope user\|system]` | Delete variable |
| `toggle` | `toggle <name> [--scope user\|system]` | Enable/disable variable |
| `backup` | `backup [--output <file>]` | Backup all variables to JSON |
| `restore` | `restore <file> [--scope user\|system]` | Restore from JSON |
| `diff` | `diff <old> <new>` | Compare two backup files |
| `merge` | `merge <old> <new> --output <file>` | Merge two backups |
| `validate` | `validate <file>` | Validate backup file format |
| `profile` | `profile <subcommand>` | Manage variable profiles |
| `path` | `path <subcommand>` | Edit PATH as a list |
| `history` | `history list [--limit N]` / `history undo <id>` | Audit and guarded rollback |
| `bulk` | `bulk import\|export <file>` | JSON/.env/CSV interchange with dry-run |
| `expand` | `expand <value>` | Resolve nested variable references |
| `agents` | `agents` | Output this AGENTS.md file |
| `help` | `help` | Show help text |

### Profile Subcommands

`profile list | create <name> [--type global|launch] [--target <exe>] [--args <args>] [--cwd <dir>] | delete <name> | show <name> | apply <name> | unapply <name> | add-var <profile> <name> <val> | remove-var <profile> <name> | edit-var <profile> <old> <new> <val> | status <name>`

### Path Subcommands

`path list [--scope] | add <dir> [--scope] [--index N] | remove <dir> [--scope] | move-up <index> [--scope] | move-down <index> [--scope]`

## Output Format

### list (JSON array)
```json
[{"name":"PATH","value":"C:\\Windows","scope":"user","isDisabled":false}]
```

### get (JSON object)
```json
{"name":"PATH","value":"C:\\Windows","scope":"user","isDisabled":false}
```

### set / delete / toggle
- Success: JSON or text message on stdout
- Failure: error message on stderr, exit code 1

### toggle
```json
{"name":"MY_VAR","scope":"user","isDisabled":true}
```

## Scope

- `user`: `HKEY_CURRENT_USER\Environment` - no elevation needed
- `system`: `HKEY_LOCAL_MACHINE\...\Environment` - requires administrator
- Default scope is always `user`

## Variable Toggle

Disabling a variable backs up its raw value and original registry value kind to `<name>_EnvManager_disabled` in the same scope, then deletes the original. `list` and `get <name>` show the original name with `isDisabled=true` when only that backup exists; the internal backup name itself is never addressable. Re-enabling restores and verifies the exact value plus kind before deleting the backup.

Safety: if both original and backup exist, toggle refuses the recovery conflict without changing either value. Protected variables must be unlocked before enable or disable.

## Profiles

Profiles are stored at `%LOCALAPPDATA%\EnvManager\profiles.json`. When applied,
original variable values are backed up as `<name>_PowerToys_<profileName>`.
Unapplying restores originals. Profiles only affect user scope.

## Debug Mode

Pass `--debug` or `-d` to enable verbose stderr logging with timestamps.
This does not affect stdout JSON output.

## Error Handling

- stderr: error messages (human-readable)
- stdout: success data (JSON)
- Exit code: 0 success, 1 failure
- `UnauthorizedAccessException`: handled for system scope without elevation

## Security Boundaries

- Input validation: variable names max 255 chars (user), rejects `=` in names
- Value length: max 32767 bytes
- Path traversal: backup files must have `.json` extension
- System directory writes: blocked
- Backup file size: 50 MB maximum
- No credential storage
- No network access

## GUI Integration

This CLI is the backend for the Env Manager GUI (Tauri + Svelte). The GUI
spawns this CLI as a subprocess for each operation. The Rust IPC layer uses
a mutex to serialize all CLI invocations, preventing race conditions.

When the GUI is used, it calls `list` after every mutation to refresh its
state. CLI-only usage must manually verify state after mutations.

## Agent Integration Tips

1. **Always parse stdout as JSON** for list/get commands
2. **Check stderr** for errors on exit code 1
3. **Use --debug** for diagnostics when troubleshooting
4. **Backup before risky operations**: run `backup` before `restore` or `apply`
5. **Verify after mutations**: run `list` after `set`/`delete`/`toggle`
6. **Prefer user scope** unless system-wide changes are explicitly needed
7. **Profile apply/unapply is atomic per-profile**: the CLI batches all
   variable changes and broadcasts WM_SETTINGCHANGE once at the end
8. **Multiple profiles can be active simultaneously**: applying a profile does NOT unapply
   other active profiles. Each profile backs up original values independently.
9. **Protected variables are blocked from profiles**: `IsProfileApplicable()` and `ProfileAddVar()`
   reject variables in the ProtectedSystemVars list (PATH, SystemRoot, APPDATA, etc.)

## Concurrency and Destructive-Action Rules

- All writes are serialized across CLI and GUI processes by a named Windows mutex. Do not add external parallel write loops.
- Read operations may run concurrently. Run mutations sequentially and verify with `list`, `get`, `profile status`, or `path list`.
- Never use `--overwrite`, `--force`, system scope, restore, or bulk import unless the caller explicitly accepts the destructive effect.
- Run bulk import once with `--dry-run`; only rerun with `--overwrite` after reviewing conflicts.
- Profiles with overlapping variables must be unapplied in reverse application order. Unsafe order is rejected.
- Profile inheritance must remain acyclic. Active profiles cannot change inheritance or PATH fragments.
- Logs intentionally omit argument values. Do not add environment values to logs because they may contain credentials.

## v0.7.0 Secrets (DPAPI)

`profile add-secret <name> <var> <value>` encrypts with DPAPI CurrentUser.
`profile edit-secret <name> <old> <new> <value>` renames + re-encrypts.
`profile remove-secret <name> <var>` removes (also from `secretVariables`).
`profile reveal-secret <name> <var>` prints plaintext to stdout (only same user can decrypt).
`profile show <name> --reveal` shows decrypted values; without `--reveal` secrets are masked as `<encrypted>`.

**Agent safety**: secrets are DPAPI-bound to the user account. Do NOT pipe `reveal-secret` output into logs or commits. For workflows that only need to spawn a target, prefer `profile launch` (decrypts in-process, never touches stdout). All secret mutations require the profile be unapplied.
