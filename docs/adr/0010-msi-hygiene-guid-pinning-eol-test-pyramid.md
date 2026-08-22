# MSI Hygiene, GUID Pinning, EOL Governance, and Test Pyramid (v0.9.28 Grill)

Status: accepted (2026-08-22)
Context: codex/v1.0.0, baseline fdc001c, pre-release parity sweep across packaging, worktree hygiene, and GUI test coverage. Six decisions were taken together as one coherent "1.0 release readiness" bundle; all four P-items come from one grill-with-docs session and share one rollout plan.

## Decision 1 (P0) — Per-component RemoveFile
Add `<RemoveFile Name="*.*" On="uninstall"/>` inside every INSTALLDIR file component plus one `<RemoveFolder On="uninstall"/>` in the INSTALLDIR DirectoryRef. This is the industry-standard base layer for MSI residue cleanup on heat/manual wxs with `Guid="*"`-style unstable component identities. The alternative — `util:RemoveFolderEx` — is rejected because it is recursive (CVE-2024-29188 reparse-point risk), needs the Remember-Property pattern, and is only justified when the install dir contains runtime-produced unknown files (our INSTALLDIR is a fixed known set).

## Decision 2 (P1) — Pin GUIDs only on the three binary components
`GuiExecutable`, `CliExecutable`, `ServiceExecutable` get explicit stable GUIDs. Everything else keeps `Guid="*"`. WiX v3's `Guid="*"` is RFC 4122 v3 name-based over (install dir + keypath filename) and is *already* build-stable for unchanged paths — Tauri's own `main.wxs` does exactly this split. We pin the three components whose underlying binary names or install arity we may one day change; registry/shortcut/ancillary components stay wild.

## Decision 3 (P3) — Keep AGENTS.cli.md in INSTALLDIR root
At 5KB the file is unequivocal content, not a forward-compat problem. P0's `*.*` RemoveFile will remove it with everything else. Moving it to `%ProgramData%\EnvManager` would multiply the component graph without adding safety; the file is a payload, not state.

## Decision 4 (P4) — WiX v3 EOL watchdog CI
`.github/workflows/wix-watchdog.yml` (weekly cron + manual dispatch) fails to issue if any of:
- `wixtoolset/wix3` publishes a release newer than 3.14.1,
- a GHSA is filed against `wixtoolset`,
- Tauri's dev-branch `crates/tauri-bundler/src/bundle/windows/msi/mod.rs` stops pointing `WIX_URL` at `wix3141rtm/wix314-binaries.zip`.
This makes silent upstream drift *loud* without vendoring the 26MB WixTools zip into LFS.

## Decision 5 (P2) — Worktree line-ending hygiene
`git add --renormalize .` once, committed as `chore(repo): normalize line endings`. Plus a root `.editorconfig` (`end_of_line = lf`, charset utf-8, indent rules) so future edits stay consistent across editors without per-branch cleanup. The `.gitattributes` already enforces `*.md text eol=lf`; the renormalize commit makes the index match, and `-Xrenormalize` handles any parallel-branch merges. dos2unix and history rewrite are rejected.

## Decision 6 (P1) — GUI test pyramid
Keep the 37 existing Vitest unit tests (mockIPC official pattern). Add `@wdio/tauri-service` for 8-12 critical-flow e2e on Windows CI (Tauri officially supports WebDriver on Windows without xvfb). MSI upgrade matrix runs in GitHub Actions via `tauri-action` (default-path + custom-path, empty-install + with-prior-install). Coverage gates: CLI 80%+ (Coverlet), Rust service 80%+ (cargo llvm-cov), frontend unit 70%+. Playwright is *not* the e2e driver for Tauri — the system WebView (WebKitGTK/WKWebView) lacks CDP on Linux/macOS; Playwright stays only for browser-mode layout/a11y work, never for IPC.

## Cross-cutting consequences
- Ponytail enforced: minimum diff per decision, no new abstractions, no vendored binaries.
- Hard boundary for every MSI installer change: re-run the 4-step silent msiexec proof from docs/build-and-release.md *every time* installer.wxs is edited.
- `ServiceControl` semantics stay untouched; nothing here weakens v0.9.27 StartServices hang mitigation.

## Evidence
- WiX v3.14.1 is the last community release (FireGiant 2025-02-06: "no future public releases").
- `Guid="*"` is deterministic per RFC 4122 (wix users mailing list, Bob Arnson; tauri main.wxs template).
- CVE-2024-29188 (GHSA-jx4p-m4wm-vvjg) fixed in 3.14.1 — justifies frozen WiX v3 usage.
- Tauri v2 officially stays on WiX v3 (tauri-apps/tauri #10348); no migration timeline.
- Registry-write layer is the heart of env-manager: unit coverage 80%+ = minimum; e2e flow-count only (5-10% of pyramid); backup/restore via `scripts/test-with-restore.ps1` is the safety net, not a coverage metric.
