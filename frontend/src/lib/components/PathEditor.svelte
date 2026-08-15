<script lang="ts">
  import { Plus, Trash2, ShieldCheck, Check, X, Power, Pencil, ChevronUp, ChevronDown, Eye, Loader2 } from 'lucide-svelte'
  import { onMount } from 'svelte'
  import { frontendLog } from '../settingsStore'
  import { pathProfileIndex } from '../stores'
  import { t } from 'svelte-i18n'
  import { showModal, isWriteInProgress, refreshTrigger } from '../stores'
  import { showToast } from '../stores'
  import {
    listPathEntries,
    addPathEntry,
    removePathEntry,
    movePathEntryUp,
    movePathEntryDown,
    renamePathEntry,
    addProtectedPath,
    removeProtectedPath,
    dedupePathEntries,
    pathHealth,
  } from '../api'
  import type { PathEntry } from '../api'
  import type { PathHealthEntry } from '../api'

 let entries: PathEntry[] = []
  // v0.9.17: reverse index from ProfilePage — normalized path -> profile names
  let pathIdx: Map<string, string[]> = new Map()
  $: if (pathProfileIndex) { pathIdx = $pathProfileIndex }
 let scope: 'user' | 'system' = 'user'
 let loading = false
 let actionLoading = false
 let newEntry = ''

  // v0.7.12 staged-move state: matches the Windows PowerToys env-var editor
  // pattern where pressing up/down only reorders locally and an Apply button
  // commits all moves at once. This lets the user click up/down many times
  // rapidly without serial IPC per click (which was slow and felt laggy).
  // stagedEntries holds the in-flight visual order (with .index left intact,
  // so we still identify each entry by its original registry position);
  // stagedActive is true while the user is mid-edit; stagedApplyLoading gates
  // the confirm button. Cancel restores from `entries` (last committed snapshot).
  let stagedEntries: PathEntry[] = []
  let stagedActive = false
 let stagedApplyLoading = false
  // Visible order: when staged moves are active we show the in-flight order;
  // otherwise the last-committed one. The #each renders this, and move buttons
  // operate on the visual position `pos` (NOT entry.index, which is the
  // original registry index used as the stable key).
  $: displayEntries = stagedActive ? stagedEntries : entries
  // get $t-stable variant via store directly (Avoid reactive retriggers): N/A.

  // Inline rename state
  let editingIndex: number | null = null
  let editValue: string = ''
  let editError: string = ''
  let healthMap: Map<string, PathHealthEntry> = new Map()
  let healthLoading = false
  let healthSummary: { healthy: number; dead: number; duplicate: number } | null = null

  onMount(async () => {
    await refresh()
  })

  // Watch refreshTrigger from App.svelte: refresh path entries when the
  // header refresh button is clicked, regardless of active view.
  $: if ($refreshTrigger > 0) {
    refresh()
  }

  async function refresh() {
    loading = true
    healthMap = new Map()
    healthSummary = null
    try {
      entries = await listPathEntries(scope)
      // Drop any in-flight staged move when entries are re-loaded from the
      // registry (refresh / scope change / external mutation); the staged
      // order would otherwise drift relative to the new baseline.
      stagedEntries = []
      stagedActive = false
      stagedApplyLoading = false
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
      void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {})
    } finally {
      loading = false
      // Safety: clear actionLoading in case a prior action's finally was skipped
      actionLoading = false
    }
  }

  function showMessage(msg: string, type: string) {
    showToast(msg, type === 'success' ? 'success' : type === 'error' ? 'error' : 'info')
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

  async function handlePathLockToggle(path: string, isProtected: boolean, isBuiltin: boolean) {
    if (isBuiltin) {
      showToast($t('protection.cannotUnlockBuiltin'), 'error')
      return
    }
    try {
      if (isProtected) {
        await removeProtectedPath(path)
        showToast($t('protection.pathUnlocked'), 'success')
      } else {
        await addProtectedPath(path)
        showToast($t('protection.pathLocked'), 'success')
      }
      await refresh()
    } catch (err) { void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  async function handleScopeChange() {
    cancelEdit()
    await refresh()
  }

  async function handleAdd() {
    const dir = newEntry.trim()
    if (!dir) return
    actionLoading = true
    try {
      await addPathEntry(dir, scope)
      newEntry = ''
      await refresh()
      showMessage($t('messages.pathEntryAdded'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
      void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {})
    } finally {
      actionLoading = false
    }
  }

  async function handleHealthCheck(dryRun = false) {
    if (healthLoading) return
    healthLoading = true
    try {
      const result = await pathHealth(scope, false, dryRun)
      healthMap = new Map(result.results.map((r) => [r.entry, r]))
      healthSummary = {
        healthy: result.healthyCount,
        dead: result.deadCount,
        duplicate: result.duplicateCount,
      }
      if (dryRun) {
        const { showToast } = await import('../stores')
        showToast($t('messages.pathHealthPreview', { values: { dead: result.deadCount, dup: result.duplicateCount } }), 'info')
      }
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
      void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {})
    } finally {
      healthLoading = false
    }
  }

  function handleRemoveDeadConfirm() {
    if (actionLoading || healthLoading) return
    const deadCount = healthSummary?.dead ?? 0
    if (deadCount === 0) {
      showToast($t('messages.pathNoDead'), 'info')
      return
    }
    showModal({
      title: $t('path.removeDead'),
      message: $t('path.confirmRemoveDead', { values: { count: deadCount } }),
      confirmLabel: $t('buttons.confirm'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        actionLoading = true
        try {
          const result = await pathHealth(scope, true, false)
          healthMap = new Map(result.results.map((r) => [r.entry, r]))
          healthSummary = {
            healthy: result.healthyCount,
            dead: result.deadCount,
            duplicate: result.duplicateCount,
          }
          await refresh()
          showToast($t('messages.pathDeadRemoved', { values: { count: deadCount } }), 'success')
        } catch (err) { void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
          showToast(err instanceof Error ? err.message : String(err), 'error')
        } finally {
          actionLoading = false
        }
      },
    })
  }

  async function handleDedupe(dryRun = false) {
    if (actionLoading) return
    actionLoading = true
    try {
      const result = await dedupePathEntries(scope, dryRun)
      if (result.removedCount === 0) {
        showToast($t('messages.pathDedupeNone'), 'info')
      } else if (dryRun) {
        showToast($t('messages.pathDedupeDryRun', { values: { count: result.removedCount } }), 'info')
      } else {
        showToast($t('messages.pathDedupeResult', { values: { count: result.removedCount } }), 'success')
        await refresh()
      }
    } catch {
      // toast already raised by api layer
    } finally {
      actionLoading = false
    }
  }

  function handleRemove(path: string) {
    showModal({
      title: $t('path.confirmRemove'),
      message: $t('path.confirmRemove'),
      confirmLabel: $t('path.removeEntry'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        actionLoading = true
        try {
          await removePathEntry(path, scope)
          await refresh()
          showMessage($t('messages.pathEntryRemoved'), 'success')
        } catch (err) {
          showMessage(err instanceof Error ? err.message : String(err), 'error')
          void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {})
        } finally {
          actionLoading = false
        }
      }
    })
  }

  // v0.7.12 staged-move handlers. Local-only swap on `stagedEntries`; no IPC
  // per click. The Apply button runs `applyStagedMoves()` to commit the whole
  // ordered sequence to the registry in one guided pass. `pos` is the visual
  // position in displayEntries (0-based), not the registry index.
  function ensureStagedActive() {
    cancelEdit()
    if (!stagedActive) {
      stagedEntries = entries.slice()
      stagedActive = true
    }
  }

  function handleMoveUp(pos: number) {
    if (pos <= 0) return
    ensureStagedActive()
    const arr = stagedEntries
    if (arr[pos].isProtected) return
    const tmp = arr[pos - 1]
    arr[pos - 1] = arr[pos]
    arr[pos] = tmp
    stagedEntries = arr
    stagedEntries = stagedEntries // reactivity poke
  }

  function handleMoveDown(pos: number) {
    if (pos >= stagedEntries.length - 1) return
    ensureStagedActive()
    const arr = stagedEntries
    if (arr[pos].isProtected) return
    const tmp = arr[pos + 1]
    arr[pos + 1] = arr[pos]
    arr[pos] = tmp
    stagedEntries = arr
    stagedEntries = stagedEntries // reactivity poke
  }

  function cancelStagedMoves() {
    stagedEntries = []
    stagedActive = false
    stagedApplyLoading = false
  }

  // Commit the staged visual order to the registry using the minimum number
  // of move-up/move-down IPCs. For each target position i (left to right),
  // locate the live registry position of the entry whose original `.index`
  // equals stagedEntries[i].index, then move it left (move-up) until it sits
  // at position i. We re-localize after every move because each move-up/down
  // shifts the live indices of the entries it crosses. O(n^2) in PATH entry
  // count — fine since N is small (<200) in practice.
  async function applyStagedMoves() {
    if (!stagedActive || stagedApplyLoading) return
    stagedApplyLoading = true
    try {
      const target = stagedEntries
      // Re-read live order so we map original .index -> current real position
      // fresh; the registry may have drifted from the snapshot we staged on.
      let live = await listPathEntries(scope)
      for (let i = 0; i < target.length; i++) {
        const wantOrigIdx = target[i].index
        let realPos = live.findIndex((e) => e.index === wantOrigIdx)
        if (realPos < 0) {
          // Entry vanished from the registry mid-stage; abort to avoid a
          // poison sequence that silently reorders other entries.
          throw new Error(`PATH entry at original index ${wantOrigIdx} no longer exists; staged move aborted`)
        }
        while (realPos > i) {
          await movePathEntryUp(realPos, scope)
          realPos--
        }
        // If realPos < i it means a prior loop misordered; since we only ever
        // move entries left, that should not happen when target was built by
        // adjacent swaps. Refresh live state defensively.
        live = await listPathEntries(scope)
      }
      stagedActive = false
      stagedEntries = []
      await refresh()
      showMessage($t('messages.pathOrderApplied'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
      void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {})
      // On failure, keep staged order visible so the user can retry or cancel.
    } finally {
      stagedApplyLoading = false
    }
  }

  // --- Inline rename ---

  function startEdit(index: number, currentPath: string) {
    editingIndex = index
    editValue = currentPath
    editError = ''
  }

  function cancelEdit() {
    clearPendingBlur()
    editingIndex = null
    editValue = ''
    editError = ''
  }

  async function confirmEdit(oldPath: string) {
    clearPendingBlur()
    const newPath = editValue.trim()
    editError = ''

    // No change -> cancel
    if (newPath === oldPath) {
      cancelEdit()
      return
    }

    // Validate: not empty
    if (!newPath) {
      editError = $t('messages.pathRenameEmpty')
      return
    }

    // Validate: no null bytes or control chars (injection prevention)
    if (/[\0-\x08\x0B\x0C\x0E-\x1F]/.test(newPath)) {
      editError = $t('messages.pathRenameInvalid')
      return
    }

    // Validate: length limit
    if (newPath.length > 32767) {
      editError = $t('messages.pathRenameTooLong')
      return
    }

    actionLoading = true
    try {
      await renamePathEntry(oldPath, newPath, scope)
      editingIndex = null
      editValue = ''
      await refresh()
      showMessage($t('messages.pathRenamed'), 'success')
    } catch (err) { void frontendLog('error', '[PathEditor] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      editError = err instanceof Error ? err.message : String(err)
    } finally {
      actionLoading = false
    }
  }

  function handleEditKeydown(e: KeyboardEvent, oldPath: string) {
    if (e.key === 'Enter') {
      e.preventDefault()
      confirmEdit(oldPath)
    } else if (e.key === 'Escape') {
      e.preventDefault()
      cancelEdit()
    }
  }

  // Click outside to cancel edit: uses a single blur handler with a
  // micro-delay to let confirm/cancel button clicks register first.
  // The previous global click handler + blur combo caused race conditions.
  let blurTimeoutId: ReturnType<typeof setTimeout> | null = null

  function handleEditBlur(oldPath: string) {
    // Clear any pending blur timeout to prevent duplicate triggers
    if (blurTimeoutId) {
      clearTimeout(blurTimeoutId)
      blurTimeoutId = null
    }
    // Use a short delay so confirm/cancel button clicks register first
    blurTimeoutId = setTimeout(() => {
      if (editingIndex !== null && editValue.trim() === oldPath) {
        cancelEdit()
      }
      blurTimeoutId = null
    }, 150)
  }

  // Called when confirm/cancel buttons are clicked - clears the pending blur
  function clearPendingBlur() {
    if (blurTimeoutId) {
      clearTimeout(blurTimeoutId)
      blurTimeoutId = null
    }
  }
</script>

<div class="space-y-3">

  <div class="flex items-center gap-2">
    <label for="path-scope" class="text-xs font-medium text-muted-foreground">{$t('path.scope')}</label>
    <select
      id="path-scope"
      bind:value={scope}
      on:change={handleScopeChange}
      class="px-2.5 py-1.5 text-xs border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary bg-card bg-accent border-border/80 text-foreground"
    >
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>
  </div>

  <div class="flex gap-2">
    <input
      type="text"
      placeholder={$t('path.entryPlaceholder')}
      bind:value={newEntry}
      on:keydown={(e) => { if (e.key === 'Enter') handleAdd() }}
      class="flex-1 px-3 py-1.5 text-xs border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary font-mono bg-card border-border/80 text-foreground"
    />
    <button
      on:click={handleAdd}
      disabled={actionLoading || !newEntry.trim()}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-primary-foreground bg-primary rounded-md hover:bg-primary transition disabled:opacity-50 bg-primary/80"
    >
          <Plus class="w-3.5 h-3.5" />
      {$t('path.addEntry')}
    </button>
    <button
      on:click={() => handleDedupe(true)}
      disabled={actionLoading || entries.length === 0}
      title={$t('path.dedupeDryRun')}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-foreground/80 bg-muted/30 rounded-md hover:bg-muted transition disabled:opacity-50 bg-accent text-foreground"
    >
      <Eye class="w-3.5 h-3.5" />
      {$t('path.dedupeDryRun')}
    </button>
    <button
      on:click={() => handleDedupe(false)}
      disabled={actionLoading || entries.length === 0}
      title={$t('path.dedupe')}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-amber-700 bg-primary/10 rounded-md hover:bg-amber-100 transition disabled:opacity-50 text-primary/80 hover:bg-primary/15"
    >
          <Trash2 class="w-3.5 h-3.5" />
      {$t('path.dedupe')}
    </button>
    <button
      on:click={() => handleHealthCheck(true)}
      disabled={actionLoading || healthLoading || entries.length === 0}
      title={$t('path.healthCheck')}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-primary bg-blue-50 rounded-md hover:bg-primary/15 transition disabled:opacity-50 bg-primary/10"
    >
          <ShieldCheck class="w-3.5 h-3.5" />
      {$t('path.healthCheck')}
    </button>
    <button
      on:click={handleRemoveDeadConfirm}
      disabled={actionLoading || healthLoading || !healthSummary || healthSummary.dead === 0}
      title={$t('path.removeDead')}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-destructive bg-destructive/10 rounded-md hover:bg-destructive/15 transition disabled:opacity-30 disabled:cursor-not-allowed"
    >
          <Trash2 class="w-3.5 h-3.5" />
      {$t('path.removeDead')}{#if healthSummary && healthSummary.dead > 0} ({healthSummary.dead}){/if}
    </button>
  </div>

  {#if loading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-5 w-5 border-b-2 border-primary"></div>
    </div>
  {:else if entries.length === 0}
    <div class="px-4 py-8 text-center text-muted-foreground text-xs">
      {$t('path.empty')}
    </div>
 {:else}
    {#if stagedActive}
      <div class="flex items-center justify-between gap-2 px-3 py-2 mb-2 rounded-md bg-blue-50 border border-blue-200 bg-primary/10 border-primary">
        <span class="text-[11px] text-primary">{$t('path.stagedActive')}</span>
        <div class="flex items-center gap-2">
          <button
            on:click={applyStagedMoves}
            disabled={stagedApplyLoading}
            class="inline-flex items-center gap-1 px-2.5 py-1 text-[11px] font-medium text-primary-foreground bg-primary hover:bg-primary rounded-md transition disabled:opacity-50"
            title={$t('path.applyMoves')}
            aria-label={$t('path.applyMoves')}
          >
            {#if stagedApplyLoading}<Loader2 class="w-3 h-3 animate-spin" />{/if}
            {$t('path.applyMoves')}
          </button>
          <button
            on:click={cancelStagedMoves}
            disabled={stagedApplyLoading}
            class="inline-flex items-center px-2.5 py-1 text-[11px] text-muted-foreground hover:bg-muted/30 rounded-md transition disabled:opacity-50 text-foreground/80 hover:bg-accent"
            title={$t('buttons.cancel')}
            aria-label={$t('buttons.cancel')}
          >
            {$t('buttons.cancel')}
          </button>
        </div>
      </div>
    {/if}
    <div class="overflow-x-auto bg-card rounded-md border border-border">
      <table class="w-full">
        <thead class="bg-muted/50 border-b border-border bg-muted border-border/80">
          <tr>
            <th class="px-2 py-1.5 text-left text-[10px] font-medium text-muted-foreground uppercase tracking-wide w-8">#</th>
            <th class="px-2 py-1.5 text-left text-[10px] font-medium text-muted-foreground uppercase tracking-wide">{$t('table.value')}</th>
            <th class="px-2 py-1.5 text-right text-[10px] font-medium text-muted-foreground uppercase tracking-wide w-24">{$t('table.actions')}</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 divide-border">
          {#each displayEntries as entry, pos (entry.index)}
            <tr class="hover:bg-muted/50 transition hover:bg-muted {entry.isDuplicate ? 'bg-amber-50/60/60 bg-primary/5' : ''} {!entry.exists ? 'bg-red-50/60 bg-destructive/20/10' : ''} {entry.isProtected ? 'bg-muted/60 bg-card/60' : ''}">
              <td class="px-2 py-1.5 text-[10px] text-muted-foreground align-top">{entry.index}</td>
              <td class="px-2 py-1.5 align-top">
                {#if editingIndex === entry.index}
                  <!-- Inline edit mode -->
                  <div class="flex items-center gap-1 edit-row">
                    <input
                      type="text"
                      bind:value={editValue}
                      on:keydown={(e) => handleEditKeydown(e, entry.path)}
                      on:blur={() => handleEditBlur(entry.path)}
                      class="flex-1 px-2 py-1 text-[10px] font-mono border border-primary rounded-md focus:outline-none focus:ring-1 focus:ring-primary bg-background text-foreground"
                      spellcheck="false"
                    />
                    <button
                      on:click={() => confirmEdit(entry.path)}
                      disabled={actionLoading}
                      class="inline-flex p-1 text-primary hover:bg-primary/10 rounded transition disabled:opacity-50 hover:bg-primary/15"
                      title={$t('buttons.save')}
                      aria-label={$t('buttons.save')}
                    >
          <Check class="w-3 h-3" />
                    </button>
                    <button
                      on:click={cancelEdit}
                      disabled={actionLoading}
                      class="inline-flex p-1 text-muted-foreground hover:text-muted-foreground hover:bg-muted/30 rounded transition disabled:opacity-50 hover:bg-accent"
                      title={$t('buttons.cancel')}
                      aria-label={$t('buttons.cancel')}
                    >
          <X class="w-3 h-3" />
                    </button>
                  </div>
                  {#if editError}
                    <div class="mt-1 text-[10px] text-destructive">{editError}</div>
                  {/if}
                {:else}
                  <!-- Display mode: click to copy -->
                  <div
                    class="text-[11px] font-mono text-foreground/80 text-foreground break-all cursor-pointer hover:text-primary transition select-none leading-relaxed"
                    title={$t('messages.clickToCopy')}
                    on:click={() => copyToClipboard(entry.path)}
                  >
                    {entry.path}
                  </div>
                <div class="mt-0.5 flex items-center gap-1 flex-wrap text-[9px]">
                    {#if healthMap.has(entry.path)}
                      {@const h = healthMap.get(entry.path)}
                      {#if h.isDead && h.isDuplicate}
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-red-200 text-red-800 bg-destructive/20/60 text-destructive">{$t('path.dead')}+{$t('path.duplicate')}</span>
                      {:else if h.isDead && !h.isDuplicate}
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-red-100 text-destructive bg-destructive/20/40">{$t('path.dead')}</span>
                      {:else if !h.isDead && h.isDuplicate}
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-amber-100 text-amber-700 bg-primary/15 text-primary/80">{$t('path.duplicate')}</span>
                      {:else}
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-green-100 text-green-700 bg-primary/15 text-primary">{$t('path.healthy')}</span>
                      {/if}
                    {:else}
                      {#if !entry.exists}
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-red-100 text-destructive bg-destructive/20/40" title={$t('path.missing')}>{$t('path.dead')}</span>
                      {/if}
                      {#if entry.isDuplicate}
                        <span class="inline-flex items-center px-1.5 py-0.5 rounded bg-amber-100 text-amber-700 bg-primary/15 text-primary/80">{$t('path.duplicate')}</span>
                      {/if}
                    {/if}
                    {#if entry.expandedPath !== entry.path}<span class="font-mono text-muted-foreground truncate max-w-xs" title={entry.expandedPath}>{entry.expandedPath}</span>{/if}
                     {#if pathIdx.has(entry.path.toLowerCase().replace(/\\+$/, ""))}<span class="text-[9px] text-blue-500 text-primary" title={pathIdx.get(entry.path.toLowerCase().replace(/\\+$/, ""))?.join(', ')}>{$t('profiles.fromProfile')} {pathIdx.get(entry.path.toLowerCase().replace(/\\+$/, ""))?.join(', ')}</span>{/if}
                  </div>
                {/if}
              </td>
              <td class="px-2 py-1.5 text-right align-top">
                {#if editingIndex !== entry.index}
                  <!-- Lock button -->
                  <button
                    on:click={() => handlePathLockToggle(entry.path, !!entry.isProtected, !!entry.isBuiltinProtected)}
                    disabled={!!entry.isBuiltinProtected}
                    class="inline-flex p-1 {entry.isProtected ? 'text-amber-600' : 'text-muted-foreground hover:text-primary/80 hover:bg-amber-50/60'} rounded transition disabled:opacity-30 disabled:cursor-not-allowed hover:bg-primary/15"
                    title={entry.isProtected ? (entry.isBuiltinProtected ? $t('protection.lockedBuiltin') : $t('protection.unlockPath')) : $t('protection.lockPath')}
                    aria-label={entry.isProtected ? $t('protection.unlockPath') : $t('protection.lockPath')}
                  >
          <Power class="w-3 h-3" />
                  </button>
                  <!-- Rename button -->
                  <button
                    on:click={() => startEdit(entry.index, entry.path)}
                    disabled={actionLoading || !!entry.isProtected}
                    class="inline-flex p-1 text-muted-foreground hover:text-primary hover:bg-primary/10 rounded transition disabled:opacity-30 hover:bg-primary/15"
                    title={entry.isProtected ? $t('protection.lockedCannotEdit') : $t('path.rename')}
                    aria-label={$t('path.rename')}
                  >
          <Pencil class="w-3 h-3" />
                  </button>
                  <button
                    on:click={() => handleMoveUp(entry.index)}
                    disabled={actionLoading || pos === 0 || entry.isProtected}
                    class="inline-flex p-1 text-muted-foreground hover:text-primary hover:bg-primary/10 rounded transition disabled:opacity-30 hover:bg-primary/15"
                    title={$t('path.moveUp')}
                    aria-label={$t('path.moveUp')}
                  >
          <ChevronUp class="w-3 h-3" />
                  </button>
                  <button
                    on:click={() => handleMoveDown(entry.index)}
                    disabled={actionLoading || pos === displayEntries.length - 1 || entry.isProtected}
                    class="inline-flex p-1 text-muted-foreground hover:text-primary hover:bg-primary/10 rounded transition disabled:opacity-30 hover:bg-primary/15"
                    title={$t('path.moveDown')}
                    aria-label={$t('path.moveDown')}
                  >
          <ChevronDown class="w-3 h-3" />
                  </button>
                  <button
                    on:click={() => handleRemove(entry.path)}
                    disabled={actionLoading || entry.isProtected}
                    class="inline-flex p-1 text-muted-foreground hover:text-destructive hover:bg-destructive/10 rounded transition disabled:opacity-30 disabled:cursor-not-allowed hover:bg-destructive/15"
                    title={$t('path.removeEntry')}
                    aria-label={$t('path.removeEntry')}
                  >
          <X class="w-3 h-3" />
                  </button>
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>
