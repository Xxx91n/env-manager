<script lang="ts">
  import { Download } from 'lucide-svelte'
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
  <div class="bg-card rounded-lg shadow-xl max-w-md w-full mx-4 bg-card" on:click|stopPropagation>
    <div class="px-5 py-3 border-b border-border border-border">
      <h2 class="text-sm font-semibold text-foreground text-foreground">
        {$t('dialogs.backupCreate')}
      </h2>
    </div>

    <div class="px-5 py-4">
      <!-- Mode toggle -->
      <div class="flex gap-1 mb-3 p-0.5 bg-muted/30 rounded-md bg-accent">
        <button
          on:click={() => (mode = 'export')}
          class="flex-1 px-3 py-1.5 text-xs font-medium rounded {mode === 'export'
            ? 'bg-card text-foreground shadow-sm bg-card text-foreground'
            : 'text-muted-foreground text-muted-foreground'}"
        >
          {$t('buttons.export')}
        </button>
        <button
          on:click={() => (mode = 'import')}
          class="flex-1 px-3 py-1.5 text-xs font-medium rounded {mode === 'import'
            ? 'bg-card text-foreground shadow-sm bg-card text-foreground'
            : 'text-muted-foreground text-muted-foreground'}"
        >
          {$t('buttons.restore')}
        </button>
      </div>

      {#if mode === 'export'}
        <p class="text-xs text-muted-foreground text-muted-foreground mb-3">
          {$t('messages.exportDescription')}
        </p>
        <button
          on:click={handleExport}
          disabled={saving}
          class="w-full flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-medium text-white bg-primary rounded-md hover:bg-green-700 transition disabled:opacity-50 bg-primary hover:bg-primary"
        >
          <Download class="w-3.5 h-3.5" />
          {saving ? $t('messages.loading') : $t('buttons.export')}
        </button>
      {:else}
        <p class="text-xs text-muted-foreground text-muted-foreground mb-3">
          {$t('messages.importDescription')}
        </p>
        <button
          on:click={handleRestore}
          disabled={saving}
          class="w-full flex items-center justify-center gap-1.5 px-4 py-2 text-xs font-medium text-white bg-primary rounded-md hover:bg-green-700 transition disabled:opacity-50 bg-primary hover:bg-primary"
        >
          <Download class="w-3.5 h-3.5" />
          {saving ? $t('messages.loading') : $t('buttons.restore')}
        </button>
      {/if}

      </div>

    <div class="px-5 py-3 border-t border-border flex justify-end border-border">
      <button
        on:click={handleClose}
        class="px-4 py-1.5 text-xs text-foreground/80 border border-gray-300 rounded-md hover:bg-muted/20 transition text-foreground border-border/80 hover:bg-accent"
      >
        {$t('buttons.close')}
      </button>
    </div>
  </div>
</div>
