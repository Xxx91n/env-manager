# Env Manager - Project Operating Instructions

This document is the single source of truth for the Env Manager project. All developers, AI agents, and LLMs must follow this specification. When any project feature or structure changes, update this file in the same commit. Detailed references live in `docs/` - keep this file concise; link out instead of inlining large tables.

---

## context-mode routing (MANDATORY)

- File edits (including patches) MUST go through ctx_batch_execute / ctx_execute_file, not apply_patch.
- ctx_* first; fallback to Codex builtins only when ctx_* can't do the same job. Read-to-analyze / search / large grep: ctx_batch_execute(commands, queries) or ctx_search(queries) - never Get-Content/Select-String into context. For data analysis use ctx_execute(code) and print only the answer.
- Web/HTTP: ctx_fetch_and_index(url, source) then ctx_search(queries). curl/wget/inline HTTP are forbidden.
- Shell OK for git, mkdir, rm, mv, cd, ls, npm install, dotnet build, cargo build, vitest, scripts/build-all.ps1 (execution, not analysis; output is bounded and acceptable).
- Windows paths in ctx sandbox: use bash form /d/Aworker/env-manager/... (lowercase drive, no D:\). PowerShell cmdlets need `pwsh -NoProfile -Command "..."`. `$`-using PowerShell logic must go in a `.ps1` and run with `-File` (inline `$` is stripped by the host transport).
- After resume: `ctx_search(sort:"timeline")` before asking the user anything. Search prior session memory before re-reading sources.
- Output artifacts as files + path + one-line summary; never inline large content. Descriptive source labels for `ctx_search(source:"label")`.
- Keep this block at the very top. Any later agent editing this file must keep the context-mode routing block intact and on top. Extended project spec follows.

---


## CodeGraph (MANDATORY for code exploration)

CodeGraph is the project's indexed code intelligence layer. The index lives at `.codegraph/` (gitignored). All agents and LLMs working on this project MUST use CodeGraph as the FIRST step for code exploration — it returns verbatim source of relevant symbols grouped by file in one capped call, far more efficient than manual Grep/Read loops.

**How to use**:
- Via MCP: call `codegraph_explore` with `projectPath: "D:\Aworker\env-manager"` and a query (symbol names, file names, or natural-language question).
- Via CLI: `codegraph explore "<query>"` or `codegraph query "<symbol>"` or `codegraph node <symbol>` or `codegraph files`.
- After any code change: run `codegraph sync .` to incrementally update the index. For a full rebuild: `codegraph index .`.
- Check index status: `codegraph status .`.

**When to call FIRST (before reading files)**:
- "How does X work?" or "Where is X defined?"
- "What calls Y?" or "What is the blast radius of changing Z?"
- Surveying an area before an edit
- Finding the call path between symbols

**When NOT needed**: trivial one-file edits where you already know the exact line, or after CodeGraph has already returned the source in this session (treat returned source as already Read — do NOT re-open those files).

**Index sync is mandatory after code changes** (same commit that changes code must update the index). The index is gitignored and never committed.
## Project Overview

- **Name**: Env Manager
- **Version**: 0.7.7
- **License**: Apache-2.0
- **Repository**: https://github.com/Xxx91n/env-manager
- **Languages**: C# (.NET 10), TypeScript, Svelte 4, Rust
- **Goal**: A modern, lightweight Windows environment variable manager with CLI and GUI dual-mode support, inspired by Microsoft PowerToys environment variable editor but standalone and agent-friendly.

## Architecture

Three layers:
1. **CLI backend** (`Program.cs`) - C# .NET 10 console app, reads/writes Windows Registry directly, compiles to `env-manager-cli.exe`.
2. **Tauri shell** (`frontend/src-tauri/`) - Rust app, embeds CLI as bundled resource, spawns CLI subprocesses, returns JSON via Tauri IPC.
3. **Svelte frontend** (`frontend/src/`) - TypeScript + Svelte 4 + TailwindCSS in WebView2. Talks to Rust only via `invoke('run_cli', ...)`.

The GUI has NO local web server. Dev: Vite at `localhost:5173`. Production: Tauri embeds static assets via its `tauri://` custom protocol.

See [docs/architecture.md](docs/architecture.md) for IPC bridge, race condition prevention, system tray, toast, caching, auto-update, security hardening, modal dialog system, rename/change-scope contracts, profile audit history, and the GUI/CLI alignment table.

## Project Structure

```
env-manager/
+- Program.cs                  # C# CLI implementation
+- env-manager.csproj          # .NET 10 project (AssemblyName: env-manager-cli)
+- AGENTS.md                   # This file (project-level operating instructions)
+- AGENTS.cli.md               # CLI-level agent guide (distributed with CLI binary)
+- README.md / README_CN.md    # English / Chinese documentation
+- docs/                       # Detailed reference (cli-commands, architecture, build-and-release, backup-and-profiles)
+- frontend/                   # Tauri GUI application (src/, src-tauri/, tests/)
+- release/                    # Build output (gitignored): portable/, cli-only/, msi/
+- bin/ obj/ dist/             # Intermediate build output (gitignored)
```

## CLI Command Quick Reference

Full table, scope, debug, error handling, profiles, toggle, path editor, path resolution: see [docs/cli-commands.md](docs/cli-commands.md).

Read-only (concurrent-safe, read-locked): `list`, `get`, `backup`, `diff`, `validate`, `agents`, `profile list/show/status, launch`, `path list, path health (no --fix)`, `path dedupe --dry-run`, `history list`, `bulk export`, `expand`, `protection list`, `update check`.

Write (serialized, write-locked): `set`, `rename`, `change-scope`, `delete`, `toggle`, `restore`, `merge`, `profile create/delete/apply/unapply/add-var/remove-var/edit-var/rename, set-launch, add-secret/edit-secret/remove-secret`, `path add/remove/move-up/move-down/rename/dedupe, path health --fix`, `history undo/delete`, `bulk import`, `protection add-path/remove-path/add-var/remove-var`.

All commands: `env-manager-cli <command> [arguments] [--flags]`. `--debug`/`-d` anywhere enables verbose stderr. `--scope user|system` (default user). Exit 0/1.

## Hard Boundaries (Red Lines)

These invariants must never be violated by any code change:

