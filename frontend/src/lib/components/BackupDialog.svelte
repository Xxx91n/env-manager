<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { t } from 'svelte-i18n'
  import { createBackup } from '../api'

  const dispatch = createEventDispatcher()

  let mode = 'export'
  let saving = false
  let message = ''

  async function handleExport() {
    saving = true
    message = ''
    try {
      const result = await createBackup()
      message = result
    } catch (err) {
      message = err instanceof Error ? err.message : 'Export failed'
    } finally {
      saving = false
    }
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
  <div class="bg-white rounded-lg shadow-lg max-w-md w-full mx-4">
    <div class="p-6 border-b border-gray-200">
      <h2 class="text-lg font-semibold text-gray-900">
        {$t('dialogs.backupCreate')}
      </h2>
    </div>

    <div class="p-6">
      <div class="flex gap-2 mb-4">
        <button
          on:click={() => (mode = 'export')}
          class="flex-1 px-4 py-2 {mode === 'export'
            ? 'bg-blue-600 text-white'
            : 'bg-gray-100 text-gray-700'} rounded-lg transition"
        >
          {$t('buttons.export')}
        </button>
        <button
          on:click={() => (mode = 'import')}
          class="flex-1 px-4 py-2 {mode === 'import'
            ? 'bg-blue-600 text-white'
            : 'bg-gray-100 text-gray-700'} rounded-lg transition"
        >
          {$t('buttons.restore')}
        </button>
      </div>

      {#if mode === 'export'}
        <p class="text-sm text-gray-600 mb-4">
          {$t('messages.exportDescription')}
        </p>
        <button
          on:click={handleExport}
          disabled={saving}
          class="w-full px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition disabled:opacity-50"
        >
          {saving ? $t('messages.loading') : $t('buttons.export')}
        </button>
      {:else}
        <p class="text-sm text-gray-600 mb-4">
          {$t('messages.importDescription')}
        </p>
        <input
          type="file"
          accept=".json"
          class="w-full px-3 py-2 border border-gray-300 rounded-lg"
        />
        <button
          disabled={saving}
          class="w-full mt-3 px-4 py-2 bg-green-600 text-white rounded-lg hover:bg-green-700 transition disabled:opacity-50"
        >
          {$t('buttons.restore')}
        </button>
      {/if}

      {#if message}
        <div class="mt-4 p-3 bg-green-50 border border-green-200 text-green-800 rounded text-sm">
          {message}
        </div>
      {/if}
    </div>

    <div class="p-6 border-t border-gray-200 flex gap-3 justify-end">
      <button
        on:click={handleClose}
        class="px-4 py-2 text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition"
      >
        {$t('buttons.cancel')}
      </button>
    </div>
  </div>
</div>
