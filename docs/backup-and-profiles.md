# Backup and Profile Formats

## Backup JSON Format

```json
{
  "timestamp": "2026-07-10T12:34:56Z",
  "version": "1.0.0",
  "variables": [
    {
      "name": "PATH",
      "value": "C:\\Windows\\System32;...",
      "scope": "user"
    }
  ]
}
```

- `timestamp`: RFC3339 / ISO 8601, UTC
- `version`: Semantic version (currently "1.0.0")
- `variables`: Array of `{ name, value, scope }`, may be empty
- `scope`: Must be "user" or "system"

## Profile JSON Format

Profiles are stored at `%LOCALAPPDATA%\EnvManager\profiles.json`:

```json
[
  {
    "id": "uuid-string",
    "name": "dev-profile",
    "isEnabled": false,
    "variables": [
      { "name": "JAVA_HOME", "value": "C:\\Program Files\\Java\\jdk-21" }
    ]
  }
]
```

- `id`: Unique identifier (GUID)
- `name`: Profile name (unique)
- `isEnabled`: Whether the profile is currently applied
- `variables`: Array of `{ name, value }` pairs
- When applied, original user variable values are backed up as `name_PowerToys_<profileName>` (PowerToys-compatible naming). `ListEnvironment` skips any key containing `_PowerToys_` so these don't appear as regular variables.

### Profile source attribution

`ListEnvironment` annotates variables that were applied from a profile by setting `profileSource` to the profile name. The GUI shows a badge next to the variable name indicating which profile it came from. This helps users distinguish profile-applied variables from manually-set ones.

### Profile drag-to-reorder

The GUI Profile page supports pointer-event-based drag-and-drop to reorder profiles (uses `pointerdown`/`pointerenter`/`pointerup` instead of HTML5 DnD, which WebView2/Tauri intercepts at the OS level causing a forbidden cursor). The order is persisted in `localStorage` as `envManager_profileOrder` and applied via `applyStoredOrder()` after `listProfiles()`. This is GUI-only sorting - no CLI calls, no profile data modification. Each profile card has a drag handle with a grab cursor.

### v0.6.0 Launch Profile Schema

Profile JSON gains these optional fields (see `ProfileData` in `Program.cs`):

- `profileType`: `"global"` (default, current apply-to-user-registry behavior) or `"launch"` (launcher template).
- `targetExecutable` (launch only): absolute or relative path to an executable. `ValidateLaunchTarget` rejects non-existent, non-executable, or System32 paths.
- `launchArguments` (launch only): optional static args appended to the spawned command.
- `workingDirectory` (launch only): optional cwd override.
- `secretVariables`: list of variable names whose values are DPAPI-encrypted on disk. **Runtime decryption is reserved for v0.7**; in v0.6.0 this field is schema-only and any value is stored as plaintext.

**Safety invariants specific to Launch profiles**:

- A Launch profile is NEVER written to the user registry. `profile launch <name>` calls `ProcessStartInfo.EnvironmentVariables.Clear()` then `env(k,v)` per profile variable.
- A Launch profile NEVER triggers `WM_SETTINGCHANGE`. New variables are visible ONLY to the spawned child process.
- A Launch profile MAY share a name with a Global profile. Cross-type name collisions are explicit because the two kinds never overlap in effect (Global writes the registry, Launch spawns a child).
- Logs continue to record only command names and arg counts. Variable values (encrypted or not) NEVER appear in CLI/Rust logs.
- `profile set-launch` mutates `profiles.json` and is classified as a **write** command (holds the CLI `Local\EnvManager.RegistryMutation` mutex + Rust `CLI_RWLOCK` write lock). `profile launch` is classified as **read-only** (spawns a child; does not touch the registry or profiles.json).

### Extended State and Safety Contracts

- `set` refuses to overwrite a different existing value unless `--overwrite` is explicit. GUI confirmation supplies this flag.
- `rename` writes and verifies the target before deleting the source. GUI renames must use this command, never delete-then-set.
- All writes acquire the cross-process `Local\EnvManager.RegistryMutation` mutex. Rust and frontend locks are additional in-process layers.
- Audit history is stored at `%LOCALAPPDATA%\EnvManager\audit.json`, capped at 2,000 entries. Undo refuses stale changes unless `--force` is explicit.
- Bulk JSON/.env/CSV imports run a dry-run conflict preview in the GUI and roll back every write if any item fails verification.
- Profiles may inherit other profiles. Cycles and missing parents are rejected; child variables override parent variables.
- Profile PATH fragments append unique entries to user PATH and use the same backup chain as variables.
- Applied profiles record `appliedAt`. Overlapping profiles must be unapplied in reverse application order; unsafe unapply is rejected.
- Inheritance and PATH fragments cannot change while a profile is active. GUI controls must expose the same disabled state.
- `profiles.json` is atomically replaced, backed up to `profiles.json.bak`, and recovered from a valid backup after JSON corruption.
- CLI/Rust logs record command names and argument counts, never argument values. Environment values may contain credentials.

## Security (full list)

