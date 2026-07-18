# Architecture

## Three Layers

1. **CLI backend** (`Program.cs`) - C# .NET 10 console application that reads/writes the Windows Registry directly. Compiles to `env-manager-cli.exe`. Handles all variable CRUD, backup/restore, diff/merge operations.
2. **Tauri shell** (`frontend/src-tauri/`) - Rust application that embeds the CLI as a bundled resource. Spawns CLI subprocesses for each operation and returns structured JSON responses to the frontend via Tauri IPC commands.
3. **Svelte frontend** (`frontend/src/`) - TypeScript + Svelte 4 + TailwindCSS UI rendered in a WebView2 window. Communicates with the Rust layer exclusively through `invoke('run_cli', ...)`.

The GUI does NOT depend on a local web server. In development, Vite serves the frontend at `localhost:5173`. In production, Tauri embeds the built static assets via its `tauri://` custom protocol - no server, no network.

## IPC Bridge

The Rust layer (`main.rs`) exposes two Tauri commands:

- `run_cli(command: String, args: Vec<String>) -> CliResponse` - Spawns the CLI subprocess, returns `{ success, data, error }`.
- `cli_diagnostics() -> serde_json::Value` - Returns resolved CLI path, GUI exe directory, and CWD for debugging.

## Cross-View Refresh

The `refreshTrigger` store in `stores.ts` is a counter incremented when the header refresh button is clicked. Each page component (Variables, ProfilePage, PathEditor) subscribes to it via a reactive statement (`$: if ($refreshTrigger > 0) { refresh() }`) and re-fetches its data. This ensures the current view's data is refreshed regardless of which page is active.

The `SettingsDialog` also dispatches a `pathChanged` event after CLI PATH changes, which `App.svelte` intercepts to increment `refreshTrigger` and call `listVariables()`. This ensures the PathEditor and all other views update immediately after adding/removing CLI from PATH.

## Race Condition Prevention

The Rust IPC layer uses a `static CLI_RWLOCK: RwLock<()>` to implement read/write lock separation:

- **Read commands** (`list`, `get`, `backup`, `diff`, `validate`, `agents`, `profile list/show/status`, `path list`, `path dedupe --dry-run`) acquire a **read lock** that allows concurrent execution. Multiple read operations can run in parallel without blocking each other.
- **Write commands** (`set`, `delete`, `toggle`, `restore`, `merge`, `profile create/delete/apply/unapply/add-var/remove-var/edit-var`, `path add/remove/move-up/move-down/rename/dedupe`) acquire a **write lock** that is exclusive. Only one write can run at a time, and no read can interleave with a write.

Concurrent reads (e.g. loading variables list + loading profiles) run in parallel, improving responsiveness. All mutations are serialized, preventing race conditions where a read could see a partial mutation.

The frontend also serializes write operations via a `writeChain` promise in `api.ts`. This ensures that even if a user double-clicks a button, the write operations execute in order rather than racing. Read operations (`runRead()`) are not serialized on the frontend side, allowing them to fire concurrently.

The `is_read_only()` function in `main.rs` determines the lock type by inspecting both the command and its first argument (subcommand for `profile` and `path`).

## System Tray

The GUI creates a system tray icon on startup. Closing the main window hides it to tray instead of exiting. Double-clicking the tray icon restores the window. Right-click context menu: Show, Quit. Implemented in `main.rs` using Tauri 2's `tray::TrayIconBuilder`.

**i18n Sync**: The tray menu text and tooltip are dynamically updated when the user changes the GUI language. The frontend calls `updateTrayLocale(showText, quitText, tooltip)` which rebuilds the tray menu with translated strings. This ensures the right-click context menu matches the GUI locale.

## Toast Notification System

All transient feedback messages (copy confirmation, action success/errors) use a **global toast store** (`showToast()` in `stores.ts`) rendered once in `App.svelte` as `fixed`-position overlays with `pointer-events-none` and `z-[60]`. No component renders its own toast. This ensures they appear on top of content without causing layout shifts or interfering with clicks. Toasts auto-dismiss after 3s (configurable) and can be clicked to dismiss early.

## Frontend Caching Mechanism

The GUI implements a multi-layer caching strategy for production-scale environments with thousands of environment variables:

