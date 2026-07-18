# CLI Command Reference

All commands follow: `env-manager-cli <command> [arguments] [--flags]`

## Command Table

| Command | Usage | Description |
|---------|-------|-------------|
| `list` | `list` | List all variables (user + system) |
| `get` | `get <name>` | Get variable value |
| `set` | `set <name> <value> [--scope user\|system]` | Set variable (default: user) |
| `rename` | `rename <old> <new> [--scope user\|system] [--overwrite]` | Rename a variable atomically (write-then-verify-then-delete) |
| `change-scope` | `change-scope <name> <new-scope> [--scope user\|system] [--overwrite]` | Move a variable to another scope atomically; refuses protected vars and cross-scope collisions without --overwrite; relocates any `_EnvManager_disabled` backup key |
| `delete` | `delete <name> [--scope user\|system]` | Delete variable (default: user) |
| `toggle` | `toggle <name> [--scope user\|system]` | Enable/disable variable (backs up value, default: user) |
| `backup` | `backup [--output <file>]` | Backup all variables to JSON |
| `restore` | `restore <file> [--scope user\|system]` | Restore variables from JSON |
| `diff` | `diff <old> <new>` | Compare two backup files |
| `merge` | `merge <old> <new> --output <file>` | Merge two backup files |
| `validate` | `validate <file>` | Validate backup file format |
| `help` | `help` | Show help text |
| `agents` | `agents [--path\|--json\|--summary]` | Output CLI AGENTS.md spec |
| `profile list` | `profile list` | List all profiles (JSON) |
| `profile create` | `profile create <name>` | Create a new empty profile |
| `profile delete` | `profile delete <name>` | Delete a profile |
| `profile show` | `profile show <name>` | Show profile details (JSON) |
| `profile apply` | `profile apply <name>` | Apply a profile (backs up existing user vars) |
| `profile unapply` | `profile unapply <name>` | Unapply a profile (restores backed-up user vars) |
| `profile add-var` | `profile add-var <profile> <name> <val>` | Add a variable to a profile |
| `profile remove-var` | `profile remove-var <profile> <name>` | Remove a variable from a profile |
| `profile edit-var` | `profile edit-var <profile> <old> <new> <val>` | Edit a variable in a profile |
| `profile status` | `profile status <name>` | Check profile application status (JSON) |
| `profile export` | `profile export <name> --output <file>` | Export profile to JSON file |
| `profile import` | `profile import <file>` | Import profile from JSON file |
| `profile rename` | `profile rename <old> <new>` | Rename a profile |
| `path list` | `path list [--scope]` | List PATH entries (JSON) |
| `path add` | `path add <dir> [--scope] [--index N]` | Add directory to PATH |
| `path remove` | `path remove <dir> [--scope]` | Remove directory from PATH |
| `path move-up` | `path move-up <index> [--scope]` | Move PATH entry up |
| `path move-down` | `path move-down <index> [--scope]` | Move PATH entry down |
| `path rename` | `path rename <old> <new> [--scope]` | Rename a PATH entry |
| `path dedupe` | `path dedupe [--scope] [--dry-run]` | Remove duplicate PATH entries (case-insensitive, preserves first, never removes protected entries; --dry-run reports without mutating) |
| `path health` | `path health [--scope] [--fix] [--dry-run]` | v0.6.0. Detect duplicates and dead (non-existent) PATH entries; --fix removes non-protected duplicates+dead (always preserves protected entries) |
| `profile set-launch` | `profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global\|launch]` | v0.6.0. Configure a Launch profile's target executable / args / cwd, or convert a profile between Global and Launch types. Never writes the registry. |
| `profile launch` | `profile launch <name> [-- <extra-args ...>]` | v0.6.0. Spawn the Launch profile's targetExecutable with an isolated env block (env_clear + inject). Never writes the registry or broadcasts WM_SETTINGCHANGE. |
| `history list` | `history list [--limit N]` | List audit history (JSON, most recent first) |
| `history undo` | `history undo <id> [--force]` | Undo a specific audit entry |
| `history delete` | `history delete <id>` or `history delete --all [--scope user\|system]` | Delete a history record or clear all by scope |
| `bulk import` | `bulk import <file> [--scope] [--overwrite] [--dry-run]` | Import variables from .json/.env/.csv |
| `bulk export` | `bulk export <file> [--scope]` | Export variables to .json/.env/.csv |
| `expand` | `expand <value>` | Expand nested %VAR% references |
| `protection list` | `protection list` | List protected vars and PATH entries (JSON) |
| `protection add-path` | `protection add-path <dir>` | Add custom protected PATH entry |
| `protection remove-path` | `protection remove-path <dir>` | Remove custom protected PATH entry |
| `protection add-var` | `protection add-var <name>` | Lock a variable (add to custom protected vars) |
| `protection remove-var` | `protection remove-var <name>` | Unlock a variable (remove from custom protected vars) |
| `update check` | `update check` | Check for latest version via GitHub Releases API |

## Scope

- `user`: `HKEY_CURRENT_USER\Environment` (no elevation required)
- `system`: `HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Session Manager\Environment` (requires administrator)

## Debug Mode

Pass `--debug` (or `-d`) anywhere in the args to enable verbose stderr logging:

