# Hard Boundaries

Invariants a change must not violate; where a test pins one, that check is the authority.

## Protected state

- Built-in protected variables and PATH entries (seeded from `protection.defaults.json`) cannot be set, deleted, toggled, renamed or scope-changed; custom locked entries behave the same until unlocked. `*_EnvManager_disabled` names are internal — never create, address, rename or delete them.
- Any Windows variable the OS derives from `ProgramFilesDir` / `CommonW6432` joins the protection list before a write path can reach it (`CommonProgramW6432` was clobbered once).

## Registry writes

- Every write takes all three locks: process mutex `Local\EnvManager.RegistryMutation`, Rust `CLI_RWLOCK`, frontend `writeChain`. No bypass.
- `SetVariable` captures the prior raw value and `RegistryValueKind`, writes, verifies the exact persisted pair, and restores on failure. PATH goes through `SetPathEntries`; never add a direct PATH write.
- rename / change-scope write and verify the target before deleting the source. Delete-then-set is forbidden.
- No TxR/TxF (deprecated by Microsoft, no registry multi-value primitive). Compensatory writes + locking + audit recovery is the only route.
- Ambiguous scope (same name in HKCU and HKLM) requires an explicit `--scope`; never auto-pick.
- Launch profiles never reach the registry and never broadcast `WM_SETTINGCHANGE`.
- Any smoke test against the real registry runs through `scripts/test-with-restore.ps1`. Raw write commands are a red line.

## Secrets

- Secret values never reach the registry, `profiles.json`, notes, logs or audit records — neither plaintext nor ciphertext. Audit records the name plus a marker.
- Secrets belong to Launch profiles only. A profile carrying a secret is never applied to the registry, including via inheritance.
- Unknown or failed providers are fail-closed: no silent fallback, no ciphertext injected as a value.
- The GUI derives the provider list from `secret-provider list`; it never hardcodes providers.
- Logs and stderr scrub secret-bearing patterns. Extending the pattern list means extending all three tiers (CLI, Tauri shell, service) in one change.

## Audit

- `history undo` refuses a stale change unless `--force` is explicit; every ledger write stays verifiable by `audit verify` (hash chain from the 64-zero genesis) — a ledger nobody verifies is just a log.

## IPC and processes

- Rust IPC: command whitelist, max 64 args, max 32767 chars per arg, NUL and control characters rejected.
- External provider processes get a fixed timeout, drained pipes before the wait, and a kill on timeout.

## Build and packaging

- `<Version>` in `env-manager.csproj` is the single source; `scripts/build.mjs` syncs `frontend/package.json` and aborts on a CLI version mismatch.
- After any source change, build without `--skip-cli` / `--skip-gui` / `--skip-service`; those flags are for repackaging only.

## MSI installer

- No `ServiceControl/@Start="install"` and no `Wait="yes"` on Stop/Remove: silent install and uninstall must not block on the service.
- `MajorUpgrade` pins `Schedule="afterInstallExecute"`; the old-service stop is its own component conditioned on `WIX_UPGRADE_DETECTED`.
- `Guid="*"` everywhere except the three rename-prone binaries, which pin explicit GUIDs. Never use `util:RemoveFolderEx`.
- Any `installer.wxs` change repasses the string-gate suites and the four-step silent msiexec runbook in `docs/build-and-release.md`.

## Frontend

- Theme is dual-axis (`data-theme` × `data-theme-style`) over tokens in `app.css`: no `dark:` prefixes, no hardcoded palette colors on theme-relative surfaces.
- `:root { color-scheme: light dark }` stays — it is the only thing that themes the WebView2 scrollbar.
- Dark `--destructive` stays at or above WCAG AA 4.5:1.
- A new user-visible string lands in all 10 locale files in the same change; CLI errors stay English and are localized in the GUI. Never `window.prompt()` — use `InputDialog`.

## Verification

- `scripts/test-all.ps1` runs both suites, keeps the full log on disk and reports exit codes only. Engine tests never touch the real registry; the differential oracle is opt-in via `EM_DIFFERENTIAL_ORACLE=1` under `test-with-restore.ps1`.
