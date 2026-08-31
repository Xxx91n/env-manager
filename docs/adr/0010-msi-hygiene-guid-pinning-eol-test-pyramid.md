# MSI Hygiene, GUID Pinning, EOL Governance, and Test Pyramid (v0.9.28 Grill)

Status: accepted (2026-08-22)
Context: codex/v1.0.0, baseline fdc001c, pre-release parity sweep across packaging, worktree hygiene, and GUI test coverage. Six decisions were taken together as one coherent "1.0 release readiness" bundle; all four P-items come from one grill-with-docs session and share one rollout plan.

## Decision 1 (P0) — Per-component RemoveFile
Add `<RemoveFile Name="*.*" On="uninstall"/>` inside every INSTALLDIR file component plus one `<RemoveFolder On="uninstall"/>` in the INSTALLDIR DirectoryRef. This is the industry-standard base layer for MSI residue cleanup on heat/manual wxs with `Guid="*"`-style unstable component identities. The alternative — `util:RemoveFolderEx` — is rejected because recursive traversal is excessive power for a fixed known payload set: RemoveFolderEx exists to remove *directories the component does not own* (e.g. AppData runtime data), whereas INSTALLDIR contents are already covered by MSI component reference counting on uninstall. The pre-3.14.1 CVE-2024-29188 (junction traversal) is fixed in our pinned toolchain, so the rejection is a power-vs-need decision (Ponytail), not a CVE workaround. Linked to Decision 4c: if the pinned WiX URL ever drifts below 3.14.1, junction-traversal returns as a live risk — that watchdog is the tripwire.

## Decision 2 (P1) — Pin GUIDs only on the three binary components
`GuiExecutable`, `CliExecutable`, `ServiceExecutable` get explicit stable GUIDs. Everything else keeps `Guid="*"`. WiX v3's `Guid="*"` generates a *deterministic, path-derived* GUID (FireGiant docs describe it as based on the install directory + KeyPath filename) that is build-stable so long as the KeyPath path does not change. The RFC 4122 v3 name-based label is our prior informal tag, not WiX-official wording. Additionally, `Guid="*"` is only valid on single-file-keypath or pure RegistryValue-keypath components; multi-file components require an explicit GUID. — Tauri's own `main.wxs` does exactly this split. We pin the three components whose underlying binary names or install arity we may one day change; registry/shortcut/ancillary components stay wild.

## Decision 3 (P3) — Keep AGENTS.cli.md in INSTALLDIR root
At 5KB the file is unequivocal content, not a forward-compat problem. P0's `*.*` RemoveFile will remove it with everything else. Moving it to `%ProgramData%\EnvManager` would multiply the component graph without adding safety; the file is a payload, not state.

## Decision 4 (P4) — WiX v3 EOL watchdog CI
`.github/workflows/wix-watchdog.yml` (weekly cron + manual dispatch) fails to issue if any of:
- FireGiant publishes a new security advisory mentioning WiX v3 (source: firegiant.com/blog RSS). Rationale: wixtoolset/wix3 was archived 2025-02-14 with 3.14.1 as the final release, so "new wix3 release" can never fire — the live signal is any *new advisory* about the frozen binary we ship.
- A GHSA is filed against `wixtoolset`.
- Tauri's dev-branch `crates/tauri-bundler/src/bundle/windows/msi/mod.rs` stops pointing `WIX_URL` at `wix3141rtm/wix314-binaries.zip` (URL+SHA-256 dual check), OR Tauri officially migrates to WiX v4/v5 (closes #10348).
This makes silent upstream drift *loud* without vendoring the 26MB WixTools zip into LFS.

## Decision 5 (P2) — Worktree line-ending hygiene
`git add --renormalize .` once, committed as `chore(repo): normalize line endings`. Plus a root `.editorconfig` (`end_of_line = lf`, charset utf-8, indent rules) so future edits stay consistent across editors without per-branch cleanup. The `.gitattributes` already enforces `*.md text eol=lf`; the renormalize commit makes the index match, and `-Xrenormalize` handles any parallel-branch merges. dos2unix and history rewrite are rejected.