1. **Debounced search** (`debouncedSearch` store in `stores.ts`): Search input debounced by 150ms before triggering filter recompute. Prevents re-running `filteredVariables` on every keystroke.
2. **TTL-based `listVariablesRaw` cache** (`api.ts`): Secondary surfaces fetch variables via `listVariablesRaw()` which caches results for 5 seconds. Pass `force=true` to bypass (used after mutations).
3. **Derived `filteredVariables` store** (`stores.ts`): Memoizes scope+search filter; only recomputes when `$variables`, `$selectedScope`, or `$debouncedSearch` change.
4. **LRU-capped `expandedValues`** (`Variables.svelte`): `%VAR%` expansion preview cache capped at 500 entries; oldest entry evicted in FIFO order.
5. **Debug log cap** (`addDebugLog` in `stores.ts`): Debug logs capped at 200 entries; older entries sliced off.
6. **Write-through cache invalidation** (`invalidateApiCache` in `api.ts`): After any write operation completes, `runWriteOperation` calls `invalidateApiCache()` which resets the variables cache timestamp and clears the PATH entries cache.
7. **PATH entries cache** (`pathEntriesCache` in `api.ts`): `listPathEntries` caches results per-scope (user/system) with the same 5-second TTL.
8. **ProtectionPage cached reads**: ProtectionPage's `refresh()` uses `listVariablesRaw()` (cached); caches are automatically invalidated after writes.

## Auto-Update

The application checks for updates via the GitHub Releases API.

**GUI**: The Settings dialog has a "Check for Updates" button. Clicking it invokes `check_for_updates` Tauri command in `main.rs`, which uses PowerShell `Invoke-RestMethod` to query `https://api.github.com/repos/Xxx91n/env-manager/releases/latest`. The response is parsed in Rust, compared against the current version, and returned to the frontend. If a newer version exists, a download link to the release page is shown. The check is triggered manually by the user - no background polling.

**CLI**: The `update check` command uses `System.Net.Http.HttpClient` to query the same GitHub Releases API. Output is JSON with `currentVersion`, `latestVersion`, `isUpdateAvailable`, and `releaseUrl`. This command is read-only and safe for concurrent execution.

**Standalone CLI package**: The build system produces `release/cli-only/` containing only the CLI binary and its runtime dependencies (DLLs, JSON config, AGENTS.cli.md) - no GUI. Mode detection: the CLI can detect whether a GUI exe exists in the same directory by checking for `env-manager.exe` alongside `env-manager-cli.exe`. If no GUI exe is found, it operates in standalone CLI mode.

## Security Hardening (Audit Findings)

1. **`DeleteVariableWithoutNotify` guard**: All internal delete paths respect the protection list. A bulk import rollback can no longer delete a protected system variable whose original value was null.
2. **`RunToggle` entry guard**: Toggle rejects protected variables at the entry point before any registry mutation, preventing inconsistent state (backup key + original both present).
3. **`RestoreBackup` explicit skip reporting**: Reports skipped count and logs each skipped variable name to stderr (was silent failure).
4. **Path traversal protection**: `ValidateFilePath` rejects non-`.json` extensions and blocks writes to `\Windows`, `\Program Files`, `\Program Files (x86)`.
5. **Rust IPC input validation**: `validate_cli_input` enforces command whitelist, max 64 args, max 32,767 chars per arg, null byte rejection, and control character rejection.
6. **Mutex-based cross-process locking**: All write operations acquire `Local\EnvManager.RegistryMutation` mutex in the CLI, plus `CLI_RWLOCK` write lock in Rust, plus `writeChain` serialization in the frontend. Three layers prevent concurrent mutations.
7. **`RunRename` and `RunChangeScope` entry guards**: Both reject protected variables at entry point (on `oldName` AND `newName` / source scope AND target scope), before any registry mutation. `RunChangeScope` also relocates any `_EnvManager_disabled` toggle-backup key from source scope to target scope so the disable state follows the variable. Reordering PATH entries (move-up / move-down) intentionally remains unprotected because reordering data is never destructive.

## Internal Modal Dialog System

