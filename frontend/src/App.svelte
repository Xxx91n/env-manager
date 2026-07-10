<script lang="ts">
  import { onMount } from 'svelte'
  import { locale, t } from 'svelte-i18n'
  import Variables from './lib/components/Variables.svelte'
  import { variables, loading, error } from './lib/stores'
  import { listVariables } from './lib/api'
  import { locales, defaultLanguage } from './lib/i18n'

  let currentLocale: string = defaultLanguage

  onMount(async () => {
    // Subscribe to locale changes
    const unsub = locale.subscribe(value => {
      if (value) {
        currentLocale = value
        localStorage.setItem('locale', value)
      }
    })

    await listVariables()
    return unsub
  })

  function switchLocale(newLocale: string) {
    locale.set(newLocale)
  }
</script>

<main class="min-h-screen bg-gray-50">
  <header class="bg-white border-b border-gray-200 px-6 py-4">
    <div class="flex justify-between items-start">
      <div>
        <h1 class="text-2xl font-bold text-gray-900">{$t('app.title')}</h1>
        <p class="text-gray-600 text-sm mt-1">{$t('app.description')}</p>
      </div>
      <div class="flex gap-2">
        {#each locales as loc}
          <button
            on:click={() => switchLocale(loc)}
            class="px-3 py-1 rounded text-sm transition"
            class:bg-blue-500={currentLocale === loc}
            class:text-white={currentLocale === loc}
            class:bg-gray-200={currentLocale !== loc}
            class:text-gray-700={currentLocale !== loc}
          >
            {loc.toUpperCase()}
          </button>
        {/each}
      </div>
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

<style global>
  @import 'tailwindcss/base';
  @import 'tailwindcss/components';
  @import 'tailwindcss/utilities';

  :global(body) {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Oxygen,
      Ubuntu, Cantarell, 'Open Sans', 'Helvetica Neue', sans-serif;
    margin: 0;
    padding: 0;
  }
</style>