- **Protected variables**: built-in protected system variables are seeded from embedded `protection.defaults.json` into `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json`. They cannot be set, deleted, toggled, renamed, scope-changed, or added to a profile at system scope. Custom user-locked variables (`protected-vars.json`) cannot be toggled, edited, or deleted until unlocked. See `IsProtectedVariable`.
- **Protected PATH entries**: built-in defaults are seeded from embedded `protection.defaults.json` into `builtin-protected-paths.json`. Built-in and custom locked entries (`protected-paths.json`) cannot be removed or edited via `path remove`/`rename`. See `IsProtectedPathEntry`. Reordering (`move-up`/`move-down`) is allowed because it is non-destructive.
- **Cross-process mutex**: all write operations acquire `Local\EnvManager.RegistryMutation` mutex. Plus Rust `CLI_RWLOCK` write lock. Plus frontend `writeChain` serialization. Three layers, never bypass.
- **Variable rename/scope-change contract**: `rename` writes+verifies target before deleting source. `change-scope` writes new scope, verifies, deletes source, relocates `_EnvManager_disabled` backup. Both reject protected `oldName`/`newName`/source-scope/target-scope at entry point. Never delete-then-set for renames.
- **GUI EditDialog 3-way save ordering**: `rename(old scope)` -> `changeScope(overwrite flag, never hardcoded true)` -> `setVariable(value, overwrite flag)`. The `--overwrite` flag flows only from explicit user confirmation of the conflict modal, never an injected synthetic `true`.
- **Profile audit**: `TryUndoProfileAudit` uses allow-list of known subcommands plus Id-based conflict detection; unknown `profile <x>` subcommands emit error and `return false` (never silently succeed); try/catch fallback returns false so `--force` contract works.
- **v0.7.0 Launch profiles + DPAPI secrets**: a Launch profile is NEVER written to the registry, NEVER broadcasts `WM_SETTINGCHANGE`. `profile launch` spawns the target with `env_clear` + inject, classified as read in Rust `is_read_only`. `profile set-launch` is a write (mutates profiles.json, holds `CLI_RWLOCK` write lock). `ValidateLaunchTarget` rejects `\Windows\System32` targets and non-executable extensions. Profile names are globally unique because all CLI profile commands are name-addressed.
- **v0.7.0 DPAPI secrets (hard boundary)**: secret variable values are DPAPI-CurrentUser encrypted on disk (base64 of `CryptProtectData` output, scope CurrentUser). Plaintext lives only in transient CLI/launcher process memory - never written to `profiles.json`, the registry, or logs. `profile reveal-secret` is the ONLY stdout-plaintext path and is DPAPI-bound to the current user (cannot be decrypted by another user or machine). `profile launch` decrypts secrets in the launcher process; if decryption fails the launch is refused (never silently inject ciphertext garbage). Audit records the variable NAME plus `<redacted>`/`<encrypted>` markers - never the plaintext or ciphertext. `add-secret`/`edit-secret` require the profile to be unapplied (same as other profile mutations).
- **v0.7.0 Secrets never applied to registry (hard boundary)**: `IsProfileApplicable` rejects any profile containing a secret variable from being applied to the user registry. Secrets on a Global profile can be stored but NEVER applied - applying would write DPAPI ciphertext garbage to the registry, violating the plaintext-never-persisted invariant. Secrets are meaningful only on Launch profiles (env_clear + inject + decrypt-in-process). `ProfileEditSecret` rejects renaming a secret into a protected system variable name (same entry-point invariant as `ProfileAddSecret`).
- **v0.8 Versioned Secret Envelopes (hard boundary)**: All secret values written to profiles.json are wrapped in a JSON envelope { provider, version, createdAt, ciphertext } for dpapi-current-user or { provider, version, targetName } for credential-manager. SecretProviderManager routes to the active provider declared in secret-providers.json. Unknown providers are fail-closed. Bare pre-v0.8 DPAPI base64 blobs are auto-detected and decrypted via DpapiCurrentUserProvider. CredentialManagerProvider uses advapi32.dll CredWriteW/CredReadW/CredDeleteW with CRED_TYPE_GENERIC; the credential blob is DPAPI-encrypted before CredMan storage. Profile stores only the CRED target name. CLI command profile secret-provider list (read) and set (write) manages the active provider. Audit records provider name, never plaintext or ciphertext.
- **v0.8 Phase 3 Secret Rotation/Export/Import (hard boundary)**: `profile secret-provider rotate` re-encrypts all secrets across all profiles with the active provider. If any secret fails to decrypt (wrong provider, deleted CredMan entry), the rotation skips it and increments the failed count - the failed secret is NOT deleted. The `profile export-secrets` command exports secrets to a DPAPI-CurrentUser-encrypted backup file (portable within the same user account). The `profile import-secrets` command imports secrets from an encrypted backup; each imported secret is verified by trial-decryption before being written to the profile. Import requires the profile to be unapplied. Rotation/export/import all generate audit entries with operation name, file path, and counts - never the secret values. Rotation = write (mutates profiles.json); export = read; import = write.
- **v0.8 Phase 4-5 Additional Secret Providers (hard boundary)**: Two new ISecretProvider implementations are registered in SecretProviderManager:

- **PowerShellSecretManagementProvider** (name: powershell-secretmanagement): delegates encryption to PowerShell Set-Secret and decryption to Get-Secret via a hosted pwsh process with CREATE_NO_WINDOW. The profile stores only the vault name and secret name in the envelope TargetName; the actual secret value lives in the PowerShell SecretManagement vault. Requires PowerShell 7 + Microsoft.SecretManagement + Microsoft.SecretStore modules installed. Fail-closed if pwsh is not found or exits non-zero. The 30-second process timeout prevents indefinite hangs.

- **VaultKV2Provider** (name: vault-kv2): reads/writes secrets via the Vault HTTP API (POST /v1/secret/data/<path> and GET /v1/secret/data/<path>). The profile stores only the mount path, secret path, and key name in the envelope TargetName. Token is read from VAULT_TOKEN env var (never persisted). TLS is mandatory: VAULT_ADDR must use https:// for non-localhost addresses; http:// is only permitted for 127.0.0.1/localhost/[::1]. Fail-closed on network errors, 403/404, or timeout (10s). The CLI does not cache decrypted material beyond the launcher process lifetime.



- **v0.7.2 Phase 6-7 SOPS + Azure Key Vault (hard boundary)**: Two new ISecretProvider implementations are registered in SecretProviderManager:

- **SopsProvider** (name: sops): shells out to a verified sops binary (-e/-d) under CREATE_NO_WINDOW with 30s timeout. The profile stores the full sops-encrypted JSON as the envelope ciphertext field. Supports Age, PGP, AWS KMS, Azure Key Vault, GCP KMS, and HashiCorp Vault decryptors via sops env vars. Binary is discovered via SOPS_PATH env var, PATH search, or common install locations. Fail-closed if sops binary is missing or non-functional. Temp files are created in a per-operation isolated directory and securely cleaned up in a finally block. The envelope is self-contained; Delete is a no-op.

- **AzureKeyVaultProvider** (name: azure-keyvault): calls Azure Key Vault REST API (PUT/GET /secrets/<name>?api-version=7.4). Profile stores only vault URI + secret name as TargetName (format: vaultUri|secretName). TLS mandatory (HTTPS only). Token obtained via managed identity (IMDS 169.254.169.254) or service principal (AZURE_CLIENT_ID/AZURE_CLIENT_SECRET/AZURE_TENANT_ID). Token cached in process memory only with 5-minute expiry buffer. 15s HTTP timeout. Fail-closed on 403/404. Supports rotation. Delete issues a soft-delete. Secret names sanitized to alphanumeric + hyphens, max 127 chars.


- **v0.7.3 Phase 8-9 1Password + AWS Secrets Manager (hard boundary)**: Two new ISecretProvider implementations:

- **OnePasswordProvider** (name: 1password): shells out to the 1Password CLI (op) binary via CREATE_NO_WINDOW with 30s timeout. Profile stores vault name + item ID + field name as TargetName (format: vault|itemId|field). Binary discovered via OP_PATH env var, PATH search, or common install locations. Fail-closed if op binary missing. Supports rotation (delete + recreate). Requires OP_ACCOUNT or OP_SERVICE_ACCOUNT_TOKEN for auth. Delete archives the 1Password item.

- **AwsSecretsManagerProvider** (name: aws-secretsmanager): calls AWS Secrets Manager REST API with SigV4 signed requests (AWS4-HMAC-SHA256). Profile stores region + secret ID as TargetName (format: region|secretId). TLS mandatory (HTTPS to secretsmanager.<region>.amazonaws.com). Auth via AWS_ACCESS_KEY_ID + AWS_SECRET_ACCESS_KEY + optional AWS_SESSION_TOKEN env vars. 15s HTTP timeout. Supports rotation via PutSecretValue. Delete uses ForceDeleteWithoutRecovery. Secret IDs sanitized to alphanumeric + /_+=.@- max 512 chars. Full SigV4 canonical request, string-to-sign, HMAC-SHA256 signing key chain implemented in-process (no AWS SDK dependency).


- **v0.7.3 GUI Secret Provider Selector (hard boundary)**: ProfilePage.svelte dynamically loads all available secret providers from `profile secret-provider list` CLI output (no hardcoded provider list). The provider selector uses a `<select>` dropdown that shows all 8 providers with i18n-localized names via `providerDisplayName()`. Clicking the provider badge toggles into selector mode; selecting a provider calls `secretProviderSet(name)` and closes the selector. The `availableProviders` array is populated from CLI output, not from a static array. When adding a new secret provider, add its i18n key to `providerDisplayName()` map in ProfilePage.svelte and all 10 translation files. **GUI/CLI sync rule**: the GUI must never hardcode a provider list; it must always derive from the CLI `secret-provider list` output so new providers appear without UI code changes.

- **v0.7.4 Provider-change confirmation modal (hard boundary)**: the GUI provider `<select>` MUST NOT call `secretProviderSet` directly. It calls `requestChangeProvider` (sets `pendingProvider` and reverts the visible `<select>` value to `activeProvider`), then a confirm modal with `secrets.providerChangeTitle` / `secrets.providerChangeWarning` / `secrets.confirmChange` i18n strings gates the actual `secretProviderSet` call via `confirmChangeProvider`. `cancelChangeProvider` clears `pendingProvider` without side effects. This prevents silent re-encryption-provider swaps when the profile already contains secrets. The CLI `profile secret-provider set` path prints an explicit warning about existing secrets keeping their previous provider's decryption (fail-closed on unknown provider) — no silent cross-provider migration occurs.

