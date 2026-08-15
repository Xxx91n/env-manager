# UI Audit Log

> This file records UI/UX audits performed across versions. DESIGN.md holds the
> authoritative design tokens and component specs; this file is the audit history.

## v0.9.20 — Theme System + Tab Indicator Audit

### Scope
- 5 bug points reported after v0.9.19 design system refactor
- Industry best-practice research via 1mcp exa + pplx kimi

### Findings + Fixes

#### 1. Color style toggle ineffective (Bug 1)
- **Root cause**: `handleThemeStyleChange` function was bound in the template
  (`on:themeStyleChange={handleThemeStyleChange}`) but had no function
  definition in the `<script>` block. Svelte silently created an undefined
  reference.
- **Secondary**: `changeThemeStyle` in SettingsDialog mutated `data-theme-style`
  DOM directly but did not `dispatch('themeStyleChange')`, so App.svelte's
  `themeStyle` state never updated.
- **Tertiary**: `themeStyle` was never read from durable IPC store on startup,
  so each restart reset to default `slate` regardless of the persisted choice.
- **Fix**: (a) Added `handleThemeStyleChange(e: CustomEvent<string>)` to
  App.svelte that updates `themeStyle` state + DOM + `.theme-changing` class;
  (b) `changeThemeStyle` now dispatches the event instead of direct DOM;
  (c) onMount reads `getSetting('themeStyle')` with allow-list validation.

#### 2. i18n missing for theme style (Bug 2)
- **Root cause**: 10 locale files lacked `settings.themeStyle`,
  `settings.themeStyleSlate`, `settings.themeStyleZinc`,
  `settings.themeStyleNeutral`.
- **Fix**: Added all 4 keys to all 10 locale files.

#### 3. Layout compression — theme selector squeezed into CLI-in-PATH div (Bug 3)
- **Root cause**: The theme selector `<div>` was nested inside the
  CLI-in-PATH wrapper `<div>`, causing vertical layout compression.
- **Fix**: Restructured SettingsDialog.svelte to make the theme selector a
  sibling element, not a child of the CLI-in-PATH section.

#### 4. Dark mode transition janky (Bug 4)
- **Root cause**: Global `*` CSS selector applied transitions to every element
  (thousands of table cells, hidden elements, etc.), causing visible jank.
- **Industry research**: css-architecture.com explicitly warns against
  `* { transition: ... }`; recommended pattern is a scoped CSS class +
  named-surface selector list.
- **Fix**: Replaced global `*` transition with `.theme-changing` class
  on `<body>` + named-surface selectors (bg-background, bg-card, border-border,
  text-foreground, etc.) for color-only properties (background-color, border-color,
  color, box-shadow). `.theme-changing` class is added before `data-theme` swap
  and removed after 250ms. Initial mount skips the transition
  (`applyDarkMode(darkMode, true)`) to avoid first-paint flash.
- **Pattern**: clash-verge-rev / VS Code / Chromium DevTools use the same
  scoped-class transition pattern for theme swaps.

#### 5. Tab indicator bar wider than text on initial load (Bug 5)
- **Root cause**: Initial `indicatorStyle` contained `left: 0px` (from a
  prior implementation) but the update path used `transform: translateX`.
  The stale `left` value caused the browser to render the bar wider than
  the active tab label on first paint. Switching tabs corrected it.
- **Fix**: Changed initial value to `'width: 0px; transform: translateX(0px)'`
  with no `left` property. The rAF retry loop in onMount then sets the correct
  width once the tab buttons have non-zero `offsetWidth`.
- **Industry research**: exa found that `ResizeObserver` fires immediately on
  `observe()`, making it more reliable than rAF retry for detecting element
  readiness. Current rAF loop (10 retries) is acceptable; `ResizeObserver`
  is a future enhancement if the rAF path proves insufficient.
- **Regression test**: `theme-style-regression.test.ts` asserts initial
  `indicatorStyle` matches `/^width:\s*0px/` to prevent regression.

### Regression Tests Added
- `frontend/src/theme-style-regression.test.ts` — 16 tests
  - Tab indicator initial width (2 tests)
  - Theme style i18n keys in all 10 locales (10 tests)
  - handleThemeStyleChange function existence + binding (4 tests)
- `frontend/src/tray-lightweight-regression.test.ts` — 4 tests
  - Tray i18n inline map covers all 10 locales
  - Tray fields (show/lightweight/quit/tooltip) present
  - `on:contextmenu` preventDefault for WebView2 right-click suppression
  - syncTrayLocale reactive handler wired

### CSS Architecture Decisions
- **No global `*` transition**: explicitly avoided per css-architecture.com
- **Named-surface transition scoped to `.theme-changing`**: only fires during
  explicit user toggles (dark mode switch, theme style switch)
- **`prefers-reduced-motion`**: future enhancement (wrap in
  `@media (prefers-reduced-motion: no-preference)`)

### Tabs CSS Pattern
- Tab indicator uses `requestAnimationFrame` retry loop (max 10 retries) to
  detect non-zero `offsetWidth` before positioning. Industry alternative:
  `ResizeObserver` fires synchronously on `observe()` and avoids the retry
  loop entirely. Track as future improvement.
