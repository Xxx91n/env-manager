<script lang="ts">
  import { createEventDispatcher, onMount } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import { locales, defaultLanguage } from '../i18n'
  import { isCliInPath, addCliToPath, removeCliFromPath, listPathEntries, checkForUpdates, bulkImport, bulkExport, pickOpenFile, pickSaveFile } from '../api'
  import { get } from 'svelte/store'
  import { setSetting, frontendLog } from '../settingsStore'
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
  let updateChecking = false
  let updateAvailable: boolean | null = null
  let latestVersion = ''
  let releaseUrl = ''
  let bulkScope: 'user' | 'system' = 'user'
  let bulkLoading = false

  onMount(async () => {
    // Check real system PATH on mount
    cliInPath = await isCliInPath()
  })

  function switchLocale(newLocale: string) {
    locale.set(newLocale)
    void frontendLog('info', 'switchLocale: setting locale=' + newLocale).catch(() => {})
    try {
      void setSetting('locale', newLocale)
    } catch {
      void frontendLog('error', 'switchLocale: setSetting threw for locale=' + newLocale).catch(() => {})
    }
    // Tray i18n is now reactively synced in App.svelte whenever $locale
    // changes. Previously this used setTimeout(200ms) here, which could race
    // with the async locale message loader and leave the tray menu stuck on
    // the previous language. The reactive subscription in App.svelte is the
    // single source of truth for tray locale sync.
  }

  function toggleDarkMode() {
    darkMode = !darkMode
    try {
      void setSetting('darkMode', String(darkMode))
    } catch {
      // Ignore storage errors
    }
    dispatch('themeChange', darkMode)
  }

  async function toggleCliInPath() {
    if (cliToggleLoading) return
    cliToggleLoading = true
    try {
     if (cliInPath) {
        // Fail-fast: if the IPC hangs (CLI subprocess wedged) surface a clear
        // error rather than leaving the toggle greyed forever. Wrap with a
        // 30s timeout so the user always sees success or a typed failure.
        const result = await withCliTimeout(removeCliFromPath(), get(tStore)('settings.cliAddFailed'))
       cliInPath = await isCliInPath()
       if (result.removed || !cliInPath) {
          showToast(get(tStore)('settings.cliRemoved'), 'success')
        } else {
          showToast(get(tStore)('settings.cliRemoveFailed') + ': ' + result.message, 'error')
        }
     } else {
        const result = await withCliTimeout(addCliToPath(), get(tStore)('settings.cliAddFailed'))
       // Force refresh PATH cache to see the new entry
       await listPathEntries('user', true)
       cliInPath = await isCliInPath()
       if (result.added || cliInPath) {
          showToast(get(tStore)('settings.cliAdded'), 'success')
          dispatch('refresh')
        } else {
          showToast(get(tStore)('settings.cliAddFailed') + ': ' + result.message, 'error')
 }

  /** 30s race: any IPC call here MUST resolve within 30s, otherwise throw
   *  with the provided failure label + timeout suffix. Mirrors the GUI's
   *  protection-page withTimeout pattern; keeps the toggle from going
   *  permanently grey when the CLI subprocess wedges (cov with AGENTS.md's
   *  fail-loud mandate). */
  function withCliTimeout<T>(p: Promise<T>, label: string): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const t = setTimeout(() => reject(new Error(label + ' (timeout after 30s)')), 30000)
      p.then((v) => { clearTimeout(t); resolve(v) }).catch((e) => { clearTimeout(t); reject(e) })
    })
  }
      }
    } catch (err) {
      showToast(get(tStore)('settings.cliAddFailed') + ': ' + (err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      cliToggleLoading = false
    }
  }

  function changeFontScale(scale: number) {
    selectedFontScale = scale
    fontScale = scale
    try {
      void setSetting('fontScale', String(scale))
    } catch {
      // Ignore
    }
    dispatch('fontScaleChange', scale)
  }

  async function handleCheckUpdate() {
    if (updateChecking) return
    updateChecking = true
    updateAvailable = null
    try {
      const version = '0.5.0'
      const info = await checkForUpdates(version)
      latestVersion = info.latestVersion
      releaseUrl = info.releaseUrl
      if (info.error) {
        // Check failed: do NOT show "up to date" - only show the error toast
        updateAvailable = null
        showToast($t('update.error'), 'error')
      } else if (info.isUpdateAvailable) {
        updateAvailable = true
        showToast($t('update.available', { values: { version: info.latestVersion } }), 'success')
      } else {
        updateAvailable = false
        showToast($t('update.upToDate'), 'info')
      }
    } catch {
      // Network/invoke failure: do NOT show "up to date" - only show error
      updateAvailable = null
      showToast($t('update.error'), 'error')
    } finally {
      updateChecking = false
    }
  }

  function openReleasePage() {
    if (releaseUrl) {
      // Use Tauri shell to open URL
      import('@tauri-apps/plugin-dialog').then(() => {
        // fallback: use window.open
        window.open(releaseUrl, '_blank')
      }).catch(() => {
        window.open(releaseUrl, '_blank')
      })
    }
  }

  async function handleBulkImport() {
    if (bulkLoading) return
    bulkLoading = true
    try {
      const file = await pickOpenFile($t('settings.bulkImportPrompt'))
      if (!file) return
      await bulkImport(file, bulkScope, false, false)
      showToast($t('settings.bulkImported'), 'success')
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      bulkLoading = false
    }
  }

  async function handleBulkExport() {
    if (bulkLoading) return
    bulkLoading = true
    try {
      let defaultPath = 'env_export.json'
      const file = await pickSaveFile($t('settings.bulkExportPrompt'), defaultPath)
      if (!file) return
      await bulkExport(file, bulkScope)
      showToast($t('settings.bulkExported'), 'success')
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      bulkLoading = false
    }
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
          class="relative inline-flex h-5 w-8 items-center rounded-full transition {darkMode ? 'bg-blue-600' : 'bg-gray-300'}"
         role="switch"
         aria-checked={darkMode}
         aria-label={$t('settings.darkMode')}
       >
         <span
            class="inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition {darkMode ? 'translate-x-3.5' : 'translate-x-0.5'}"
         ></span>
       </button>
      </div>

      <div class="flex items-center justify-between">
        <span class="text-xs font-medium text-gray-600 dark:text-gray-400">{$t('settings.addCliToPath')}</span>
        <button
         on:click={toggleCliInPath}
         disabled={cliToggleLoading}
          class="relative inline-flex h-5 w-8 items-center rounded-full transition {cliInPath ? 'bg-blue-600' : 'bg-gray-300'} disabled:opacity-50"
         role="switch"
         aria-checked={cliInPath}
         aria-label={$t('settings.addCliToPath')}
       >
         <span
            class="inline-block h-3.5 w-3.5 transform rounded-full bg-white shadow transition {cliInPath ? 'translate-x-3.5' : 'translate-x-0.5'}"
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

    <div class="px-5 py-3 space-y-2">
      <div>
        <label class="block text-xs font-medium text-gray-600 mb-1.5 dark:text-gray-400">
          {$t('update.title')}
        </label>
        <div class="flex items-center gap-2">
          <button
            on:click={handleCheckUpdate}
            disabled={updateChecking}
            class="px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
          >
            {#if updateChecking}
              <svg class="animate-spin h-3 w-3 inline mr-1" viewBox="0 0 24 24"><circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" fill="none"/><path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8V0C5.37 0 0 5.37 0 12h4z"/></svg>
              {$t('update.checking')}
            {:else}
              {$t('update.check')}
            {/if}
          </button>
          {#if updateAvailable === true}
            <button
              on:click={openReleasePage}
              class="px-3 py-1.5 text-xs font-medium text-blue-600 hover:underline dark:text-blue-400"
            >
              {$t('update.download', { values: { version: latestVersion } })}
            </button>
          {:else if updateAvailable === false}
            <span class="text-xs text-green-600 dark:text-green-400">{$t('update.upToDate')}</span>
          {/if}
        </div>
      </div>
    </div>

    <div class="px-5 py-3 space-y-2 border-t border-gray-200 dark:border-gray-700">
      <div>
        <label class="block text-xs font-medium text-gray-600 mb-1.5 dark:text-gray-400">
          {$t('settings.bulkTitle')}
        </label>
        <div class="flex items-center gap-2 flex-wrap">
          <select
            bind:value={bulkScope}
            class="px-2 py-1 text-xs border border-gray-300 rounded-md dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
          >
            <option value="user">{$t('scope.user')}</option>
            <option value="system">{$t('scope.system')}</option>
          </select>
          <button
            on:click={handleBulkImport}
            disabled={bulkLoading}
            class="px-3 py-1 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition disabled:opacity-50 dark:text-gray-200 dark:bg-gray-800 dark:border-gray-600 dark:hover:bg-gray-700"
          >
            {$t('settings.bulkImport')}
          </button>
          <button
            on:click={handleBulkExport}
            disabled={bulkLoading}
            class="px-3 py-1 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition disabled:opacity-50 dark:text-gray-200 dark:bg-gray-800 dark:border-gray-600 dark:hover:bg-gray-700"
          >
            {$t('settings.bulkExport')}
          </button>
        </div>
        <p class="mt-1 text-[10px] text-gray-400 dark:text-gray-500">{$t('settings.bulkHint')}</p>
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
