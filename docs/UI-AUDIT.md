# UI-AUDIT.md — UI Design System Audit Log

> This document records UI design system audits: gaps found, priorities assigned, fixes applied.
> DESIGN.md holds the living spec (updated during migration). This file is the audit history.

## Audit Round 1 — v0.9.20 Design System Migration (2026-08-15)

### Scope
Full migration from Tailwind `dark:` prefix pattern to CSS variable token system.

### Migration Summary

| Metric | Before | After |
|--------|--------|-------|
| `dark:` prefix occurrences | 275 | 0 |
| Inline `<svg>` icons | 45 | 0 |
| Global CSS file | None | `src/app.css` (5.3KB, 3 base color sets) |
| Tailwind color extension | 1 custom gray | 14 token-based colors |
| Icon library | None | lucide-svelte (Svelte 4 compat) |
| Theme persistence | localStorage only | IPC `gui-settings.json` (durable) |
| Build CSS size | ~28KB | 33.36KB (token system + 3 palettes) |
| Build JS size | ~320KB | 344KB (lucide tree-shaken) |

### Changes by Phase

#### Phase 1: Foundation
- Created `src/app.css` with dual-axis token system: `data-theme` (light/dark) × `data-theme-style` (slate/zinc/neutral)
- Added `$lib` alias to `vite.config.ts` + `tsconfig.json` paths
- Created `.svelte-kit/tsconfig.json` sentinel for shadcn-svelte CLI
- Installed: lucide-svelte, bits-ui@0.22.0 (Svelte 4 compat), tailwindcss-animate, clsx, tailwind-merge
- Created `src/lib/utils.ts` (cn function)
- Created `components.json` for shadcn-svelte CLI

#### Phase 2: App.svelte
- Replaced 3 inline SVGs: Monitor, RefreshCw, Settings
- Migrated 11 `dark:` prefixes to token variables
- Added `import './app.css'` to `main.ts`

#### Phase 3: Core Components (Variables, ProfilePage, SettingsDialog, PathEditor)
- 276 `dark:` prefixes → token variables
- 37 inline SVGs → lucide icons (Plus, Search, Lock, FileText, Pencil, Trash2, Eye, ShieldCheck, Check, X, Power, ChevronUp, ChevronDown, Download, MapPin, Play, Tag, Archive, Loader2)

#### Phase 4: Remaining Components (HistoryPage, ProtectionPage, ServicePage, AuditPage, BackupDialog, CloneCombobox, ConfirmDialog, EditDialog, InputDialog)
- 330 `dark:` prefixes → token variables
- 5 inline SVGs → lucide icons (Lock, Download)

#### Phase 5: Theme Selector + Build + Docs
- Dark mode logic: `classList.toggle('dark')` → `setAttribute('data-theme', 'dark'/'light')`
- Tailwind `darkMode` config: `['selector', '[data-theme="dark"]']`
- SettingsDialog: dual-axis theme selector (light/dark toggle + slate/zinc/neutral base color)
- Theme persistence via IPC `gui-settings.json` (durable, not localStorage)
- i18n: `settings.themeStyle` key added to all 10 locales
- Version: 0.9.19 → 0.9.20

### Gaps Found and Fixed

| Component | Gap | Fix | Priority |
|-----------|-----|-----|----------|
| Global | No CSS variable token system | Created app.css with 3 base color sets × 2 modes | P0 |
| App.svelte | dark: class toggle unreliable in portable | data-theme attribute + IPC persistence | P0 |
| All components | 45 inline SVGs (maintenance burden) | Replaced with lucide-svelte (tree-shaken) | P1 |
| SettingsDialog | No theme style selector | Added slate/zinc/neutral selector | P1 |
| Tailwind config | Hardcoded dark gray colors | Token-based colors via `hsl(var(--token))` | P1 |

### Remaining Gaps (Future Work)

| Gap | Priority | Notes |
|-----|----------|-------|
| shadcn-svelte component adoption (Tabs, Dialog, Select) | P2 | Infrastructure ready; actual component migration deferred to avoid regression risk |
| `mode-watcher` not used (auto dark mode) | P3 | Would need SvelteKit adapter; not feasible in Vite-only setup |
| Localized theme style names (currently "slate"/"zinc"/"neutral" in English) | P3 | Could add i18n keys for style names |
| CSS animation polish (spring physics) | P3 | Currently cubic-bezier(0.2, 0, 0, 1) approximation; svelte-motion not Svelte 4 compatible |

### Risk Matrix

| Risk | Mitigation |
|------|------------|
| bits-ui@0.22.0 abandons Svelte 4 | Pin version; migration to bits-ui 1.x requires Svelte 5 upgrade |
| lucide-svelte deprecated | Pin version; alternative is `@lucide/svelte` (Svelte 5 only) |
| Token cascade specificity | app.css uses `[data-theme-style]` attribute selectors (higher specificity than class) |
| WebView2 localStorage unreliable | Theme persists via IPC `gui-settings.json` (v0.7.7 proven pattern) |