- **v0.7.4 PowerShell -EncodedCommand (hard boundary)**: `PowerShellSecretManagementProvider.RunPowerShell` MUST use `pwsh -EncodedCommand <base64(UTF-16LE)>` instead of `-Command "<escaped script>"`. The prior `-Command` path doubled single quotes inside the script then wrapped the whole script in outer `"`, which produced `''Stop''` tokens that pwsh parsed as a `ParserError: Unexpected token 'Stop''`. `-EncodedCommand` (Microsoft-recommended) eliminates all shell quoting. `EscapeForPowerShell` is still used for single-quoted strings INSIDE the script body, but the script itself is never passed through a shell quoting layer.

- **v0.7.4 Regular/secret variable display split (hard boundary)**: ProfilePage.svelte MUST render `profile.variables` in two visually separated sections — "Regular variables" (i18n `profiles.regularVariables`) and "Secret variables" (i18n `profiles.secretVariables`, amber styling with lock icon). A variable is secret iff `selectedProfile?.secretVariables?.includes(pv.name)`. `profile.secretVariables` is the authoritative list; never infer secret-ness from value patterns. The secret block shows `<encrypted>` for the value (never the ciphertext). This separation keeps the audit trail visible and prevents accidental edits to secret-bearing variables.

- **v0.7.4 Global profiles cannot hold secrets (decision)**: Global profiles are prohibited from holding secret variables. The CLI `IsProfileApplicable` already rejects any profile containing `SecretVariables` from being applied to the user registry; the GUI additionally gates the secret provider UI behind `profile.profileType === 'launch' && !profile.isEnabled`. Rationale: secrets on a Global profile could only ever leak ciphertext garbage to the registry on apply (no plaintext path exists for Global profiles), so storing them there is pure risk with no benefit. Users wanting secrets MUST use a Launch profile. The i18n key `profiles.secretsGlobalDisabled` documents this in the UI.

- **v0.7.4 Launch-target / Vault CLI error i18n (frontend only)**: CLI error messages remain English (CLI is locale-neutral per AGENTS.md); the GUI `localizeError` function in ProfilePage.svelte maps `ValidateLaunchTarget` messages (empty / not found / wrong extension / System32 rejection) and `VaultKV2Provider` messages (`VAULT_ADDR environment variable not set` / `VAULT_ADDR must use https://`) to i18n keys `errors.launchTargetEmpty`, `errors.launchTargetMissing` (`{path}`), `errors.launchTargetInvalidExt` (`{ext}`), `errors.launchTargetSystem32`, `errors.vaultAddrNotSet`, `errors.vaultTlsRequired`. When a new CLI error message is introduced, add a regex branch + i18n key here; never let an English CLI string surface to the GUI verbatim.

- **v0.7.4 Microsoft YaHei UI font (hard boundary)**: App.svelte global font-family MUST lead with "'Microsoft YaHei UI', 'Segoe UI', ..." so Chinese/CJK users get the native Microsoft YaHei UI face on Windows. The font stack still falls back to Segoe UI / system-ui / sans-serif for non-CJK locales and macOS/Linux. Never remove 'Microsoft YaHei UI' from the stack; it is the canonical Windows CJK UI font.

- **v0.7.4 HistoryPage column-resize highlight fix (hard boundary)**: `HistoryPage.svelte` column-resize MUST use a `col-resizing` class on the dragged `<th>` plus a `col-resizing-active` class on the table root to pin the highlight indicator to the dragged column head. The resize hit area (`th.resize::after`) is 6px wide with `pointer-events: auto` and `z-index: 2` so hover/click stays inside the current `<th>` and does not jump to the adjacent column when dragging rightward. Without this pin, the blue indicator jumped to the right neighbour on right-drag even though the left column was being resized. `endResize` MUST remove both classes.

- **v0.7.4 Clone-from-existing combobox (hard boundary)**: The "clone from existing variable" picker in ProfilePage's add-var panel MUST be a self-rendered combobox (`<input>` + `<ul>` dropdown), NOT a native `<select>`. The native `<select>` dropdown is OS-controlled, so typing into a separate search `<input>` above it has no visual feedback (the dropdown list does not visibly re-filter inline). The combobox: `on:input` reopens the dropdown and resets `cloneHighlightIndex`; ArrowDown/Up moves highlight within the first 10 results; Enter selects; Esc closes; `on:mousedown` on an item (with `preventDefault` to keep focus) calls `handleCloneSelect` and clears the search. `on:blur` closes after a 150ms delay so click-through works. Each list item shows the variable name (mono) plus a greyed value preview truncated to 40% width.

- **v0.7.5 PowerShell SecretManagement preflight + CLIXML stripper (hard boundary)**: `PowerShellSecretManagementProvider` MUST call `EnsureSecretManagementAvailable()` (probe `Get-Module -ListAvailable Microsoft.SecretManagement`) and `EnsureVaultRegistered()` (probe `Get-SecretVault EnvManager`, auto-`Register-SecretVault -ModuleName Microsoft.SecretStore -AllowClobber`) before any `Set-Secret`/`Get-Secret`/`Remove-Secret` call. This turns the catastrophic `#< CLIXML <Objs ...><S S="Error">Set-Secret is not recognized...</S></Objs>` into a clear actionable message: "PowerShell SecretManagement module is not installed. Run: pwsh -Command \"Install-Module Microsoft.SecretManagement, Microsoft.SecretStore -Scope CurrentUser -Force\"". All pwsh stderr is unwrapped by `StripClixml` before being thrown (parses `<S S="Error">` elements, restores `_x001B_/_x000D_/_x000A_/_x0009_` escapes, strips ANSI color sequences). The `EnvManager` vault is created on first use so users do not need to register it manually. This lives entirely inside `Encrypt`/`Decrypt`/`Delete`; no provider-level state crosses the boundary.

- **v0.7.5 Secret provider activation preflight (hard boundary)**: `SecretProviderManager.SetActiveProvider(name)` MUST run a no-op probe Encrypt/Decrypt/Delete round-trip under the new provider BEFORE committing the config file change. A provider that throws on the probe (pwsh missing module, Vault no `VAULT_ADDR`, cloud credentials missing, network down) is REJECTED at config time with `Cannot activate provider '<name>': <upstream message>. Fix the provider environment first...`. This is the same pattern PowerToys uses to validate extension dependencies at config time, not at use time. The user no longer gets a silent "active provider set to foo" success followed by a CLIXML catastrophe on the next `add-secret`; the failure surfaces where the user can actually act on it. The probe uses an unmistakable sentinel context (`__env_manager_compat_probe__`) so it cannot collide with a real variable name; best-effort Delete swallows provider-specific side effects.

- **v0.7.5 Non-launch profile secret rejection (hard boundary, CLI side)**: `ProfileAddSecret` and `ProfileEditSecret` MUST reject profiles whose `ProfileType` is not `launch` at entry point, before any encryption is performed. The error directs the user to `profile set-launch <name> --target <exe>` to convert a Global profile to Launch first. The GUI side is already gated by `profile.profileType === 'launch' && !profile.isEnabled` for the secret UI; this closes the CLI-side gap so a script or LLM cannot bypass the GUI restriction and create an inert secret on a Global profile that `IsProfileApplicable` would later refuse to apply anyway. Rationale is unchanged from v0.7.4: a secret on a Global profile can only ever write DPAPI ciphertext garbage to the registry on apply, so storing it there is pure risk with no benefit.

- **v0.7.5 Inheritance cycle / self-inheritance rejection (hard boundary)**: `ProfileSetInherits` MUST call `HasInheritanceCycle(target, requestedParents, allProfiles)` (DFS over the existing `Inherits` chains) AND refuse `targetName` appearing in the requested parents before mutating `profile.Inherits`. A cycle (A inherits B which inherits A) or a self-loop (A inherits A) would make `ResolveProfileVariables`/`ResolveProfilePaths` infinite-loop and the profile un-recoverable; rejecting at entry prevents a poisoned graph from being serialized to `profiles.json` and crashing every subsequent load. The DFS uses a visited-set so it survives diamond inheritance without false-negatives.

- **v0.7.5 Profile type filter (GUI)**: ProfilePage.svelte renders a segmented All / Global / Local control above the profile list. The filter is a view-only filter (no CLI call, no profile mutation); the choice is persisted in `localStorage` as `profileTypeFilter` so the user's last filter survives re-opening the GUI. `filteredProfiles` replaces `profileList` in the `{#each}` loop.

