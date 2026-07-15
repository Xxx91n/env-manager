<script lang="ts">
  import { onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { showToast, showModal, refreshTrigger } from '../stores'
  import { listProtection, addProtectedPath, removeProtectedPath, addProtectedVar, removeProtectedVar, listVariables, listPathEntries } from '../api'
  import type { ProtectionData } from '../api'

  let data: ProtectionData | null = null
  let loading = false
  let activeTab: 'vars' | 'paths' = 'vars'

  // For adding custom protected vars from existing variables
  let availableVars: { name: string; scope: string }[] = []
  let selectedVarName = ''

  // For adding custom protected paths from existing PATH entries
  let availablePathEntries: string[] = []
  let selectedPathEntry = ''

  onMount(refresh)

  $: if ($refreshTrigger > 0) refresh()

  async function refresh() {
    loading = true
    try {
      data = await listProtection()
      // Load available vars and paths for the "add from existing" dropdowns
      const [varsResult, pathsResult] = await Promise.all([
        (async () => {
          try { return await listVariables() } catch { return null }
        })(),
        (async () => {
          try { return await listPathEntries('user') } catch { return null }
        })(),
      ])
      if (varsResult) {
        availableVars = varsResult
          .filter(v => !data!.protectedVars.builtIn.includes(v.name) && !data!.protectedVars.custom.includes(v.name))
          .map(v => ({ name: v.name, scope: v.scope }))
      }
      if (pathsResult) {
        availablePathEntries = pathsResult
          .map(e => e.path)
          .filter(p => !data!.protectedPaths.builtIn.includes(p) && !data!.protectedPaths.custom.includes(p))
      }
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      loading = false
    }
  }

  async function handleAddVar() {
    const name = selectedVarName.trim()
    if (!name) return
    try {
      await addProtectedVar(name)
      selectedVarName = ''
      await refresh()
      showToast($t('protection.varLocked'), 'success')
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  function handleRemoveVar(name: string) {
    showModal({
      title: $t('protection.unlockVar'),
      message: $t('protection.unlockVarConfirm', { values: { name } }),
      confirmLabel: $t('protection.unlockVar'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        try {
          await removeProtectedVar(name)
          await refresh()
          showToast($t('protection.varUnlocked'), 'success')
        } catch (err) {
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }

  async function handleAddPath() {
    const entry = selectedPathEntry.trim()
    if (!entry) return
    try {
      await addProtectedPath(entry)
      selectedPathEntry = ''
      await refresh()
      showToast($t('protection.pathLocked'), 'success')
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  function handleRemovePath(entry: string, isBuiltIn: boolean) {
    if (isBuiltIn) {
      showToast($t('protection.cannotRemoveBuiltin'), 'error')
      return
    }
    showModal({
      title: $t('protection.unlockPath'),
      message: $t('protection.unlockPathConfirm', { values: { name: entry } }),
      confirmLabel: $t('protection.unlockPath'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        try {
          await removeProtectedPath(entry)
          await refresh()
          showToast($t('protection.pathUnlocked'), 'success')
        } catch (err) {
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }
</script>

<div class="space-y-3">
  <!-- Tab selector -->
  <div class="flex items-center gap-2">
    <div class="inline-flex rounded-md border border-gray-200 dark:border-gray-700 overflow-hidden">
      <button
        on:click={() => activeTab = 'vars'}
        class="px-4 py-1.5 text-xs font-medium transition {activeTab === 'vars'
          ? 'bg-blue-600 text-white dark:bg-blue-500'
          : 'bg-white text-gray-600 hover:bg-gray-50 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700'}"
      >
        {$t('protection.protectedVars')}
      </button>
      <button
        on:click={() => activeTab = 'paths'}
        class="px-4 py-1.5 text-xs font-medium transition {activeTab === 'paths'
          ? 'bg-blue-600 text-white dark:bg-blue-500'
          : 'bg-white text-gray-600 hover:bg-gray-50 dark:bg-gray-800 dark:text-gray-300 dark:hover:bg-gray-700'}"
      >
        {$t('protection.protectedPaths')}
      </button>
    </div>
    <button on:click={refresh} class="ml-auto px-3 py-1.5 text-xs text-gray-600 hover:bg-gray-100 rounded-md dark:text-gray-300 dark:hover:bg-gray-700">
      {$t('buttons.refresh')}
    </button>
  </div>

  {#if loading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-5 w-5 border-b-2 border-blue-600"></div>
    </div>
  {:else if !data}
    <div class="px-4 py-8 text-center text-gray-400 text-xs">{$t('messages.loading')}</div>
  {:else if activeTab === 'vars'}
    <!-- Protected Variables Tab -->
    <div class="space-y-4">
      <!-- Built-in protected vars -->
      <div class="bg-white border border-gray-200 rounded-md overflow-hidden dark:bg-gray-800 dark:border-gray-700">
        <div class="px-4 py-2.5 border-b border-gray-200 bg-gray-50 dark:bg-gray-750 dark:border-gray-700">
          <span class="text-xs font-medium text-gray-600 dark:text-gray-300">{$t('protection.builtinVars')} ({data.protectedVars.builtIn.length})</span>
        </div>
        <div class="max-h-64 overflow-y-auto">
          <div class="grid grid-cols-2 sm:grid-cols-3 gap-1 p-3">
            {#each data.protectedVars.builtIn as varName (varName)}
              <div class="flex items-center gap-1.5 px-2 py-1 rounded bg-gray-50 dark:bg-gray-750">
                <svg class="w-3 h-3 text-amber-500 flex-shrink-0" fill="currentColor" viewBox="0 0 20 20">
                  <path fill-rule="evenodd" d="M5 9V7a5 5 0 0110 0v2a2 2 0 012 2v5a2 2 0 01-2 2H5a2 2 0 01-2-2v-5a2 2 0 012-2zm8-2v2H7V7a3 3 0 016 0z" clip-rule="evenodd" />
                </svg>
                <span class="text-xs font-mono text-gray-700 dark:text-gray-200 truncate" title={varName}>{varName}</span>
              </div>
            {/each}
          </div>
        </div>
      </div>
      <div class="text-[10px] text-gray-400 px-1">{$t('protection.varsNote')}</div>

      <!-- Custom protected vars (user-locked) -->
      <div class="bg-white border border-gray-200 rounded-md overflow-hidden dark:bg-gray-800 dark:border-gray-700">
        <div class="px-4 py-2.5 border-b border-gray-200 bg-gray-50 dark:bg-gray-750 dark:border-gray-700">
          <span class="text-xs font-medium text-gray-600 dark:text-gray-300">{$t('protection.customVars')} ({data.protectedVars.custom.length})</span>
        </div>
        {#if data.protectedVars.custom.length === 0}
          <div class="px-4 py-6 text-center text-[10px] text-gray-400">{$t('protection.noCustomVars')}</div>
        {:else}
          <div class="divide-y divide-gray-100 dark:divide-gray-700">
            {#each data.protectedVars.custom as varName (varName)}
              <div class="flex items-center justify-between px-4 py-2">
                <span class="text-xs font-mono text-gray-700 dark:text-gray-200 truncate" title={varName}>{varName}</span>
                <button
                  on:click={() => handleRemoveVar(varName)}
                  class="text-red-500 hover:text-red-700 transition flex-shrink-0 ml-2"
                  title={$t('protection.unlockVar')}
                  aria-label={$t('protection.unlockVar')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M8 11V7a4 4 0 018 0v4M5 9h14a1 1 0 011 1v8a1 1 0 01-1 1H5a1 1 0 01-1-1v-8a1 1 0 011-1z" />
                  </svg>
                </button>
              </div>
            {/each}
          </div>
        {/if}
      </div>

      <!-- Add custom protected var from existing -->
      <div class="flex gap-2">
        <select
          bind:value={selectedVarName}
          on:keydown={(e) => { if (e.key === 'Enter') handleAddVar() }}
          class="flex-1 px-3 py-1.5 text-xs border border-gray-300 rounded-md font-mono focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
        >
          <option value="">-- {$t('protection.selectVar')} --</option>
          {#each availableVars as v (v.name + v.scope)}
            <option value={v.name}>{v.name} ({v.scope})</option>
          {/each}
        </select>
        <button
          on:click={handleAddVar}
          disabled={!selectedVarName.trim()}
          class="px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
        >
          {$t('protection.lockVar')}
        </button>
      </div>
    </div>
  {:else}
    <!-- Protected PATH Entries Tab -->
    <div class="space-y-4">
      <!-- Built-in protected path entries -->
      <div class="bg-white border border-gray-200 rounded-md overflow-hidden dark:bg-gray-800 dark:border-gray-700">
        <div class="px-4 py-2.5 border-b border-gray-200 bg-gray-50 dark:bg-gray-750 dark:border-gray-700">
          <span class="text-xs font-medium text-gray-600 dark:text-gray-300">{$t('protection.builtinPaths')}</span>
        </div>
        <div class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each data.protectedPaths.builtIn as entry (entry)}
            <div class="flex items-center justify-between px-4 py-2">
              <span class="text-xs font-mono text-gray-700 dark:text-gray-200 truncate" title={entry}>{entry}</span>
              <span class="text-[10px] text-gray-400 flex-shrink-0 ml-2">{$t('protection.builtin')}</span>
            </div>
          {/each}
        </div>
      </div>

      <!-- Custom protected path entries -->
      <div class="bg-white border border-gray-200 rounded-md overflow-hidden dark:bg-gray-800 dark:border-gray-700">
        <div class="px-4 py-2.5 border-b border-gray-200 bg-gray-50 dark:bg-gray-750 dark:border-gray-700 flex items-center justify-between">
          <span class="text-xs font-medium text-gray-600 dark:text-gray-300">{$t('protection.customPaths')}</span>
        </div>
        {#if data.protectedPaths.custom.length === 0}
          <div class="px-4 py-6 text-center text-[10px] text-gray-400">{$t('protection.noCustomPaths')}</div>
        {:else}
          <div class="divide-y divide-gray-100 dark:divide-gray-700">
            {#each data.protectedPaths.custom as entry (entry)}
              <div class="flex items-center justify-between px-4 py-2">
                <span class="text-xs font-mono text-gray-700 dark:text-gray-200 truncate" title={entry}>{entry}</span>
                <button
                  on:click={() => handleRemovePath(entry, false)}
                  class="text-red-500 hover:text-red-700 transition flex-shrink-0 ml-2"
                  title={$t('protection.unlockPath')}
                  aria-label={$t('protection.unlockPath')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M8 11V7a4 4 0 018 0v4M5 9h14a1 1 0 011 1v8a1 1 0 01-1 1H5a1 1 0 01-1-1v-8a1 1 0 011-1z" />
                  </svg>
                </button>
              </div>
            {/each}
          </div>
        {/if}
      </div>

      <!-- Add custom protected path from existing PATH entries -->
      <div class="flex gap-2">
        <select
          bind:value={selectedPathEntry}
          on:keydown={(e) => { if (e.key === 'Enter') handleAddPath() }}
          class="flex-1 px-3 py-1.5 text-xs border border-gray-300 rounded-md font-mono focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
        >
          <option value="">-- {$t('protection.selectPath')} --</option>
          {#each availablePathEntries as entry (entry)}
            <option value={entry}>{entry}</option>
          {/each}
        </select>
        <button
          on:click={handleAddPath}
          disabled={!selectedPathEntry.trim()}
          class="px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
        >
          {$t('protection.lockPath')}
        </button>
      </div>
    </div>
  {/if}
</div>
