<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { t } from 'svelte-i18n'
  import { createBackup, restoreBackup } from '../api'

  const dispatch = createEventDispatcher()

  let mode = 'export'
  let saving = false
  let message = ''
  let messageType = 'success'
  let selectedFile: File | null = null

  async function handleExport() {
    saving = true
    message = ''
    try {
      const result = await createBackup()
      message = result
      messageType = 'success'
    } catch (err) {
      message = err instanceof Error ? err.message : 'Export failed'
      messageType = 'error'
    } finally {
      saving = false
    }
  }

  async function handleRestore() {
    if (!selectedFile) {
      message = $t('labels.backupFile')
      messageType = 'error'
      return
    }

    saving = true
    message = ''
    try {
      const filePath = (selectedFile as any).path || selectedFile.name
      await restoreBackup(filePath)
      message = $t('messages.backupRestored')
      messageType = 'success'
    } catch (err) {
      message = err instanceof Error ? err.message : 'Restore failed'
      messageType = 'error'
    } finally {
      saving = false
    }
  }

  function handleFileSelect(e: Event) {
    const input = e.target as HTMLInputElement
    if (input.files && input.files.length > 0) {
      selectedFile = input.files[0]
    }
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div class="fixed inset-0 bg-black/40 flex items-center justify-center z-50" on:click={handleClose}>
  <div class="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 dark:bg-gray-800" on:click|stopPropagation>
    <div class="px-5 py-3 border-b border-gray-200 dark:border-gray-700">
      <h2 class="text-sm font-semibold text-gray-900 dark:text-gray-100">
        {$t('dialogs.backupCreate')}
      </h2>
    </div>

    <div class="px-5 py-4">
      <!-- Mode toggle -->
      <div class="flex gap-1 mb-3 p-0.5 bg-gray-100 rounded-md dark:bg-gray-700">
        <button
          on:click={() => (mode = 'export')}
          class="flex-1 px-3 py-1.5 text-xs font-medium rounded {mode === 'export'
            ? 'bg-white text-gray-900 shadow-sm dark:bg-gray-800 dark:text-gray-100'
            : 'text-gray-500 dark:text-gray-400'}"
        >
          {$t('buttons.export')}
        </button>
        <button
          on:click={() => (mode = 'import')}
          class="flex-1 px-3 py-1.5 text-xs font-medium rounded {mode === 'import'
            ? 'bg-white text-gray-900 shadow-sm dark:bg-gray-800 dark:text-gray-100'
            : 'text-gray-500 dark:text-gray-400'}"
        >
          {$t('buttons.restore')}
        </button>
      </div>

      {#if mode === 'export'}
        <p class="text-xs text-gray-500 dark:text-gray-400 mb-3">
          {$t('messages.exportDescription')}
        </p>
        <button
          on:click={handleExport}
          disabled={saving}
          class="w-full flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-medium text-white bg-green-600 rounded-md hover:bg-green-700 transition disabled:opacity-50 dark:bg-green-500 dark:hover:bg-green-600"
        >
          <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
          </svg>
          {saving ? $t('messages.loading') : $t('buttons.export')}
        </button>
      {:else}
        <p class="text-xs text-gray-500 dark:text-gray-400 mb-3">
          {$t('messages.importDescription')}
        </p>
        <input
          type="file"
          accept=".json"
          on:change={handleFileSelect}
          class="w-full text-xs text-gray-500 file:mr-3 file:py-1.5 file:px-3 file:rounded-md file:border-0 file:text-xs file:font-medium file:bg-blue-50 file:text-blue-700 hover:file:bg-blue-100 dark:text-gray-400 dark:file:bg-gray-700 dark:file:text-blue-300"
        />
        <button
          on:click={handleRestore}
          disabled={saving}
          class="w-full mt-3 flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-medium text-white bg-green-600 rounded-md hover:bg-green-700 transition disabled:opacity-50 dark:bg-green-500 dark:hover:bg-green-600"
        >
          <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
          </svg>
          {saving ? $t('messages.loading') : $t('buttons.restore')}
        </button>
      {/if}

      {#if message}
        <div class="mt-3 p-2.5 rounded-md text-xs {messageType === 'success'
          ? 'bg-green-50 border border-green-200 text-green-800 dark:bg-green-900/30 dark:border-green-700 dark:text-green-300'
          : 'bg-red-50 border border-red-200 text-red-800 dark:bg-red-900/30 dark:border-red-700 dark:text-red-300'}">
          {message}
        </div>
      {/if}
    </div>

    <div class="px-5 py-3 border-t border-gray-200 flex justify-end dark:border-gray-700">
      <button
        on:click={handleClose}
        class="px-4 py-1.5 text-xs text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
      >
        {$t('buttons.close')}
      </button>
    </div>
  </div>
</div>
