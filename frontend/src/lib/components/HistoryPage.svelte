<script lang="ts">
  import { onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { bulkExport, bulkImport, listHistory, undoHistory, deleteHistory, clearHistory } from '../api'
  import type { AuditEntry } from '../api'
  import { showModal, showToast } from '../stores'
  import { open, save } from '@tauri-apps/plugin-dialog'

  let allHistory: AuditEntry[] = []
  let loading = false
  let scope: 'user' | 'system' | 'profile' | 'all' = 'all'

  // i18n helper: map audit command string to localized operation name
  function getOperationLabel(command: string, tFn: (key: string) => string): string {
    // v0.7.5: try the full command first (e.g. 'path add', 'history undo')
    // and fall back to the leading word if the full key is missing.
    // Previous code sliced entry.command split(' ')[0] upstream and lost the
    // subcommand; this restores 'path add'/'path remove'/'history undo' as
    // distinct labels instead of all collapsing to 'path' / 'history'.
    //
    // The translation function is passed explicitly ($t) so that Svelte's
    // reactive dependency tracker sees $t referenced in the template call
    // site and re-renders the cell when the locale changes. Without this,
    // $t inside a function body is NOT tracked by Svelte's static reactivity
    // analyzer, so the action column never updated on locale switch.
    const fullKey = 'history.op.' + command
    const fullTranslated = tFn(fullKey)
    if (fullTranslated !== fullKey) return fullTranslated
    const head = command.split(' ')[0]
    const headKey = 'history.op.' + head
    const headTranslated = tFn(headKey)
    if (headTranslated !== headKey) return headTranslated
    return command
  }

  // Derived: filter history by selected scope
  $: filteredHistory = scope === 'all' ? allHistory : allHistory.filter(e => e.scope === scope)

  onMount(() => {
    loadColWidths()
    refresh()
  })

  async function refresh() {
    loading = true
    try {
      allHistory = await listHistory()
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      loading = false
    }
  }

  async function handleImport() {
    const file = await open({
      title: $t('bulk.import'),
      multiple: false,
      filters: [{ name: $t('bulk.formats'), extensions: ['json', 'env', 'csv'] }],
    })
    if (typeof file !== 'string') return
    const importScope = scope === 'all' ? 'user' : scope
    try {
      const preview = await bulkImport(file, importScope as 'user' | 'system', false, true) as { conflicts?: unknown[]; count?: number }
      const conflicts = preview.conflicts?.length ?? 0
      showModal({
        title: $t('bulk.import'),
        message: $t('bulk.importConfirm', { values: { count: preview.count ?? 0, conflicts } }),
        confirmLabel: $t('bulk.import'),
        cancelLabel: $t('buttons.cancel'),
        variant: conflicts > 0 ? 'warning' : 'info',
        onConfirm: async () => {
          try {
            await bulkImport(file, importScope as 'user' | 'system', conflicts > 0, false)
            await refresh()
            showToast($t('bulk.imported'), 'success')
          } catch (err) {
            showToast(err instanceof Error ? err.message : String(err), 'error')
          }
        },
      })
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  async function handleExport() {
    const exportScope = scope === 'all' ? 'user' : scope
    const file = await save({
      title: $t('bulk.export'),
      defaultPath: `environment-${exportScope}.env`,
      filters: [{ name: $t('bulk.formats'), extensions: ['json', 'env', 'csv'] }],
    })
    if (!file) return
    try {
      await bulkExport(file, exportScope as 'user' | 'system')
      showToast($t('bulk.exported'), 'success')
    } catch (err) {
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  function handleUndo(entry: AuditEntry) {
    showModal({
      title: $t('history.undo'),
      message: $t('history.undoConfirm', { values: { name: entry.name } }),
      confirmLabel: $t('history.undo'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'warning',
      onConfirm: async () => {
        try {
          await undoHistory(entry.id)
          await refresh()
          showToast($t('history.undone'), 'success')
        } catch (err) {
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }

  function handleDelete(entry: AuditEntry) {
    showModal({
      title: $t('history.delete'),
      message: $t('history.deleteConfirm', { values: { name: entry.name } }),
      confirmLabel: $t('buttons.delete'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        try {
          await deleteHistory(entry.id)
          await refresh()
          showToast($t('history.deleted'), 'success')
        } catch (err) {
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }

  function handleClearAll() {
    showModal({
      title: $t('history.clearAll'),
      message: $t('history.clearAllConfirm'),
      confirmLabel: $t('buttons.delete'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        try {
          await clearHistory(scope)
          await refresh()
          showToast($t('history.cleared'), 'success')
        } catch (err) {
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }

  // Column resize: drag the column header border to change width. Persisted to
  // localStorage so the user's preference survives across sessions.
  let colWidths: Record<string, number> = {}
  const COL_STORAGE_KEY = 'history-col-widths'
  const COL_DEFAULTS: Record<string, number> = { time: 144, action: 120, scope: 96, name: 144, change: 240, ops: 96 }

  function loadColWidths() {
    try {
      const stored = JSON.parse(localStorage.getItem(COL_STORAGE_KEY) || '{}')
      colWidths = { ...COL_DEFAULTS, ...stored }
    } catch {
      colWidths = { ...COL_DEFAULTS }
    }
    applyColWidths()
  }

  function applyColWidths() {
    const root = document.querySelector('.history-table-root') as HTMLElement | null
    if (!root) return
    for (const [k, v] of Object.entries(colWidths)) {
      root.style.setProperty(`--col-${k}`, `${v}px`)
    }
  }

  let resizing: { col: string; startX: number; startW: number } | null = null

  // Cache the table root element to avoid querySelector on every drag frame.
  let resizeRoot: HTMLElement | null = null

  function startResize(e: MouseEvent) {
    const th = e.currentTarget as HTMLElement
    const col = th.dataset.col
    if (!col) return
    th.classList.add('col-resizing')
    resizeRoot = document.querySelector('.history-table-root') as HTMLElement | null
    resizeRoot?.classList.add('col-resizing-active')
    resizing = { col, startX: e.clientX, startW: colWidths[col] ?? COL_DEFAULTS[col] ?? 120 }
    document.body.classList.add('select-none', 'cursor-col-resize')
    window.addEventListener('mousemove', onResizeMove)
    window.addEventListener('mouseup', endResize)
    e.preventDefault()
  }

  // rAF-batched resize: mousemove only computes the new width and schedules
  // a single DOM write per animation frame. This avoids layout thrashing
  // when the browser fires mousemove events faster than 60fps. The
  // `pendingFrame` flag prevents redundant rAF scheduling.
  let pendingFrame = false
  let pendingWidth = 0

  function onResizeMove(e: MouseEvent) {
    if (!resizing) return
    const dx = e.clientX - resizing.startX
    pendingWidth = Math.max(60, Math.min(800, resizing.startW + dx))
    if (!pendingFrame) {
      pendingFrame = true
      requestAnimationFrame(() => {
        if (resizing && resizeRoot) {
          // Only update CSS variable — do NOT update colWidths (Svelte reactive)
          // during drag. Updating the reactive store triggers {#each} re-evaluation
          // on every frame, which is the root cause of column resize jank.
          resizeRoot.style.setProperty('--col-' + resizing.col, pendingWidth + 'px')
        }
        pendingFrame = false
      })
    }
  }

  function endResize() {
    if (resizing) {
      colWidths[resizing.col] = pendingWidth
      try { localStorage.setItem(COL_STORAGE_KEY, JSON.stringify(colWidths)) } catch {}
    }
    resizeRoot?.querySelectorAll('.col-resizing').forEach(el => el.classList.remove('col-resizing'))
    resizeRoot?.classList.remove('col-resizing-active')
   resizing = null
   document.body.classList.remove('select-none', 'cursor-col-resize')
   window.removeEventListener('mousemove', onResizeMove)
   window.removeEventListener('mouseup', endResize)
    resizeRoot = null
 }
</script>

<style>
 .history-table-root { max-height: 70vh; }
 /* table-layout:fixed prevents the browser from recalculating ALL column
    widths on every cell width change — only the changed column reflows.
    Combined with rAF-batched writes, this gives smooth 60fps drag. */
 .history-table-root table { table-layout: fixed; }
 .history-table-root th.resize { position: relative; }
  .history-table-root th.resize::after {
    content: '';
    position: absolute;
    top: 0;
    right: 0;
    width: 6px;
    /* Extend left edge so the hit area starts inside this th, not in the next one */
    margin-left: -2px;
    height: 100%;
    cursor: col-resize;
    background: transparent;
    transition: background 0.15s;
    pointer-events: auto;
    z-index: 2;
  }
  /* Only the th currently being dragged shows the blue highlight.
     During active resize the neighbours' hover highlight is suppressed
     so the blue bar doesn't appear to jump to the next column when
     the pointer crosses the boundary while dragging right. */
  .history-table-root th.resize:hover::after {
    background: rgba(59, 130, 246, 0.4);
  }
  .history-table-root th.resize.col-resizing::after {
    background: rgba(59, 130, 246, 0.7);
  }
  .history-table-root.col-resizing-active th.resize:hover::after {
    background: transparent;
  }
</style>
<div class="space-y-3">
  <div class="flex items-center gap-2">
    <select bind:value={scope} on:change={refresh} class="px-2.5 py-1.5 text-xs border border-gray-300 rounded-md bg-white dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100">
      <option value="all">{$t('scope.all')}</option>
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
      <option value="profile">{$t('scope.profile')}</option>
    </select>
    <button on:click={handleImport} class="px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700">{$t('bulk.import')}</button>
    <button on:click={handleExport} class="px-3 py-1.5 text-xs font-medium border border-gray-300 rounded-md hover:bg-gray-50 dark:border-gray-600 dark:hover:bg-gray-700">{$t('bulk.export')}</button>
    {#if filteredHistory.length > 0}
      <button on:click={handleClearAll} class="px-3 py-1.5 text-xs font-medium text-red-600 border border-red-300 rounded-md hover:bg-red-50 dark:border-red-700 dark:hover:bg-red-900/30">{$t('history.clearAll')}</button>
    {/if}
    <button on:click={refresh} class="ml-auto px-3 py-1.5 text-xs text-gray-600 hover:bg-gray-100 rounded-md dark:text-gray-300 dark:hover:bg-gray-700">{$t('buttons.refresh')}</button>
  </div>

  <div class="history-table-root bg-white border border-gray-200 rounded-md overflow-auto dark:bg-gray-800 dark:border-gray-700">
    {#if loading}
      <div class="p-8 text-center text-xs text-gray-400">{$t('messages.loading')}</div>
    {:else if filteredHistory.length === 0}
      <div class="p-8 text-center text-xs text-gray-400">{$t('history.empty')}</div>
    {:else}
      <table class="w-full table-fixed">
        <colgroup>
          <col style="width: var(--col-time, 144px);">
          <col style="width: var(--col-action, 120px);">
          <col style="width: var(--col-scope, 96px);">
          <col style="width: var(--col-name, 144px);">
          <col style="width: var(--col-change, 240px);">
          <col style="width: var(--col-ops, 96px);">
        </colgroup>
        <thead class="bg-gray-50 border-b border-gray-200 dark:bg-gray-750 dark:border-gray-700">
          <tr>
            <th data-col="time" on:mousedown={startResize} class="resize px-3 py-2 text-left text-[10px] text-gray-500 select-none cursor-col-resize">{$t('history.time')}</th>
            <th data-col="action" on:mousedown={startResize} class="resize px-3 py-2 text-left text-[10px] text-gray-500 select-none cursor-col-resize">{$t('history.action')}</th>
            <th data-col="scope" on:mousedown={startResize} class="resize px-3 py-2 text-left text-[10px] text-gray-500 select-none cursor-col-resize">{$t('scope.scope')}</th>
            <th data-col="name" on:mousedown={startResize} class="resize px-3 py-2 text-left text-[10px] text-gray-500 select-none cursor-col-resize">{$t('table.name')}</th>
            <th data-col="change" on:mousedown={startResize} class="resize px-3 py-2 text-left text-[10px] text-gray-500 select-none cursor-col-resize">{$t('history.change')}</th>
            <th class="px-3 py-2"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each filteredHistory as entry (entry.id)}
            <tr class="hover:bg-gray-50 dark:hover:bg-gray-750">
              <td class="px-3 py-2 text-[10px] text-gray-500">{new Date(entry.timestamp).toLocaleString()}</td>
              <td class="px-3 py-2 text-[10px] font-mono break-all whitespace-normal" title={entry.command}>{getOperationLabel(entry.command, $t)}</td>
              <td class="px-3 py-2 text-[10px]">
                {#if entry.scope === 'profile'}
                  <span class="px-1.5 py-0.5 rounded text-[9px] font-medium bg-amber-100 text-amber-800 dark:bg-amber-900/40 dark:text-amber-300">{$t('scope.profile')}</span>
                {:else}
                  <span class="px-1.5 py-0.5 rounded text-[9px] font-medium {entry.scope === 'user' ? 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300' : 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300'}">{entry.scope === 'user' ? $t('scope.user') : $t('scope.system')}</span>
                {/if}
              </td>
              <td class="px-3 py-2 text-xs font-mono truncate" title={entry.name}>{entry.name}</td>
              <td class="px-3 py-2 text-[10px] font-mono text-gray-500 truncate" title={`${entry.oldValue ?? 'null'} -> ${entry.newValue ?? 'null'}`}>{entry.oldValue ?? 'null'} -> {entry.newValue ?? 'null'}</td>
              <td class="px-3 py-2 text-right whitespace-nowrap">
                <button on:click={() => handleUndo(entry)} class="text-[10px] text-blue-600 hover:underline mr-2">{$t('history.undo')}</button>
                <button on:click={() => handleDelete(entry)} class="text-[10px] text-red-600 hover:underline">{$t('buttons.delete')}</button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </div>
</div>
