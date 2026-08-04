<script lang="ts">
  import { onMount } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import Variables from './lib/components/Variables.svelte'
  import ProfilePage from './lib/components/ProfilePage.svelte'
  import SettingsDialog from './lib/components/SettingsDialog.svelte'
  import PathEditor from './lib/components/PathEditor.svelte'
 import HistoryPage from './lib/components/HistoryPage.svelte'
 import ProtectionPage from './lib/components/ProtectionPage.svelte'
 import AuditPage from './lib/components/AuditPage.svelte'
 import ServicePage from './lib/components/ServicePage.svelte'
  import ConfirmDialog from './lib/components/ConfirmDialog.svelte'
  import { variables, loading, error, activeView, modal, isWriteInProgress, debugLogs, refreshTrigger, toasts, dismissToast } from './lib/stores'
  import { listVariables, updateTrayLocale } from './lib/api'
  import { getSetting, frontendLog } from './lib/settingsStore'
 import { defaultLanguage, applyPersistedLocale } from './lib/i18n'
 
   
  let currentLocale: string = defaultLanguage
  let initError: string | null = null
  let showSettings = false
  let darkMode = false
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
  const trayI18n: Record<string, { show: string; quit: string; tooltip: string }> = {
    en: { show: 'Show', quit: 'Quit', tooltip: 'Env Manager' },
    zh: { show: '显示', quit: '退出', tooltip: '环境变量管理器' },
    ja: { show: '表示', quit: '終了', tooltip: '環境変数マネージャー' },
    ko: { show: '표시', quit: '종료', tooltip: '환경변수 관리자' },
    de: { show: 'Anzeigen', quit: 'Beenden', tooltip: 'Umgebungsvariablen-Verwaltung' },
    fr: { show: 'Afficher', quit: 'Quitter', tooltip: "Gestionnaire de variables d'environnement" },
    es: { show: 'Mostrar', quit: 'Salir', tooltip: 'Gestor de variables de entorno' },
    pt: { show: 'Exibir', quit: 'Sair', tooltip: 'Gerenciador de variáveis de ambiente' },
    ru: { show: 'Показать', quit: 'Выход', tooltip: 'Менеджер переменных среды' },
    ar: { show: 'إظهار', quit: 'خروج', tooltip: 'مدير متغيرات البيئة' },
  }

  function syncTrayLocale(loc: string) {
    const t = trayI18n[loc] ?? trayI18n.en
    updateTrayLocale(t.show, t.quit, t.tooltip)
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
    applyDarkMode(darkMode)
    applyFontScale(fontScale)

    try {
      await listVariables()
    } catch (err) {
      initError = err instanceof Error ? err.message : String(err)
    }
  })

  function applyDarkMode(isDark: boolean) {
    darkMode = isDark
    if (typeof document !== 'undefined') {
      document.documentElement.classList.toggle('dark', isDark)
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

  function handleFontScaleChange(e: CustomEvent<number>) {
    applyFontScale(e.detail)
  }
</script>

<svelte:head>
  <title>{$t('app.title')}</title>
</svelte:head>

<div class="min-h-screen bg-gray-50 text-gray-900 transition-colors dark:bg-gray-900 dark:text-gray-100">
  <header class="bg-white border-b border-gray-200 px-5 py-3 dark:bg-gray-800 dark:border-gray-700">
    <div class="flex items-center justify-between">
      <div class="flex items-center gap-3">
        <svg class="w-5 h-5 text-blue-600 dark:text-blue-400" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
          <path stroke-linecap="round" stroke-linejoin="round" d="M9.75 17L9 20l-1 1h8l-1-1-.75-3M3 13h18M5 17h14a2 2 0 002-2V5a2 2 0 00-2-2H5a2 2 0 00-2 2v10a2 2 0 002 2z" />
        </svg>
        <div>
          <h1 class="text-sm font-semibold leading-tight">{$t('app.title')}</h1>
          <p class="text-xs text-gray-500 dark:text-gray-400 leading-tight">{$t('app.description')}</p>
        </div>
      </div>
      <div class="flex items-center gap-2">
        <button
          on:click={() => {
            // Refresh the current active view, not just variables
            refreshTrigger.update(n => n + 1)
            // Also refresh variables store directly (variables page + backups)
            listVariables()
          }}
          class="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-md transition dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700"
          title={$t('buttons.refresh')}
          aria-label={$t('buttons.refresh')}
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4 4v5h.582m15.356 2A8.001 8.001 0 004.582 9m0 0H9m11 11v-5h-.581m0 0a8.003 8.003 0 01-15.357-2m15.357 2H15" />
          </svg>
        </button>
        <button
          on:click={() => (showSettings = true)}
          class="p-1.5 text-gray-500 hover:text-gray-700 hover:bg-gray-100 rounded-md transition dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700"
          title={$t('nav.settings')}
          aria-label={$t('nav.settings')}
        >
          <svg class="w-4 h-4" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M10.325 4.317c.426-1.756 2.924-1.756 3.35 0a1.724 1.724 0 002.573 1.066c1.543-.94 3.31.826 2.37 2.37a1.724 1.724 0 001.065 2.572c1.756.426 1.756 2.924 0 3.35a1.724 1.724 0 00-1.066 2.573c.94 1.543-.826 3.31-2.37 2.37a1.724 1.724 0 00-2.572 1.065c-.426 1.756-2.924 1.756-3.35 0a1.724 1.724 0 00-2.573-1.066c-1.543.94-3.31-.826-2.37-2.37a1.724 1.724 0 00-1.065-2.572c-1.756-.426-1.756-2.924 0-3.35a1.724 1.724 0 001.066-2.573c-.94-1.543.826-3.31 2.37-2.37.996.608 2.296.07 2.572-1.065z" />
            <path stroke-linecap="round" stroke-linejoin="round" d="M15 12a3 3 0 11-6 0 3 3 0 016 0z" />
          </svg>
        </button>
      </div>
    </div>

    <nav class="flex gap-1 mt-3">
      <button
        on:click={() => activeView.set('variables')}
        disabled={$isWriteInProgress}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition disabled:opacity-50 disabled:cursor-not-allowed {$activeView === 'variables'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.variables')}
      </button>
      <button
        on:click={() => activeView.set('profiles')}
        disabled={$isWriteInProgress}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'profiles'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.profiles')}
      </button>
      <button
        on:click={() => activeView.set('path')}
        disabled={$isWriteInProgress}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'path'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.path')}
      </button>
      <button
        on:click={() => activeView.set('history')}
        disabled={$isWriteInProgress}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'history'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.history')}
      </button>
      <button
        on:click={() => activeView.set('protection')}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'protection'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
       {$t('nav.protection')}
     </button>
      <button
        on:click={() => activeView.set('service')}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'service'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.service')}
      </button>
     <button
       on:click={() => activeView.set('audit')}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'audit'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.audit')}
      </button>
    </nav>
  </header>

  <div class="px-5 py-4">
    {#if initError}
      <div class="fixed top-4 left-1/2 -translate-x-1/2 px-3 py-2 bg-red-600 text-white rounded-md text-xs shadow-lg z-50 pointer-events-none max-w-md">
        <p class="font-medium mb-0.5">{$t('errors.cliExecutionFailed')}</p>
        <p class="font-mono break-all opacity-80">{initError}</p>
      </div>
    {/if}

    {#if $activeView === 'variables'}
      {#if $loading}
        <div class="flex justify-center py-8">
          <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600"></div>
        </div>
      {:else}
        <Variables />
      {/if}
    {:else if $activeView === 'profiles'}
      <ProfilePage />
    {:else if $activeView === 'path'}
      <PathEditor />
    {:else if $activeView === 'history'}
      <HistoryPage />
    {:else if $activeView === 'protection'}
      <ProtectionPage />
   {:else if $activeView === 'audit'}
     <AuditPage />
    {:else if $activeView === 'service'}
      <ServicePage />
    {/if}
  </div>
</div>

{#if showSettings}
  <SettingsDialog
    darkMode={darkMode}
    on:close={() => (showSettings = false)}
    on:themeChange={handleThemeChange}
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
          ? 'bg-green-600 text-white'
          : toast.type === 'error'
            ? 'bg-red-600 text-white'
            : 'bg-gray-800 text-white dark:bg-gray-700'}"
        on:click={() => dismissToast(toast.id)}
      >
        {toast.message}
      </div>
    {/each}
  </div>
{/if}

<ConfirmDialog />

<style lang="postcss">
  @tailwind base;
  @tailwind components;
  @tailwind utilities;

  :global(body) {
    /* Microsoft YaHei UI first so Chinese glyphs render with the canonical Windows
       CJK UI face; Segoe UI keeps Latin/digits looking native on Windows 10/11;
       system-ui + sans-serif are the safe tail for any locale without YaHei. */
    font-family: 'Microsoft YaHei UI', 'Segoe UI', -apple-system, BlinkMacSystemFont, system-ui, sans-serif;
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
  :global(*:hover) {
    scrollbar-color: rgba(0, 0, 0, 0.25) transparent;
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
  :global(*:hover)::-webkit-scrollbar-thumb {
    background-color: rgba(0, 0, 0, 0.25);
  }
  :global(*:active)::-webkit-scrollbar-thumb {
    background-color: rgba(0, 0, 0, 0.4);
  }
  :global(.dark *) {
    scrollbar-color: rgba(255, 255, 255, 0) transparent;
  }
  :global(.dark *:hover) {
    scrollbar-color: rgba(255, 255, 255, 0.3) transparent;
  }
  :global(.dark *)::-webkit-scrollbar-thumb {
    background-color: rgba(255, 255, 255, 0);
  }
  :global(.dark *:hover)::-webkit-scrollbar-thumb {
    background-color: rgba(255, 255, 255, 0.3);
  }
  :global(.dark *:active)::-webkit-scrollbar-thumb {
    background-color: rgba(255, 255, 255, 0.45);
  }
</style>
