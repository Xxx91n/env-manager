<script lang="ts">
  import { onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { showModal } from '../stores'
  import {
    listPathEntries,
    addPathEntry,
    removePathEntry,
    movePathEntryUp,
    movePathEntryDown,
  } from '../api'
  import type { PathEntry } from '../api'

  let entries: PathEntry[] = []
  let scope: 'user' | 'system' = 'user'
  let loading = false
  let actionLoading = false
  let newEntry = ''
  let message = ''
  let messageType = ''

  onMount(async () => {
    await refresh()
  })

  async function refresh() {
    loading = true
    try {
      entries = await listPathEntries(scope)
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      loading = false
    }
  }

  function showMessage(msg: string, type: string) {
    message = msg
    messageType = type
    setTimeout(() => { message = ''; messageType = '' }, 3000)
  }

  async function handleScopeChange() {
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
        } finally {
          actionLoading = false
        }
      }
    })
  }

  async function handleMoveUp(index: number) {
    actionLoading = true
    try {
      await movePathEntryUp(index, scope)
      await refresh()
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleMoveDown(index: number) {
    actionLoading = true
    try {
      await movePathEntryDown(index, scope)
      await refresh()
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }
</script>

<div class="space-y-3">
  {#if message}
    <div class="p-2.5 rounded-md text-xs {messageType === 'success'
      ? 'bg-green-50 border border-green-200 text-green-800 dark:bg-green-900/30 dark:border-green-700 dark:text-green-300'
      : 'bg-red-50 border border-red-200 text-red-800 dark:bg-red-900/30 dark:border-red-700 dark:text-red-300'}">
      {message}
    </div>
  {/if}

  <div class="flex items-center gap-2">
    <label for="path-scope" class="text-xs font-medium text-gray-600 dark:text-gray-400">{$t('path.scope')}</label>
    <select
      id="path-scope"
      bind:value={scope}
      on:change={handleScopeChange}
      class="px-2.5 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
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
      class="flex-1 px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 font-mono dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
    />
    <button
      on:click={handleAdd}
      disabled={actionLoading || !newEntry.trim()}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
    >
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
        <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" />
      </svg>
      {$t('path.addEntry')}
    </button>
  </div>

  {#if loading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-5 w-5 border-b-2 border-blue-600"></div>
    </div>
  {:else if entries.length === 0}
    <div class="px-4 py-8 text-center text-gray-400 text-xs dark:text-gray-500">
      {$t('path.empty')}
    </div>
  {:else}
    <div class="overflow-x-auto bg-white rounded-md border border-gray-200 dark:bg-gray-800 dark:border-gray-700">
      <table class="w-full">
        <thead class="bg-gray-50 border-b border-gray-200 dark:bg-gray-750 dark:border-gray-600">
          <tr>
            <th class="px-2 py-1.5 text-left text-[10px] font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-8">#</th>
            <th class="px-2 py-1.5 text-left text-[10px] font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">{$t('table.value')}</th>
            <th class="px-2 py-1.5 text-right text-[10px] font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-20">{$t('table.actions')}</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each entries as entry (entry.index)}
            <tr class="hover:bg-gray-50 transition dark:hover:bg-gray-750">
              <td class="px-2 py-1.5 text-[10px] text-gray-400 dark:text-gray-500">{entry.index}</td>
              <td class="px-2 py-1.5 text-[10px] font-mono text-gray-700 dark:text-gray-300 break-all">{entry.path}</td>
              <td class="px-2 py-1.5 text-right">
                <button
                  on:click={() => handleMoveUp(entry.index)}
                  disabled={actionLoading || entry.index === 0}
                  class="inline-flex p-1 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded transition disabled:opacity-30 dark:hover:text-blue-400 dark:hover:bg-blue-900/30"
                  title={$t('path.moveUp')}
                  aria-label={$t('path.moveUp')}
                >
                  <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M5 15l7-7 7 7" />
                  </svg>
                </button>
                <button
                  on:click={() => handleMoveDown(entry.index)}
                  disabled={actionLoading || entry.index === entries.length - 1}
                  class="inline-flex p-1 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded transition disabled:opacity-30 dark:hover:text-blue-400 dark:hover:bg-blue-900/30"
                  title={$t('path.moveDown')}
                  aria-label={$t('path.moveDown')}
                >
                  <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M19 9l-7 7-7-7" />
                  </svg>
                </button>
                <button
                  on:click={() => handleRemove(entry.path)}
                  disabled={actionLoading}
                  class="inline-flex p-1 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition dark:hover:text-red-400 dark:hover:bg-red-900/30"
                  title={$t('path.removeEntry')}
                  aria-label={$t('path.removeEntry')}
                >
                  <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                  </svg>
                </button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>
