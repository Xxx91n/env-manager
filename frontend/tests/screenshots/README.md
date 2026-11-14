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
  - #319 bumped the devDependency to ^1.65.0, whose Chromium build is 1254
    (158.0.8127.18). The #315 compare runs pinned 1.64.1 via
    `npm install --no-save @playwright/test@1.64.1` to stay on the recorded
    browser build (1248) and keep the baseline comparable. A future re-shoot
    that moves to 1.65+ must re-record this block and re-judge baseline
    comparability, per the owner's note on #315.
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

## High-contrast focus spot check (`forced-colors.spec.ts`)

Ticket #315 (spec #310) requires a Windows High Contrast focus-ring spot check:
the `focus:outline-none` → `focus:outline-hidden` rename behaves differently
only under `forced-colors: active`, which the 28-shot baseline (normal mode)
cannot see. The check runs `page.emulateMedia({ forcedColors: 'active' })`,
focuses the Variables scope `select` (one of the 20 renamed spots), and asserts
the focus indicator is actually PAINTED by diffing focused vs unfocused pixels
of the same region inside the page — `getComputedStyle` is not a usable oracle
here, because under forced-colors emulation Chromium reports
`outline-style: none` even while painting the author outline.

Findings, in order:

1. **Pre-fix regression (fixed in `src/app.css`):** app.css's unlayered
   `input:focus, select:focus, textarea:focus { outline: none }` beat
   `@layer utilities` regardless of specificity, so the forced-colors branch of
   `.focus\:outline-hidden:focus` (outline: 2px solid + offset) never applied.
   Focused form controls kept only the `focus:border-primary` color change with
   no outline. In v3 the utility's (0,2,0) out-ranked this (0,1,1) rule —
   cascade layers changed the winner, which is a migration-caused regression,
   caught by this spot check (focused/unfocused diff was 220 border-only pixels;
   after the fix 712 pixels including the outline).
2. The fix wraps the rule in `@layer base` (see the comment in `app.css`):
   utilities win back on focus, normal-mode computed values are unchanged (the
   utility's normal branch is `outline-style: none` too), inputs without a
   `focus:outline-hidden` class still get `outline: none`, and the
   `theme-style-regression.test.ts` source assertion still matches.
3. Chromium's UA focus fallback also changed the bare computed picture; that is
   why the assertion is paint-based (pixel diff > 100) rather than
   computed-style based.

Evidence: `evidence/forced-colors-focus-select.png` (focused state after the
fix, forced accent outline visible around the select).

The check runs as part of the same suite and config (29 tests total). It is
local-only like the rest of the suite.

## #315 compare runs (2026-11-14, on d737d91)

Current HEAD including #314's 27 semantic renames, same machine and browser
build (1248) as the recorded environment, `npm install --no-save
@playwright/test@1.64.1`:

```
npx playwright test --config playwright.screenshots.config.ts   # 28 passed, exit 0 (run 1)
npx playwright test --config playwright.screenshots.config.ts   # 28 passed, exit 0 (run 2)
```

Two independent compare runs, both exit 0, zero snapshot churn (git status
clean). Conclusion: all 28 shots render identical to the v4 baseline under the
three acceptance criteria (layout / color / readability) — the 27 renames
produced no visible change in normal mode, consistent with #314 renaming only
semantics-equivalent utilities. After the app.css forced-colors fix above, two
further full runs passed 29/29 (28 baseline + 1 forced-colors), again with zero
snapshot churn: normal mode is pixel-identical before and after the fix.

## Real-window walkthrough (ticket #315, 2026-11-14)

Per spec #310: the real window is part of acceptance but not the primary
evidence. Walked the 8 screens in light and dark with the portable artifact
built by CI run 43568747649 on this branch's HEAD, driven via window-level
screenshots and real mouse clicks (WebView2 ignores injected browser args in
this build, so no CDP attach). Screenshots stay local (`em315-scratch`) and are
not committed — they show this machine's real registry data. Findings:

- Environment gap, not a UI defect: the host had no .NET 10 Desktop Runtime, so
  the GUI's CLI IPC failed (red guidance toast, all data pages empty). Installed
  the official runtime (10.0.15 win-x64, Authenticode signature verified) and
  re-walked; everything below is post-install.
- All 8 screens (Variables, Profiles, Path Editor, History & Import, Protection,
  Service Manager, Audit Ledger, Settings dialog) render correctly in light and
  dark: real data tables (variables, PATH entries, 35 built-in protected
  variables), correct per-scope badges, no light/dark color bleed, readable text
  everywhere.
- Scrollbars: window/table scrollbars appear and track correctly (table larger
  than viewport at 1016x708 window).
- Native controls: select dropdowns, radio pair (Global/Launch), toggle
  switches, font-size slider, all render and operate correctly in both themes.
- Focus: clicking the Variables search input shows the ring focus indicator in
  the real window (dark theme), matching the emulated forced-colors check above.
- Problems found: none beyond the missing-runtime environment gap above; theme
  was restored to light after the walkthrough.


## v4 re-baseline (2026-11-14): the titlebar border now renders

The v3 baseline carried a latent rendering bug, not a design choice. The app
shell's second global style (`@tailwind base` inside `App.svelte`) was scoped
by Svelte into the shell's scope class (`.svelte-<hash>, …::before, …::after`)
with `border-width: 0` among its declarations — class-level specificity,
injected after `app.css` — which silently zeroed `border-bottom: 1px solid
hsl(var(--border))` on `.titlebar` (verified via computed styles on a v3 dev
server: `border-bottom-width: 0px`). Deleting that shell block, as #313
requires, lets the titlebar's own declared border render.
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
