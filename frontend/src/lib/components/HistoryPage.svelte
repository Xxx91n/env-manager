<script lang="ts">
  import { onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { bulkExport, bulkImport, listHistory, undoHistory } from '../api'
  import type { AuditEntry } from '../api'
  import { showModal, showToast } from '../stores'
  import { open, save } from '@tauri-apps/plugin-dialog'

  let history: AuditEntry[] = []
  let loading = false
  let scope: 'user' | 'system' = 'user'

  onMount(refresh)

  async function refresh() {
    loading = true
    try {
      history = await listHistory()
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
    try {
      const preview = await bulkImport(file, scope, false, true) as { conflicts?: unknown[]; count?: number }
      const conflicts = preview.conflicts?.length ?? 0
      showModal({
        title: $t('bulk.import'),
        message: $t('bulk.importConfirm', { values: { count: preview.count ?? 0, conflicts } }),
        confirmLabel: $t('bulk.import'),
        cancelLabel: $t('buttons.cancel'),
        variant: conflicts > 0 ? 'warning' : 'info',
        onConfirm: async () => {
          try {
            await bulkImport(file, scope, conflicts > 0, false)
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
    const file = await save({
      title: $t('bulk.export'),
      defaultPath: `environment-${scope}.env`,
      filters: [{ name: $t('bulk.formats'), extensions: ['json', 'env', 'csv'] }],
    })
    if (!file) return
    try {
      await bulkExport(file, scope)
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
</script>

<div class="space-y-3">
  <div class="flex items-center gap-2">
    <select bind:value={scope} class="px-2.5 py-1.5 text-xs border border-gray-300 rounded-md bg-white dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100">
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>
    <button on:click={handleImport} class="px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700">{$t('bulk.import')}</button>
    <button on:click={handleExport} class="px-3 py-1.5 text-xs font-medium border border-gray-300 rounded-md hover:bg-gray-50 dark:border-gray-600 dark:hover:bg-gray-700">{$t('bulk.export')}</button>
    <button on:click={refresh} class="ml-auto px-3 py-1.5 text-xs text-gray-600 hover:bg-gray-100 rounded-md dark:text-gray-300 dark:hover:bg-gray-700">{$t('buttons.refresh')}</button>
  </div>

  <div class="bg-white border border-gray-200 rounded-md overflow-hidden dark:bg-gray-800 dark:border-gray-700">
    {#if loading}
      <div class="p-8 text-center text-xs text-gray-400">{$t('messages.loading')}</div>
    {:else if history.length === 0}
      <div class="p-8 text-center text-xs text-gray-400">{$t('history.empty')}</div>
    {:else}
      <table class="w-full table-fixed">
        <thead class="bg-gray-50 border-b border-gray-200 dark:bg-gray-750 dark:border-gray-700">
          <tr>
            <th class="w-36 px-3 py-2 text-left text-[10px] text-gray-500">{$t('history.time')}</th>
            <th class="w-24 px-3 py-2 text-left text-[10px] text-gray-500">{$t('history.action')}</th>
            <th class="w-40 px-3 py-2 text-left text-[10px] text-gray-500">{$t('table.name')}</th>
            <th class="px-3 py-2 text-left text-[10px] text-gray-500">{$t('history.change')}</th>
            <th class="w-16 px-3 py-2"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each history as entry (entry.id)}
            <tr>
              <td class="px-3 py-2 text-[10px] text-gray-500">{new Date(entry.timestamp).toLocaleString()}</td>
              <td class="px-3 py-2 text-[10px] font-mono truncate" title={entry.command}>{entry.command.split(' ')[0]}</td>
              <td class="px-3 py-2 text-xs font-mono truncate" title={entry.name}>{entry.name}</td>
              <td class="px-3 py-2 text-[10px] font-mono text-gray-500 truncate" title={`${entry.oldValue ?? '∅'} -> ${entry.newValue ?? '∅'}`}>{entry.oldValue ?? '∅'} -> {entry.newValue ?? '∅'}</td>
              <td class="px-3 py-2 text-right"><button on:click={() => handleUndo(entry)} class="text-[10px] text-blue-600 hover:underline">{$t('history.undo')}</button></td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </div>
</div>
