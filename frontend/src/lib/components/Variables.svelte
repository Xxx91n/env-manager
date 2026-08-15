<script lang="ts">
  import { Search, Plus, Power, Tag, FileText, Pencil, Trash2, Archive } from 'lucide-svelte'
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
          <Search class="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-muted-foreground" />
      <input
        type="text"
        placeholder={$t('messages.searchPlaceholder')}
        bind:value={$search}
        class="w-full pl-8 pr-3 py-1.5 text-xs border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary bg-card border-border/80 text-foreground"
      />
    </div>

    <select
      bind:value={$selectedScope}
      class="px-2.5 py-1.5 text-xs border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary bg-card border-border/80 text-foreground"
    >
      <option value="all">{$t('table.scope')}</option>
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>

    <button
      on:click={() => (showEditDialog = true)}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-primary-foreground bg-primary rounded-md hover:bg-primary transition bg-primary/80"
    >
          <Plus class="w-3.5 h-3.5" />
      {$t('buttons.add')}
    </button>

    <button
      on:click={handleShowBackup}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-foreground/80 bg-card border border-border rounded-md hover:bg-muted/50 transition text-foreground border-border/80 hover:bg-accent"
    >
      <Archive class="w-3.5 h-3.5" />
      {$t('buttons.backup')}
    </button>
  </div>

  <div class="overflow-x-auto bg-card rounded-md border border-border">
    {#if filteredVars.length === 0}
      <div class="px-4 py-8 text-center text-muted-foreground text-xs">
        {$t('messages.noData')}
      </div>
    {:else}
      <table class="w-full">
        <thead class="bg-muted/50 border-b border-border bg-muted border-border/80">
          <tr>
            <th class="px-3 py-2 text-left text-xs font-medium text-muted-foreground uppercase tracking-wide w-12">
              {$t('table.enabled')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-muted-foreground uppercase tracking-wide">
              {$t('table.name')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-muted-foreground uppercase tracking-wide w-20">
              {$t('table.scope')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-muted-foreground uppercase tracking-wide">
              {$t('table.value')}
            </th>
            <th class="px-3 py-2 text-right text-xs font-medium text-muted-foreground uppercase tracking-wide w-24">
              {$t('table.actions')}
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 divide-border">
          {#each filteredVars as variable (variable.name + variable.scope)}
            <tr class="hover:bg-muted/50 transition hover:bg-muted {variable.isDisabled ? 'opacity-50' : ''} {variable.isProtected ? 'bg-muted/60 bg-card/60' : ''}">
              <td class="px-3 py-2">
                <button
                  on:click={() => handleToggle(variable.name, variable.scope)}
                  disabled={$isWriteInProgress || togglingKeys[variable.name + ':' + variable.scope] === true || !!variable.isProtected || !!variable.isBuiltinProtected}
                  class="relative inline-flex h-4 w-7 items-center rounded-full transition disabled:opacity-30 disabled:cursor-not-allowed {variable.isDisabled ? 'bg-border bg-muted' : 'bg-primary bg-primary/80'}"
                  role="switch"
                  aria-checked={!variable.isDisabled}
                  title={variable.isProtected ? $t('protection.lockedCannotToggle') : variable.isDisabled ? $t('messages.clickToEnable') : $t('messages.clickToDisable')}
                >
                  <span
                    class="inline-block h-3 w-3 transform rounded-full bg-card shadow transition {variable.isDisabled ? 'translate-x-0.5' : 'translate-x-3.5'}"
                  ></span>
                </button>
              </td>
              <td
                class="px-3 py-2 text-xs font-mono text-foreground cursor-pointer hover:text-primary transition select-none"
                title={$t('messages.clickToCopy')}
                on:click={() => copyToClipboard(variable.name)}
              >
                <div class="flex items-center gap-1.5">
                  <span>{#each highlightParts(variable.name, $debouncedSearch) as part}<span class={part.match ? 'bg-yellow-200 text-foreground bg-primary/40 text-primary-foreground' : ''}>{part.text}</span>{/each}</span>
                  {#if variable.profileSource}
                    <span
                      class="inline-flex px-1 py-0.5 rounded text-[9px] font-medium bg-purple-50 text-purple-700 bg-primary/15 text-primary"
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
                    ? 'bg-blue-50 text-primary bg-primary/20/40 text-primary'
                    : 'bg-primary/10 text-amber-700 bg-primary/15 text-primary/80'}"
                >
                  {variable.scope === 'user' ? $t('scope.user') : $t('scope.system')}
                </span>
              </td>
              <td
                class="px-3 py-2 text-xs text-muted-foreground font-mono text-foreground/80 max-w-md truncate cursor-pointer hover:text-primary transition select-none"
                title={previewTitle(variable)}
                on:mouseenter={() => scheduleExpandedPreview(variable)}
                on:click={() => copyToClipboard(variable.value)}
              >
                {#each highlightParts(variable.value, $debouncedSearch) as part}<span class={part.match ? 'bg-yellow-200 text-foreground bg-primary/40 text-primary-foreground' : ''}>{part.text}</span>{/each}
                {#if expandedValues[variable.scope + ':' + variable.name] && expandedValues[variable.scope + ':' + variable.name] !== variable.value}
                  <div class="mt-0.5 text-[9px] text-muted-foreground truncate" title={expandedValues[variable.scope + ':' + variable.name]}>
                    -> {expandedValues[variable.scope + ':' + variable.name]}
                  </div>
                {/if}
              </td>
              <td class="px-3 py-2 text-right text-xs">
                <button
                  on:click={() => handleLockToggle(variable.name, !!variable.isProtected, !!variable.isBuiltinProtected)}
                  disabled={!!variable.isBuiltinProtected}
                  class="inline-flex p-1 {variable.isProtected ? 'text-amber-600' : 'text-muted-foreground hover:text-primary/80 hover:bg-amber-50/60'} rounded transition disabled:opacity-30 disabled:cursor-not-allowed hover:bg-primary/15"
                  title={variable.isProtected ? (variable.isBuiltinProtected ? $t('protection.lockedBuiltin') : $t('protection.unlockVar')) : $t('protection.lockVar')}
                  aria-label={variable.isProtected ? $t('protection.unlockVar') : $t('protection.lockVar')}
                >
          <Power class="w-3.5 h-3.5" />
                </button>
                                <div class="relative inline-flex group">
                  <button
                  on:click={() => handleNote(variable.name)}
                  on:focus={() => ensureNotesLoaded()}
                  on:mouseenter={() => { hoveredNoteVar = variable.name; ensureNotesLoaded() }}
                  on:mouseleave={() => hoveredNoteVar = null}
                  class="inline-flex p-1 text-muted-foreground hover:text-primary/80 hover:bg-primary/10 rounded transition hover:bg-primary/15"
                  title={getNoteSync(variable.name) ? $t('notes.editNote') : $t('notes.addNote')}
                >
                  {#if getNoteSync(variable.name)}
          <Tag class="w-3.5 h-3.5" />
                  {:else}
          <FileText class="w-3.5 h-3.5" />
                  {/if}
                </button>
                  {#if hoveredNoteVar === variable.name && getNoteSync(variable.name)}
                    <div class="absolute bottom-full left-0 mb-1 px-2.5 py-1.5 bg-card text-primary-foreground text-[10px] rounded shadow-lg whitespace-pre-wrap max-w-[240px] break-words z-50 pointer-events-none bg-accent">
                      {getNoteSync(variable.name)?.note}
                    </div>
                  {/if}
                </div>
                <button
                  on:click={() => handleEdit(variable)}
                  disabled={$isWriteInProgress || togglingKeys[variable.name + ':' + variable.scope] === true || !!variable.isProtected}
                  class="inline-flex p-1 text-muted-foreground hover:text-primary hover:bg-primary/10 rounded transition disabled:opacity-30 disabled:cursor-not-allowed hover:bg-primary/15"
                  title={variable.isProtected ? $t('protection.lockedCannotEdit') : $t('buttons.edit')}
                  aria-label={$t('buttons.edit')}
                >
          <Pencil class="w-3.5 h-3.5" />
                </button>
                <button
                  on:click={() => handleDelete(variable.name, variable.scope)}
                  disabled={$isWriteInProgress || togglingKeys[variable.name + ':' + variable.scope] === true || !!variable.isProtected}
                  class="inline-flex p-1 text-muted-foreground hover:text-destructive hover:bg-destructive/10 rounded transition disabled:opacity-30 disabled:cursor-not-allowed hover:bg-destructive/15"
                  title={variable.isProtected ? $t('protection.lockedCannotDelete') : $t('buttons.delete')}
                  aria-label={$t('buttons.delete')}
                >
          <Trash2 class="w-3.5 h-3.5" />
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
