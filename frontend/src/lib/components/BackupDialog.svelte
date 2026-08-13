<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { frontendLog } from '../settingsStore'
  import { t } from 'svelte-i18n'
  import { createBackup, restoreBackup, pickSaveFile, pickOpenFile } from '../api'
  import { showToast } from '../stores'

  const dispatch = createEventDispatcher()

  let mode = 'export'
  let saving = false

  async function handleExport() {
    const fileName = await pickSaveFile($t('dialogs.backupExportPrompt'), 'env-manager-backup.json')
    if (!fileName) return
    saving = true
    try {
      const result = await createBackup(fileName)
      showToast($t('messages.backupExported'), 'success')
    } catch (err) { void frontendLog('error', '[BackupDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : $t('messages.backupExportFailed'), 'error')
    } finally {
      saving = false
    }
  }

  async function handleRestore() {
    const filePath = await pickOpenFile($t('dialogs.backupExportPrompt'), '')
    if (!filePath) return

    saving = true
    try {
      await restoreBackup(filePath)
      showToast($t('messages.backupRestored'), 'success')
    } catch (err) { void frontendLog('error', '[BackupDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : $t('messages.backupExportFailed'), 'error')
    } finally {
      saving = false
    }
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div
  class="fixed inset-0 bg-black/40 flex items-center justify-center z-50"
  on:click={handleClose}
  on:keydown={(e) => { if (e.key === 'Escape') handleClose() }}
  role="presentation"
  tabindex="-1">
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
        <button
          on:click={handleRestore}
          disabled={saving}
          class="w-full flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-medium text-white bg-green-600 rounded-md hover:bg-green-700 transition disabled:opacity-50 dark:bg-green-500 dark:hover:bg-green-600"
        >
          <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
            <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
          </svg>
          {saving ? $t('messages.loading') : $t('buttons.restore')}
        </button>
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