The GUI uses an internal Svelte store-based modal system instead of browser `confirm()`/`alert()`. The `modal` writable store in `stores.ts` holds the current `ModalConfig`. The `ConfirmDialog.svelte` component renders globally in `App.svelte`. All confirmation dialogs use `showModal()` from `stores.ts`.

## Variable Rename and Scope Change (GUI)

The GUI EditDialog supports three orthogonal mutations on an existing variable: rename, change scope, and value edit. These combine into four paths dispatched through `changeScope`, `renameVariable`, and `setVariable` API calls (all serialized via the frontend `writeChain` and the Rust `RwLock` write lock):

1. **Name changed only** -> `renameVariable(original, name, scope, overwrite)` -> CLI `rename` (writes+verifies target, then deletes source; no delete-then-set race).
2. **Scope changed only** -> `changeScope` -> CLI `change-scope` (writes to the new hive, verifies, then deletes the source entry; preserves the `_EnvManager_disabled` toggle backup by relocating it as well).
3. **Scope and name both changed** -> `changeScope(original, scope, oldScope, overwrite)` followed by `renameVariable(original, name, scope, overwrite)` and an optional `setVariable(name, value, scope, true)` if the value also changed.
4. **Value only** -> `setVariable(name, value, scope, overwrite)`.

The `--overwrite` flag is passed through only when the user explicitly confirmed the conflict modal. The EditDialog never silently clobbers an existing target-scope variable by injecting a synthetic `true`. If the target scope already holds a same-name (case-insensitive) variable, the conflict modal fires first and the user must confirm.

**Security**: Both `rename` and `change-scope` share the same protection contract:
- `IsProtectedVariable(oldName, scope)` is checked at entry; protected variables cannot be renamed or moved out of their scope.
- `IsProtectedVariable(newName, scope)` is checked for the target slot so a rename or scope-move cannot land on top of a protected variable.
- Variable name validation: no empty names, no `=`, max 255 chars (user scope).
- `change-scope` refuses cross-scope name collisions unless `--overwrite` is explicit, mirroring the `rename` contract. When a variable exists in both user and system scope, the caller must specify `--scope` explicitly; auto-detection is rejected to avoid silent ambiguous mutations.

## Profile Audit History

Profile-level mutations (`create`, `delete`, `rename`, `add-var`, `remove-var`, `edit-var`) modify `profiles.json` rather than the registry, so they bypass the standard snapshot diff the CLI writes for registry mutations in `Main()`. They are recorded explicitly via `RecordProfileAudit()` (see `ProfileAudit.cs`) so the user can see profile changes in `history list` and undo them via `history undo <id>`.

- Audit entries for profile mutations carry `Scope = "profile"` so the GUI and `history` command can distinguish them from registry-level entries.
- `OldValue` / `NewValue` store a compact JSON summary of the affected profile (`id`, `name`, `isEnabled`, `inherits`, `pathEntries`, `variables`) so an undo restores that profile state without clobbering other profiles.
- `TryUndoProfileAudit(entry)` in `ProfileAudit.cs` reverses create (delete), delete (re-create from `OldValue`), rename (restore old name), add-var (remove the added variable), remove-var (re-add the removed variable), and edit-var (restore the pre-edit variable). `apply`, `unapply`, `set-inherits`, `add-path`, and `remove-path` are non-undoable no-ops; unknown `profile <x>` subcommands emit an error and `return false` rather than silently reporting success.
- `RunHistoryCommand` dispatches profile entries to `TryUndoProfileAudit` and registry entries to the stale-value-verified undo path. The two paths never overlap.


## v0.7.0 Launch Profile + DPAPI Secrets Architecture

Per-app launch profiles extend the profile system without modifying existing Global profile behavior. A Launch profile is stored in `profiles.json` alongside Global profiles but is **never** written to the registry.

**Data model**: `ProfileData` gains `profileType` (`"global" | "launch"`, default `"global"`), `targetExecutable`, `launchArguments`, `workingDirectory`, and `secretVariables` (DPAPI-encrypted secrets, decrypted at spawn time by `profile launch` and `profile reveal-secret`).

**Validation**: `ValidateProfiles` splits the uniqueness set by type (Global names unique within Global, Launch names unique within Launch, cross-type collisions allowed). `ValidateLaunchTarget` refuses targets inside `\Windows\System32` and rejects non-executable extensions.