- **v0.7.5 Add-var / add-path GUI input validation (hard boundary)**: the GUI add-var/add-path panels MUST run `validateVarNameInput` (rejects `=, NUL, CR, LF, TAB`, length > 255) and `validatePathInput` (rejects `;`, `\0`, `\r`, `\n`, length > 32767) BEFORE IPC. The CLI already has `ValidatePathFragment` and the variable-name guard as defense-in-depth, but failing on the GUI layer keeps the IPC surface clean and gives the user a snappy i18n-localized error (`errors.varNameInvalid`/`errors.pathInvalidChars`/`errors.varNameTooLong`/`errors.pathTooLong`) instead of a raw CLI stderr echo. Never bypass these validations - they protect against the exact PATH-injection-by-semicolon and `NUL`-byte-burying attack vectors the CLI guards against.

- **v0.7.5 History page operation label full-command-first lookup (hard boundary)**: `HistoryPage.svelte`'s `getOperationLabel(command)` MUST try `history.op.<full command>` (e.g. `history.op.path add`) FIRST, then fall back to `history.op.<leading word>` (e.g. `history.op.path`), then return the raw command string. The previous code sliced `entry.command.split(' ')[0]` upstream and lost the subcommand, so `path add`/`path remove`/`path move-up`/`path move-down` all collapsed to the generic "Path" label and `history undo`/`history delete` fell back to plain English. The fix restores subroutine-level granularity for the user-visible History table.

- **v0.7.5 GUI DPI-aware glyph rendering (hard boundary)**: `App.svelte` global CSS MUST include `text-rendering: optimizeLegibility`, `font-feature-settings: "kern" 1, "liga" 1, "calt" 1`, and `-webkit-text-size-adjust: 100%` in addition to the existing 'Microsoft YaHei UI' first font stack. These four together fix the "fuzzy on zoom, blurry on 4K Windows scaling" complaint by enabling TrueType hinting, kerning, common ligatures, and contextual alternates in the Tauri WebView2 host. This is the cc-switch + PowerToys pattern ported wholesale - no new dependency, no font swap.

- **v0.7.6 Secret provider activation error i18n (hard boundary)**: GUI `localizeError` in ProfilePage.svelte MUST match the CLI activation message `Cannot activate provider '<name>': <upstream>. Fix the provider environment first ...` BEFORE falling through to raw CLI text. The match (a) extracts the provider name from the quoted segment, (b) routes `<upstream>` to the provider-specific i18n key (`errors.activate.pwsh` / `errors.activate.sops` / `errors.activate.azure` / `errors.activate.op` / `errors.activate.aws` / `errors.activate.vaultAddr` / `errors.activate.vaultTls` / `errors.activate.vaultToken`), (c) falls back to `errors.activate.generic` with `{upstream}` placeholder if the upstream does not match any known pattern, and (d) returns a single localized message via the `errors.activateProvider` template `{name} + {reason}`. The provider name in the template is rendered through `providerDisplayNameFn()` (a thin alias for the existing `providerDisplayName()`) so the same localized label used by the provider selector is reused here. Native runtime Vault errors (Decrypt at launch time, NOT ActivationError) keep the original two-line `errors.vaultAddrNotSet` / `errors.vaultTlsRequired` mapping. Adding a new secret provider means: add an `errors.activate.<provider>` i18n key to all 10 locales, add a regex branch in `localizeError`, and document the activation-error surface in this hard-boundary ledger.

- **v0.7.6 History operation label i18n call-site (hard boundary)**: HistoryPage.svelte MUST pass `entry.command` (the full audit command string) into `getOperationLabel`, NOT `entry.command.split(' ')[0]`. The `getOperationLabel` function does full-then-head fallback itself (`history.op.<full command>` before `history.op.<leading word>`), so slicing at the call site discards the subcommand and re-introduces the v0.7.4 regression where `path add` / `path remove` / `path move-up` / `path move-down` collapse to "Path" and `history undo` / `history delete` fall back to English. The fix is one line at the call site. Any new audit-recorded command must (a) add a `history.op.<full command>` key to all 10 locales AND (b) verify the GUI label renders correctly under both English and a non-English locale before merge.
- **v0.7.7 Inheritance chain secret propagation (hard boundary)**: `IsProfileApplicable` in `ProfileEffective.cs` MUST collect the union of `SecretVariables` across the ENTIRE inheritance chain (via `CollectInheritedSecrets`), not just `profile.SecretVariables` on the profile under test. The prior v0.7 check only walked the profile's own list, so a Global profile that inherited a Launch profile with `secretVariables=['OPENAI_API_KEY']` silently passed apply validation and would write DPAPI ciphertext garbage for `OPENAI_API_KEY` to `HKCU\Environment`. The fix rejects apply when ANY inherited variable name matches ANY inherited profile's secret list. The walk uses a visited set keyed by profile Name so a poisoned profiles.json with an undetected cycle cannot infinite-loop. `CollectInheritedSecrets` is defined alongside `IsProfileApplicable` and is the authoritative source for inherited-secret membership; do not add new ad-hoc walk functions.
- **v0.7.7 Global-inherits-Launch topology rejection (hard boundary)**: `ProfileSetInherits` in `EnvFeatures.cs` MUST reject three inheritance combinations at set time, not just at apply time: (a) a Global profile inheriting any Launch profile (`Error: A Global profile cannot inherit from a Launch profile...`), (b) a Launch profile inheriting another Launch profile that already carries secrets (`Error: A Launch profile cannot inherit from another Launch profile that already carries secrets...`), and (c) self-inheritance (v0.7.5 guard retained). The Global<-Launch rejection applies unconditionally because a Launch profile may later be changed to add secrets, and once a Global is applied the inherited ciphertext would silently start leaking. The Launch<-Launch rejection only fires when the parent Launch profile has `secretVariables.Count > 0`, because an empty Launch parent is safe. These checks run BEFORE the new chain is saved to `profiles.json` so a user sees the rejection immediately. If a profiles.json is poisoned (hand-edited cycle), set-inherits wraps ResolveProfileVariables / ResolveProfilePaths in try/catch, prints a clear InvalidDataException message, and returns exit 1 without persisting -- the command itself no longer bricks.
- **v0.7.7 GUI inherits-checkbox combinatorial block (hard boundary)**: `ProfilePage.svelte` inherits checkbox MUST be `disabled` and visually de-emphasized (opacity-40 + cursor-not-allowed) when the combination is blocked: target is `global` + candidate is `launch`, OR target is `launch` + candidate is `launch` + candidate has `secretVariables?.length > 0`. The tooltip MUST show `$t('errors.inheritBlocked')`. A `launch` badge (amber styling) is rendered next to launch-type candidates so the user sees the topology at a glance. The GUI rejects at click time so the user is not forced into a CLI round-trip to learn the rejection; the CLI rejects a second time if the GUI contract is bypassed (e.g. direct CLI invocation). When a new inheritance-blocking rule is added, update GUI combinatorial predicate + CLI injection point + i18n key for all 10 locales + this ledger entry.
- **v0.7.7 Live CLI inheritance-protection integration test (hard boundary)**: any change to `ProfileSetInherits`, `IsProfileApplicable`, `CollectInheritedSecrets`, or the v0.7.7 hard boundaries above MUST be validated by running `pwsh -NoProfile -ExecutionPolicy Bypass -File scripts/test-inheritance-protection.ps1`. The script backs up `%LOCALAPPDATA%\EnvManager\profiles.json`, creates four `EM_INHERIT_TEST_*` profiles (global, launch+secret, launch-plain, second global), verifies four cases (global<-launch_secret rejected, launch<-launch_secret rejected, global<-global_accepted, self<-self rejected), cleans up the test profiles, and restores the original profiles.json. The script does NOT mutate the registry. Default CLI exe path is `bin\Release\net10.0-windows\env-manager-cli.exe`; falls back to Debug builds if Release is absent. The previous default `bin\Release\net10.0\env-manager-cli.exe` (pre-multi-target) is intentionally NOT tried because it is a stale 7/11 v0.3.0 build that prints `Unknown command: profile`.

