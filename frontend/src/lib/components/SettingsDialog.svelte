<script lang="ts">
  import { createEventDispatcher, onMount } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import { locales, defaultLanguage } from '../i18n'
  import { updateTrayLocale, isCliInPath, addCliToPath, removeCliFromPath } from '../api'
  import { get } from 'svelte/store'
  import { showToast } from '../stores'
  import { t as tStore } from 'svelte-i18n'

  export let darkMode = false
  export let fontScale: number = 1

  const dispatch = createEventDispatcher()

  let selectedFontScale: number = fontScale
  $: if (fontScale !== selectedFontScale) selectedFontScale = fontScale

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

  let cliInPath = false
  let cliToggleLoading = false

  onMount(async () => {
    // Check real system PATH on mount
    cliInPath = await isCliInPath()
  })

  function switchLocale(newLocale: string) {
    locale.set(newLocale)
    try {
      localStorage.setItem('locale', newLocale)
    } catch {
      // Ignore storage errors
    }
    // Sync tray menu text and tooltip with the new locale
    setTimeout(() => {
      const showText = get(tStore)('tray.show')
      const quitText = get(tStore)('tray.quit')
      const tooltip = get(tStore)('tray.tooltip')
      updateTrayLocale(showText, quitText, tooltip)
    }, 200)
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

  async function toggleCliInPath() {
    if (cliToggleLoading) return
    cliToggleLoading = true
    cliMessage = ''
    try {
      if (cliInPath) {
        const result = await removeCliFromPath()
        // Immediately re-check real PATH to update toggle state
        cliInPath = await isCliInPath()
        if (result.removed || !cliInPath) {
          showToast(get(tStore)('settings.cliRemoved'), 'success')
        } else {
          showToast(get(tStore)('settings.cliRemoveFailed'), 'error')
        }
      } else {
        const result = await addCliToPath()
        // Immediately re-check real PATH to update toggle state
        cliInPath = await isCliInPath()
        if (result.added || cliInPath) {
          showToast(get(tStore)('settings.cliAdded'), 'success')
        } else {
          showToast(get(tStore)('settings.cliAddFailed'), 'error')
        }
      }
    } finally {
      cliToggleLoading = false
    }
    // Notify parent to refresh all views (PATH entries changed)
    dispatch('pathChanged')
  }

  function changeFontScale(scale: number) {
    selectedFontScale = scale
    fontScale = scale
    try {
      localStorage.setItem('fontScale', String(scale))
    } catch {
      // Ignore
    }
    dispatch('fontScaleChange', scale)
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div
  class="fixed inset-0 bg-black/40 flex items-center justify-center z-50"
  on:click={handleClose}
  on:keydown={(e) => { if (e.key === 'Escape') handleClose() }}
  role="presentation"
  tabindex="-1">
  <div class="bg-white rounded-lg shadow-xl max-w-sm w-full mx-4 dark:bg-gray-800" on:click|stopPropagation>
    <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700">
      <h2 class="text-sm font-semibold text-gray-900 dark:text-gray-100">{$t('nav.settings')}</h2>
    </div>

    <div class="px-5 py-4 space-y-4">
      <div>
        <label for="settings-lang" class="block text-xs font-medium text-gray-600 mb-1.5 dark:text-gray-400">
          {$t('settings.language')}
        </label>
        <select
          id="settings-lang"
          on:change={(e) => switchLocale(e.currentTarget.value)}
          value={currentLocale}
          class="w-full px-3 py-1.5 text-sm border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
        >
          {#each locales as loc}
            <option value={loc}>{languageNames[loc]}</option>
          {/each}
        </select>
      </div>

      <div class="flex items-center justify-between">
        <span class="text-xs font-medium text-gray-600 dark:text-gray-400">{$t('settings.darkMode')}</span>
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

      <div class="flex items-center justify-between">
        <span class="text-xs font-medium text-gray-600 dark:text-gray-400">{$t('settings.addCliToPath')}</span>
        <button
          on:click={toggleCliInPath}
          disabled={cliToggleLoading}
          class="relative inline-flex h-5 w-9 items-center rounded-full transition {cliInPath ? 'bg-blue-600' : 'bg-gray-300'} disabled:opacity-50"
          role="switch"
          aria-checked={cliInPath}
          aria-label={$t('settings.addCliToPath')}
        >
          <span
            class="inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition {cliInPath ? 'translate-x-4' : 'translate-x-0.5'}"
          ></span>
        </button>
      </div>
      <div>
        <label for="settings-font-size" class="block text-xs font-medium text-gray-600 mb-1.5 dark:text-gray-400">
          {$t('settings.fontSize')}
        </label>
        <div class="flex items-center gap-2">
          <button
            on:click={() => changeFontScale(0.85)}
            class="px-2 py-1 text-xs border rounded transition {selectedFontScale === 0.85 ? 'bg-blue-600 text-white border-blue-600 dark:bg-blue-500 dark:border-blue-500' : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'}"
          >
            A
          </button>
          <button
            on:click={() => changeFontScale(1)}
            class="px-2 py-1 text-sm border rounded transition {selectedFontScale === 1 ? 'bg-blue-600 text-white border-blue-600 dark:bg-blue-500 dark:border-blue-500' : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'}"
          >
            A
          </button>
          <button
            on:click={() => changeFontScale(1.15)}
            class="px-2 py-1 text-base border rounded transition {selectedFontScale === 1.15 ? 'bg-blue-600 text-white border-blue-600 dark:bg-blue-500 dark:border-blue-500' : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'}"
          >
            A
          </button>
          <button
            on:click={() => changeFontScale(1.3)}
            class="px-2 py-1 text-lg border rounded transition {selectedFontScale === 1.3 ? 'bg-blue-600 text-white border-blue-600 dark:bg-blue-500 dark:border-blue-500' : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'}"
          >
            A
          </button>
          <button
            on:click={() => changeFontScale(1.45)}
            class="px-2 py-1 text-xl border rounded transition {selectedFontScale === 1.45 ? 'bg-blue-600 text-white border-blue-600 dark:bg-blue-500 dark:border-blue-500' : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'}"
          >
            A
          </button>
          <button
            on:click={() => changeFontScale(1.6)}
            class="px-2 py-1 text-2xl border rounded transition {selectedFontScale === 1.6 ? 'bg-blue-600 text-white border-blue-600 dark:bg-blue-500 dark:border-blue-500' : 'border-gray-300 text-gray-600 hover:bg-gray-50 dark:border-gray-600 dark:text-gray-400 dark:hover:bg-gray-700'}"
          >
            A
          </button>
        </div>
      </div>
    </div>

    <div class="px-5 py-3 border-t border-gray-200 flex justify-end dark:border-gray-700">
      <button
        on:click={handleClose}
        class="px-4 py-1.5 text-sm text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
      >
        {$t('buttons.close')}
      </button>
    </div>
  </div>
</div>