## Decision 6 (P1) — GUI test pyramid
Keep the 37 existing Vitest unit tests (mockIPC official pattern). Add `@wdio/tauri-service` for 8-12 critical-flow e2e on Windows CI (Tauri officially supports WebDriver on Windows without xvfb). MSI upgrade matrix runs in GitHub Actions via `tauri-action` (default-path + custom-path, empty-install + with-prior-install). Coverage gates: CLI 80%+ (Coverlet), Rust service 80%+ (cargo llvm-cov), frontend unit 70%+. Playwright is *not* the e2e driver for Tauri. On Linux (WebKitGTK) and macOS (WKWebView) the system WebView lacks CDP; on Windows, WebView2 *technically* supports Playwright via `chromium.connectOverCDP`, but requires shipping the production binary with `--remote-debugging-port`, which enlarges attack surface. We standardize on the cross-platform WebDriver protocol (`@wdio/tauri-service` + Edge WebDriver on Windows) and reserve Playwright strictly for browser-mode layout/a11y, never for IPC.

## Cross-cutting consequences
- Ponytail enforced: minimum diff per decision, no new abstractions, no vendored binaries.
- Hard boundary for every MSI installer change: re-run the 4-step silent msiexec proof from docs/build-and-release.md *every time* installer.wxs is edited.
- `ServiceControl` semantics stay untouched; nothing here weakens v0.9.27 StartServices hang mitigation.

## Evidence
- WiX v3.14.1 is the last community release (FireGiant 2025-02-06: "no future public releases").
- `Guid="*"` is deterministic and path-derived per FireGiant "How To: Generate a GUID" documentation (Bob Arnson, Joy of Setup; Tauri main.wxs template uses it for shortcut/registry components).
- CVE-2024-29188 (GHSA-jx4p-m4wm-vvjg) fixed in 3.14.1 — justifies frozen WiX v3 usage.
- Tauri v2 officially stays on WiX v3 (tauri-apps/tauri #10348); no migration timeline.
- Registry-write layer is the heart of env-manager: unit coverage 80%+ = minimum; e2e flow-count only (5-10% of pyramid); backup/restore via `scripts/test-with-restore.ps1` is the safety net, not a coverage metric.

## Amendment (T04 amendment, architecture-recovery issue 04, 2026-09-01): pyramid extended to the C# engine

Decision 6 originally scoped the test pyramid to the GUI. The architecture-recovery wave (spec: "ADR 0010 decision 6 is amended to extend the test pyramid from GUI-only to the C# engine") extends it as follows:

- **Seam, not registry**: C# engine tests run against the `IEnvironmentScope` seam (issue 01). Production = `RegistryScope` (registry + WM_SETTINGCHANGE P/Invoke); tests = `InMemoryScope` (dictionary-backed double, counted broadcasts). The registry/P-Invoke layer itself stays a thin adapter covered by the top-of-pyramid `test-with-restore.ps1` smoke, not by unit coverage.
- **Lane**: xUnit in `tests/EnvManager.Engine.Tests/`, wired into the `build.yml` `verify` job (issue 02), gating PRs. Same layering as the GUI Vitest lane: unit tests are the base, integration scripts are the apex.
- **Layers after issues 03/04**: write-path command cores (set/delete/toggle/rename/change-scope/PATH, issue 03) and profile/secret flows (apply/unapply/pre-flight validation/inheritance-chain secret propagation, issue 04) are unit-tested against `InMemoryScope`. Hard boundaries (protected entries, rename write-verify-delete, v0.7.7 inherited-secret rejection) are executable tests, per the spec.
- **Red-first falsification as acceptance evidence**: boundary tests must be demonstrably falsifiable - e.g. the launch-inherits-secret-launch poisoned-JSON variant fails when the inherited-secret union walk regresses to own-list-only (demonstrated live in ticket 04).
- **Coverage numbers**: the 80%+ CLI gate from Decision 6 now reads as the engine coverage target, measured via the seam lanes; the registry adapter and P/Invoke surfaces remain excluded (they are the apex smoke's job).

