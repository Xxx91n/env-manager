# Screenshot baseline

Playwright screenshot baseline of the GUI on Tailwind v3, captured before the
Tailwind 4 migration (#311, spec #310). 28 shots: 8 screens x slate light/dark
(16) plus the main table across all 6 theme styles x light/dark (12).

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
`{ entries }`, so on v3 the page renders its empty state even when records
exist. The baseline keeps that real behavior rather than faking a shape the app
never sees.

## Baseline run evidence (2026-11-13)

Captured on unchanged Tailwind v3 dependencies, before any migration change:

```
npx playwright test --config playwright.screenshots.config.ts --update-snapshots  # 28 passed, exit 0
npx playwright test --config playwright.screenshots.config.ts                     # 28 passed, exit 0
npx playwright test --config playwright.screenshots.config.ts                     # 28 passed, exit 0
```

One generate run plus two independent compare runs, all exit 0 (repeat-stable).
Spot-checked 8 shots by eye (variables light/dark, settings light, service dark,
main-table blue dark, main-table amber light, audit light, path-editor light):
all show fully rendered UI, no blank/loading shells.
