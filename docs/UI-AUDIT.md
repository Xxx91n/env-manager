# UI Audit Log

## v0.9.21 Audit Round — 6-Color Theme + Custom Titlebar + CSS Containment

### 1. Theme Color System

**Status**: Overhauled  
**Changes**:
- Removed `zinc` and `neutral` base color options (too desaturated, lost semantic signal)
- Added 5 new tinted base colors: `blue` (220deg), `violet` (263deg), `rose` (340deg), `cyan` (190deg), `amber` (38deg)
- Each color defines complete HSL token sets for both light and dark modes
- Selector changed from 3 buttons to `<select>` dropdown for extensibility
- All 10 locale files updated with `themeStyleBlue/Violet/Rose/Cyan/Amber` i18n keys

**Verification**: `theme-style-regression.test.ts` — 26 tests, all pass

### 2. Custom Window Titlebar

**Status**: Implemented  
**Changes**:
- `tauri.conf.json`: `decorations: false`, `label: "main"` — removes native Windows frame
- `capabilities/default.json`: Added `core:window:allow-close/minimize/toggle-maximize/start-dragging`
- `App.svelte`: Titlebar div with `data-tauri-drag-region`, 3 control buttons (minimize/maximize/close) using `getCurrentWindow()` API
- `app.css`: `.titlebar` (h:32px, bg:hsl(var(--card))), `.titlebar-drag` (app-region:drag), `.titlebar-btn` (w:46px, app-region:no-drag), `.titlebar-btn.close:hover` (red destructive hover)
- Icons: `lucide-svelte` Minus/Square/X

**Industry Reference**: VS Code, Postman, Tauri 2 official `decorations:false` pattern

### 3. CSS Containment for Performance

**Status**: Implemented  
**Changes**:
- `app.css`: `.table-container`, `.list-container`, `.scroll-area` → `contain: layout paint`
- `.offscreen-optimizable` → `content-visibility: auto; contain-intrinsic-size: auto 500px`
- Applied `table-container` to: HistoryPage table root, Variables table wrapper
- Applied `list-container` to: ProtectionPage, AuditPage, ServicePage, ProfilePage scrollable containers

**Industry Reference**: web.dev `content-visibility` + kimi research on dark-mode jank reduction

### 4. Test Coverage

- `theme-style-regression.test.ts`: 26 tests covering tab indicator, theme i18n keys (6 colors × 10 locales), handleThemeStyleChange binding, titlebar config, CSS containment classes
- No Playwright (user hard constraint) — pure Vitest source-level assertion tests

### Audit Date: 2026-08-15