**Runtime (`profile launch`)**: spawns the target with `ProcessStartInfo { UseShellExecute=false, EnvironmentVariables.Clear(), env(\"K\",\"v\") }` for each profile variable and PATH entries. The child process receives ONLY the profile's environment and never inherits the parent's. `WM_SETTINGCHANGE` is NOT broadcast. Logs record only command names and arg counts - never variable values (preserves the "no env values in logs" hard boundary from AGENTS.md).

**Race condition prevention**: `profile launch` is classified as read-only in Rust `is_read_only` (it does not mutate the registry or profiles.json). `profile set-launch` is classified as write (it mutates profiles.json) and acquires the `CLI_RWLOCK` write lock. The existing three-layer mutex (CLI `Local\EnvManager.RegistryMutation` + Rust `CLI_RWLOCK` + frontend `writeChain`) continues to protect all registry mutations.

**Backward compatibility**: existing `profiles.json` without `profileType` defaults to `"global"` and behaves identically to v0.5.0 (forward-compatible).

## GUI/CLI Alignment

**WARNING: When adding or changing GUI features, you MUST verify the CLI has matching support.** The GUI communicates with the CLI exclusively through `invoke('run_cli', { command, args })`. Every GUI action maps to a CLI command. If a GUI feature is added without CLI support, it will fail at runtime with "Unknown command".

### Current Alignment Status (v0.7.0)

| GUI Feature | CLI Command | API Function | Aligned |
|---|---|---|---|
| List variables | `list` | `listVariables()` | Yes |
| Get variable | `get` | `getVariable()` | Yes |
| Set variable | `set` | `setVariable()` | Yes |
| Delete variable | `delete` | `deleteVariable()` | Yes |
| Toggle variable | `toggle` | `toggleVariable()` | Yes |
| Backup | `backup` | `createBackup()` | Yes |
| Restore | `restore` | `restoreBackup()` | Yes |
| Profile list | `profile list` | `listProfiles()` | Yes |
| Profile create | `profile create` | `createProfile()` | Yes |
| Profile delete | `profile delete` | `deleteProfile()` | Yes |
| Profile apply | `profile apply` | `applyProfile()` | Yes |
| Profile unapply | `profile unapply` | `unapplyProfile()` | Yes |
| Profile show | `profile show` | `showProfile()` | Yes |
| Profile add-var | `profile add-var` | `addProfileVar()` | Yes |
| Profile remove-var | `profile remove-var` | `removeProfileVar()` | Yes |
| Profile edit-var | `profile edit-var` | `editProfileVar()` | Yes |
| Profile status | `profile status` | `getProfileStatus()` | Yes |
| Path list | `path list` | `listPathEntries()` | Yes |
| Path add | `path add` | `addPathEntry()` | Yes |
| Path remove | `path remove` | `removePathEntry()` | Yes |
| Path move-up | `path move-up` | `movePathEntryUp()` | Yes |
| Path move-down | `path move-down` | `movePathEntryDown()` | Yes |
| Tray locale sync | `update_tray_locale` | `updateTrayLocale()` | Yes |
| CLI agents spec | `agents` | `getCliAgentsSpec()` | Yes |
| Add CLI to PATH | `path add` | `addCliToPath()` | Yes |
| Profile source annotation | `list` (profileSource field) | N/A (automatic) | Yes |
| Profile export | `profile export` | `exportProfile()` | Yes |
| Profile import | `profile import` | `importProfile()` | Yes |
| Profile rename | `profile rename` | `renameProfile()` | Yes |
| Rename PATH entry | `path rename` | `renamePathEntry()` | Yes |
| Dedupe PATH entries | `path dedupe` | `dedupePathEntries()` | Yes |
| Rename variable | `rename` | `renameVariable()`, `EditDialog` | Yes |
| Change variable scope | `change-scope` | `changeScope()`, `EditDialog` | Yes |
| Profile audit history | N/A (automatic) | N/A (automatic) | Yes |
| v0.7.0 PATH health check | `path health [--fix] [--dry-run]` | `pathHealth()` + Health Check / Remove Dead buttons | Yes (CLI/API/GUI) |
| v0.7.0 Profile set-launch | `profile set-launch` | `profileSetLaunch()` + type badge + Create bar type selector | Yes (CLI/API/GUI) |
| v0.7.0 Profile launch (isolated env spawn) | `profile launch` | `profileLaunch()` + Launch button + browse target | Yes (CLI/API/GUI) |
| v0.7.0 Variable conflict confirmation modal | `set --overwrite` strictly enforced | (reuse) EditDialog Save + conflictConfirm | Yes (CLI/GUI) |

