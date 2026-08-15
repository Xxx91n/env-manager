<script lang="ts">
 import { onMount } from 'svelte'
 import { t } from 'svelte-i18n'
 import { get as getStore } from 'svelte/store'
  import {
    serviceStatus, serviceHealth, servicePing, serviceReload,
    serviceStart, serviceStop, serviceRefreshMount, serviceRotateMount
  } from '../api'
  import { showToast } from '../stores'
  import { frontendLog } from '../settingsStore'
  function localizeProvider(provider: string, t: (k: string) => string): string {
    const map: Record<string, string> = { 'dpapi-current-user': 'secrets.providerDpapi', 'credential-manager': 'secrets.providerCredMan', 'powershell-secretmanagement': 'secrets.providerPsm', 'vault-kv2': 'secrets.providerVault', 'sops': 'secrets.providerSops', 'azure-keyvault': 'secrets.providerAzure', '1password': 'secrets.provider1Password', 'aws-secretsmanager': 'secrets.providerAws' };
    const key = map[provider] || provider;
    const translated = t(key);
    return translated === key ? provider : translated;
  }
  function localizeRefreshPolicy(policy: string, t: (k: string) => string): string {
    const map: Record<string, string> = { 'CreatedOnly': 'secrets.policy.createdOnly', 'OnDemand': 'secrets.policy.onDemand', 'Periodic': 'secrets.policy.periodic' };
    const key = map[policy] || policy;
    const translated = t(key);
    return translated === key ? policy : translated;
  }

  const tStore = t

  let serviceRunning = false
  let serviceHealthData: any = null
  let serviceLoading = false
  let serviceError: string | null = null

  onMount(() => {
    void refreshServiceStatus()
  })

  async function refreshServiceStatus() {
    serviceLoading = true
    try {
      const status = await serviceStatus()
      serviceRunning = status?.ok === true && status?.data?.running === true
      if (serviceRunning) {
        try {
          const healthResult = await serviceHealth()
          serviceHealthData = healthResult.ok ? healthResult.data : null
        } catch { serviceHealthData = null }
      } else {
        serviceHealthData = null
      }
    } catch {
      serviceRunning = false
      serviceHealthData = null
    } finally {
      serviceLoading = false
    }
  }

  async function handleServicePing() {
    if (serviceLoading) return
    serviceLoading = true
    serviceError = null
    try {
      const result = await servicePing()
      if (!result.ok) serviceError = result.message || 'ping failed'
      await refreshServiceStatus()
    } catch (err) { void frontendLog('error', '[ServicePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      serviceError = err instanceof Error ? err.message : String(err)
    } finally { serviceLoading = false }
  }

  async function handleServiceReload() {
    if (serviceLoading || !serviceRunning) return
    serviceLoading = true
    serviceError = null
    try {
      const result = await serviceReload()
      if (!result.ok) { serviceError = result.message || 'reload failed' }
      else { showToast(getStore(tStore)('settings.service.reloaded'), 'success') }
      await refreshServiceStatus()
     } catch (err) { serviceError = err instanceof Error ? err.message : String(err); void frontendLog('error', '[ServicePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {}) }
    finally { serviceLoading = false }
  }

  async function handleServiceShutdown() {
    if (serviceLoading || !serviceRunning) return
    serviceLoading = true
    serviceError = null
    try {
      await serviceStop()
      showToast(getStore(tStore)('settings.service.stopped'), 'info')
      await refreshServiceStatus()
     } catch (err) { serviceError = err instanceof Error ? err.message : String(err); void frontendLog('error', '[ServicePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {}) }
    finally { serviceLoading = false }
  }

  async function handleServiceStart() {
    if (serviceLoading || serviceRunning) return
    serviceLoading = true
    serviceError = null
    try {
      await refreshServiceStatus()
      if (serviceRunning) { showToast(getStore(tStore)('settings.service.started'), 'success'); return }
      await serviceStart()
      await refreshServiceStatus()
      if (serviceRunning) { showToast(getStore(tStore)('settings.service.started'), 'success') }
      else { serviceError = getStore(tStore)('settings.service.startFailed') }
     } catch (err) { serviceError = err instanceof Error ? err.message : String(err); void frontendLog('error', '[ServicePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {}) }
    finally { serviceLoading = false }
  }

  async function handleMountRefresh(mountId: string) {
    if (serviceLoading) return
    serviceLoading = true
    try {
      const result = await serviceRefreshMount(mountId)
      if (!result.ok) serviceError = result.message || 'refresh failed'
      await refreshServiceStatus()
     } catch (err) { serviceError = err instanceof Error ? err.message : String(err); void frontendLog('error', '[ServicePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {}) }
    finally { serviceLoading = false }
  }

  async function handleMountRotate(mountId: string) {
    if (serviceLoading) return
    serviceLoading = true
    try {
      const result = await serviceRotateMount(mountId)
      if (!result.ok) serviceError = result.message || 'rotate failed'
      else { showToast(getStore(tStore)('settings.service.rotated'), 'success') }
      await refreshServiceStatus()
     } catch (err) { serviceError = err instanceof Error ? err.message : String(err); void frontendLog('error', '[ServicePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {}) }
    finally { serviceLoading = false }
  }
</script>

<div class="space-y-3">
  <div class="flex items-center justify-between">
    <div>
      <h2 class="text-base font-semibold text-foreground">{$t('nav.service')}</h2>
      <p class="text-xs text-muted-foreground mt-1">{$t('service.description')}</p>
    </div>
    <button on:click={refreshServiceStatus} disabled={serviceLoading}
      class="px-3 py-1.5 text-xs font-medium rounded-md border border-border hover:bg-muted/20 transition border-border/80 hover:bg-accent disabled:opacity-50">
      {$t('buttons.refresh')}
    </button>
  </div>

  <div class="bg-card border border-border rounded-md p-4">
    <div class="flex items-center gap-3 mb-2">
      <span class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-full text-xs font-medium {serviceRunning ? 'bg-green-100 text-green-700 bg-primary/20 text-primary' : 'bg-muted/30 text-muted-foreground bg-card text-muted-foreground'}">
        <span class="w-1.5 h-1.5 rounded-full {serviceRunning ? 'bg-green-500' : 'bg-muted-foreground/50'}"></span>
        {serviceRunning ? $t('settings.service.running') : $t('settings.service.stopped')}
      </span>
      {#if serviceLoading}
        <span class="text-xs text-muted-foreground">{$t('settings.service.checking')}</span>
      {/if}
      <button on:click={handleServicePing} disabled={serviceLoading}
        class="px-2 py-0.5 text-xs font-medium text-muted-foreground border border-border rounded-md hover:bg-muted/20 transition disabled:opacity-50 text-foreground/80 border-border/80 hover:bg-accent">
        {$t('settings.service.ping')}
      </button>
      <button on:click={handleServiceReload} disabled={serviceLoading || !serviceRunning}
        class="px-2 py-0.5 text-xs font-medium text-muted-foreground border border-border rounded-md hover:bg-muted/20 transition disabled:opacity-50 text-foreground/80 border-border/80 hover:bg-accent">
        {$t('settings.service.reload')}
      </button>
      <button on:click={handleServiceShutdown} disabled={serviceLoading || !serviceRunning}
        class="px-2 py-0.5 text-xs font-medium text-destructive border border-destructive/30 rounded-md hover:bg-destructive/10 transition disabled:opacity-50 border-destructive hover:bg-destructive/20">
        {$t('settings.service.shutdown')}
      </button>
      <button on:click={handleServiceStart} disabled={serviceLoading || serviceRunning}
        class="px-2 py-0.5 text-xs font-medium text-primary border border-green-300 rounded-md hover:bg-primary/10 transition disabled:opacity-50 border-primary hover:bg-primary/20">
        {$t('settings.service.start')}
      </button>
    </div>
    {#if serviceError && serviceRunning}
      <p class="text-xs text-primary/80">{serviceError}</p>
    {:else if serviceError && !serviceRunning}
      <p class="text-xs text-primary/80">{$t('settings.service.notRunningHint')}</p>
    {/if}
    {#if serviceHealthData?.mounts?.length > 0}
      <div class="mt-2 space-y-1 max-h-40 overflow-y-auto">
        {#each serviceHealthData.mounts as mount}
          <div class="flex items-center gap-2 text-xs px-2 py-1 rounded bg-muted/20 bg-card">
            <span class="font-mono" title={mount.provider}>{localizeProvider(mount.provider, $t)}</span>
            <span class="text-muted-foreground">{mount.name}</span>
            <span class="px-1 rounded {mount.healthy ? 'text-green-600' : 'text-red-600'}">{mount.healthy ? $t('settings.service.healthy') : $t('settings.service.unhealthy')}</span>
            <span class="text-muted-foreground" title={mount.refreshPolicy}>{localizeRefreshPolicy(mount.refreshPolicy, $t)}</span>
            {#if mount.lastFetchedAt}
              <span class="text-muted-foreground ml-auto text-[10px] whitespace-nowrap truncate max-w-[180px]" title={mount.lastFetchedAt}>
                {new Date(mount.lastFetchedAt).toLocaleString()}
              </span>
            {/if}
            <button on:click={() => handleMountRefresh(mount.id)} disabled={serviceLoading}
              class="px-1 text-xs text-primary hover:underline disabled:opacity-50">
              {$t('settings.service.refreshBtn')}
            </button>
            <button on:click={() => handleMountRotate(mount.id)} disabled={serviceLoading}
              class="px-1 text-xs text-primary hover:underline disabled:opacity-50">
              {$t('settings.service.rotateBtn')}
            </button>
          </div>
        {/each}
      </div>
    {/if}
  </div>
</div>
