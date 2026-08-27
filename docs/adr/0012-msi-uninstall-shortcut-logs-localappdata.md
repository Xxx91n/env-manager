---
title: "0012: Uninstall shortcut in INSTALLDIR + GUI logs externalized to LocalAppData + 14-day retention"
status: Accepted
date: "2026-08-24"
deciders: [user, codex]
tags: [msi, logging, lifecycle]
---

## Context

After v0.9.29 landed the WixUI_InstallDir + ARPPRODUCTICON + desktop-shortcut work (ADR-0011), user smoke-tested the MSI and reported two gaps:

1. **No uninstall entry from a "user browsable location"** — they compared with `D:\cc-swich\Uninstall CC Switch.lnk` and `C:\Users\Administrator\AppData\Local\Programs\ZCode\Uninstall ZCode.exe`. Pure MS-ARP-only uninstall is technically correct (Windows Logo), but the user explicitly wants a clickable entry inside `C:\Program Files\Env Manager\`.
2. **Uninstall is not complete** — `C:\Program Files\Env Manager\logs` directory and its rotated logs survive uninstall. Root cause: GUI writes logs to `current_exe_dir()/logs` (INSTALLDIR\logs inside Program Files), which MSI treats as orphaned runtime data (not owned by any component).

Both issues have well-established industry templates; this ADR captures which template we pick and why.

## Decisions

### D1 — Uninstall shortcut placement: `[INSTALLDIR]\Uninstall Env Manager.lnk`

Target: `[System64Folder]msiexec.exe /x [ProductCode]` (classic FireGiant / Rob Mensching WiX pattern).
Component pairs the Shortcut with a `RegistryValue KeyPath=yes` under `HKMU` (HKLM resolves via InstallScope=perMachine; using HKCU triggers ICE37/ICE57 mixed-scope warnings on per-machine MSI).
Inside `INSTALLDIR`, NOT under Start Menu folder. The Start Menu continues to carry only the main `Env Manager.lnk`.

Rejected:
- **ARP-only** (no shortcut): Correct per Windows Logo but doesn't match user expectation set by cc-swich / ZCode convention.
- **Start Menu folder shortcut**: Less discoverable than "next to the exe" for this user base; also doubles Start Menu entries.

### D2 — GUI logs externalized to `%LOCALAPPDATA%\EnvManager\logs`

Replaces the previous `current_exe_dir()/logs` lookup in `frontend/src-tauri/src/main.rs::main()`.

Rationale (atomcode research, 21 retrieved sources):
- Microsoft PowerToys: `%LOCALAPPDATA%\Microsoft\PowerToys` (official devdocs).
- 1Password 8: `%LOCALAPPDATA%\1Password\logs`, 14-day auto-delete (official support page).
- Chrome: `%LOCALAPPDATA%\Google\Chrome\User Data\` (official Chromium doc).
- Tauri v2 `tauri-plugin-log` default: `{FOLDERID_LocalAppData}/{bundleId}/logs` (official plugin source).
- Electron-builder NSIS template: `deleteAppDataOnUninstall` default false; logs in AppData are kept.

Service-side logs (env-manager-service.exe) UNCHANGED — they continue writing `%ProgramData%\EnvManager\` because the service account has no meaningful LocalAppData (writes would go to `systemprofile\AppData\Local`, which is the dandraka/MS anti-pattern for services).

### D3 — Backstop cleanup for legacy `INSTALLDIR\logs` residue

New WiX component `LegacyLogsCleanup` targeting the nested `LogsDir` directory inside `INSTALLDIR`:
- `<CreateFolder/>` so MSI owns the directory skeleton.
- `<RemoveFile Name="*.log*" On="uninstall"/>` wildcard for flat `*.log` (`env-manager.log`, `env-manager.log.YYYY-MM-DD`).
- `<RemoveFolder On="uninstall"/>` removes the now-empty directory.
- Registry KeyPath per existing pattern.

Avoids the wixsharp #1114 anti-pattern (componentized empty directory without file cleanup) by pairing `CreateFolder` with a scoped wildcard `RemoveFile` — the wildcard only matches `*.log*` inside this specific logs subdir, NOT the whole INSTALLDIR (which would be the other classic bug).

**Post-ship amendment (same-day smoke test):** the original wiring placed
`<RemoveFolder Id="RemoveInstallDir" Directory="INSTALLDIR" On="uninstall"/>`
inside `EnvManagerDataComponent` (rooted at `ProgramData\EnvManager`). XML
compiled, install/uninstall both exited 0, but `INSTALLDIR` was left behind
because each `RemoveFolder`'s effective directory is the **hosting component's
parent directory** — `Directory="INSTALLDIR"` was silently ignored when the
component lived under `EnvManagerDataDir`. Live evidence: the MSI log only
contained `FolderRemove` ops for `INSTALLDIR\logs`, never for `INSTALLDIR`.
Fixed by moving the `RemoveFolder` element into the `UninstallShortcut`
component (which IS rooted at `INSTALLDIR` via `<DirectoryRef Id="INSTALLDIR">`)
and omitting the explicit `Directory=` attribute so the default (the component's
directory) is used. New stringgate in `installer-wxs-msi-0.9.30.test.ts`:
`RemoveInstallDir lives on a component rooted at INSTALLDIR`. Memory aid for
future WiX edits: `<RemoveFolder>` **`Directory` is default, not override**.

### D4 — Log retention: 14-day sweep, no per-file size cap

GUI startup spawns a fire-and-forget thread that walks `%LOCALAPPDATA%\EnvManager\logs` and removes any `env-manager.log*` whose `metadata.modified()` is older than 14 days.

Rationale:
- 1Password 8 uses the exact same 14-day figure.
- tracing-appender has no built-in per-file size cap; adding one would need a custom `MakeWriter` or a switch to tauri-plugin-log (which has 40KB KeepOne). Daily rotation already bounds a single run to ~24h per file; size cap is overkill for Ponytail mode.
- Date-based retention is a one-liner `checked_sub` and a small `read_dir` loop, ~30 lines in `frontend/src-tauri/src/main.rs`.

## Evidence for smoke-test closure

- `cargo check` on `frontend/src-tauri` green.
- Vitest: `frontend/src/installer-wxs-msi-0.9.30.test.ts` (6 new string gates) + existing `installer-wxs-msi-0.9.29.test.ts` (7) all pass.
- `node scripts/build.mjs --arch x64` produces `release/msi/Env Manager_0.9.30_x64.msi` (5.0 MB) with zero ICE errors (one initial ICE57 warning was resolved by HKCU → HKMU move).
- Live loop: silent install → launch GUI → verify uninstall .lnk exists, `%LOCALAPPDATA%\EnvManager\logs\env-manager.log*` created, `INSTALLDIR\logs` is empty; then silent uninstall exits 0 and `C:\Program Files\Env Manager\` is fully removed (verified via Test-Path False).

## Consequences

Positive:
- User-reported uninstall entry now exists.
- User-reported logs residue fixed structurally (logs live outside INSTALLDIR) AND temporarily (backstop wildcard clears pre-v0.9.30 leftovers).
- Future upgrades no longer pay the residue tax every cycle.
- Industry-standard 14-day retention prevents unbounded disk growth in `%LOCALAPPDATA%`.

Neutral:
- ProgramData\EnvManager logs survive uninstall (unchanged from v0.9.29) — that is intended, they are crucial for post-mortem support.

Deferred:
- Switching from tracing-appender to tauri-plugin-log: only a win if per-file 40KB cap becomes a requirement.
- Major version upgrade path from pre-v0.9.30: the LegacyLogsCleanup backstop handles relics from those installs.