### Alignment Checklist

When adding a new GUI feature:
1. Add the CLI command in `Program.cs`
2. Add the API function in `frontend/src/lib/api.ts`
3. Add the command to `ALLOWED_COMMANDS` in `main.rs` (current: list, get, set, rename, change-scope, delete, toggle, backup, restore, diff, merge, validate, help, profile, path, agents, history, bulk, expand, protection, update - v0.6.0 subcommands profile set-launch/launch/health/secrets and path health are subcommand-routed through 'profile'/'path' top-level entries already in ALLOWED_COMMANDS). Also add write commands to `WRITE_COMMANDS` and read commands to `READ_COMMANDS` as appropriate. v0.7.0 secrets subcommands (add-secret/edit-secret/remove-secret/reveal-secret) are READ-or-WRITE classified by the CLI internal switch (add/edit/remove-secret = write, reveal-secret = read).
4. Add UI in the appropriate `.svelte` component
5. Add i18n strings to ALL translation files
6. Update the alignment table above
7. Add test coverage

## Agent Integration

### CLI Agents Command

The CLI exposes an `agents` command that outputs the CLI-level AGENTS.md specification:
- `env-manager-cli agents` - Outputs the full AGENTS.cli.md content to stdout
- `env-manager-cli agents --path` - Outputs the file path of AGENTS.cli.md

This follows the industry pattern where CLI tools expose a machine-readable specification file that AI agents and LLMs can read to understand the tool's API, safety boundaries, and integration patterns. After first invoking the CLI, agents should call `agents` to discover the full contract.

### CLI-Level AGENTS.md

`AGENTS.cli.md` is distributed alongside the CLI binary in both portable and MSI installations. It is bundled as a Tauri resource and resolved at runtime via `AppContext.BaseDirectory`. The file contains command reference, output format specification, security boundaries, validation rules, error handling conventions, and agent integration tips.

### GUI Agents API

The frontend exposes `getCliAgentsSpec()` and `getCliAgentsPath()` in `api.ts` for programmatic access to the CLI specification from the GUI.


## v0.7.0 DPAPI Secrets Runtime

**Encryption**: `DpapiHelper.EncryptSecret/DecryptSecret` in `EnvFeatures.cs` uses `crypt32.dll` P/Invoke `CryptProtectData`/`CryptUnprotectData` with `CryptProtectUiForbidden=0x01` and no entropy, producing CurrentUser-scope ciphertext equivalent to `System.Security.Cryptography.ProtectedData.Protect(CurrentUser)`. No NuGet dependency; MSVC and MinGW toolchains compatible. Ciphertext stored as base64 in `profiles.json` `variables[].Value`; variable name also appears in the profile `secretVariables` array as a marker.

**Decryption paths**:
1. `profile launch <name>` - decrypts in the launcher process memory before injecting into the child env block. If decryption fails the launch is refused (`return 1`) so ciphertext garbage is never silently injected.
2. `profile reveal-secret <name> <var>` - the ONLY stdout-plaintext path. DPAPI-bound to the current user; another user or machine cannot decrypt.

**Plaintext lifecycle**: plaintext bytes `byte[]` are zeroed after use in the launcher. Plaintext is NEVER written to `profiles.json`, the registry, the audit log, or any Rust/frontend store.

**Audit records**: NAME only plus `<redacted>`/`<encrypted>` markers - never the plaintext or ciphertext value. This preserves the existing "logs never record environment values" hard boundary.

**GUI integration**: the Svelte `ProfilePage` masks secret variable values as a placeholder; the API layer exposes `profileAddSecret`/`profileEditSecret`/`profileRemoveSecret`/`profileRevealSecret` wrappers. Secret entry is gated behind Launch type and unapplied state. DPAPI decryption for GUI display is NOT implemented - the backend `profile reveal-secret` is the only plaintext surface.
