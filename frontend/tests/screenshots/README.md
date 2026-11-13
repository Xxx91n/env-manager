# Screenshot baseline

Playwright screenshot baseline of the GUI on Tailwind v4, captured during the
config→stylesheet migration (#313, spec #310; originally shot on v3 by #311).
28 shots: 8 screens x slate light/dark (16) plus the main table across all 6
theme styles x light/dark (12).

Local-only by design, like `tests/e2e` — never wired into CI. Baselines are
machine- and browser-specific: re-shoot on the same machine with the same
Chromium build, or the comparison is meaningless.

## Run

From `frontend/`:

```
npx playwright install chromium          # once, before the first run
npx playwright test --config playwright.screenshots.config.ts            # compare
npx playwright test --config playwright.screenshots.config.ts --update-snapshots  # re-baseline
```

The config starts the Vite dev server itself (`npm run dev`, port 5173).

## Capture environment (recorded for re-shoots)

- OS: Windows (win32)
- `@playwright/test` 1.64.1, Chromium build 1248, browser version 156.0.8078.4
- Viewport 1280x800, `deviceScaleFactor: 1`, locale `en-US`, timezone `UTC`,
  `colorScheme: light`
- Fake data injected with the package's own `mockIPC` via `page.addInitScript`
  before any page script runs (`mock-data.ts`); 0.15s color transitions disabled
  and `document.fonts.ready` awaited before each shot.

## Filenames

`<screen>-slate-<light|dark>-chromium-win32.png` for the 8-screen set, and
`main-table-<style>-<light|dark>-chromium-win32.png` for the 6-style set
(`slate`, `blue`, `violet`, `rose`, `cyan`, `amber`). The slate main-table pair
appears in both sets by design (16 + 12 = 28).

## Comparison threshold

`threshold: 0.2` (Playwright's default). #306 originally specified 0, but this
machine's Chromium jitters rounded-border antialiasing on fractional rem offsets
(13px root font) by 1 color unit between runs, so threshold 0 cannot produce a
stable repeat run. The owner chose 0.2: real theme/layout changes sit far above
it; only sub-perceptual raster noise passes.

## Note on the Audit Ledger shot

`audit list` returns a JSON string, but `AuditPage` only accepts an array or
`{ entries }`, so the page renders its empty state even when records exist.
The baseline keeps that real behavior rather than faking a shape the app
never sees.

## v4 re-baseline (2026-11-14): the titlebar border now renders

The v3 baseline carried a latent rendering bug, not a design choice. The app
shell's second global style (`@tailwind base` inside `App.svelte`) was scoped
by Svelte into `.svelte-1n46o8q, .svelte-1n46o8q::before, …::after { border-width: 0; … }` —
class-level specificity, injected after `app.css` — which silently zeroed
`border-bottom: 1px solid hsl(var(--border))` on `.titlebar` (verified via
computed styles on a v3 dev server: `border-bottom-width: 0px`). Deleting that
shell block, as #313 requires, lets the titlebar's own declared border render.
The 32px bar now spends 1px on the border, so the titlebar text sits 0.5px
higher (rasterizes 1px up); everything else on all 28 shots is pixel-identical
to the v3 baseline below the pixelmatch threshold.

The first compare run against the v3 baseline failed all 28 shots on exactly
this one region (~139–164 differing pixels each, all in the titlebar). The
owner-facing call was to keep the border (it is what the CSS always declared)
and re-baseline, documented here rather than papered over.

```
npx playwright test --config playwright.screenshots.config.ts --update-snapshots  # 28 passed, exit 0
npx playwright test --config playwright.screenshots.config.ts                     # 28 passed, exit 0
npx playwright test --config playwright.screenshots.config.ts                     # 28 passed, exit 0
```

One generate run plus two independent compare runs, all exit 0 (repeat-stable).
Spot-checked by eye (main-table blue dark, main-table amber light, variables
slate dark): all show fully rendered UI, correct per-style theme colors, no
blank/loading shells.

## v3 baseline evidence (archived, 2026-11-13)

Captured on unchanged Tailwind v3 dependencies before any migration change:
one generate run plus two compare runs, all 28 passed, exit 0. Superseded by
the v4 re-baseline above; see git history of this file for the full text.
