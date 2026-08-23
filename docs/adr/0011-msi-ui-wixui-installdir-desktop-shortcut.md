# MSI UI — WixUI_InstallDir + Desktop Shortcut + en-US wxl (v0.9.29 Grill)

Status: accepted (2026-08-23)
Context: codex/v1.0.0, post-f073733 icon cherry-pick (fb164b9). The previous v0.9.27/v0.9.28 MSI had NO UI sequence — users saw a silent-install-like black screen with no progress, no direction. User acceptance testing on the 0.9.28 MSI surfaced this absence as "MSI installs without a user interface" and "no desktop shortcut option". This ADR locks the six implementation decisions that follow from accepting the WixUI_InstallDir pattern.

## Decision 1 — WixUI_InstallDir 4-dialog sequence, no license page
Sequence: Welcome → InstallDir → VerifyReady → Finish. Apache-2.0 has no consent requirement, so no License dialog. Source: FireGiant WiX v3 manual documents this as the standard minimal WixUI_InstallDir set.

## Decision 2 — Desktop shortcut checkbox
A VerifyReadyDlg-pattern checkbox on the InstallDir dialog, bound to `INSTALLDESKTOPSHORTCUT=1|0`. When 1, creates a Desktop shortcut; when 0, only Start Menu shortcut. This is a custom control on a cloned dialog, following the CustomizeDlg pattern. No new dialog files.

## Decision 3 — ARPPRODUCTICON
`<Icon Id="ProductIcon" SourceFile="$(var.IconPath)"/>` + `<Property Id="ARPPRODUCTICON" Value="ProductIcon"/>`, where IconPath is the cherry-picked icon.ico. Two-line change.

## Decision 4 — MSUI string-gate tests
frontend/src/installer-wxs-msi-0.9.29.test.ts with six assertions on the installer.wxs source:
1. `WIXUI_INSTALLDIR` property present, value is `INSTALLDIR` (all-uppercase) — closes the ICE blind spot of runtime error 2819.
2. `<UIRef Id="WixUI_InstallDir"/>` present.
3. `-ext WixUIExtension.dll` present in both candle AND light args in build.mjs.
4. `ARPPRODUCTICON` property present.
5. `INSTALLDESKTOPSHORTCUT` property present.
6. Absence of `WixUILicenseRtf` / License dialog references (no license page).

Plus an inline ICE suppression array in build.mjs with per-ICE one-line comments.

## Decision 5 — Vendor WixUI_en-US.wxl in frontend/scripts
`WixUI_en-US.wxl` (~200 lines, English strings for all 4 included dialogs) is committed to the repo. This is a deliberate deviation from mainstream practice (Mode A in research: mature projects do NOT commit this file). Our reasons:
- WiX v3 is EOL (frozen, no future releases): no risk of upstream drift.
- Airgap-friendly: builds without external network download.
- CI reproducibility: no need to extract sdk\wixui\* from wix314-binaries.zip on every CI run.
- The file is static; once correct, it never changes.
Rejected: relying on the WiX toolchain's built-in copy (requires network-extract on CI, adds supply-chain depth, contradicts ADR-0010 watchdog).

## Decision 6 — Reinstall smoke test (not full CI matrix)
`msiexec /i MSI /qn` run twice in succession (simulate reinstall / repair) on a test VM. Full 0.9.28→0.9.29 upgrade matrix (A1 mode) is deferred to the GUI test pyramid's CI phase, not this session.

## Consequences
- Ponytail: no new source abstractions; one new .wxl file + four wxs elements + two build.mjs line changes.
- No new external dependencies: WixUI_InstallDir is part of the existing Tauri-bundled WiX v3 toolchain; we only add the extension DLL and localization file.
- The ARPPRODUCTICON decision is independent of the i18n README translations deferred to main.