# Security Policy

## Supported Versions

Env Manager is in active development. Security fixes are applied to the latest release only.

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
| < Latest| No        |

## Reporting a Vulnerability

If you discover a security vulnerability in Env Manager, please report it responsibly:

1. **Do NOT open a public GitHub issue.**
2. Go to the repository's [Security tab](https://github.com/Xxx91n/env-manager/security/advisories/new) and create a private security advisory using GitHub's built-in feature.
3. Describe the vulnerability with enough detail to reproduce: affected component (CLI, GUI, Service, IPC), attack surface, version, and steps to trigger.
4. If you have a proposed fix, include it in the advisory.

You will receive a response within 72 hours. If the vulnerability is confirmed, a patch will be released as soon as possible depending on severity.

## Security Architecture

Env Manager handles environment variable mutation and secret provider integration. Key security surfaces:

- **Cross-process mutex**: All write operations acquire `Local\EnvManager.RegistryMutation` mutex plus a Rust `CLI_RWLOCK` write lock plus a frontend `writeChain` serialization. Three layers, never bypassed.
- **Secret storage**: Secret values are encrypted via provider-specific mechanisms (DPAPI, CredMan, Vault, SOPS, Azure KV, 1Password, AWS SM). Plaintext never persists to disk or logs.
- **Launch profile isolation**: Launch profiles inject variables into a child process via `env_clear + inject`. Never written to registry, never broadcast `WM_SETTINGCHANGE`.
- **Named pipe IPC**: Anti-squatting (`PIPE_FIRST_PIPE_INSTANCE`) prevents pipe hijacking. 65536-byte request cap. Newline-delimited JSON protocol.
- **Input validation**: CLI command whitelist, max 64 args, max 32767 chars per arg, null bytes and control characters rejected.

See `AGENTS.md` for the complete list of 40+ hard boundaries.

## Known Dependency Advisories & Disposition

As of 2026-08-29, all 11 open Dependabot advisories were triaged using a reachability-based pyramid (runtime-risk first; dev-only / SSR-only / platform-unreachable second). All 11 were assessed as **VEX: Not Affected** and dismissed with per-alert comments in the GitHub alert timeline (audit-visible). Summary:

| Alerts | Package (installed) | Reason | Tracking |
|--------|--------------------|--------|----------|
| 7 (medium) | `svelte 4.2.20` | SSR-only XSS family; env-manager is a purely client-side SPA inside WebView2 (no SSR rendering path). Fixes exist only on Svelte 5.x. | Svelte 5 migration: issue #28 |
| 3 (1 high, 2 medium) | `vite 5.4.21` | Dev-server-only advisories (require `--host` network exposure); production artifacts ship via `tauri://` with no dev server. Vite 5 is EOL upstream. | Vite 6.4.3+ upgrade: issue #29 |
| 1 (medium) | `esbuild 0.19.12` | Dev-server CORS advisory; not present in production builds. | Same as above |
| 1 (medium) | Rust `glib 0.18.5` (Tauri transitive) | gtk-rs stack is not compiled on Windows builds. Same disposition as tauri-apps/tauri#12048 (`status: upstream`). | — |

Production application code carries no runtime vulnerability among these. If the architecture changes (e.g., SSR or network-exposed dev server is introduced), these dispositions must be re-evaluated.

### Machine-readable VEX documents (npm audit gate)

The `verify` job's npm audit step consumes `security/vex/*.json` (OpenVEX) through `scripts/npm-audit-vex.mjs`: any advisory at or above `--audit-level=high` without a covering `not_affected` statement fails the build, and each exemption prints the provenance recorded on its statement. Keep those files in sync with the disposition tables below; a statement whose package no longer matches the audit report fails the gate. The `provenance` key on each statement is a repo-local OpenVEX extension (unknown keys are ignored by conformant processors) and is what makes every exemption audit-visible.

Triage of 2026-10-25 (audit-gate alert set at `--omit=dev --audit-level=high`, from issue #105):

| Alerts | Package (installed) | Reason | Tracking |
|--------|--------------------|--------|----------|
| 1 (high) | `source-map-js 1.2.1` | Source maps are parsed only by the build-time toolchain (vite/postcss); the shipped WebView2 SPA never consumes a source map at runtime. Fix exists (>= 1.2.2) and flows via dependabot/auto-merge; drop the VEX statement then. | — |
| 1 (moderate) | `postcss-selector-parser 6.1.4` | Exercised only while tailwindcss compiles first-party CSS at build time; no adversary-controlled selector input in shipped artifacts. No fix on the 6.x line (patch is 7.1.6, reached with the Tailwind 4 toolchain). | Tailwind 4 migration: issue #99 |
| 6 (moderate) | `svelte 4.2.20` (via `bits-ui` → `@melt-ui/svelte`) | SSR-only XSS family; env-manager is a purely client-side SPA inside WebView2 (no SSR rendering path). Fixes exist only on Svelte 5.x. | Svelte 5 migration: issue #28 |
