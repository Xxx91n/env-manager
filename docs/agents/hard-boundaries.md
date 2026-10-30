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
- Any smoke test against the real registry runs through `scripts/test-with-restore.ps1`.

## Secrets

- For the secret verbs (`add-secret`/`edit-secret`/`remove-secret`), secret **plaintext** never reaches the registry, `profiles.json`, notes, logs or audit records. The stored form is a provider envelope or a `mount:<id>` reference; audit records the name plus a marker. The generic `add-var`/`edit-var`/`remove-var` verbs do not consult the secret marker and are a known violation of this invariant - see the Plaintext lifecycle section of `docs/architecture.md`.
- Secrets belong to Launch profiles only. A profile carrying a secret is never applied to the registry, including via inheritance.
- Unknown or failed providers are fail-closed: no silent fallback, no ciphertext injected as a value.
- The GUI derives the provider list from `secret-provider list`; it never hardcodes providers.
- Logs and stderr scrub secret-bearing patterns. Extending the pattern list means extending all three tiers (CLI, Tauri shell, service) in one change.

## Audit

- `history undo` refuses a stale change unless `--force` is explicit; every ledger write stays verifiable by `audit verify` (hash chain from the 64-zero genesis) — a ledger nobody verifies is just a log.

## IPC and processes

- Rust IPC: command whitelist, max 64 args, max 32767 chars per arg, NUL and control characters rejected.
- External provider processes get a fixed timeout, drained pipes before the wait, and a kill on timeout.
- Both named pipes are created with the declared descriptor in `service/src/ipc.rs` (`BA`/`SY` full access, `OW` generic-all), handed to `CreateNamedPipeW` as `lpSecurityAttributes`. Never create a pipe with the default descriptor and never retune a DACL after creation: `SetNamedSecurityInfoW` by name answers `87 ERROR_INVALID_PARAMETER` for a named pipe, and a per-instance post-creation change takes the listener down. Widening or narrowing the descriptor changes who may connect at all, so such a change is re-verified by the manual `Pipe DACL Observation` workflow, and its run id belongs in the PR description.

## Service control (SCM)

- `service/src/scm.rs` owns the control protocol: `StartServiceCtrlDispatcherW` runs once on the process's initial thread, `service_main` builds the tokio runtime on the SCM thread, and `run_server` creates the named-pipe listener, reports `SERVICE_RUNNING`, then starts the reconcile loop. Slow work moved ahead of the RUNNING report brings back the boot-time 1053 failure.
- The stop path fits inside the wait hint the SCM was told: `SHUTDOWN_DRAIN` plus `RUNTIME_DRAIN` stay under `STOP_WAIT_HINT_MS`, asserted at compile time in `service/src/scm.rs`.
- A stop converges the rotation it interrupts (issue #153): `ipc::accept_loop` joins its in-flight connection handlers before the server body returns, and `reconcile::run_cli_child` terminates *and reaps* the delegated CLI rotate child inside that window, so `SERVICE_STOPPED` never names a process that can still write `profiles.json`. Never hand a rotation back to a detached task, and never wait a child out: the same window answers `SERVICE_ACCEPT_SHUTDOWN` during logoff. Recovery of a lost rotation is replay - the mount keeps its old `lastRotatedAt`, so the next `rotate` or reconcile tick does the work again. The guarantee covers the direct CLI child; a provider helper it spawned (`op`/`sops`/`pwsh`) is not tree-killed, and none of those helpers write `profiles.json` or `secretMount.json`.

## Build and packaging

- `<Version>` in `env-manager.csproj` is the single source; `scripts/build.mjs` syncs `frontend/package.json` and aborts on a CLI version mismatch.
- After any source change, build without `--skip-cli` / `--skip-gui` / `--skip-service`; those flags are for repackaging only.

## MSI installer

- No `ServiceControl/@Start="install"` and no `Wait="yes"` on Stop/Remove: silent install and uninstall must not block on the service.
- `MajorUpgrade` pins `Schedule="afterInstallExecute"`; the old-service stop is its own component conditioned on `WIX_UPGRADE_DETECTED`.
- `Guid="*"` everywhere except the three rename-prone binaries, which pin explicit GUIDs. Never use `util:RemoveFolderEx`.
- Any `installer.wxs` change repasses three real acceptance layers: WiX structural compilation (`scripts/build.mjs` compiles `installer.wxs` into the MSI, so structure errors fail the build — but only when WiX 3.14 is present: set the `WIX` env var or install WiX 3.14; without it, build.mjs logs "WiX tools not found, skipping MSI" and skips the MSI, leaving structure errors hidden on a local build), the five-cell silent MSI install/upgrade matrix (`scripts/test-msi-upgrade-matrix.ps1` and the `msi-upgrade-matrix` job in `.github/workflows/build.yml`), and the four-step silent msiexec runbook in `docs/build-and-release.md`.

## Frontend

- Theme is dual-axis (`data-theme` × `data-theme-style`) over tokens in `app.css`: no `dark:` prefixes, no hardcoded palette colors on theme-relative surfaces.
- `:root { color-scheme: light dark }` stays — it is the only thing that themes the WebView2 scrollbar.
- Dark `--destructive` stays at or above WCAG AA 4.5:1.
- A new user-visible string lands in all 10 locale files in the same change; CLI errors stay English and are localized in the GUI. Never `window.prompt()` — use `InputDialog`.

## Verification

- `scripts/test-all.ps1` runs both suites, keeps the full log on disk and reports exit codes only. Engine tests never touch the real registry; the differential oracle is opt-in via `EM_DIFFERENTIAL_ORACLE=1` under `test-with-restore.ps1`.
- `scripts/check-bare-ids.ps1` fails CI when a bare ticket/issue number, a retired ADR or decision-ledger number, or a version label survives outside `docs/history/` and `CHANGELOG.md`.
