<script lang="ts">
  import { onMount } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import Variables from './lib/components/Variables.svelte'
  import { variables, loading, error } from './lib/stores'
  import { listVariables } from './lib/api'
  import { locales, defaultLanguage } from './lib/i18n'

  const languageNames: Record<string, string> = {
    en: 'English',
    zh: '中文',
    ja: '日本語',
    ko: '한국어',
    de: 'Deutsch',
    fr: 'Francais',
    es: 'Espanol',
    pt: 'Portugues',
    ru: 'Русский',
    ar: 'العربية',
  }

  let currentLocale: string = defaultLanguage

  // Reactively sync currentLocale from the locale store
  $: if ($locale) currentLocale = $locale

  onMount(async () => {
    await listVariables()
  })

  function switchLocale(newLocale: string) {
    locale.set(newLocale)
    localStorage.setItem('locale', newLocale)
  }
</script>

<main class="min-h-screen bg-gray-50">
  <header class="bg-white border-b border-gray-200 px-6 py-4">
    <div class="flex justify-between items-start">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">{$t('app.title')}</h1>
        <p class="text-gray-600 text-sm mt-1">{$t('app.description')}</p>
      </div>
      <select
        on:change={(e) => switchLocale(e.currentTarget.value)}
        value={currentLocale}
        class="px-3 py-1.5 border border-gray-300 rounded-lg text-sm bg-white focus:outline-none focus:ring-2 focus:ring-blue-500"
        aria-label={$t('settings.language')}
      >
        {#each locales as loc}
          <option value={loc}>{languageNames[loc]}</option>
        {/each}
      </select>
    </div>
  </header>

  <div class="container mx-auto px-6 py-8">
    {#if $error}
      <div class="bg-red-50 border border-red-200 text-red-800 px-4 py-3 rounded mb-4">
        {$error}
      </div>
    {/if}

    {#if $loading}
      <div class="flex justify-center py-8">
        <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-gray-900"></div>
      </div>
    {:else}
      <Variables />
    {/if}
  </div>
</main>

<style lang="postcss">
  @tailwind base;
  @tailwind components;
  @tailwind utilities;

  :global(body) {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen,
      Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
    margin: 0;
    padding: 0;
  }
</style>
