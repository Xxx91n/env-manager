# 0009: Stay on Tauri-Bundled WiX v3.14.1 and Repair installer.wxs Semantics (v0.9.27)

Status: accepted — 2026-08-22

## Context

User reported that the v0.9.26 MSI installer hangs midway through installation, and that the
hang also blocks subsequent MSI installs (an "another version is installed, please remove first"
style symptom). Our MSI is built by `tauri-bundler` v2.x, which hard-codes WiX v3.14.1
(`WIX_URL = ".../wix3141rtm/wix314-binaries.zip"` in `tauri/crates/tauri-bundler/src/bundle/windows/msi/mod.rs`).
On 2026-07-06 the Tauri maintainer FabianLars confirmed the project has no plan to migrate
the bundled WiX version (issue #10348). WiX v3 itself was declared end-of-life by FireGiant
on 2025-02-06 and WiX v5 reached EOL on 2026-02-05; WiX v6 is the current stable line and
v7 the newest.

The installer.wxs we ship under `frontend/scripts/installer.wxs` was written against the
WiX v3 mental model but contains three semantic violations:

1. We register `SetServiceDelayedAutoStart` + `SetServiceFailureActions` as
   `sc.exe` deferred custom actions outside the MSI transaction (no rollback, ICE63 risk).
2. `MajorUpgrade` does not declare `@Schedule` and relies on the default
   `afterInstallValidate`, which lets `RemoveExistingProducts` start deleting files
   while the previous version's service is still running, producing file-in-use reboot
   prompts and the perceived install-time hang.
3. The previous-version service stop on upgrade is governed by the sibling
   `ServiceControl Stop="both"` in the same component as the new `ServiceInstall`,
   which ties the stop/keep-alive semantics to installation of the *new* component
   rather than to discovery of the *old* version (`WIX_UPGRADE_DETECTED`).

## Decision

For the v0.9.27 release we stay on Tauri-bundled WiX v3.14.1 and repair the semantic bugs
inside `installer.wxs` (no wixproj migration, no candle/light replacement with
`wix build`, no introduction of `dotnet tool install -g wix`):

- Replace the two `sc.exe` deferred custom actions with an inline
  `util:ServiceConfig` (`WixUtilExtension`) nested inside the existing
  `ServiceInstall`, declaring `First/Second/Third="restart"`,
  `RestartServiceDelayInSeconds="60"`, `ResetPeriodInDays="1"`, so the restart
  policy lives inside the MSI transaction.
- Pin `<MajorUpgrade Schedule="afterInstallExecute" .../>` explicitly so
  `RemoveExistingProducts` runs after `InstallExecute`; the old version's service
  is cleanly stopped before its files evaporate.
- Split "stop the previous version's service" into a separate component gated on
  `WIX_UPGRADE_DETECTED` with `ServiceControl Stop="uninstall"` (no `Remove`,
  no `Start`), so ICE63 does not flag the new-component stop action and the
  upgrade sequence does not depend on file-occupancy detection.
- Drop `ServiceControl Start="install"` entirely — the new installer does **not** start the
  service during setup. Combined with `Wait="no"`, this removes the 30-s SCM-handshake
  ceiling from the silent-install path. The service is `Start="auto"` in `ServiceInstall`
  so SCM starts it on next boot; the GUI exposes an in-app start button for immediate use.
- Keep `ServiceControl Stop="both" Remove="uninstall" Wait="no"` so `StopServices`/
  `DeleteServices` remain non-blocking.

Delivery is split into two commits so install-side and upgrade-side regressions are
independently bisectable: commit 1 fixes sc.exe → util:ServiceConfig; commit 2 pins
`Schedule="afterInstallExecute"` and adds the dedicated stop-old-service component.

A four-step local proof is required before pushing:

1. **Baseline regression**: rebuild the v0.9.26 MSI from the broken `installer.wxs`,
   silent-install on a clean VM/sandbox, and document the hang.
2. v0.9.27 silent install – `msiexec /i ... /quiet /norestart`.
3. v0.9.26 → v0.9.27 silent upgrade: confirm no hang, no "another product is installed"
   dialog, old service stopped, new service running within 60 s.
4. v0.9.27 silent uninstall: confirm clean removal of `%ProgramFiles%\Env Manager\`
   and the `EnvManagerService` SCM entry.

## Consequences

**Positive** ——
- MSI transaction integrity: service configuration is now transactional and rolls back
  with the install.
- Upgrade ordering matches WiX v3 industry template (wix-users mail-list consensus,
  FireGiant documentation, PowerToys v0.94 pre-migration baseline).
- winget Phase-2 silent-install readiness preserved (per v2 bundle target).

**Negative / accepted** ——
- We continue to ship on WiX v3.14.1 (EOL since 2025-02-06); there is no upstream
  security or feature fix channel through tauri-bundler until Tauri v3 ships.
- The MSI retains the FireGiant WiX v3 MSI EngineVersion 450 ceiling; we cannot use
  v4+ only features (e.g., `UpgradeCode` implicit-derivation improvements,
  `ServiceConfig FailureActionsOnNonCrashFailures`) in this branch.

## Alternatives considered (and rejected)

- **Bypass Tauri bundle, host our own WiX v7 `.wixproj`** — rejected for this branch:
  doubles the diff surface, removes the handlebars templating value Tauri provides,
  and is a separate v1.0.x modernization effort that deserves its own grill cycle.
- **Keep `sc.exe` and just adjust the `NOT Installed` condition** — rejected:
  no transaction participation, no rollback, ICE63 still flagged on upgrade.


## Validation evidence (2026-08-22, local codex/v1.0.0)

Four-step silent msiexec proof executed on the same machine (`/quiet /norestart`, exit code + log analysis):

| Step | Target | Result |
|---|---|---|
| 1. Baseline regression | v0.9.26 broken MSI | Hang reproduced at `InstallFinalize` → `StartServices`, lowest ActionStart op, killed at 60 s |
| 2. Silent install | v0.9.27 fixed MSI | 1140 ms, exit=0, all ActionStart steps complete, `EnvManagerService` installed STOPPED (Start=auto → next boot) |
| 3. Silent upgrade | v0.9.26 → v0.9.27 | 371 ms, exit=0 |
| 4. Silent uninstall | v0.9.27 | 322 ms, exit=0, SCM entry gone |

Logs in `%TEMP%`: `msi-v0926-baseline-install.log`, `msi-v0927-install4.log`,
`msi-v0927-upgrade.log`, `msi-v0927-uninstall.log`.

Root cause confirmed empirically: the hang sits at `ServiceControl Action=1, Wait=1` on
`EnvManagerService` inside `StartServices` — Win32 service start exceeding the 30 s SCM
handshake ceiling under DPAPI and named-pipe boot. Removing `Start="install"` from WiX
sidesteps the blocking path completely; the service now boots on demand or next restart,
not under setup's scrutiny.

## References

- Tauri issue #10348 — FabianLars 2026-07-06: no plan to upgrade bundled WiX.
- tauri-bundler `msi/mod.rs` — `WIX_URL` hard-coded to WiX 3.14.1 RTM.
- FireGiant "WiX v3 and WiX v4 are no longer in community support" (2025-02-06).
- FireGiant `util:ServiceConfig` documentation and "How to: Install a Windows
  Service howto".
- MSDN ServiceControl Table — 30-second MSI wait semantics.
- Stack Overflow 31045524 — REP ordering and "stop old service first" pattern.
- Stack Overflow 22117138 — `Stop="both" Wait="yes"` semantics (30 s cap, not indefinite).
- Plan file: `.codex-tmp/grill-plan-msi-wix-v3-fix-v0927.md`.
- Handoff: `%TEMP%\env-manager-handoff-v0927-msi.md`.
