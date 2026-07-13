<script lang="ts">
  import { onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { showModal, isWriteInProgress, refreshTrigger } from '../stores'
  import {
    listPathEntries,
    addPathEntry,
    removePathEntry,
    movePathEntryUp,
    movePathEntryDown,
    renamePathEntry,
  } from '../api'
  import type { PathEntry } from '../api'

  let entries: PathEntry[] = []
  let scope: 'user' | 'system' = 'user'
  let loading = false
  let actionLoading = false
  let newEntry = ''
  let message = ''
  let messageType = ''
  let copyFeedback = ''

  // Inline rename state
  let editingIndex: number | null = null
  let editValue: string = ''
  let editError: string = ''

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
    try {
      entries = await listPathEntries(scope)
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      loading = false
      // Safety: clear actionLoading in case a prior action's finally was skipped
      actionLoading = false
    }
  }

  function showMessage(msg: string, type: string) {
    message = msg
    messageType = type
    setTimeout(() => { message = ''; messageType = '' }, 3000)
  }

  function copyToClipboard(text: string) {
    navigator.clipboard.writeText(text).then(() => {
      copyFeedback = $t('messages.copied')
      setTimeout(() => { copyFeedback = '' }, 1500)
    }).catch(() => {
      const textarea = document.createElement('textarea')
      textarea.value = text
      textarea.style.position = 'fixed'
      textarea.style.opacity = '0'
      document.body.appendChild(textarea)
      textarea.select()
      try {
        document.execCommand('copy')
        copyFeedback = $t('messages.copied')
        setTimeout(() => { copyFeedback = '' }, 1500)
      } catch { /* ignore */ }
      document.body.removeChild(textarea)
    })
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
    cancelEdit()
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
    cancelEdit()
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
    } catch (err) {
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

{#if copyFeedback}
  <div class="fixed top-4 left-1/2 -translate-x-1/2 px-3 py-1.5 bg-gray-800 text-white text-xs rounded-md shadow-lg z-50 pointer-events-none transition-opacity dark:bg-gray-700">
    {copyFeedback}
  </div>
{/if}

{#if message}
  <div class="fixed top-4 left-1/2 -translate-x-1/2 px-3 py-1.5 rounded-md text-xs shadow-lg z-50 pointer-events-none transition-opacity {messageType === 'success'
    ? 'bg-green-600 text-white'
    : 'bg-red-600 text-white'}">
    {message}
  </div>
{/if}

<div class="space-y-3">

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
            <th class="px-2 py-1.5 text-right text-[10px] font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-24">{$t('table.actions')}</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each entries as entry (entry.index)}
            <tr class="hover:bg-gray-50 transition dark:hover:bg-gray-750">
              <td class="px-2 py-1.5 text-[10px] text-gray-400 dark:text-gray-500 align-top">{entry.index}</td>
              <td class="px-2 py-1.5 align-top">
                {#if editingIndex === entry.index}
                  <!-- Inline edit mode -->
                  <div class="flex items-center gap-1 edit-row">
                    <input
                      type="text"
                      bind:value={editValue}
                      on:keydown={(e) => handleEditKeydown(e, entry.path)}
                      on:blur={() => handleEditBlur(entry.path)}
                      class="flex-1 px-2 py-1 text-[10px] font-mono border border-blue-500 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 dark:bg-gray-900 dark:border-blue-400 dark:text-gray-100"
                      spellcheck="false"
                    />
                    <button
                      on:click={() => confirmEdit(entry.path)}
                      disabled={actionLoading}
                      class="inline-flex p-1 text-green-600 hover:bg-green-50 rounded transition disabled:opacity-50 dark:text-green-400 dark:hover:bg-green-900/30"
                      title={$t('buttons.save')}
                      aria-label={$t('buttons.save')}
                    >
                      <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M5 13l4 4L19 7" />
                      </svg>
                    </button>
                    <button
                      on:click={cancelEdit}
                      disabled={actionLoading}
                      class="inline-flex p-1 text-gray-400 hover:text-gray-600 hover:bg-gray-100 rounded transition disabled:opacity-50 dark:hover:bg-gray-700"
                      title={$t('buttons.cancel')}
                      aria-label={$t('buttons.cancel')}
                    >
                      <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                        <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                      </svg>
                    </button>
                  </div>
                  {#if editError}
                    <div class="mt-1 text-[10px] text-red-600 dark:text-red-400">{editError}</div>
                  {/if}
                {:else}
                  <!-- Display mode: click to copy -->
                  <div
                    class="text-[11px] font-mono text-gray-700 dark:text-gray-200 break-all cursor-pointer hover:text-blue-600 dark:hover:text-blue-400 transition select-none leading-relaxed"
                    title={$t('messages.clickToCopy')}
                    on:click={() => copyToClipboard(entry.path)}
                  >
                    {entry.path}
                  </div>
                {/if}
              </td>
              <td class="px-2 py-1.5 text-right align-top">
                {#if editingIndex !== entry.index}
                  <!-- Rename button -->
                  <button
                    on:click={() => startEdit(entry.index, entry.path)}
                    disabled={actionLoading}
                    class="inline-flex p-1 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded transition disabled:opacity-30 dark:hover:text-blue-400 dark:hover:bg-blue-900/30"
                    title={$t('path.rename')}
                    aria-label={$t('path.rename')}
                  >
                    <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                    </svg>
                  </button>
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
                {/if}
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    </div>
  {/if}
</div>
