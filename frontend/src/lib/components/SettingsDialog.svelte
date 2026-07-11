<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import { locales, defaultLanguage } from '../i18n'

  export let darkMode = false

  const dispatch = createEventDispatcher()

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
  $: if ($locale) currentLocale = $locale

  function switchLocale(newLocale: string) {
    locale.set(newLocale)
    try {
      localStorage.setItem('locale', newLocale)
    } catch {
      // Ignore storage errors
    }
  }

  function toggleDarkMode() {
    darkMode = !darkMode
    try {
      localStorage.setItem('darkMode', String(darkMode))
    } catch {
      // Ignore storage errors
    }
    dispatch('themeChange', darkMode)
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div class="fixed inset-0 bg-black/40 flex items-center justify-center z-50" on:click={handleClose}>
  <div class="bg-white rounded-lg shadow-xl max-w-sm w-full mx-4" on:click|stopPropagation>
    <div class="px-5 py-4 border-b border-gray-200">
      <h2 class="text-sm font-semibold text-gray-900">{$t('nav.settings')}</h2>
    </div>

    <div class="px-5 py-4 space-y-4">
      <div>
        <label for="settings-lang" class="block text-xs font-medium text-gray-600 mb-1.5">
          {$t('settings.language')}
        </label>
        <select
          id="settings-lang"
          on:change={(e) => switchLocale(e.currentTarget.value)}
          value={currentLocale}
          class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white"
        >
          {#each locales as loc}
            <option value={loc}>{languageNames[loc]}</option>
          {/each}
        </select>
      </div>

      <div class="flex items-center justify-between">
        <span class="text-xs font-medium text-gray-600">{$t('settings.darkMode')}</span>
        <button
          on:click={toggleDarkMode}
          class="relative inline-flex h-5 w-9 items-center rounded-full transition {darkMode ? 'bg-blue-600' : 'bg-gray-300'}"
          role="switch"
          aria-checked={darkMode}
          aria-label={$t('settings.darkMode')}
        >
          <span
            class="inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition {darkMode ? 'translate-x-4' : 'translate-x-0.5'}"
          ></span>
        </button>
      </div>
    </div>

    <div class="px-5 py-3 border-t border-gray-200 flex justify-end">
      <button
        on:click={handleClose}
        class="px-4 py-1.5 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition"
      >
        {$t('buttons.close')}
      </button>
    </div>
  </div>
</div>