- **v0.7.7 Single-instance GUI (hard boundary)**: `main.rs` MUST register `tauri_plugin_single_instance` as the first plugin after `tauri_plugin_dialog::init()`. The plugin's callback calls `restore_window(app)` so a second launch of `env-manager.exe` restores and focuses the existing window instead of opening a duplicate. Single-instance is enforced at the OS/process level (named mutex), not at the JS layer, so it cannot be bypassed by a stray window-open call. The Cargo dependency is `tauri-plugin-single-instance = "2.0"` (must match the Tauri 2.x line; pin to 2.x, do not jump to a hypothetical 3.x without testing the callback ABI). When upgrading Tauri, verify the single-instance plugin's callback signature is still `Fn(&AppHandle, Vec<String>, String)` — a signature change would silently compile but never fire the restore on second launch.
- **v0.7.7 Settings persistence on startup (hard boundary)**: `App.svelte` onMount MUST read `darkMode` and `fontScale` from `localStorage` and apply them BEFORE the first variable-list fetch. The settings ARE persisted correctly by `SettingsDialog.svelte` (it writes to `localStorage.setItem('darkMode', ...)` / `setFontScale`), but prior to v0.7.7 the App component never read them back on startup, so every relaunch reset dark mode and font size to defaults. The locale is already read back by `i18n.ts`'s `setupI18n()` (it reads `localStorage.getItem('locale')` and async-switches), so locale persistence was never broken; only darkMode and fontScale were. When adding a new persisted setting, add the read-back in the SAME onMount block in App.svelte — never leave a setting that is written but not read back, that is the exact pattern that caused this regression.
- **v0.7.7 Settings persistence via Rust IPC (hard boundary, supersedes the localStorage-only description above)**: WebView2 localStorage is unreliable in the portable build (the tauri-plugin-store file `gui-settings.json` was never created on disk, confirmed by inspecting every app-data directory). GUI settings (locale, darkMode, fontScale) MUST be persisted via two Rust IPC commands declared in `main.rs`: `read_gui_setting(key: String) -> serde_json::Value` and `write_gui_setting(key: String, value: String) -> bool`. These read/write `%LOCALAPPDATA%\EnvManager\gui-settings.json` directly via `std::fs` + `serde_json`, the SAME proven path used by `profiles.json` / `audit.json`. `frontend/src/lib/settingsStore.ts` wraps `invoke('read_gui_setting')` / `invoke('write_gui_setting')` with a localStorage fallback (localStorage is a sync-read instant cache, not the durable store). `setSetting` writes BOTH localStorage (for instant first-render) AND the IPC file (for durability across restarts). `setupI18n` in `i18n.ts` MUST NOT use `getLocaleFromNavigator()` as a fallback in the first-resolve path: on Chinese Windows the navigator returns `zh-CN`, which overwrites the user's explicit `en` choice when localStorage is empty/unflushed; first-resolve uses `stored || defaultLocale` only, and the async IPC store correction (which reads the real persisted file) wins over stale localStorage. Never re-introduce `tauri-plugin-store` (removed from Cargo.toml, main.rs, package.json) - it did not create the file in the portable build. Never re-introduce `getLocaleFromNavigator` in the first-resolve path. When adding a new persisted GUI setting: add a key, read via `getSetting(key)` in `App.svelte` onMount BEFORE the first fetch, write via `setSetting(key, value)` in the settings dialog handler so both layers stay in sync. ADDITIONALLY (v0.7.7 locale regression fix): `setupI18n` in `i18n.ts` MUST NOT call `setSetting('locale', resolved)` at startup. The prior bug was that setupI18n unconditionally wrote the sync-resolved locale (stale localStorage or defaultLocale) to the durable IPC file BEFORE the async `getSetting('locale')` could read and protect the user's actual persisted choice. This caused the user's explicit `en` preference to be overwritten by `zh` (stale localStorage) on every restart. The fix: setupI18n writes to localStorage sync (for first render), but the IPC file is only written by `SettingsDialog.switchLocale` (user explicit action) or by the async-correction path when the IPC file is empty but localStorage has a value (first-run seeding). The IPC `getSetting('locale')` is the SINGLE SOURCE OF TRUTH and authoritative over stale localStorage after init. Never re-introduce an unconditional `setSetting` in the startup path.
- **v0.7.7 History action column reactive $t (hard boundary)**: `HistoryPage.svelte`'s `getOperationLabel` MUST accept the translation function as a parameter (`tFn: (key: string) => string`) and the template call site MUST pass `$t` explicitly: `{getOperationLabel(entry.command, $t)}`. The prior v0.7.6 fix only corrected the call-site argument (`entry.command` instead of `entry.command.split(' ')[0]`), but kept `$t` INSIDE the function body. Svelte's static reactivity analyzer does NOT track `$store` reads inside function bodies called from the template — only `$store` references that appear directly in the template or in `$:` reactive statements are wired to re-render. So the action column never updated on locale switch even though every other `$t(...)` in the same component did. The fix surfaces `$t` at the call site so Svelte wires the cell to the locale store. When adding any new function that translates inside the History table (or any `#each` body), pass `$t` as an argument from the template; do not rely on the function closing over `$t`. The sentinel placeholder (hover-title showing the raw English `entry.command`) is by design and NOT a bug — it gives the user the original CLI audit string for debugging and should not be i18n'd.
- **v0.7.7 Launch badge i18n (hard boundary)**: the amber `launch` badge next to a launch-type profile candidate in the inherits list (`ProfilePage.svelte`) MUST render `{$t('profiles.typeLaunch')}`, not the hardcoded string `launch`. The key `profiles.typeLaunch` exists in all 10 locales (en=Launch, zh=启动, ja=起動, ko=런치, de=Starten, fr=Lancer, es=Lanzar, pt=Iniciar, ru=Запуск, ar=إطلاق). The badge lets the user see at a glance which candidates are Launch-typed (and therefore subject to the Global<-Launch inheritance block), so localizing it is required — a Chinese user seeing the English word `launch` in an otherwise-Chinese UI is the symptom that triggered this fix. When adding a new profile-type badge anywhere, use the existing `profiles.type<Name>` key; never hardcode the type label.

