<script lang="ts">
  import { t } from 'svelte-i18n'
  import { frontendLog } from '../settingsStore'
  import { variables, selectedScope, search, debouncedSearch, filteredVariables, error, showModal, isWriteInProgress } from '../stores'
  import { showToast } from '../stores'
  import { deleteVariable, createBackup, toggleVariable, setVariable, expandVariableValue, addProtectedVar, removeProtectedVar, listVariables } from '../api'
  import { highlightParts } from '../features'
  import EditDialog from './EditDialog.svelte'
  import BackupDialog from './BackupDialog.svelte'
  import { loadNotes, getNoteSync, upsertNote } from '../notesStore'
  import { openInputDialog } from '../stores'

  let filteredVars = $filteredVariables
  let editingVar = null
  let showEditDialog = false
  let notesLoaded = false
  let notesTick = 0  // bump to re-render note indicators
  let hoveredNoteVar: string | null = null  // bump to re-render note indicators

  async function ensureNotesLoaded() {
    if (!notesLoaded) {
      await loadNotes()
      notesLoaded = true
    }
    notesTick++
  }

  async function handleNote(variableName: string) {
    await ensureNotesLoaded()
    const existing = getNoteSync(variableName)
    const result = await openInputDialog({
      title: existing ? $t('notes.editNote') : $t('notes.addNote'),
      defaultValue: existing?.note ?? '',
      placeholder: $t('notes.notePlaceholder'),
      allowEmpty: true,
    })
    if (result === null) return // cancelled
    await upsertNote(variableName, result)
    notesTick++
  }
  let showBackupDialog = false
  let togglingKeys: Record<string, boolean> = {}
  let expandedValues: Record<string, string> = {}
  const MAX_EXPANDED_CACHE = 500
  let previewTimer: ReturnType<typeof setTimeout> | null = null

  // filteredVars is the memoized derived store filteredVariables from stores.ts
  $: filteredVars = $filteredVariables

  function scheduleExpandedPreview(variable: { name: string; value: string; scope: string }) {
    const key = variable.scope + ':' + variable.name
    if (!variable.value.includes('%') || expandedValues[key]) return
    if (previewTimer) clearTimeout(previewTimer)
    previewTimer = setTimeout(async () => {
      try {
        const expanded = await expandVariableValue(variable.value)
        const entries = Object.entries(expandedValues)
        if (entries.length >= MAX_EXPANDED_CACHE) {
          // Evict oldest entry (FIFO approximation, prevents unbounded memory growth)
          entries.sort((a, b) => a[0].localeCompare(b[0]))
          const evicted = entries.slice(0, entries.length - MAX_EXPANDED_CACHE + 1)
          for (const [k] of evicted) delete expandedValues[k]
        }
        expandedValues = { ...expandedValues, [key]: expanded }
      } catch { /* preview is non-critical */ }
    }, 250)
  }

  function previewTitle(variable: { name: string; value: string; scope: string }): string {
    const expanded = expandedValues[variable.scope + ':' + variable.name]
    return expanded && expanded !== variable.value
      ? `${$t('messages.expandedValue')}: ${expanded}`
      : `${$t('messages.clickToCopy')} ${variable.value}`
  }
  function copyToClipboard(text: string) {
    navigator.clipboard.writeText(text).then(() => {
      showToast($t('messages.copied'), 'info', 1500)
    }).catch(() => {
      const textarea = document.createElement('textarea')
      textarea.value = text
      textarea.style.position = 'fixed'
      textarea.style.opacity = '0'
      document.body.appendChild(textarea)
      textarea.select()
      try {
        document.execCommand('copy')
        showToast($t('messages.copied'), 'info', 1500)
      } catch { /* ignore */ }
      document.body.removeChild(textarea)
    })
  }

  function handleDelete(name: string, scope: string) {
    if ($variables.some((variable) => variable.name === name && variable.scope === scope && variable.isProtected)) return
    showModal({
      title: $t('dialogs.deleteConfirm'),
      message: $t('messages.deleteConfirmText', { values: { name } }),
      confirmLabel: $t('buttons.delete'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
            try {
          await deleteVariable(name, scope as 'user' | 'system')
        } catch (err) { void frontendLog('error', '[Variables] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      }
    })
  }

  async function handleToggle(name: string, scope: string) {
    const current = $variables.find(v => v.name === name && v.scope === scope)
    if (!current || current.isProtected || current.isBuiltinProtected) return

    const key = name + ':' + scope
    // Disable the toggle button while in-flight
    togglingKeys = { ...togglingKeys, [key]: true }
    error.set(null)

    // Optimistic UI: flip the slider immediately without waiting for CLI.
    // If the CLI fails, we revert. This gives instant visual feedback.
    const wasDisabled = !!$variables.find(v => v.name === name && v.scope === scope)?.isDisabled
    variables.update(vars => vars.map(v => {
      if (v.name === name && v.scope === scope) {
        return { ...v, isDisabled: !wasDisabled }
      }
      return v
    }))

    try {
      await toggleVariable(name, scope as 'user' | 'system')
      // toggleVariable() in api.ts already calls listVariables() to confirm
    } catch (err) { void frontendLog('error', '[Variables] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      // Revert optimistic update on failure
      variables.update(vars => vars.map(v => {
        if (v.name === name && v.scope === scope) {
          return { ...v, isDisabled: wasDisabled }
        }
        return v
      }))
      localError = err instanceof Error ? err.message : String(err)
      setTimeout(() => { localError = '' }, 3000)
    } finally {
      const next = { ...togglingKeys }
      delete next[key]
      togglingKeys = next
    }
  }

  async function handleLockToggle(name: string, isProtected: boolean, isBuiltin: boolean) {
    // Built-in protected variables cannot be unlocked
    if (isBuiltin) {
      showToast($t('protection.cannotUnlockBuiltin'), 'error')
      return
    }
    try {
      if (isProtected) {
        await removeProtectedVar(name)
        showToast($t('protection.varUnlocked'), 'success')
      } else {
        await addProtectedVar(name)
        showToast($t('protection.varLocked'), 'success')
      }
      await listVariables()
    } catch (err) { void frontendLog('error', '[Variables] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  function handleEdit(v) {
    if (v?.isProtected) return
    editingVar = v
    showEditDialog = true
  }

  function handleCloseEdit() {
    showEditDialog = false
    editingVar = null
  }

  function handleShowBackup() {
    showBackupDialog = true
  }

  function handleCloseBackup() {
    showBackupDialog = false
  }
</script>

<div class="space-y-3">
  <!-- Toolbar -->
  <div class="flex gap-2 items-center">
    <div class="relative flex-1">
      <svg class="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
      </svg>
      <input
        type="text"
        placeholder={$t('messages.searchPlaceholder')}
        bind:value={$search}
        class="w-full pl-8 pr-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
      />
    </div>

    <select
      bind:value={$selectedScope}
      class="px-2.5 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
    >
      <option value="all">{$t('table.scope')}</option>
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>

    <button
      on:click={() => (showEditDialog = true)}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition dark:bg-blue-500 dark:hover:bg-blue-600"
    >
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
        <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" />
      </svg>
      {$t('buttons.add')}
    </button>

    <button
      on:click={handleShowBackup}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:bg-gray-800 dark:border-gray-600 dark:hover:bg-gray-700"
    >
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M5 8h14M5 8a2 2 0 110-4h14a2 2 0 110 4M5 8v10a2 2 0 002 2h10a2 2 0 002-2V8m-9 4h4" />
      </svg>
      {$t('buttons.backup')}
    </button>
  </div>

  <div class="overflow-x-auto bg-white rounded-md border border-gray-200 dark:bg-gray-800 dark:border-gray-700">
    {#if filteredVars.length === 0}
      <div class="px-4 py-8 text-center text-gray-400 text-xs dark:text-gray-500">
        {$t('messages.noData')}
      </div>
    {:else}
      <table class="w-full">
        <thead class="bg-gray-50 border-b border-gray-200 dark:bg-gray-750 dark:border-gray-600">
          <tr>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-12">
              {$t('table.enabled')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">
              {$t('table.name')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-20">
              {$t('table.scope')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">
              {$t('table.value')}
            </th>
            <th class="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-24">
              {$t('table.actions')}
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each filteredVars as variable (variable.name + variable.scope)}
            <tr class="hover:bg-gray-50 transition dark:hover:bg-gray-750 {variable.isDisabled ? 'opacity-50' : ''} {variable.isProtected ? 'bg-gray-100/60 dark:bg-gray-800/60' : ''}">
              <td class="px-3 py-2">
                <button
                  on:click={() => handleToggle(variable.name, variable.scope)}
                  disabled={$isWriteInProgress || togglingKeys[variable.name + ':' + variable.scope] === true || !!variable.isProtected || !!variable.isBuiltinProtected}
                  class="relative inline-flex h-4 w-7 items-center rounded-full transition disabled:opacity-30 disabled:cursor-not-allowed {variable.isDisabled ? 'bg-gray-300 dark:bg-gray-600' : 'bg-blue-600 dark:bg-blue-500'}"
                  role="switch"
                  aria-checked={!variable.isDisabled}
                  title={variable.isProtected ? $t('protection.lockedCannotToggle') : variable.isDisabled ? $t('messages.clickToEnable') : $t('messages.clickToDisable')}
                >
                  <span
                    class="inline-block h-3 w-3 transform rounded-full bg-white shadow transition {variable.isDisabled ? 'translate-x-0.5' : 'translate-x-3.5'}"
                  ></span>
                </button>
              </td>
              <td
                class="px-3 py-2 text-xs font-mono text-gray-900 dark:text-gray-100 cursor-pointer hover:text-blue-600 dark:hover:text-blue-400 transition select-none"
                title={$t('messages.clickToCopy')}
                on:click={() => copyToClipboard(variable.name)}
              >
                <div class="flex items-center gap-1.5">
                  <span>{#each highlightParts(variable.name, $debouncedSearch) as part}<span class={part.match ? 'bg-yellow-200 text-gray-900 dark:bg-yellow-500/60 dark:text-white' : ''}>{part.text}</span>{/each}</span>
                  {#if variable.profileSource}
                    <span
                      class="inline-flex px-1 py-0.5 rounded text-[9px] font-medium bg-purple-50 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300"
                      title={$t('messages.fromProfile', { values: { name: variable.profileSource } })}
                    >
                      {variable.profileSource}
                    </span>
                  {/if}
                </div>
              </td>
              <td class="px-3 py-2 text-xs">
                <span
                  class="inline-flex px-1.5 py-0.5 rounded text-xs font-medium {variable.scope ===
                  'user'
                    ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                    : 'bg-amber-50 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300'}"
                >
                  {variable.scope === 'user' ? $t('scope.user') : $t('scope.system')}
                </span>
              </td>
              <td
                class="px-3 py-2 text-xs text-gray-600 font-mono dark:text-gray-300 max-w-md truncate cursor-pointer hover:text-blue-600 dark:hover:text-blue-400 transition select-none"
                title={previewTitle(variable)}
                on:mouseenter={() => scheduleExpandedPreview(variable)}
                on:click={() => copyToClipboard(variable.value)}
              >
                {#each highlightParts(variable.value, $debouncedSearch) as part}<span class={part.match ? 'bg-yellow-200 text-gray-900 dark:bg-yellow-500/60 dark:text-white' : ''}>{part.text}</span>{/each}
                {#if expandedValues[variable.scope + ':' + variable.name] && expandedValues[variable.scope + ':' + variable.name] !== variable.value}
                  <div class="mt-0.5 text-[9px] text-gray-400 truncate" title={expandedValues[variable.scope + ':' + variable.name]}>
                    -> {expandedValues[variable.scope + ':' + variable.name]}
                  </div>
                {/if}
              </td>
              <td class="px-3 py-2 text-right text-xs">
                <button
                  on:click={() => handleLockToggle(variable.name, !!variable.isProtected, !!variable.isBuiltinProtected)}
                  disabled={!!variable.isBuiltinProtected}
                  class="inline-flex p-1 {variable.isProtected ? 'text-amber-500' : 'text-gray-400 hover:text-amber-500 hover:bg-amber-50'} rounded transition disabled:opacity-30 disabled:cursor-not-allowed dark:hover:bg-amber-900/30"
                  title={variable.isProtected ? (variable.isBuiltinProtected ? $t('protection.lockedBuiltin') : $t('protection.unlockVar')) : $t('protection.lockVar')}
                  aria-label={variable.isProtected ? $t('protection.unlockVar') : $t('protection.lockVar')}
                >
                  <svg class="w-3.5 h-3.5" fill="{variable.isProtected ? 'currentColor' : 'none'}" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    {#if variable.isProtected}
                      <path stroke-linecap="round" stroke-linejoin="round" d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                    {:else}
                      <path stroke-linecap="round" stroke-linejoin="round" d="M8 11V7a4 4 0 018 0v4M5 9h14a1 1 0 011 1v8a1 1 0 01-1 1H5a1 1 0 01-1-1v-8a1 1 0 011-1z" />
                    {/if}
                  </svg>
                </button>
                                <div class="relative inline-flex group">
                  <button
                  on:click={() => handleNote(variable.name)}
                  on:focus={() => ensureNotesLoaded()}
                  on:mouseenter={() => { hoveredNoteVar = variable.name; ensureNotesLoaded() }}
                  on:mouseleave={() => hoveredNoteVar = null}
                  class="inline-flex p-1 text-gray-400 hover:text-amber-600 hover:bg-amber-50 rounded transition dark:hover:text-amber-400 dark:hover:bg-amber-900/30"
                  title={getNoteSync(variable.name) ? $t('notes.editNote') : $t('notes.addNote')}
                >
                  {#if getNoteSync(variable.name)}
                    <svg class="w-3.5 h-3.5" fill="currentColor" viewBox="0 0 20 20">
                      <path d="M5 3a2 2 0 00-2 2v10a2 2 0 002 2h6a2 2 0 002-2V5a2 2 0 00-2-2H5z" />
                      <path d="M15 7v8a2 2 0 01-2 2H7v1a2 2 0 002 2h6a2 2 0 002-2V9a2 2 0 00-2-2h-1z" />
                    </svg>
                  {:else}
                    <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M9 12h6m-6 4h6m2 5H7a2 2 0 01-2-2V5a2 2 0 012-2h5.586a1 1 0 01.707.293l5.414 5.414a1 1 0 01.293.707V19a2 2 0 01-2 2z" />
                    </svg>
                  {/if}
                </button>
                  {#if hoveredNoteVar === variable.name && getNoteSync(variable.name)}
                    <div class="absolute bottom-full left-0 mb-1 px-2.5 py-1.5 bg-gray-800 text-white text-[10px] rounded shadow-lg whitespace-pre-wrap max-w-[240px] break-words z-50 pointer-events-none dark:bg-gray-700">
                      {getNoteSync(variable.name)?.note}
                    </div>
                  {/if}
                </div>
                <button
                  on:click={() => handleEdit(variable)}
                  disabled={$isWriteInProgress || togglingKeys[variable.name + ':' + variable.scope] === true || !!variable.isProtected}
                  class="inline-flex p-1 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded transition disabled:opacity-30 disabled:cursor-not-allowed dark:hover:text-blue-400 dark:hover:bg-blue-900/30"
                  title={variable.isProtected ? $t('protection.lockedCannotEdit') : $t('buttons.edit')}
                  aria-label={$t('buttons.edit')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                  </svg>
                </button>
                <button
                  on:click={() => handleDelete(variable.name, variable.scope)}
                  disabled={$isWriteInProgress || togglingKeys[variable.name + ':' + variable.scope] === true || !!variable.isProtected}
                  class="inline-flex p-1 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition disabled:opacity-30 disabled:cursor-not-allowed dark:hover:text-red-400 dark:hover:bg-red-900/30"
                  title={variable.isProtected ? $t('protection.lockedCannotDelete') : $t('buttons.delete')}
                  aria-label={$t('buttons.delete')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </div>
</div>

{#if showEditDialog}
  <EditDialog
    variable={editingVar}
    on:close={handleCloseEdit}
    on:save={() => handleCloseEdit()}
  />
{/if}

{#if showBackupDialog}
  <BackupDialog on:close={handleCloseBackup} />
{/if}
