<script lang="ts">
  import { onMount } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import Variables from './lib/components/Variables.svelte'
  import SettingsDialog from './lib/components/SettingsDialog.svelte'
  import ProfileDialog from './lib/components/ProfileDialog.svelte'
  import PathEditor from './lib/components/PathEditor.svelte'
  import { variables, loading, error, activeView } from './lib/stores'
  import { listVariables } from './lib/api'
  import { defaultLanguage } from './lib/i18n'

  let currentLocale: string = defaultLanguage
  let initError: string | null = null
  let showSettings = false
  let showProfiles = false
  let showPathEditor = false
  let darkMode = false

  $: if ($locale) currentLocale = $locale

  onMount(async () => {
    try {
      const stored = typeof localStorage !== 'undefined' ? localStorage.getItem('darkMode') : null
      darkMode = stored === 'true'
    } catch {
      // Ignore
    }
    applyDarkMode(darkMode)
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

  function handleThemeChange(e: CustomEvent<boolean>) {
    applyDarkMode(e.detail)
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
          on:click={() => listVariables()}
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
        class="px-3 py-1.5 text-xs font-medium rounded-md transition {$activeView === 'variables'
          ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300'
          : 'text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700'}"
      >
        {$t('nav.variables')}
      </button>
      <button
        on:click={() => { showProfiles = true; }}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700"
      >
        {$t('nav.profiles')}
      </button>
      <button
        on:click={() => { showPathEditor = true; }}
        class="px-3 py-1.5 text-xs font-medium rounded-md transition text-gray-500 hover:text-gray-700 hover:bg-gray-100 dark:text-gray-400 dark:hover:text-gray-200 dark:hover:bg-gray-700"
      >
        {$t('nav.path')}
      </button>
    </nav>
  </header>

  <div class="px-5 py-4">
    {#if initError}
      <div class="bg-red-50 border border-red-200 text-red-800 px-3 py-2 rounded-md mb-3 text-xs dark:bg-red-900/30 dark:border-red-700 dark:text-red-300">
        <p class="font-medium mb-0.5">{$t('errors.cliExecutionFailed')}</p>
        <p class="font-mono break-all opacity-80">{initError}</p>
      </div>
    {/if}

    {#if $error}
      <div class="bg-red-50 border border-red-200 text-red-800 px-3 py-2 rounded-md mb-3 text-xs dark:bg-red-900/30 dark:border-red-700 dark:text-red-300">
        {$error}
      </div>
    {/if}

    {#if $loading}
      <div class="flex justify-center py-8">
        <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600"></div>
      </div>
    {:else}
      <Variables />
    {/if}
  </div>
</div>

{#if showSettings}
  <SettingsDialog
    darkMode={darkMode}
    on:close={() => (showSettings = false)}
    on:themeChange={handleThemeChange}
  />
{/if}

{#if showProfiles}
  <ProfileDialog on:close={() => { showProfiles = false; }} />
{/if}

{#if showPathEditor}
  <PathEditor on:close={() => { showPathEditor = false; }} />
{/if}

<style lang="postcss">
  @tailwind base;
  @tailwind components;
  @tailwind utilities;

  :global(body) {
    font-family: 'Segoe UI', -apple-system, BlinkMacSystemFont, system-ui, sans-serif;
    margin: 0;
    padding: 0;
    font-size: 13px;
    -webkit-font-smoothing: antialiased;
    -moz-osx-font-smoothing: grayscale;
  }

  :global([dir='rtl']) {
    text-align: right;
  }
</style>