- **v0.7.8 Locale persistence startup read-order (hard boundary, supersedes v0.7.7 locale sub-entry)**: `setupI18n` in `frontend/src/lib/i18n.ts` MUST NOT seed `localeStore` from a synchronous guess (stale WebView2 `localStorage` or `navigator.language`) before the authoritative durable IPC read resolves. The prior bug: on a Chinese Windows host, stale `localStorage` held `zh` from a prior session; `setupI18n` unconditionally queued `localeStore.set(zh)` synchronously, and once the async `getSetting('locale')` ("en") completed it was too late because the sync-set already overwrote the durable IPC file via a stray `setSetting('locale', resolved)` in the startup path. Fix: `setupI18n` inits the store with the default locale (`en`) for first paint and persists the localStorage hint only (for instant first-render feedback); it MUST NOT call `localeStore.set` with a guessed value and MUST NOT call `setSetting` in the startup path. A new exported `applyPersistedLocale()` awaits the durable `getSetting('locale')` IPC read, calls `localeStore.set(persisted)` authoritatively, updates `localStorage` to match, and seeds the IPC file from `localStorage` ONLY on first run (durable empty + localStorage non-empty). `App.svelte` onMount MUST `import { applyPersistedLocale } from './lib/i18n'` and `await applyPersistedLocale()` AFTER the `fontScale`/`darkMode` read block and BEFORE the first variable-list fetch. The durable IPC file (`%LOCALAPPDATA%\EnvManager\gui-settings.json` via `read_gui_setting`/`write_gui_setting`) is the SINGLE SOURCE OF TRUTH for locale; stale `localStorage` is read-only for first-render hint and never propagated to the durable store at startup. Never re-introduce `getLocaleFromNavigator()` in the first-resolve path (it returns `zh-CN` on Chinese Windows and overwrites an explicit `en`). Never re-introduce an unconditional `setSetting('locale', ...)` in the startup path. When adding a new persisted GUI setting: add a read in the SAME `App.svelte` onMount block, never leave a setting that is written but not read back.
- **v0.7.8 Secret provider activation error i18n keys (hard boundary amendment to v0.7.6 entry)**: the v0.7.6 provider-activation-error i18n ledger is extended with two new keys — `errors.activate.sopsConfig` (matches CLI `sops encryption failed` + `config file not found|no keys provided`, routes to SOPS `.sops.yaml` / `SOPS_PATH` fix guidance) and `errors.activate.opAccounts` (matches CLI `1Password CLI .*No accounts configured|No accounts configured for use with 1Password CLI`, routes to desktop-app-integration / `op account add` / `OP_SERVICE_ACCOUNT_TOKEN` / Connect guidance). Both keys added to ALL 10 translation files. The `localizeError` function in `ProfilePage.svelte` MUST match these patterns before falling through to `errors.activate.generic`. When a new secret-provider activation-failure mode is discovered, add an `errors.activate.<mode>` i18n key to all 10 locales, add a regex branch to `localizeError`, and document the upstream fix in `docs/secret-providers-guide.md`.
- **v0.7.8 Secret provider activation error inline display (hard boundary)**: the GUI provider-change confirmation flow (`requestChangeProvider` -> confirm modal -> `confirmChangeProvider` -> `secretProviderSet`) MUST surface a failed activation probe as an INLINE amber banner directly below the provider `<select>`, NOT only as a toast/modal. `ProfilePage.svelte` keeps a `providerErrorMessage: string | null` reactive state; `confirmChangeProvider`'s catch sets `providerErrorMessage = localizedMessage` (in addition to the legacy `showMessage` toast for redundancy). The inline banner renders `secrets.activeProvider + providerErrorMessage` with a Close button that clears `providerErrorMessage` without side effects. `requestChangeProvider` and `cancelChangeProvider` MUST clear `providerErrorMessage` so a stale error from a prior attempt does not linger. Rationale: a transient toast disappears and the user re-selects blindly, re-hitting the same misconfiguration; the inline banner stays until the user fixes the environment (install module / set env var) and closes it.
- **v0.7.8 Edge-style true-overlay scrollbar (hard boundary)**: `App.svelte` global scrollbar CSS MUST NOT pin `::-webkit-scrollbar { width: ...px }`. Pinning width reserves a dedicated layout track (8px) and contradicts the "float over content, zero layout space" goal that matches Edge/VS Code. The CSS sets `scrollbar-width: thin` + `scrollbar-color` (Firefox fallback, also auto-hide: thumb-color rgba 0 when idle, 0.25 on hover) but leaves `::-webkit-scrollbar` width unset so Chromium renders a transient overlay that does not shift content. `scrollbar-gutter` MUST stay at the browser default (`auto`); never set `scrollbar-gutter: stable` (it reserves space, the opposite of the goal). The thumb `background-color` transition (0.15s) + `border-radius: 8px` is a visual-consistency enhancement over the bare native overlay; the dark-mode block mirrors the light-mode block with white-on-transparent thumb colors. Never re-add `width: 8px` to `::-webkit-scrollbar`.
- **`RunChangeScope` ambiguous-scope rejection**: when a variable exists in both user and system scope, the caller must specify `--scope` explicitly; auto-detection is rejected (no silent pick of user).
- **`path dedupe` HashSet isolation**: the dedupe `seen` HashSet only records non-protected entries, so protected entries are never treated as duplicates of themselves or each other.
- **Backup file validation**: `.json` extension required; writes to `\Windows`, `\Program Files`, `\Program Files (x86)` blocked; 50 MB cap.
- **Disabled-variable restoration**: `toggle` writes a same-scope backup with the original raw registry value and `RegistryValueKind`. Re-enable verifies both exactly before deleting the backup; if both original and backup exist, it refuses the conflict. `list` and `get` project a backup-only disabled variable through the same contract for both user and system scope. Names ending in `_EnvManager_disabled` are internal and cannot be created, addressed, renamed, deleted, or moved directly.
- **v0.7.1 Profile variable/PATH scope selector**: `profile add-var <name> <var> <value> [--scope user|system]` and `profile add-path <name> <dir> [--scope user|system]` accept an optional scope. `ProfileVariable.Scope` (default `"user"`) is persisted in profiles.json and consumed by `ProfileApply` to route the variable to HKCU or HKLM. `ProfileData.PathScopes` is a List<string> parallel to `PathEntries` by index; older profiles.json files written before this field existed load as an empty list and `ProfileApply` treats a missing/out-of-range entry as `"user"` so existing behaviour is unchanged. `ProfileAddVarWithScope`/`ProfileAddPathWithScope` are the argv-parsing wrappers in Program.cs that resolve `--scope` and delegate to `ProfileAddVar`/`ProfileAddPath`. The GUI add-var/add-path panels expose this as a `<select>` bound to `newVarScope`/`newPathScope`; the API layer (`addProfileVar`/`addProfilePath` in api.ts) forwards it as the trailing `--scope` CLI arg. Never write a profile variable or PATH entry without recording its scope — a missing scope silently defaults to `"user"`, which on a real apply would put a system-intended variable into HKCU.
- **Profile creation**: profile names are globally unique because commands are name-addressed. `profile create <name> --type launch --target <exe> [--args <args>] [--cwd <dir>]` validates and persists Launch profiles in one write transaction. Never create a Global profile and convert it in a later operation.
- **Frontend cache consistency**: variable and PATH reads use bounded 5-second caches with generation-aware single-flight reads. A successful write advances the generation; older reads must not refill caches or replace newer visible state. The frontend write busy store is reference-counted until the serialized queue drains.
- **PathList non-blocking Directory.Exists (hard boundary)**: `PathList` must NEVER call `Directory.Exists` synchronously on each PATH entry. Slow UNC/network/non-existent paths can block for several seconds each, which caused the real-world 8s protection-page hang. Use `FastDirectoryExists` which wraps `Directory.Exists` in `Task.Run` with a 200ms per-entry timeout; entries that miss the window resolve to `exists=false` (treated as dead) rather than blocking the whole response. This mirrors the resilience pattern PowerToys applies to PATH health checks.
- **Protection page per-call timeout (hard boundary)**: `ProtectionPage.svelte` refresh must NOT use `Promise.all` for `listVariablesRaw()` + `listPathEntries('user')` + `listPathEntries('system')` without per-call timeouts. A single blocked call (e.g. HKLM access denied, or a slow system PATH entry) hung the whole page. The refresh wraps each call in a `withTimeout(p, ms)` helper that collapses failures/timeouts to `null`, then assembles `allPathEntries` from whichever results arrived. The CLI-side `FastDirectoryExists` change above is the primary fix; the frontend timeout is defense in depth so future slow calls cannot regress this.
- **Launch profile apply hard boundary (v0.7.1)**: `ProfileApply` MUST reject any profile whose `ProfileType == "launch"` at entry point with a clear error directing the user to `profile launch <name>`. Launch profiles are *local* only: their variables are injected into a child process env block via `env_clear + env(k,v)` and must NEVER be written to the user registry, never broadcast `WM_SETTINGCHANGE`, never be persisted beyond the launcher process lifetime. Allowing apply would silently demote a Launch (local) profile into a Global-style persistent registry write, violating the locality contract users rely on. The GUI toggle on a Launch profile is disabled with an i18n tooltip (`profiles.launchApplyDisabled`) for the same reason; `profile set-launch` is the only path that converts between global<->launch and it carries full audit + validation.
- **Trailing-backslash + quote argv recovery**: .NET argv tokenization can fold a quoted value ending with `\` together with following args. `LenientArgs.WasArgsCorruptedByTrailingBackslashQuote` detects the signature (an arg element containing both a quote and an embedded long-option/separator marker such as ` --scope `, ` --overwrite`, ` --index `, or standalone ` -- `); only then `LenientArgs.Tokenize` re-scans `Environment.CommandLine` with quotes as terminators and backslashes literal. Recovery replaces known-corrupted argv even when its token count changes. A standalone `--` stays intact for `profile launch <name> -- <extra-args>`. Clean argv from the Tauri/Rust `Command::arg()` path is never re-tokenized. See `ArgTokenizer.cs`.
- **Verified registry writes**: `SetVariable` captures the prior raw value and registry kind, writes, then verifies the exact persisted raw value and kind before reporting success. If verification or writing fails, it restores the prior value (or deletes a newly-created value) and verifies that restoration. PATH mutations must use `SetPathEntries` and therefore this transaction; never add a direct PATH registry write path.
- **Rust IPC input validation**: command whitelist, max 64 args, max 32,767 chars per arg, null bytes and control characters rejected.
- **Audit history**: `%LOCALAPPDATA%\EnvManager\audit.json`, capped at 2,000 entries. Undo refuses stale changes unless `--force` explicit. Profile entries carry `Scope = "profile"` and route to `TryUndoProfileAudit`.
- **Live test harness (hard boundary after incident)**: any local CLI smoke test against the REAL registry MUST use `scripts/test-with-restore.ps1`. Before testing it snapshots every value name, unexpanded value, and `RegistryValueKind` in `HKCU\Environment` plus accessible `HKLM\SYSTEM\CurrentControlSet\Control\Session Manager\Environment`, and byte-snapshots test-owned Env Manager internal configuration (`profiles.json`, `audit.json`, and protection JSON files). It compares exact before/after state, restores internal configuration after the suite, and on any failure or drift reconciles every registry value (including deleting newly introduced values) before broadcasting `WM_SETTINGCHANGE`. HKLM is skipped, with an explicit warning, only when an administrator-accessible snapshot cannot be created. The harness retains `.reg` and `.json` artifacts on failure or `-KeepBackup`, otherwise removes them on a clean run. Never run raw registry-mutating CLI smoke commands against the host. Backups live in `.test-backups/` (gitignored).
- **Per-session host snapshot (hard boundary after incident)**: before any local dev session that touches the CLI or build, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/snapshot-host-env.ps1` once. It exports `HKCU\Environment` and `HKLM\...\Environment` to `.env_bak/<UTC-timestamp>/` and copies Env Manager's internal configs from `%LOCALAPPDATA%\EnvManager\` (profiles.json, audit.json, protected-vars.json, protected-paths.json, builtin-protected-vars.json, builtin-protected-paths.json) into `.env_bak/<UTC-timestamp>/internal-configs/`. `.env_bak/` is gitignored and NOT auto-cleaned. This is the per-session forensic complement to `test-with-restore.ps1` (which does per-run backup+restore). The prior incident (test harness only backed up HKCU, so a RunSet regression clobbered the user system PATH) motivated both guards.