- No credential storage - only manages environment variables
- Direct Registry API via `Microsoft.Win32.Registry`, no COM
- IPC isolation - CLI runs as a separate subprocess
- Input length validation (32767 byte limit on variable names/values)
- Permission separation: user scope needs no elevation, system scope requires administrator
- `UnauthorizedAccessException` handled explicitly for system scope without elevation
- Variable name validation: rejects empty names, names >255 chars (user scope), names containing `=`
- Path traversal protection: backup files must have `.json` extension, writes to system directories blocked
- Backup file size cap: 50 MB maximum to prevent DoS via large files
- CLI command whitelist in Rust IPC layer: only known commands can spawn subprocesses (list, get, set, delete, toggle, backup, restore, diff, merge, validate, profile, path, agents, help)
- Process isolation: CREATE_NO_WINDOW flag prevents console flicker and information leakage
- **Critical system variable protection (config-driven)**: system-scope modifications to protected variables are blocked in SetVariable, DeleteVariable, and SetVariableWithoutNotify. The built-in ProtectedSystemVars and ProtectedPathEntries are loaded from external editable JSON files in `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json` and `builtin-protected-paths.json` respectively, created from hardcoded defaults on first run. Users/admins can edit these files to customize which built-in variables and PATH entries are protected without recompiling. The defaults include: PATHEXT, PSMODULEPATH, SystemRoot, windir, ComSpec, TEMP, TMP, USERPROFILE, SystemDrive, ProgramFiles, ProgramFiles(x86), ProgramData, HOMEDRIVE, HOMEPATH, NUMBER_OF_PROCESSORS, OS, PROCESSOR_ARCHITECTURE, PROCESSOR_IDENTIFIER, PROCESSOR_LEVEL, PROCESSOR_REVISION, ALLUSERSPROFILE, APPDATA, COMMONPROGRAMFILES, COMMONPROGRAMFILES(x86), COMPUTERNAME, LOCALAPPDATA, LOGONSERVER, OneDrive, OneDriveConsumer, PUBLIC, SESSIONNAME, USERDOMAIN, USERNAME; and for PATH: `C:\Windows\System32`, `C:\Windows`, `C:\Windows\System32\Wbem`, `C:\Windows\System32\WindowsPowerShell\v1.0\`. PATH as a variable name is NOT in the protected-vars list - it is protected per-entry via ProtectedPathEntries (built-in Windows system paths loaded from the JSON file) plus custom entries stored in `protected-paths.json`. SetPathEntries checks IsProtectedPathEntry before allowing removal of any PATH entry. These are also blocked from being added to profiles via IsProfileApplicable() and ProfileAddVar().
- **Custom protected variables (user-lockable)**: Users can lock any variable via the GUI lock button or CLI `protection add-var`. Locked variables are stored in `%LOCALAPPDATA%\EnvManager\protected-vars.json`. Locked variables cannot be toggled, edited, or deleted. The `list` command annotates each variable with `isProtected` (true if protected by built-in or custom rules) and `isBuiltinProtected` (true only if protected by hardcoded built-in rules, not user locks). Built-in protected variable (system scope) cannot be unlocked; custom locks can be removed via `protection remove-var`.
- **Path Editor lock buttons**: PATH entries can be locked/unlocked via the GUI lock button or CLI `protection add-path`/`remove-path`. Locked PATH entries are grayed out and their move/remove buttons are disabled. Built-in protected PATH entries (e.g. `C:\Windows\System32`) cannot be unlocked; custom locks can be removed.
- **Protection page layout**: The protection page has two tabs (Protected Variables / Protected PATH Entries) and a shared scope filter (`all`/`user`/`system`, defaulted from the global `selectedScope` store). Each tab renders the **add-from-existing selector at the top**, then the built-in protected list in the middle, then the custom (user-locked) list at the bottom. Custom variables can only be added from a dropdown of existing variables (loaded via `listVariablesRaw`, filtered by the selected scope); custom PATH entries can only be added from a dropdown of existing user+system PATH entries (filtered by the selected scope). When scope is set to `user`, the built-in variable list renders a placeholder explaining built-in protection only applies to system scope.
- **Toggle backup name collision prevention**: variables whose name ends with `_EnvManager_disabled` cannot be toggled, preventing backup key confusion
- **Profile name validation**: rejects empty/whitespace names, names >255 chars, names with null/newline/carriage-return chars
- **Profile variable name validation**: rejects empty names, names >255 chars, names containing `=`
- **PathAdd directory validation**: rejects empty paths, null bytes, paths exceeding max length (for direct CLI usage)
- **PathRename injection prevention**: validates new directory for empty values, null bytes, duplicates, max length
- **Path total length validation**: SetPathEntries rejects PATH values exceeding 32767 chars before writing
- **DiffBackups/MergeBackups file size validation**: both input files checked against 50 MB cap before deserialization (OOM prevention)
- **ListEnvironment O(n) optimization**: GetValueNames() cached in a HashSet instead of called per-variable (was O(n^2))
- **BroadcastSettingChange timeout reduced**: 500ms instead of 1000ms to prevent CLI exit delays
- **RunToggle null-scope crash fix**: ParseScope null return now properly checked before dereference
- **Control character rejection** in Rust IPC layer: rejects args containing control characters (prevents terminal injection)
- **Read/write lock separation** in Rust IPC: read commands share a read lock (concurrent), write commands use an exclusive write lock
- **Frontend write serialization**: writeChain in api.ts serializes all write operations to prevent UI-level races (double-click, rapid actions)

### Agent Safety Guidelines

When an AI agent uses the CLI directly:

1. **Always use `--scope user`** for non-interactive workflows. System scope requires elevation and may fail silently.
2. **Call `agents --json` first** to discover the full command contract, safety boundaries, and async support per command.
3. **Read commands are safe to batch** (list, get, backup, diff, validate, agents, profile list/show/status, path list). They acquire a read lock and can run concurrently.
4. **Write commands are serialized**. Do not fire multiple write commands in parallel - they will queue and execute in order, which may cause unexpected delays.
5. **Never delete critical system variables**. The CLI blocks system-scope modifications to protected variables, but user-scope PATH deletion is allowed and could break the agent's own environment. Always backup first.
6. **Profile names must be 1-255 chars** with no null bytes, newlines, or carriage returns. Variable names in profiles must not contain `=`.
7. **Backup files must have `.json` extension** and cannot be in system directories. Files exceeding 50 MB are rejected.
