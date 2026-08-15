<script lang="ts">
  import { onMount } from 'svelte'
  import { Monitor, RefreshCw, Settings, Minus, Square, X, Sun, Moon } from 'lucide-svelte'
  import { getCurrentWindow } from '@tauri-apps/api/window'
  import { t, locale } from 'svelte-i18n'
  import Variables from './lib/components/Variables.svelte'
  import SettingsDialog from './lib/components/SettingsDialog.svelte'
  import ConfirmDialog from './lib/components/ConfirmDialog.svelte'
  import InputDialog from './lib/components/InputDialog.svelte'
  import { variables, loading, error, activeView, modal, isWriteInProgress, debugLogs, refreshTrigger, toasts, dismissToast } from './lib/stores'
  import { preloadAdjacentPages,  listVariables, updateTrayLocale } from './lib/api'

  // Lazy-loaded components (Phase 4: Svelte code splitting)
  // Variables stays static (default tab, always needed on first load)
  // The other 7 tabs load on-demand via dynamic import() with a cache Map
  // to avoid re-importing on repeated tab switches.
  // Dynamic import map for lazy-loaded tabs (Phase 1: code splitting activated)
  // Each tab maps to a factory that returns a dynamic import() promise.
  // Vite/Rollup will create separate chunks for each import() call.
  const lazyImporters: Record<string, () => Promise<any>> = {
    profiles: () => import('./lib/components/ProfilePage.svelte'),
    path: () => import('./lib/components/PathEditor.svelte'),
    history: () => import('./lib/components/HistoryPage.svelte'),
    protection: () => import('./lib/components/ProtectionPage.svelte'),
    audit: () => import('./lib/components/AuditPage.svelte'),
    service: () => import('./lib/components/ServicePage.svelte'),
  }
  const lazyComponentCache: Record<string, any> = {}
  async function loadComponent<T>(key: string, importer: () => Promise<{ default: T }>): Promise<T> {
    if (!lazyComponentCache[key]) {
      lazyComponentCache[key] = await importer()
    }
    return lazyComponentCache[key].default
  }
  // Track which lazy component is currently loaded for each tab
  let lazyProfile: any = null
  let lazyPath: any = null
  let lazyHistory: any = null
  let lazyProtection: any = null
  let lazyAudit: any = null
  let lazyService: any = null

  // Preload a component in the background (fire-and-forget)
  function preloadComponent(key: string, importer: () => Promise<any>) {
    if (!lazyComponentCache[key]) {
      importer().then(mod => { lazyComponentCache[key] = mod }).catch(() => {})
    }
  }
  import { getSetting, setSetting, frontendLog } from './lib/settingsStore'
 import { defaultLanguage, applyPersistedLocale } from './lib/i18n'
 
   

  // Tab bar ARIA + Material 3 — APG pattern: roving tabindex + arrow-key navigation
  const tabItems = [
    { id: 'variables', labelKey: 'nav.variables' },
    { id: 'profiles', labelKey: 'nav.profiles' },
    { id: 'path', labelKey: 'nav.path' },
    { id: 'history', labelKey: 'nav.history' },
    { id: 'protection', labelKey: 'nav.protection' },
    { id: 'service', labelKey: 'nav.service' },
    { id: 'audit', labelKey: 'nav.audit' },
  ]
  // v0.9.24: CSS-only tab indicator — no JS measurement, no offsetWidth.
  // Each tab button has its own border-b that shows when active.
  // This eliminates the MUI #7187 class bug where offsetWidth includes
  // button padding (px-3 = 12px each side), making indicator wider than text.
  let tabRefs: HTMLButtonElement[] = []

  function handleTabKeydown(e: KeyboardEvent, idx: number) {
    let nextIdx: number | null = null
    switch (e.key) {
      case 'ArrowRight':
      case 'ArrowDown':
        e.preventDefault()
        nextIdx = (idx + 1) % tabItems.length
        break
      case 'ArrowLeft':
      case 'ArrowUp':
        e.preventDefault()
        nextIdx = (idx - 1 + tabItems.length) % tabItems.length
        break
      case 'Home':
        e.preventDefault()
        nextIdx = 0
        break
      case 'End':
        e.preventDefault()
        nextIdx = tabItems.length - 1
        break
    }
    if (nextIdx !== null) {
      activeView.set(tabItems[nextIdx].id)
      setTimeout(() => tabRefs[nextIdx!]?.focus(), 0)
    }
  }

    let currentLocale: string = defaultLanguage
  let initError: string | null = null
  let showSettings = false
  let darkMode = false
  let themeStyle = 'slate'
  let fontScale: number = 1

  $: if ($locale) currentLocale = $locale

  // Reactive tray i18n: whenever the svelte-i18n locale store changes (either
  // because setupI18n queued a microtask to switch from the synchronous "en"
  // boot default to the user saved locale, or because the user picked a new
  // language in Settings), we synchronise the system tray menu and tooltip.
  // This replaces the previous imperative `timeout`-based approach which synced
  // the tray from inside onMount BEFORE i18n loaded the saved locale, causing the
  // tray to permanently show English ("Show" / "Quit") even when the user had
  // chosen a different language.
 let lastSyncedTrayLocale = ''
  let trayLocaleSyncing = false
 $: if ($locale && $locale !== lastSyncedTrayLocale) {
   lastSyncedTrayLocale = $locale
    syncTrayLocale($locale)
  }

  // Synchronous tray translation map — avoids the svelte-i18n lazy loader
  // race entirely. The tray only needs 3 keys (show/quit/tooltip), so we inline
  // them rather than depending on the async dynamic import to have resolved.
  // This is the pattern recommended by pwm research on Tauri tray i18n (2025):
  // don't pipe tray strings through an async i18n layer; inline a sync lookup
  // so the tray update is fire-and-forget from the reactive Svelte handler.
  const trayI18n: Record<string, { show: string; lightweight: string; quit: string; tooltip: string }> = {
    en: { show: 'Show', lightweight: 'Lightweight Mode', quit: 'Quit', tooltip: 'Env Manager' },
    zh: { show: '显示', lightweight: '轻量模式', quit: '退出', tooltip: '环境变量管理器' },
    ja: { show: '表示', lightweight: 'ライトモード', quit: '終了', tooltip: '環境変数マネージャー' },
    ko: { show: '표시', lightweight: '경량 모드', quit: '종료', tooltip: '환경변수 관리자' },
    de: { show: 'Anzeigen', lightweight: 'Leichtmodus', quit: 'Beenden', tooltip: 'Umgebungsvariablen-Verwaltung' },
    fr: { show: 'Afficher', lightweight: 'Mode léger', quit: 'Quitter', tooltip: "Gestionnaire de variables d'environnement" },
    es: { show: 'Mostrar', lightweight: 'Modo ligero', quit: 'Salir', tooltip: 'Gestor de variables de entorno' },
    pt: { show: 'Exibir', lightweight: 'Modo leve', quit: 'Sair', tooltip: 'Gerenciador de variáveis de ambiente' },
    ru: { show: 'Показать', lightweight: 'Лёгкий режим', quit: 'Выход', tooltip: 'Менеджер переменных среды' },
    ar: { show: 'إظهار', lightweight: 'الوضع الخفيف', quit: 'خروج', tooltip: 'مدير متغيرات البيئة' },
  }

  function syncTrayLocale(loc: string) {
    const t = trayI18n[loc] ?? trayI18n.en
    updateTrayLocale(t.show, t.lightweight, t.quit, t.tooltip)
  }
  onMount(async () => {
    // Read persisted settings from localStorage (settings are saved there by
    // SettingsDialog but were never read back on startup, so every restart
    // reset darkMode/fontScale to defaults. This reads them back before applying.)
    try {
      const storedDarkMode = await getSetting('darkMode')
      if (storedDarkMode === 'true') darkMode = true
    } catch { /* best-effort */ }
    try {
      const storedThemeStyle = await getSetting('themeStyle')
      if (storedThemeStyle && ['slate', 'blue', 'violet', 'rose', 'cyan', 'amber'].includes(storedThemeStyle)) {
        themeStyle = storedThemeStyle
      }
    } catch { /* best-effort */ }
    try {
      const storedFontScale = await getSetting('fontScale')
      if (storedFontScale) {
        const parsed = parseFloat(storedFontScale)
        if (!isNaN(parsed) && parsed > 0) fontScale = parsed
      }
    } catch { /* best-effort */     }
    // Read authoritative locale from the durable IPC store and switch the
    // svelte-i18n locale store. This is the single source of truth for locale;
    // a stale WebView2 localStorage can no longer resurrect a switched-away
    // language on restart.
    try {
      await applyPersistedLocale()
    } catch (err) {
      void frontendLog('error', 'App onMount: applyPersistedLocale threw').catch(() => {})
    }
    applyDarkMode(darkMode, true)
    document.documentElement.setAttribute('data-theme-style', themeStyle)
    applyFontScale(fontScale)

    try {
      await listVariables()
      // v0.9.19: Preload adjacent page data in background for instant tab switch
      void preloadAdjacentPages()
      // Phase 1: preload adjacent lazy components in background for instant tab switch
      preloadComponent('profiles', lazyImporters.profiles)
      setTimeout(() => preloadComponent('path', lazyImporters.path), 300)
    } catch (err) {
      initError = err instanceof Error ? err.message : String(err)
    }
  })
  // Phase 4: silent performance telemetry — log view switch timing for developer diagnostics
  let perfPrevView = 'variables'
  let perfPrevTime = performance.now()
  $: if ($activeView !== perfPrevView) {
    const elapsed = performance.now() - perfPrevTime
    void frontendLog('debug', 'perf.viewSwitch from=' + perfPrevView + ' to=' + $activeView + ' elapsedMs=' + elapsed.toFixed(0)).catch(() => {})
    perfPrevView = $activeView
    perfPrevTime = performance.now()
  }

  function applyDarkMode(isDark: boolean, skipTransition = false) {
    darkMode = isDark
    if (typeof document !== 'undefined') {
      // v0.9.24: withoutTransition pattern (reemus.dev "ultimate solution"):
      // disable → action → getComputedStyle(force reflow) → enable SYNCHRONOUSLY.
      // The prior code removed the style in requestAnimationFrame (async), leaving
      // a 1-frame gap where transitions re-enabled → white flash on locked elements.
      let styleEl: HTMLStyleElement | null = null
      const disableTransitions = !skipTransition
      if (disableTransitions) {
        styleEl = document.createElement('style')
        styleEl.textContent = '* { transition: none !important; }'
        document.head.appendChild(styleEl)
      }

      // Apply mode change synchronously (inside the transition-disabled window)
      document.documentElement.setAttribute('data-theme', isDark ? 'dark' : 'light')

      if (styleEl) {
        // Force browser to evaluate the transition-disabled state by reading
        // computed style — this flushes the pending style change synchronously.
        void window.getComputedStyle(styleEl).opacity
        // Remove style SYNCHRONOUSLY (not in rAF) — reemus.dev best practice:
        // the style must be removed in the same synchronous frame it was applied
        // so no rAF gap exists where transitions could re-enable and flash.
        styleEl.remove()
      }
    }
  }

  function applyFontScale(scale: number) {
    fontScale = scale
    if (typeof document !== 'undefined') {
      document.documentElement.style.fontSize = (13 * scale) + 'px'
    }
  }

  function handleThemeChange(e: CustomEvent<boolean>) {
    applyDarkMode(e.detail)
  }

  function handleThemeStyleChange(e: CustomEvent<string>) {
    themeStyle = e.detail
    if (typeof document !== 'undefined') {
      document.body.classList.add('theme-changing')
      document.documentElement.setAttribute('data-theme-style', e.detail)
      setTimeout(() => {
        document.body.classList.remove('theme-changing')
      }, 250)
    }
  }

  function handleFontScaleChange(e: CustomEvent<number>) {
    applyFontScale(e.detail)
  }