- **v0.7.1 Incident: CommonProgramW6432 disabled via toggle (2026-07-22)**: `CommonProgramW6432` was a built-in Windows REG_EXPAND_SZ system variable (value `C:\Program Files\Common Files`) but was missing from `protection.defaults.json`, so the `toggle` command could rename it to `CommonProgramW6432_EnvManager_disabled` and delete the original. The user noticed `opencode` and `npm` broke because system PATH resolution depends on this family of variables. Root cause: `protection.defaults.json` listed `CommonProgramFiles` and `CommonProgramFiles(x86)` but omitted `CommonProgramW6432` and `ProgramW6432`. Recovery: wrote `CommonProgramW6432` back to HKLM with the exact original value and REG_EXPAND_SZ kind, verified, deleted the `_EnvManager_disabled` backup, broadcast WM_SETTINGCHANGE. Post-incident fix: added `COMMONPROGRAMW6432` and `PROGRAMW6432` to `protection.defaults.json` and re-seeded `%LOCALAPPDATA%\EnvManager\builtin-protected-vars.json`. All future built-in Windows variables that the OS derives from `ProgramFilesDir`/`ProgramFilesDir(x86)`/`CommonProgramFilesDir`/`CommonW6432` must be added to the protection list before any toggle/delete/set/rename/change-scope path can touch them.
- **Logs never record environment values** - CLI/Rust log command names and argument counts only. Values may contain credentials.

### Agent Safety Guidelines

1. Always use `--scope user` for non-interactive workflows. System scope requires elevation and may fail silently.
2. Call `agents --json` first to discover the full command contract, safety boundaries, and async support.
3. Read commands are safe to batch concurrently. Write commands are serialized - do not fire multiple writes in parallel.
4. Never delete critical system variables. Always backup first.
5. Profile names: 1-255 chars, no null bytes, newlines, carriage returns. Variable names in profiles: no `=`.
6. Backup files: `.json` extension, not in system directories, under 50 MB.

## i18n (Internationalization)

10 languages: en, zh, ja, ko, de, fr, es, pt, ru, ar. Engine: `svelte-i18n` (ICU MessageFormat).

**i18n sync is mandatory when adding any new user-facing string** (button label, message, dialog text, error):
1. Add the key to `frontend/src/lib/translations/en.json` (the reference).
2. Add the same key with translated value to ALL other 9 translation files.
3. Use `$t('key')` in Svelte components - never hardcode display text.
4. Register any new locale in `frontend/src/lib/i18n.ts` (both `register()` call and `supportedLocales` array).

ICU caveat: single quotes `'` are escape characters. Never wrap a `{placeholder}` in single quotes - `'{name}'` produces the literal text `{name}`. Use bare `{name}` or double single quotes `''` for a literal quote.

Default locale (en) loads synchronously via `addMessages()` so the UI renders under Tauri's custom protocol. Other locales load lazily.

## Testing

Frontend unit tests use Vitest with jsdom. Tests live alongside source as `*.test.ts`. Setup at `frontend/tests/setup.ts` mocks `@tauri-apps/api/core` `invoke` and `svelte-i18n`.

```powershell
cd frontend
npx vitest run          # Run all tests once
npm run test:ui         # Interactive test UI
npm run test:coverage   # With coverage report
npm run test:e2e        # Playwright E2E tests
```

Mandatory rules:
1. **Before every commit**: `npx vitest run` from `frontend/`, all tests must pass.
2. **New feature = new tests**: new CLI command, GUI component, store, API function, or i18n key must add unit test coverage in the same commit.
3. **i18n key completeness**: `src/lib/translations.test.ts` validates every `en.json` key exists in all 9 non-English files with non-empty values.
4. **Build verification after code changes**: run `powershell -NoProfile -ExecutionPolicy Bypass -File frontend/scripts/build-all.ps1` and verify `release/portable/env-manager.exe` launches. Do not commit code that breaks the build. See [docs/build-and-release.md](docs/build-and-release.md).
5. **No emoji in tests** - same no-emoji rule as the rest of the project.
6. **Live CLI test harness (two-pronged gate)**: when validating the published CLI in `release/cli-only/`, run `powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-with-restore.ps1`. This script backs up `HKCU\Environment` to a `.reg` file before tests, runs a smoke suite that touches only `EM_TEST_`-prefixed user variables and profiles, then verifies registry drift is zero (leftover `EM_TEST_` keys or modified pre-existing keys trigger restore). On any failure (test fails OR registry drift detected), it restores from the backup, broadcasts `WM_SETTINGCHANGE`, and exits non-zero while keeping the backup for forensics. On a clean run the backup is auto-deleted. Never run live CLI smoke tests against the real registry without this script (it is the only guard that prevents test-time mutation of the host machine).

Test file inventory:

| File | Coverage |
|------|----------|
| `src/App.test.ts` | Root component rendering, navigation |
| `src/lib/stores.test.ts` | Svelte stores (variables, loading, error, scope, search, settings) |
| `src/lib/api.test.ts` | Tauri IPC bridge, CLI command invocations, response parsing |
| `src/lib/i18n.test.ts` | Locale registration, default locale, localStorage persistence |
| `src/lib/translations.test.ts` | Translation key completeness across all 10 locales |
| `src/lib/race.test.ts` | CLI/GUI race condition prevention, toggle safety, rapid toggle serialization |
| `src/lib/sync.test.ts` | CLI/GUI state synchronization, mutation triggers refresh, error store lifecycle |
| `src/lib/debug.test.ts` | Debug logging, 200-entry cap, isWriteInProgress tracking |
| `src/lib/profile-drag.test.ts` | Pointer reorder, localStorage persistence, stale/duplicate order recovery, and large-list preservation |
| `src/lib/multi-profile.test.ts` | Single-profile policy, backup/restore, protected variable rejection |
| `src/lib/change-scope-protection-profile.test.ts` | change-scope CLI args, protected-variable rejection, profile audit record + undo |
| `src/lib/path-dedupe.test.ts` | dedupePathEntries CLI args (dry-run, scope), result shape, failure propagation |
| `src/lib/review-regressions.test.ts` | Code-review invariants: EditDialog ordering, protected-variable guards, PATH dedupe isolation, ambiguous scope rejection, profile audit fail-loud, transactional PATH writes, argv recovery, harness rollback verification, safe startup fallback, and console-free update checks |
| `src/lib/quoting.test.ts` | GUI argv safety: trailing-backslash/quote values stay independent array elements (no merging with --scope/--index/--overwrite) |
| `src/lib/v0.6-launch-health.test.ts` | v0.6.0 profileSetLaunch/profileLaunch/pathHealth API arg construction, fix/dry-run routing, read vs write classification |
| `src/lib/v0.7-secrets.test.ts` | v0.7 profileAddSecret/EditSecret/RemoveSecret/RevealSecret CLI args (path, write classification), secretVariables type surface, design invariants |
| `src/lib/path-badge-exclusivity.test.ts` | PathEditor badge exclusivity after health check (single duplicate badge regression, all boolean combinations) |
| `src/lib/components/Variables.test.ts` | Protected variable controls are disabled and cannot dispatch a toggle IPC call |
| `src/lib/history-col-resize.test.ts` | HistoryPage column resize persistence, defaults, corrupt-storage fallback, clamp range |
| `scripts/test-with-restore.ps1` | Live CLI smoke harness: exact HKCU plus accessible HKLM snapshots, disabled-variable raw-value/RegistryValueKind recovery, raw trailing-backslash command-line regression, exact drift verification, and transactional reconciliation on failure. |
| `frontend/src/lib/inheritance-protection.test.ts` | v0.7.7 setProfileInheritance IPC contract: `args` contains `set-inherits`, profile name, and forwarded parent name(s); empty parents list forwards silently; empty profile name is forwarded as-is |
| `scripts/test-inheritance-protection.ps1` | Live CLI integration harness for v0.7.7 Inheritance chain secret propagation + Global<-Launch / Launch<-Launch-secret / self-inheritance hard boundaries. Backs up profiles.json, creates four `EM_INHERIT_TEST_*` profiles, verifies CLI rejection messages, restores profiles.json. Does NOT mutate the registry. |
| `scripts/snapshot-host-env.ps1` | Per-session forensic snapshot: export HKCU+HKLM Environment hives + copy EnvManager internal configs to `.env_bak/<timestamp>/` (not auto-cleaned) |

