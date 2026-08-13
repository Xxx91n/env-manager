<script lang="ts">
  import { onMount } from 'svelte'
  import { frontendLog } from '../settingsStore'
  import { t } from 'svelte-i18n'
  import { auditList } from '../api'
  import { showToast } from '../stores'

  let auditEntries: any[] = []
  let auditLoading = false

  onMount(async () => {
    await loadAudit()
  })

  async function loadAudit() {
    auditLoading = true
    try {
      const result = await auditList()
      auditEntries = Array.isArray(result) ? result : (result?.entries ?? [])
    } catch (err) {
      auditEntries = []
    } finally {
      auditLoading = false
    }
  }
</script>

<div class="space-y-3">
  <div class="flex items-center justify-between">
    <div>
      <h2 class="text-base font-semibold text-gray-800 dark:text-gray-200">{$t('nav.audit')}</h2>
      <p class="text-xs text-gray-500 dark:text-gray-400 mt-1">{$t('audit.description')}</p>
    </div>
    <button
      on:click={loadAudit}
      disabled={auditLoading}
      class="px-3 py-1.5 text-xs font-medium rounded-md border border-gray-300 hover:bg-gray-50 transition dark:border-gray-600 dark:hover:bg-gray-700 disabled:opacity-50"
    >
      {auditLoading ? $t('audit.loading') : $t('audit.refresh')}
    </button>
  </div>

  <!-- Architecture distinction notice -->
  <div class="px-3 py-2 rounded-md bg-blue-50 dark:bg-blue-900/20 border border-blue-200 dark:border-blue-800">
    <p class="text-xs text-blue-700 dark:text-blue-300">{$t('audit.vsHistory')}</p>
  </div>

  {#if auditLoading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600"></div>
    </div>
  {:else if auditEntries.length > 0}
    <div class="max-h-[60vh] overflow-y-auto space-y-1">
      {#each auditEntries as entry}
        <div class="text-xs px-3 py-1.5 rounded bg-gray-50 dark:bg-gray-800 font-mono border border-gray-100 dark:border-gray-700">
          <span class="text-gray-400 mr-2">{entry.timestamp}</span>
          <span class="text-gray-700 dark:text-gray-300">{entry.command}</span>
        </div>
      {/each}
    </div>
    <p class="text-xs text-gray-400">{auditEntries.length} {$t('audit.entries')}</p>
  {:else}
    <div class="text-center py-8">
      <p class="text-sm text-gray-400">{$t('audit.empty')}</p>
    </div>
  {/if}
</div>
