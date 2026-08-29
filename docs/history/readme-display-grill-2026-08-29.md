# README Display Overhaul — Grill Session 2026-08-29

> Grill-with-docs session record. Branch: project display. Companion research:
> atomcode research report (18 sources, indexed in ctx as `atomcode-readme-research`).
> Follows the same process pattern as docs/history/design-review-research.md.

## Problem Statement

1. Top hero SVG is static, hardcodes `v0.9.26` (csproj is 0.9.30 — drift already happened), and cannot animate.
2. README is cluttered: 60+ lines of inline Feature & Version History mixed into the body.
3. Narrative is not proactive enough; should center agent-friendliness (CLI), config/profile system, secret features.
4. Translations are mechanical; need higher-quality native rendering across 9 locales.

## Research Findings (atomcode, confidence: high, 18 sources)

- **Version anti-drift**: industry standard is a shields.io dynamic release badge (`img.shields.io/github/v/release/...`, proven in fastfetch + vhs raw READMEs); cli/cli and microsoft/terminal omit version entirely. No mature repo hardcodes version strings. Dynamic badge verified live: already resolves `v0.9.30` (release shipped 2026-08-27).
- **Motion on GitHub**: camo proxy + CORB/CSP break animated SVG (community discussion #59781; HN #25349068 termtosvg failure). The oil-oil/beautify-github-readme repo's own hero is `hero.gif` (1.8MB) rendered from a static SVG source via the skill's `scripts/render_motion_gif.py` (Python + Pillow + ffmpeg + rsvg-convert). Tier ranking for real terminal recordings: vhs > asciinema+agg > terminalizer (stalled).
- **CHANGELOG separation**: keepachangelog is the de-facto standard; starship/fastfetch/asciinema all keep a root CHANGELOG.md. Our inline history also violates ADR 0003 (CHANGELOG as record carrier).
- **i18n**: two industry routes — hosted platforms (GitLocalize, tensorflow/docs-l10n) vs CI drift enforcement (pyvista-wasm ADR-0007: English as sole authority + structure-consistency check + staleness detection). Quality comes from glossary + LLM draft + native review; CI only prevents drift.
- **Agentic narrative**: AGENTS.md (60k+ repos) + llms.txt (v2 spec) are the 2025-2026 second-layer README narrative; cli/cli sells an "Agent skills" section. We already ship AGENTS.md + AGENTS.cli.md — a head start.

## Decisions (Q1-Q5, all user-approved)

| # | Question | Decision |
|---|---|---|
| D1 | Version display | Replace static badge with shields.io dynamic release badge; remove hardcoded `v0.9.26` text from hero.svg |
| D2 | Motion scope | Skill-native pipeline for hero (`hero.svg` → motion spec → `render_motion_gif.py` → `hero.gif`, SVG stays as editable source/fallback) + vhs-recorded real CLI demo GIF in a demos section |
| D3 | Information architecture | Extract inline version history to `CHANGELOG.md` (keepachangelog format, ADR 0003 compliant); new section order: logo/hero -> dynamic badges -> one-liner + tagline -> demos -> Features (agent-native CLI first, profiles/config, secret provider matrix, GUI) -> For AI Agents section (AGENTS.md / AGENTS.cli.md) -> Install/Quickstart -> Architecture -> docs index -> i18n switcher -> Contributing -> License |
| D4 | i18n governance | Keep `docs/i18n/` layout (ADR 0008 #7); add ADR-0007-style CI check (structure consistency + staleness detection, English sole authority); glossary from CONTEXT.md Language chapter; atomcode-assisted native-quality rewrite of all 9 locales, zh_CN as the complete reference |
| D5 | hero.gif storyboard | "Boot -> tri-color activation": blinking cursor -> type `$ env` -> three peaks light up sequentially (Dev emerald -> Staging cyan -> Prod amber, ease-out) -> spiral tail draws closed loop (.env lifecycle) -> hold 1.5s -> seamless loop. Reuse existing hero.svg geometry after removing the version text. Target <2MB, 2-6s loop |

## Execution Plan (phases, acceptance-test closed loops)

### Phase 0 — Foundation
- [x] Remove `v0.9.26` tspans from `docs/assets/brand/hero.svg` (keep everything else).
- [x] Replace static release badge with `https://img.shields.io/github/v/release/Xxx91n/env-manager`.
- [x] Acceptance: badge renders live version; hero.svg still renders (open in browser / GitHub preview).

### Phase 1 — CHANGELOG extraction
- [x] Move the inline Feature & Version History block into `CHANGELOG.md` in keepachangelog format (Unreleased + reverse-chronological, ADR 0003 compliance).
- [x] README keeps a single link line to CHANGELOG.md.
- [x] Acceptance: `scripts/check-doc-sync.ps1` passes; no `v\d+\.\d+` history entries remain in README.md; CHANGELOG covers the moved versions.

### Phase 2 — README restructure (English authoritative)
- [x] Reorder sections per D3; add For AI Agents section; add demos section placeholder.
- [x] Acceptance: heading order matches D3; all links resolve (`docs/cli-commands.md`, `AGENTS.cli.md`, CHANGELOG.md); renders correctly on GitHub and in local markdown preview.

### Phase 3 — hero.gif via skill pipeline
- [x] Redraw hero.svg per D5 (no version text), write `hero-motion.json`, render with `render_motion_gif.py` (requires Python+Pillow+ffmpeg+rsvg-convert on Windows; fall back to reporting missing deps).
- [x] Acceptance: ffprobe verifies 2-6s loop, <2MB; first/settled frames pixel-identical where settled; GIF plays at top of README; SVG remains in repo.

### Phase 4 — vhs demo GIF
- [x] Write `docs/assets/demo.tape` (cli: `list` -> profile create/launch flow), run `vhs demo.tape`, embed GIF in demos section.
- [x] Acceptance: GIF replays deterministically from the .tape; size sane; content shows real CLI output.

### Phase 5 — i18n quality + drift guard
- [x] Write glossary from CONTEXT.md Language chapter; atomcode-assisted rewrite of 9 locales (zh_CN as complete reference); CI script `check-readme-i18n` (structure consistency + staleness).
- [x] Acceptance: script exits non-zero on injected drift (negative test); all locales pass after rewrite; language switcher block updated in every file.

### Phase 6 — Docs/metadata consistency
- [x] Update AGENTS.md/CONTEXT.md references if section names change; keep docs/i18n paths intact.
- [x] Acceptance: full doc sync check passes; no stale references.

## Hard Boundaries (carried from AGENTS.md)

- No push without explicit user authorization.
- Version single source remains `env-manager.csproj` (ADR 0003); README/SVG never carry version strings again (D1 closes that surface).
- Mirrors (GitLab/Codeberg) sync via existing ADR 0008 automation — this branch only changes presentation.

## Follow-up: rsvg-convert Dependency Formalization + i18n Native Translation

Date: 2026-08-29 (project-display branch)

### Problem 1: rsvg-convert wrapper in .codex-tmp (ephemeral, gitignored)

The hero.gif rendering pipeline depended on a `.codex-tmp/rsvg-convert.cmd`
wrapper delegating to `@resvg/resvg-js` from `.codex-tmp/raster/node_modules/`.
This was ephemeral (gitignored), not reproducible across clones or CI.

**Resolution**: atomcode research (13 sources, high confidence) confirmed
`@resvg/resvg-js@2.6.2` as committed npm devDependency is the industry
standard for Windows CI SVG rendering. Native `rsvg-convert` has known
Windows stdout corruption defects (librsvg#676, #812).

- `@resvg/resvg-js` locked into `package.json` devDependencies
- `scripts/rsvg-convert.js` + `scripts/rsvg-convert.cmd` committed to repo
- `.codex-tmp/` temp files removed
- hero.gif re-rendered successfully with new wrapper (1200x220, 100 frames, 20FPS, 0.09MB)
- ADR-0013 created documenting the decision
- CONTEXT.md updated with "rsvg-convert Wrapper" glossary term

### Problem 2: i18n translation quality (all 9 locales were Chinese copies)

All 8 non-zh_CN locale files had Chinese content (copied from zh_CN
without translation). Structure was correct (13 H2, switcher, gifs)
but prose was wrong language.

**Resolution**: atomcode `[fast]` mode produced native-quality
translations for ja, ko, de, fr, es, pt, ru, ar. Each locale's
description, tagline, and body text replaced with translated content.
Technical terms preserved untranslated per spec.

- 2 atomcode batches (4 languages each), ~3 min per batch
- demo.gif HTML reference block added to all locale files
- tagline inserted for each locale
- `check-readme-i18n.ps1` passes (13 H2, 9 locales, structure consistent)
- `check-doc-sync.ps1` passes
- `translations/` temp directory cleaned up
- CONTEXT.md updated with "atomcode-assisted Translation" and "i18n README Drift Check" terms