## Coding Standards

- **C#**: 4-space indent, 120 char max line, `using` for Registry keys, catch specific exceptions (no empty catch), explicit types on public API, `var` for locals.
- **TypeScript/Svelte**: 2-space indent, strict mode, no implicit any, JSDoc on exports, `$:` reactive syntax, props validation.
- **Rust**: 4-space indent, `log` crate macros for diagnostics, `#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]` to hide the release console. Every Windows-native child process spawned by the Tauri shell must apply `CREATE_NO_WINDOW`, including CLI execution, `where`, and update checks.
- **No emoji** in source, tests, docs, or commit messages.
- **Font size scaling**: 6 presets (85%-160%) in Settings, applied via `document.documentElement.style.fontSize = 13 * scale + px`, CSS rem units throughout, persisted as `fontScale` in localStorage.
- **Add CLI to PATH**: Settings dialog one-click button. Calls `cli_diagnostics` to resolve CLI path (no hardcoding), extracts dir, checks for duplicates in user PATH, adds via `path add`. Implemented as `addCliToPath()` in `api.ts`.

## File encoding

- All files: UTF-8 without BOM.
- Line endings: enforced by `.gitattributes` at repo root. Default text: LF on disk and in index. Windows-native scripts (`.bat`, `.ps1`, `.cmd`): CRLF. Binary assets (`png`, `ico`, `exe`, `dll`, `msi`, etc.): marked `binary`, never normalized.
- `core.autocrlf` is `false` at the repo level. Do not re-enable it.
- `frontend/node_modules/` is gitignored; never tracked. If tracked files appear: `git rm -r --cached frontend/node_modules` and commit.
- After any `.gitattributes` change: `git add --renormalize .` and commit the line-ending-only diff.
- `apply_patch` does byte-exact matching. If a patch fails for context that looks identical, suspect CRLF/LF mismatch on disk and re-inspect the target region before retrying. Never write a file with mixed line endings.

## Commit Convention

Conventional Commits:

```
<type>(<scope>): <subject>

<body>
```

Types: `feat`, `fix`, `docs`, `refactor`, `test`, `perf`, `chore`
Scopes: `cli`, `gui`, `backup`, `registry`, `i18n`, `docs`, `build`

## Mandatory Build After Code Changes

Every commit that modifies CLI, GUI, or build code MUST produce compiled artifacts in `release/` before pushing. See [docs/build-and-release.md](docs/build-and-release.md) for the full build procedure, prerequisites, output layout, and release steps.

```powershell
Get-Process -Name 'env-manager*' -ErrorAction SilentlyContinue | Stop-Process -Force
cd frontend
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/build-all.ps1
```

Verify: `release/portable/env-manager.exe`, `release/portable/env-manager-cli.exe`, `release/cli-only/env-manager-cli.exe`, `release/msi/Env Manager_X.Y.Z_x64.msi` (no locale suffix). The `release/` directory is gitignored - artifacts are for local testing only, not committed to git.

## Mandatory Git Push After Code Changes (Provenance)

Every commit that modifies CLI, GUI, build code, or documentation MUST also `git push` to the remote immediately after the local commit is created and the build artifacts are produced. This is a provenance mandate: the project has had real incidents (see `v0.7.1 Incident: CommonProgramW6432 disabled via toggle` above) where_after a fix was implemented locally but never pushed, another agent resumed work and re-introduced the bug because the remote lagged. Local-only commits are invisible to the next agent and the remote is the only authoritative time-ordered history.

The mandate numbers two paths:

1. **Path A (preferred): remote push succeeds** -- after `build-all.ps1`, `git add`, `git commit`, then `git push origin main`. The PAT pattern at the bottom of this file is used, then the PAT is immediately cleared from the remote URL.
2. **Path B (fallback): remote push fails** -- if the remote is unreachable or the PAT is exhausted, the local commit is STILL authoritative and the push is retried at the next opportunity. The local branch MUST be left in a pushable state (clean tree, fast-forwardable). Never use `git reset --hard` to "clean up" a failed-push commit; that erases the local provenance trail. Document the failed push in the commit message body if applicable.

Nothing in this section weakens the live-test harness mandate or the mandatory build-after-code-changes mandate; it complements them with a remote-provenance guarantee.


## Documentation Maintenance

When the project changes, update files in the same commit:

| Event | Files to update |
|-------|----------------|
| New CLI command | AGENTS.md (quick reference), docs/cli-commands.md, README.md, README_CN.md, docs/architecture.md alignment table |
| Changed command args | AGENTS.md, docs/cli-commands.md, README.md, README_CN.md |
| New GUI feature | AGENTS.md, docs/architecture.md alignment table, README.md, README_CN.md, all 10 translation files |
| New debug log point | docs/build-and-release.md (Logging section) |
| Dependency update | docs/build-and-release.md, AGENTS.md if it affects build/architecture |
| Build change | AGENTS.md, docs/build-and-release.md, README.md, README_CN.md |
| Directory structure change | AGENTS.md |
| CodeGraph index change | docs/build-and-release.md (CodeGraph section) |
| Code change (any) | Run build-all.ps1, verify release/ artifacts |
| New test file | AGENTS.md (test inventory), docs if it documents new behavior |
| Architecture/IPC/security change | docs/architecture.md, docs/backup-and-profiles.md, AGENTS.md hard boundaries |

A commit that does not update AGENTS.md (and the relevant `docs/` file) when the project has changed is considered incomplete.

## How to Add a New CLI Command

1. Add a `case` in `Program.cs` `Main()` switch statement.
2. Implement the command method.
3. Update `ShowHelp()` with usage text.
4. Add to the command table in [docs/cli-commands.md](docs/cli-commands.md) and the quick reference in AGENTS.md.
5. If write command: add to `WRITE_COMMANDS` in `frontend/src-tauri/src/main.rs`. If read command: add to `READ_COMMANDS`.
6. Update `ALLOWED_COMMANDS` in `main.rs`.
7. Update `README.md` and `README_CN.md`.
8. Add the API function in `frontend/src/lib/api.ts` and the GUI surface in the appropriate `.svelte` component.
9. Add i18n strings to all 10 translation files.
10. Update the alignment table in [docs/architecture.md](docs/architecture.md).
11. Add test coverage.
12. Run `build-all.ps1` and verify release artifacts.

## Detailed Reference Index

| Topic | File |
|-------|------|
| Full CLI command table, scope, debug, error handling, profiles, toggle, path editor, path resolution | [docs/cli-commands.md](docs/cli-commands.md) |
| Architecture, IPC bridge, race condition prevention, system tray, toast, caching, auto-update, security hardening, modal dialog, rename/change-scope, profile audit history, GUI/CLI alignment table | [docs/architecture.md](docs/architecture.md) |
| Build system, prerequisites, output layout, mandatory build rules, release steps, dependencies, CodeGraph, performance targets, logging, debugging | [docs/build-and-release.md](docs/build-and-release.md) |
| Backup JSON format, profile JSON format, extended state and safety contracts, full security list, agent safety guidelines | [docs/backup-and-profiles.md](docs/backup-and-profiles.md) |
| CLI-level agent guide (distributed with CLI binary) | [AGENTS.cli.md](AGENTS.cli.md) |
| Secrets architecture blueprint: current capabilities, limitations, v0.8-v1.0 phased roadmap, anti-rejection checklist, risk counter-decisions | [docs/secret-architecture-blueprint.md](docs/secret-architecture-blueprint.md) |