</script>

<svelte:window on:resize={() => requestAnimationFrame(updateIndicator)} on:contextmenu={(e) => e.preventDefault()} />

<svelte:head>
  <title>{$t('app.title')}</title>
</svelte:head>

<div class="titlebar" data-tauri-drag-region>
  <div class="titlebar-drag" data-tauri-drag-region>
    <span class="text-xs font-medium text-muted-foreground select-none">Env Manager</span>
  </div>
  <div class="titlebar-controls">
    <button class="titlebar-btn" on:click={() => getCurrentWindow().minimize()} title={$t('app.minimize')}>
      <Minus class="w-3.5 h-3.5" />
    </button>
    <button class="titlebar-btn" on:click={() => getCurrentWindow().toggleMaximize()} title={$t('app.maximize')}>
      <Square class="w-3 h-3" />
    </button>
    <button class="titlebar-btn close" on:click={() => getCurrentWindow().close()} title={$t('app.close')}>
      <X class="w-3.5 h-3.5" />
    </button>
  </div>
</div>

<div class="h-screen flex flex-col overflow-hidden bg-background text-foreground" style="padding-top: 32px;">
  <header class="bg-card border-b border-border px-5 py-3 flex-shrink-0">
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-3">
        <Monitor class="w-5 h-5 text-primary" />
        <div>
          <h1 class="text-sm font-semibold leading-tight">{$t('app.title')}</h1>
          <p class="text-xs text-muted-foreground leading-tight">{$t('app.description')}</p>
        </div>
      </div>
      <div class="flex items-center gap-2">
        <button
          on:click={() => { applyDarkMode(!darkMode); void setSetting('darkMode', String(!darkMode)) }}
          class="p-1.5 text-muted-foreground hover:text-foreground hover:bg-accent rounded-md transition"
          title={darkMode ? $t('settings.lightMode') : $t('settings.darkMode')}
          aria-label={darkMode ? $t('settings.lightMode') : $t('settings.darkMode')}
        >
          {#if darkMode}
            <Sun class="w-4 h-4" />
          {:else}
            <Moon class="w-4 h-4" />
          {/if}
        </button>
        <button
          on:click={() => {
            // Refresh the current active view, not just variables
            refreshTrigger.update(n => n + 1)
            // Also refresh variables store directly (variables page + backups)
            listVariables()
          }}
          class="p-1.5 text-muted-foreground hover:text-foreground hover:bg-accent rounded-md transition"
          title={$t('buttons.refresh')}
          aria-label={$t('buttons.refresh')}
        >
          <RefreshCw class="w-4 h-4" />
        </button>
        <button
          on:click={() => (showSettings = true)}
          class="p-1.5 text-muted-foreground hover:text-foreground hover:bg-accent rounded-md transition"
          title={$t('nav.settings')}
          aria-label={$t('nav.settings')}
        >
          <Settings class="w-4 h-4" />
        </button>
      </div>
    </div>

    <div class="mt-3">
      <div
        role="tablist"
        aria-label="{$t('nav.variables')} — {$t('nav.audit')}"
        class="flex gap-1"
      >
        {#each tabItems as tab, i}
          <button
            bind:this={tabRefs[i]}
            role="tab"
            aria-selected={$activeView === tab.id}
            aria-controls="tabpanel-content"
            tabindex={$activeView === tab.id ? 0 : -1}
            disabled={$isWriteInProgress && (tab.id === 'variables' || tab.id === 'profiles' || tab.id === 'path')}
            on:click={() => activeView.set(tab.id)}
            on:keydown={(e) => handleTabKeydown(e, i)}
            class="px-3 py-2 text-xs font-medium transition-colors duration-200 rounded-t-md border-b-2 {$activeView === tab.id
              ? 'text-primary font-semibold border-primary'
              : 'text-muted-foreground hover:text-foreground hover:bg-accent border-transparent'} disabled:opacity-50 disabled:cursor-not-allowed"
          >
            {$t(tab.labelKey)}
          </button>
        {/each}
      </div>
    </div>
  </header>

  <div class="px-5 py-4 h-full overflow-y-auto">
    {#if initError}
      <div class="fixed top-4 left-1/2 -translate-x-1/2 px-3 py-2 bg-destructive text-destructive-foreground rounded-md text-xs shadow-lg z-50 pointer-events-none max-w-md">
        <p class="font-medium mb-0.5">{$t('errors.cliExecutionFailed')}</p>
        <p class="font-mono break-all opacity-80">{initError}</p>
      </div>
    {/if}

    {#if $activeView === 'variables'}
      {#if $loading}
        <div class="flex justify-center py-8">
          <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div>
        </div>
      {:else}
        <Variables />
      {/if}
    {:else if $activeView === 'profiles'}
      {#await loadComponent('profiles', lazyImporters.profiles)}
        <div class="flex justify-center py-8"><div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div></div>
      {:then mod}
        {@const Comp = mod}
        <Comp />
      {:catch err}
        <div class="flex flex-col items-center gap-2 py-8 text-muted-foreground"><Monitor class="w-6 h-6"/><p class="text-xs">{$t('errors.chunkLoadFailed')}</p><button on:click={() => { delete lazyComponentCache['profiles']; activeView.set('profiles') }} class="text-xs text-primary underline">{$t('common.retry')}</button></div>
      {/await}
    {:else if $activeView === 'path'}
      {#await loadComponent('path', lazyImporters.path)}
        <div class="flex justify-center py-8"><div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div></div>
      {:then mod}
        {@const Comp = mod}
        <Comp />
      {:catch err}
        <div class="flex flex-col items-center gap-2 py-8 text-muted-foreground"><Monitor class="w-6 h-6"/><p class="text-xs">{$t('errors.chunkLoadFailed')}</p><button on:click={() => { delete lazyComponentCache['path']; activeView.set('path') }} class="text-xs text-primary underline">{$t('common.retry')}</button></div>
      {/await}
    {:else if $activeView === 'history'}
      {#await loadComponent('history', lazyImporters.history)}
        <div class="flex justify-center py-8"><div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div></div>
      {:then mod}
        {@const Comp = mod}
        <Comp />
      {:catch err}
        <div class="flex flex-col items-center gap-2 py-8 text-muted-foreground"><Monitor class="w-6 h-6"/><p class="text-xs">{$t('errors.chunkLoadFailed')}</p><button on:click={() => { delete lazyComponentCache['history']; activeView.set('history') }} class="text-xs text-primary underline">{$t('common.retry')}</button></div>
      {/await}
    {:else if $activeView === 'protection'}
      {#await loadComponent('protection', lazyImporters.protection)}
        <div class="flex justify-center py-8"><div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div></div>
      {:then mod}
        {@const Comp = mod}
        <Comp />
      {:catch err}
        <div class="flex flex-col items-center gap-2 py-8 text-muted-foreground"><Monitor class="w-6 h-6"/><p class="text-xs">{$t('errors.chunkLoadFailed')}</p><button on:click={() => { delete lazyComponentCache['protection']; activeView.set('protection') }} class="text-xs text-primary underline">{$t('common.retry')}</button></div>
      {/await}
    {:else if $activeView === 'audit'}
      {#await loadComponent('audit', lazyImporters.audit)}
        <div class="flex justify-center py-8"><div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div></div>
      {:then mod}
        {@const Comp = mod}
        <Comp />
      {:catch err}
        <div class="flex flex-col items-center gap-2 py-8 text-muted-foreground"><Monitor class="w-6 h-6"/><p class="text-xs">{$t('errors.chunkLoadFailed')}</p><button on:click={() => { delete lazyComponentCache['audit']; activeView.set('audit') }} class="text-xs text-primary underline">{$t('common.retry')}</button></div>
      {/await}
    {:else if $activeView === 'service'}
      {#await loadComponent('service', lazyImporters.service)}
        <div class="flex justify-center py-8"><div class="animate-spin rounded-full h-6 w-6 border-b-2 border-primary"></div></div>
      {:then mod}
        {@const Comp = mod}
        <Comp />
      {:catch err}
        <div class="flex flex-col items-center gap-2 py-8 text-muted-foreground"><Monitor class="w-6 h-6"/><p class="text-xs">{$t('errors.chunkLoadFailed')}</p><button on:click={() => { delete lazyComponentCache['service']; activeView.set('service') }} class="text-xs text-primary underline">{$t('common.retry')}</button></div>
      {/await}
    {/if}
  </div>
</div>

{#if showSettings}
  <SettingsDialog
    darkMode={darkMode}
      themeStyle={themeStyle}
    on:close={() => (showSettings = false)}
    on:themeChange={handleThemeChange}
    on:themeStyleChange={handleThemeStyleChange}
    fontScale={fontScale}
    on:fontScaleChange={handleFontScaleChange}
    on:pathChanged={() => {
      refreshTrigger.update(n => n + 1)
      listVariables()
    }}
  />
{/if}

<!-- Global toast notification layer: renders toasts as fixed overlays
     at app root level, preventing layout shifts from per-component rendering -->
{#if $toasts.length > 0}
  <div class="fixed top-4 left-1/2 -translate-x-1/2 z-[60] flex flex-col gap-2 pointer-events-none">
    {#each $toasts as toast (toast.id)}
      <div
        class="px-3.5 py-2 rounded-md text-xs shadow-lg pointer-events-auto cursor-pointer transition-all {toast.type === 'success'
          ? 'bg-green-600 text-primary-foreground'
          : toast.type === 'error'
            ? 'bg-destructive text-primary-foreground'
            : 'bg-primary text-primary-foreground'}"
        on:click={() => dismissToast(toast.id)}
      >
        {toast.message}
      </div>
    {/each}
  </div>
{/if}

<ConfirmDialog />
  <InputDialog />

<style lang="postcss">
  @tailwind base;
  @tailwind components;
  @tailwind utilities;

  :global(body) {
    /* Microsoft YaHei UI first so Chinese glyphs render with the canonical Windows
       CJK UI face; Segoe UI keeps Latin/digits looking native on Windows 10/11;
       system-ui + sans-serif are the safe tail for any locale without YaHei. */
    font-family: 'Microsoft YaHei UI', 'Segoe UI', -apple-system, BlinkMacSystemFont, system-ui, sans-serif;
    scroll-behavior: smooth;
    margin: 0;
    padding: 0;
    font-size: 13px;
    -webkit-font-smoothing: antialiased;
    -moz-osx-font-smoothing: grayscale;
    /* v0.7.5: DPI-aware glyph rendering. These four together fix the
       "fuzzy on zoom, blurry on 4K Windows scaling" complaint. */
    text-rendering: optimizeLegibility;
    font-feature-settings: "kern" 1, "liga" 1, "calt" 1;
    -webkit-text-size-adjust: 100%;
    -webkit-text-rendering: optimizeLegibility;
  }

 :global([dir='rtl']) {
   text-align: right;
 }

  /* Edge-style true-overlay auto-hide scrollbar: thumb hidden when idle,
     transient overlay on hover/scroll. Reserves ZERO layout space by NOT pinning
     ::-webkit-scrollbar width — Chromium native overlay floats over content,
     matching Edge/VS Code. scrollbar-color is the Firefox fallback (thin,
     auto-hide). scrollbar-gutter left at browser default (auto) so no space
     is reserved. */
  :global(*) {
    scrollbar-width: thin;
    scrollbar-color: rgba(0, 0, 0, 0) transparent;
  }
  /* Width intentionally NOT set on ::-webkit-scrollbar: pinning it to 8px
     would reserve a dedicated 8px track, contradicting the true-overlay goal.
     Chromium renders a transient overlay that does not shift layout. We style
     only the thumb so the thin overlay stays visually consistent when visible. */
  :global(*)::-webkit-scrollbar { background: transparent; }
  :global(*)::-webkit-scrollbar-track { background: transparent; }
  :global(*)::-webkit-scrollbar-thumb {
    background-color: rgba(0, 0, 0, 0);
    border-radius: 8px;
    transition: background-color 0.15s ease;
  }
  :global(*:active)::-webkit-scrollbar-thumb {
    background-color: rgba(0, 0, 0, 0.4);
  }
  :global([data-theme="dark"] *) {
    scrollbar-color: rgba(255, 255, 255, 0) transparent;
  }
  :global([data-theme="dark"] *)::-webkit-scrollbar-thumb {
    background-color: rgba(255, 255, 255, 0);
  }
  :global([data-theme="dark"] *:active)::-webkit-scrollbar-thumb {
    background-color: rgba(255, 255, 255, 0.45);
  }
  /* v0.9.18: Disable WebView2 native context menu globally. */
  /* Allow text selection only in input/textarea/contenteditable. */
  :global(body) {
    -webkit-user-select: none;
    user-select: none;
  }
  :global(input), :global(textarea), :global([contenteditable]) {
    -webkit-user-select: text;
    user-select: text;
  }
</style>