```powershell
env-manager-cli list --debug
env-manager-cli set MY_VAR value --scope user --debug
```

Debug output goes to stderr with timestamps: `[debug] HH:mm:ss.fff message`. This does not affect stdout JSON output. The GUI's Rust layer captures and logs all stderr to the Tauri log.

Key instrumented methods: `Main()` (all args), `ListEnvironment`, `GetVariable`, `SetVariable`, `DeleteVariable`, `CreateBackup`/`RestoreBackup`, `ApplyProfile`/`UnapplyProfile`, `RunPathCommand`.

## Error handling

- Errors go to stderr, success output to stdout
- Exit code: 0 = success, 1 = failure
- The GUI catches CLI errors and displays them as transient toasts (auto-dismiss after 3s), not as persistent banners. This prevents duplicate error display when both the CLI stderr and the GUI error store would show the same message.

## Profiles (detailed)

Profiles are sets of preconfigured variables applied/unapplied as a group. When applied, original values of affected user variables are backed up. Unapplying restores originals. Profiles only affect user scope.

**Single active profile policy**: Only one profile can be active at a time. Applying a new profile automatically unapplies any currently-active profile first.

**Backup key naming**: `<varname>_PowerToys_<profileName>` (PowerToys-compatible). `ListEnvironment` skips keys containing `_PowerToys_` so backups don't appear as regular variables.

- Profile inheritance can be changed while a profile is active: the CLI automatically unapplies, updates inheritance, and re-applies with the new resolved variable set
- Profile storage: `%LOCALAPPDATA%\EnvManager\profiles.json`
- Profile variables override user variables when applied
- **v0.6.0 Profile types**: `profileType: "global" | "launch"`. Global profiles apply to the user registry (current behavior). Launch profiles are launcher templates that are NEVER written to the registry: `profile launch <name>` spawns `targetExecutable` with an isolated environment block (`env_clear` + inject profile vars + PATH entries), and never broadcasts `WM_SETTINGCHANGE`. Global and Launch profile name spaces are independent: a Global and a Launch profile MAY share a name. `ValidateLaunchTarget` rejects targets inside `\\Windows\\System32` and non-executable extensions.
- v0.6.0 new profile subcommands: `profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]` and `profile launch <name> [-- <extra-args ...>]`
- v0.6.0 schema fields: `profileType`, `targetExecutable`, `launchArguments`, `workingDirectory`, `secretVariables` (DPAPI encryption is schema-only in v0.6.0; runtime decryption is reserved for v0.7)
- `profile status` checks `IsCorrectlyApplied()` (mirrors PowerToys)
- `IsProfileApplicable()` rejects invalid variable names (>255 chars, contains `=`) and profiles containing protected system variables
- `ApplyProfile()` skips any protected system variables in the profile variable list
- `ProfileAddVar()` rejects adding protected system variables to a profile
- Variable name validation: user-scope names limited to 255 chars (registry limit), rejects `=` in names
- Values containing `%` are stored as `REG_EXPAND_SZ` (matches Windows default editor behavior)
- List-type variables (`PATH`, `PATHEXT`, `PSMODULEPATH`, `_NT_SYMBOL_PATH`, etc.) are detected for list-style editing

## Variable Toggle (Enable/Disable)

Variables can be toggled on/off without deleting them. When disabled:
- The original value is backed up to a registry key named `<name>_EnvManager_disabled`
- The original variable is deleted from the active environment
- The `list` command shows disabled variables with `isDisabled: true` and their backed-up value
- Re-enabling restores the original value and deletes the backup key

**Safety**: The toggle verifies backup write success before deleting the original. If the backup fails, the original variable is preserved unchanged. `RunToggle` rejects protected variables at the entry point before any registry mutation.

**Delete cleanup**: When `delete` is called on a disabled variable, the CLI also removes the `_EnvManager_disabled` backup key and any `_PowerToys_<profileName>` profile backup keys for that variable name.

## Path Editor

PATH variable edited as a list of directory entries. Entries can be added, removed, and reordered. Supports both user and system scopes.

Duplicate removal via `path dedupe` (mirrors PowerToys issue #40402). The CLI preserves the first occurrence (case-insensitive `OrdinalIgnoreCase` matching) and never removes protected PATH entries. `--dry-run` reports what would be removed without mutating. The GUI PathEditor exposes this as two toolbar buttons: dry-run preview (eyeball icon) and destructive execute (trash icon).

**v0.6.0 PATH health** (`path health [--scope] [--fix] [--dry-run]`): detects duplicates AND dead (non-existent) PATH entries. Protected entries are NEVER reported as duplicates (defense-in-depth: HashSet isolation) and `--fix` NEVER removes a protected entry even if it appears dead. `--fix` writes a cleaned PATH preserving order; `--dry-run` reports what would change. Without `--fix` the command is pure read. The GUI Path Editor renders a status badge per row (`healthy` / `duplicate` / `dead` / `duplicate+dead`) and a "Remove dead" bulk action.

## CLI Path Resolution Order

1. Tauri resource directory (`BaseDirectory::Resource`) - production MSI install
2. Adjacent to GUI exe - portable distribution
3. Dev mode relative paths - `../../../../bin/Release/net10.0/`
4. Current working directory
5. PATH fallback (`where env-manager-cli.exe`)
