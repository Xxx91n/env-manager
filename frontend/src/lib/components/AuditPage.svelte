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
    } catch (err) { void frontendLog('error', '[AuditPage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      auditEntries = []
    } finally {
      auditLoading = false
    }
  }
</script>

<div class="space-y-3">
  <div class="flex items-center justify-between">
    <div>
      <h2 class="text-base font-semibold text-gray-800 text-foreground">{$t('nav.audit')}</h2>
      <p class="text-xs text-muted-foreground text-muted-foreground mt-1">{$t('audit.description')}</p>
    </div>
    <button
      on:click={loadAudit}
      disabled={auditLoading}
      class="px-3 py-1.5 text-xs font-medium rounded-md border border-gray-300 hover:bg-muted/20 transition border-border/80 hover:bg-accent disabled:opacity-50"
    >
      {auditLoading ? $t('audit.loading') : $t('audit.refresh')}
    </button>
  </div>

  <!-- Architecture distinction notice -->
  <div class="px-3 py-2 rounded-md bg-blue-50 bg-primary/10 border border-blue-200 border-primary">
    <p class="text-xs text-primary text-primary">{$t('audit.vsHistory')}</p>
  </div>

  {#if auditLoading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-blue-600"></div>
    </div>
  {:else if auditEntries.length > 0}
    <div class="max-h-[60vh] overflow-y-auto space-y-1">
      {#each auditEntries as entry}
        <div class="text-xs px-3 py-1.5 rounded bg-muted/20 bg-card font-mono border border-gray-100 border-border">
          <span class="text-gray-400 mr-2">{entry.timestamp}</span>
          <span class="text-foreground/80 text-foreground/80">{entry.command}</span>
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
