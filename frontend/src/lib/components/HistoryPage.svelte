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

  // Derived: filter history by selected scope
  $: filteredHistory = scope === 'all' ? allHistory : allHistory.filter(e => e.scope === scope)

  onMount(refresh)

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
</script>

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

  <div class="bg-white border border-gray-200 rounded-md overflow-hidden dark:bg-gray-800 dark:border-gray-700">
    {#if loading}
      <div class="p-8 text-center text-xs text-gray-400">{$t('messages.loading')}</div>
    {:else if filteredHistory.length === 0}
      <div class="p-8 text-center text-xs text-gray-400">{$t('history.empty')}</div>
    {:else}
      <table class="w-full table-fixed">
        <thead class="bg-gray-50 border-b border-gray-200 dark:bg-gray-750 dark:border-gray-700">
          <tr>
            <th class="w-36 px-3 py-2 text-left text-[10px] text-gray-500">{$t('history.time')}</th>
            <th class="w-20 px-3 py-2 text-left text-[10px] text-gray-500">{$t('history.action')}</th>
            <th class="w-16 px-3 py-2 text-left text-[10px] text-gray-500">{$t('scope.scope')}</th>
            <th class="w-36 px-3 py-2 text-left text-[10px] text-gray-500">{$t('table.name')}</th>
            <th class="px-3 py-2 text-left text-[10px] text-gray-500">{$t('history.change')}</th>
            <th class="w-24 px-3 py-2"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each filteredHistory as entry (entry.id)}
            <tr class="hover:bg-gray-50 dark:hover:bg-gray-750">
              <td class="px-3 py-2 text-[10px] text-gray-500">{new Date(entry.timestamp).toLocaleString()}</td>
              <td class="px-3 py-2 text-[10px] font-mono truncate" title={entry.command}>{entry.scope === 'profile' ? entry.command : entry.command.split(' ')[0]}</td>
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
