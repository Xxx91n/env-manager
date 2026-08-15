<script lang="ts">
  import { Power } from 'lucide-svelte'
  import { createEventDispatcher, onMount } from 'svelte'
  import { t, locale } from 'svelte-i18n'
  import { locales, defaultLanguage } from '../i18n'
  import { isCliInPath, addCliToPath, removeCliFromPath, listPathEntries, checkForUpdates, bulkImport, bulkExport, pickOpenFile, pickSaveFile, serviceStatus, serviceHealth, servicePing, serviceRefreshMount, serviceRotateMount, serviceShutdown, serviceReload, serviceStart, serviceStop, exportState, importState } from '../api'
  import { get } from 'svelte/store'
  import { setSetting, frontendLog, getLightweightConfig as getLwConfig, setLightweightConfig as setLwConfig } from '../settingsStore'
  import { showToast } from '../stores'
  import { t as tStore } from 'svelte-i18n'

  export let darkMode = false
  export let fontScale: number = 1
  export let themeStyle: string = 'slate'

  // Lightweight mode config
  let autoLightweight = true
  let lightweightTimeout = 10
  let lightweightLoading = false

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

  // v0.9.0 Phase B+C: Service control panel state
  let serviceRunning = false
  let serviceHealthData: any = null
  let serviceLoading = false
  let serviceError: string | null = null


  onMount(async () => {
    // Check real system PATH on mount
    cliInPath = await isCliInPath()
    void refreshServiceStatus()
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

  function changeThemeStyle(style: string) {
    themeStyle = style
    void setSetting('themeStyle', style)
    dispatch('themeStyleChange', style)
    // v0.9.20 Add theme-changing class for smooth color base transition
    if (typeof document !== 'undefined' && document.body) {
      document.body.classList.add('theme-changing')
      setTimeout(() => {
        document.body.classList.remove('theme-changing')
      }, 250)
    }
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
        const result = await withCliTimeout(removeCliFromPath(), get(tStore)('settings.cliAddFailed'))
        cliInPath = await isCliInPath()
        if (result.removed || !cliInPath) {
          showToast(get(tStore)('settings.cliRemoved'), 'success')
        } else {
          showToast(get(tStore)('settings.cliRemoveFailed') + ': ' + result.message, 'error')
        }
      } else {
        const result = await withCliTimeout(addCliToPath(), get(tStore)('settings.cliAddFailed'))
        await listPathEntries('user', true)
        cliInPath = await isCliInPath()
        if (result.added || cliInPath) {
          showToast(get(tStore)('settings.cliAdded'), 'success')
          dispatch('refresh')
        } else {
          showToast(get(tStore)('settings.cliAddFailed') + ': ' + result.message, 'error')
        }
      }
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(get(tStore)('settings.cliAddFailed') + ': ' + (err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      cliToggleLoading = false
    }
  }

  /** 30s race: any IPC call here MUST resolve within 30s, otherwise throw
   *  with the provided failure label + timeout suffix. Mirrors the GUI's
   *  protection-page withTimeout pattern; keeps the toggle from going
   *  permanently grey when the CLI subprocess wedges. */
  function withCliTimeout<T>(p: Promise<T>, label: string): Promise<T> {
    return new Promise<T>((resolve, reject) => {
      const t = setTimeout(() => reject(new Error(label + ' (timeout after 30s)')), 30000)
      p.then((v) => { clearTimeout(t); resolve(v) }).catch((e) => { clearTimeout(t); reject(e) })
    })
  }

  // v0.9.0 Phase B+C: Service control panel handlers
  async function handleServicePing() {
    if (serviceLoading) return
    serviceLoading = true
    serviceError = null
    try {
      const result = await servicePing()
      if (!result.ok) {
        serviceError = result.message || 'ping failed'
      }
      await refreshServiceStatus()
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally {
      serviceLoading = false
    }
  }

  async function handleServiceReload() {
    if (serviceLoading || !serviceRunning) return
    serviceLoading = true
    serviceError = null
    try {
      const result = await serviceReload()
      if (!result.ok) {
        serviceError = result.message || 'reload failed'
      } else {
        showToast(get(tStore)('settings.service.reloaded'), 'success')
      }
      await refreshServiceStatus()
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally {
      serviceLoading = false
    }
  }

  async function handleServiceShutdown() {
    if (serviceLoading || !serviceRunning) return
    serviceLoading = true
    serviceError = null
    try {
      // v0.9.3: Use direct Tauri IPC stop_service (not CLI relay) for faster stop.
      // The service is a persistent daemon; only explicit user Stop kills it.
      await serviceStop()
      showToast(get(tStore)('settings.service.shutdown'), 'success')
      await refreshServiceStatus()
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally {
      serviceLoading = false
    }
  }

  async function handleServiceStart() {
    if (serviceLoading || serviceRunning) return
    serviceLoading = true
    serviceError = null
    try {
      // Check if service is already running first (avoid duplicate spawn)
      await refreshServiceStatus()
      if (serviceRunning) {
        showToast(get(tStore)('settings.service.started'), 'success')
        return
      }
      await serviceStart()
      // Give the service a moment to start up before checking status
      await new Promise(r => setTimeout(r, 3000))
      await refreshServiceStatus()
      if (serviceRunning) {
        showToast(get(tStore)('settings.service.started'), 'success')
      } else {
        serviceError = get(tStore)('settings.service.startFailed')
      }
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally {
      serviceLoading = false
    }
  }


  async function handleMountRefresh(mountId: string) {
    if (serviceLoading) return
    serviceLoading = true
    try {
      const result = await serviceRefreshMount(mountId)
      if (!result.ok) {
        serviceError = result.message || 'refresh failed'
      }
      await refreshServiceStatus()
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally {
      serviceLoading = false
    }
  }

  async function handleMountRotate(mountId: string) {
    if (serviceLoading) return
    serviceLoading = true
    try {
      const result = await serviceRotateMount(mountId)
      if (!result.ok) {
        serviceError = result.message || 'rotate failed'
      } else {
        showToast(get(tStore)('settings.service.rotated'), 'success')
      }
      await refreshServiceStatus()
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally {
      serviceLoading = false
    }
  }

  async function refreshServiceStatus() {
    try {
      const status = await serviceStatus()
      // v0.9.2: api.ts now returns unified {ok, data, message}
      serviceRunning = status?.ok === true && status?.data?.running === true
      if (serviceRunning) {
        const healthResult = await serviceHealth()
      serviceHealthData = healthResult.ok ? healthResult.data : null
      } else {
        serviceHealthData = null
      }
    } catch {
      serviceRunning = false
      serviceHealthData = null
    }
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
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      bulkLoading = false
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

  function handleClose() {
    dispatch('close')
  }

  // v0.9.9: Full-state disaster recovery handlers
  let drLoading = false
  let drError: string | null = null

  async function handleExportState() {
    if (drLoading) return
    drLoading = true
    drError = null
    try {
      const file = await pickSaveFile($t('settings.drExportPrompt'), 'env-manager-state.dpapi')
      if (!file) { drLoading = false; return }
      const result = await exportState(file)
      showToast($t('settings.drExported', { values: { count: result.exported } }), 'success')
    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      drError = err instanceof Error ? err.message : String(err)
      showToast(drError, 'error')
    }
    drLoading = false
  }

  async function handleImportState() {
    if (drLoading) return
    drLoading = true
    drError = null
    try {
      const file = await pickOpenFile($t('settings.drImportPrompt'))
      if (!file) { drLoading = false; return }
      // Dry-run first to validate
      await importState(file, true)
      const result = await importState(file, false)
      showToast($t('settings.drImported', { values: { count: result.imported } }), 'success')

    } catch (err) { void frontendLog('error', '[SettingsDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      drError = err instanceof Error ? err.message : String(err)
      showToast(drError, 'error')
    }
    drLoading = false
  }

  // ---- Lightweight mode ----

  async function loadLightweightConfig() {
    try {
      const config = await getLwConfig()
      autoLightweight = config.enabled
      lightweightTimeout = config.timeoutMinutes
    } catch {
      // defaults are fine
    }
  }

  async function handleToggleAutoLightweight() {
    autoLightweight = !autoLightweight
    await saveLightweightConfig()
  }

  async function handleLightweightTimeoutChange(e: Event) {
    const val = parseInt((e.target as HTMLInputElement).value, 10)
    lightweightTimeout = isNaN(val) || val < 1 ? 1 : val > 120 ? 120 : val
    await saveLightweightConfig()
  }

  async function saveLightweightConfig() {
    lightweightLoading = true
    try {
      await setLwConfig(autoLightweight, lightweightTimeout)
      showToast($t('settings.lightweightSaved'), 'success')
    } catch {
      showToast($t('settings.lightweightSaveFailed'), 'error')
    } finally {
      lightweightLoading = false
    }
  }

  onMount(() => {
    loadLightweightConfig()
  })
</script>

<div
  class="fixed inset-0 bg-background/40 flex items-center justify-center z-50"
  on:click={handleClose}
  on:keydown={(e) => { if (e.key === 'Escape') handleClose() }}
  role="presentation"
  tabindex="-1">
  <div class="bg-card rounded-lg shadow-xl max-w-sm max-h-[90vh] overflow-y-auto w-full mx-4" on:click|stopPropagation>
    <div class="px-5 py-4 border-b border-border">
      <h2 class="text-sm font-semibold text-foreground">{$t('nav.settings')}</h2>
    </div>

    <div class="px-5 py-4 space-y-4">
      <div>
        <label for="settings-lang" class="block text-xs font-medium text-muted-foreground mb-1.5">
          {$t('settings.language')}
        </label>
        <select
          id="settings-lang"
          on:change={(e) => switchLocale(e.currentTarget.value)}
          value={currentLocale}
          class="w-full px-3 py-1.5 text-sm border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary bg-card bg-accent border-border/80 text-foreground"
        >
          {#each locales as loc}
            <option value={loc}>{languageNames[loc]}</option>
          {/each}
        </select>
      </div>

      <div class="flex items-center justify-between">
        <span class="text-xs font-medium text-muted-foreground">{$t('settings.darkMode')}</span>
        <button
         on:click={toggleDarkMode}
          class="relative inline-flex h-5 w-8 items-center rounded-full transition {darkMode ? 'bg-primary' : 'bg-border'}"
         role="switch"
         aria-checked={darkMode}
         aria-label={$t('settings.darkMode')}
       >
         <span
            class="inline-block h-3.5 w-3.5 transform rounded-full bg-card shadow transition {darkMode ? 'translate-x-3.5' : 'translate-x-0.5'}"
         ></span>
       </button>
      </div>

      <div class="flex items-center justify-between">
        <span class="text-xs font-medium text-muted-foreground">{$t('settings.addCliToPath')}</span>
        <button
          on:click={toggleCliInPath}
          disabled={cliToggleLoading}
          class="relative inline-flex h-5 w-8 items-center rounded-full transition {cliInPath ? 'bg-primary' : 'bg-border'} disabled:opacity-50"
          role="switch"
          aria-checked={cliInPath}
          aria-label={$t('settings.addCliToPath')}
        >
          <span
            class="inline-block h-3.5 w-3.5 transform rounded-full bg-card shadow transition {cliInPath ? 'translate-x-3.5' : 'translate-x-0.5'}"
          ></span>
        </button>
      </div>

      <!-- Theme base color selector -->
      <div class="flex items-center justify-between py-1">
        <span class="text-xs font-medium text-muted-foreground">{$t('settings.themeStyle')}</span>
        <select
          on:change={(e) => changeThemeStyle(e.currentTarget.value)}
          class="px-2.5 py-1 text-xs border rounded border-border bg-card text-foreground focus:ring-1 focus:ring-ring"
          aria-label={$t('settings.themeStyle')}
        >
          {#each ['slate', 'blue', 'violet', 'rose', 'cyan', 'amber'] as style}
            <option value={style} selected={themeStyle === style}>
              {$t('settings.themeStyle' + (style.charAt(0).toUpperCase()) + style.slice(1))}
            </option>
          {/each}
        </select>
      </div>
      <div>
        <label for="settings-font-size" class="block text-xs font-medium text-muted-foreground mb-1.5">
          {$t('settings.fontSize')}
        </label>
        <div class="flex items-center gap-3">
          <input
            type="range"
            min={0.8}
            max={1.5}
            step={0.05}
            value={selectedFontScale}
            on:input={(e) => changeFontScale(parseFloat(e.currentTarget.value))}
            class="flex-1 h-1.5 rounded-full appearance-none bg-border cursor-pointer
                   accent-[hsl(var(--primary))]"
            id="settings-font-size"
            aria-label={$t('settings.fontSize')}
          />
          <span class="text-xs font-medium text-muted-foreground tabular-nums min-w-[3rem] text-right">
            {Math.round(selectedFontScale * 100)}%
          </span>
        </div>
      </div>
    </div>

    <div class="px-5 py-3 space-y-2">
      <div>
        <label class="block text-xs font-medium text-muted-foreground mb-1.5">
          {$t('update.title')}
        </label>
        <div class="flex items-center gap-2">
          <button
            on:click={handleCheckUpdate}
            disabled={updateChecking}
            class="px-3 py-1.5 text-xs font-medium text-primary-foreground bg-primary rounded-md hover:bg-primary transition disabled:opacity-50 bg-primary/80"
          >
            {#if updateChecking}
          <Power class="animate-spin h-3 w-3 inline mr-1" />
              {$t('update.checking')}
            {:else}
              {$t('update.check')}
            {/if}
          </button>
          {#if updateAvailable === true}
            <button
              on:click={openReleasePage}
              class="px-3 py-1.5 text-xs font-medium text-primary hover:underline"
            >
              {$t('update.download', { values: { version: latestVersion } })}
            </button>
          {:else if updateAvailable === false}
            <span class="text-xs text-primary">{$t('update.upToDate')}</span>
          {/if}
        </div>
      </div>
    </div>

    <div class="px-5 py-3 space-y-2 border-t border-border">
      <div>
        <label class="block text-xs font-medium text-muted-foreground mb-1.5">
          {$t('settings.bulkTitle')}
        </label>
        <div class="flex items-center gap-2 flex-wrap">
          <select
            bind:value={bulkScope}
            class="px-2 py-1 text-xs border border-border rounded-md bg-accent border-border/80 text-foreground"
          >
            <option value="user">{$t('scope.user')}</option>
            <option value="system">{$t('scope.system')}</option>
          </select>
          <button
            on:click={handleBulkImport}
            disabled={bulkLoading}
            class="px-3 py-1 text-xs font-medium text-foreground/80 bg-card border border-border rounded-md hover:bg-muted/50 transition disabled:opacity-50 text-foreground border-border/80 hover:bg-accent"
          >
            {$t('settings.bulkImport')}
          </button>
          <button
            on:click={handleBulkExport}
            disabled={bulkLoading}
            class="px-3 py-1 text-xs font-medium text-foreground/80 bg-card border border-border rounded-md hover:bg-muted/50 transition disabled:opacity-50 text-foreground border-border/80 hover:bg-accent"
          >
            {$t('settings.bulkExport')}
          </button>
        </div>
        <p class="mt-1 text-[10px] text-muted-foreground">{$t('settings.bulkHint')}</p>
      </div>
    </div>


    <div class="px-5 py-3 space-y-2 border-t border-border">
      <div>
        <label class="block text-xs font-medium text-muted-foreground mb-1.5">
          {$t('settings.drTitle')}
        </label>
        <div class="flex items-center gap-2 flex-wrap">
          <button
            on:click={handleExportState}
            disabled={drLoading}
            class="px-3 py-1 text-xs font-medium text-foreground/80 bg-card border border-border rounded-md hover:bg-muted/50 transition disabled:opacity-50 text-foreground border-border/80 hover:bg-accent"
          >
            {$t('settings.drExport')}
          </button>
          <button
            on:click={handleImportState}
            disabled={drLoading}
            class="px-3 py-1 text-xs font-medium text-foreground/80 bg-card border border-border rounded-md hover:bg-muted/50 transition disabled:opacity-50 text-foreground border-border/80 hover:bg-accent"
          >
            {$t('settings.drImport')}
          </button>
        </div>
        <p class="mt-1 text-[10px] text-muted-foreground">{$t('settings.drHint')}</p>
      </div>
    </div>

    <div class="px-5 py-3 space-y-2 border-t border-border">
      <div>
        <label class="block text-xs font-medium text-muted-foreground mb-1.5">
          {$t('settings.lightweightTitle')}
        </label>
        <div class="flex items-center gap-3">
          <button
            on:click={handleToggleAutoLightweight}
            disabled={lightweightLoading}
            class="relative inline-flex h-5 w-8 items-center rounded-full transition {autoLightweight ? 'bg-primary' : 'bg-border bg-muted'} disabled:opacity-50"
            role="switch"
            aria-checked={autoLightweight}
          >
            <span class="inline-block h-3.5 w-3.5 transform rounded-full bg-card transition {autoLightweight ? 'translate-x-3.5' : 'translate-x-0.5'}" />
          </button>
          <span class="text-xs text-muted-foreground">
            {$t('settings.lightweightAuto')}
          </span>
          {#if autoLightweight}
            <div class="flex items-center gap-1.5 ml-2">
              <input
                type="number"
                min="1"
                max="120"
                bind:value={lightweightTimeout}
                on:change={handleLightweightTimeoutChange}
                class="w-16 px-2 py-1 text-xs border border-border rounded-md bg-card border-border/80 text-foreground"
              />
              <span class="text-xs text-muted-foreground">
                {$t('settings.lightweightMinutes')}
              </span>
            </div>
          {/if}
        </div>
        <p class="mt-1 text-[10px] text-muted-foreground">{$t('settings.lightweightHint')}</p>
      </div>
    </div>
    <div class="px-5 py-3 border-t border-border flex justify-end">
      <button
        on:click={handleClose}
        class="px-4 py-1.5 text-sm text-foreground/80 border border-border rounded-md hover:bg-muted/50 transition text-foreground border-border/80 hover:bg-accent"
      >
        {$t('buttons.close')}
      </button>
    </div>
  </div>
</div>
