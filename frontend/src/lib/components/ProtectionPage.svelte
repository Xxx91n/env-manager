<script lang="ts">
  import { Lock } from 'lucide-svelte'
  import { onMount } from 'svelte'
  import { frontendLog } from '../settingsStore'
  import { t } from 'svelte-i18n'
  import { showToast, showModal, refreshTrigger, selectedScope } from '../stores'
  import { listProtection, addProtectedPath, removeProtectedPath, addProtectedVar, removeProtectedVar, listVariablesRaw, listPathEntries } from '../api'
  import type { ProtectionData } from '../api'
  import CloneCombobox from './CloneCombobox.svelte'

  let data: ProtectionData | null = null
  let loading = false
  let activeTab: 'vars' | 'paths' = 'vars'

  // All available variables from the system (cached via listVariablesRaw)
  let allVars: { name: string; scope: string }[] = []
  // All available PATH entries from both scopes
  let allPathEntries: { path: string; scope: string }[] = []

  let selectedVarName = ''
  let selectedPathEntry = ''

  // Local scope filter — initialises from the shared store (defaults to 'all')
  let scopeFilter: 'all' | 'user' | 'system' = 'all'

  // Sync local filter with shared store on mount
  $: scopeFilter = $selectedScope === 'all' ? 'all' : $selectedScope as 'user' | 'system'

  onMount(refresh)

  $: if ($refreshTrigger > 0) refresh()

  async function refresh() {
    loading = true
    try {
      // v0.9.19: All 4 IPC calls launched concurrently (SWR makes listProtection instant on cache hit)
      const withTimeout = (p, ms) => Promise.race([
        p,
        new Promise((_, reject) => setTimeout(() => reject(new Error("timeout")), ms)),
      ]).catch(() => null)
      const [protectionResult, varsResult, userPaths, systemPaths] = await Promise.all([
        withTimeout(listProtection(), 4500),
        withTimeout(listVariablesRaw(), 4500),
        withTimeout(listPathEntries("user"), 4500),
        withTimeout(listPathEntries("system"), 4500),
      ])
      data = protectionResult
      if (varsResult) {
        allVars = varsResult
          .filter(v => !data!.protectedVars.builtIn.includes(v.name) && !data!.protectedVars.custom.includes(v.name))
          .map(v => ({ name: v.name, scope: v.scope }))
      }
      // Combine user + system PATH entries; a timeout yields null -> empty list
      const up = userPaths ?? []
      const sp = systemPaths ?? []
      allPathEntries = [
        ...up.map(e => ({ path: e.path, scope: "user" })),
        ...sp.map(e => ({ path: e.path, scope: "system" })),
      ].filter(p => !data!.protectedPaths.builtIn.includes(p.path) && !data!.protectedPaths.custom.includes(p.path))
    } catch (err) { void frontendLog('error', '[ProtectionPage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : String(err), "error")
    } finally {
      loading = false
    }
  }

  // Filter available vars and path entries by current scope selection
  $: filteredVars = scopeFilter === 'all'
    ? allVars
    : allVars.filter(v => v.scope === scopeFilter)
  $: filteredPathEntries = scopeFilter === 'all'
    ? allPathEntries
    : allPathEntries.filter(p => p.scope === scopeFilter)

  // Filter built-in vars display by scope — built-in protection is system-only,
  // so we show all built-ins regardless of filter (they only apply to system scope).
  $: filteredBuiltinVars = scopeFilter === 'all'
    ? (data?.protectedVars.builtIn ?? [])
    : scopeFilter === 'system'
      ? (data?.protectedVars.builtIn ?? [])
      : [] // user scope has no built-in protection
  // Built-in protected paths are system-only; hide them when filtering to user scope
  $: filteredBuiltinPaths = scopeFilter === 'user'
    ? []
    : (data?.protectedPaths.builtIn ?? [])

  // Custom vars have no scope info from CLI, so we show them regardless
  // (custom protection applies to any scope the user locks from).
  $: filteredCustomVars = data?.protectedVars.custom ?? []
  // Custom protected paths: filter by scope by matching against loaded PATH data
  // (custom paths stored as plain strings; we resolve their scope this way)
  $: filteredCustomPaths = scopeFilter === 'all'
    ? (data?.protectedPaths.custom ?? [])
    : (data?.protectedPaths.custom ?? []).filter(cp =>
        allPathEntries.some(pe => pe.path === cp && pe.scope === scopeFilter))

  async function handleAddVar() {
    const name = selectedVarName.trim()
    if (!name) return
    try {
      await addProtectedVar(name)
      selectedVarName = ''
      await refresh()
      showToast($t('protection.varLocked'), 'success')
    } catch (err) { void frontendLog('error', '[ProtectionPage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  function handleRemoveVar(name: string) {
    showModal({
      title: $t('protection.unlockVar'),
      message: $t('protection.unlockVarConfirm', { values: { name } }),
      confirmLabel: $t('protection.unlockVar'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        try {
          await removeProtectedVar(name)
          await refresh()
          showToast($t('protection.varUnlocked'), 'success')
        } catch (err) { void frontendLog('error', '[ProtectionPage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }

  async function handleAddPath() {
    const entry = selectedPathEntry.trim()
    if (!entry) return
    try {
      await addProtectedPath(entry)
      selectedPathEntry = ''
      await refresh()
      showToast($t('protection.pathLocked'), 'success')
    } catch (err) { void frontendLog('error', '[ProtectionPage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showToast(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  function handleRemovePath(entry: string, isBuiltIn: boolean) {
    if (isBuiltIn) {
      showToast($t('protection.cannotRemoveBuiltin'), 'error')
      return
    }
    showModal({
      title: $t('protection.unlockPath'),
      message: $t('protection.unlockPathConfirm', { values: { name: entry } }),
      confirmLabel: $t('protection.unlockPath'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        try {
          await removeProtectedPath(entry)
          await refresh()
          showToast($t('protection.pathUnlocked'), 'success')
        } catch (err) { void frontendLog('error', '[ProtectionPage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
          showToast(err instanceof Error ? err.message : String(err), 'error')
        }
      },
    })
  }
</script>

<div class="space-y-3">
  <!-- Tab selector + scope filter + refresh -->
  <div class="flex items-center gap-2 flex-wrap">
    <div class="inline-flex rounded-md border border-border overflow-hidden">
      <button
        on:click={() => activeTab = 'vars'}
        class="px-4 py-1.5 text-xs font-medium transition {activeTab === 'vars'
          ? 'bg-primary text-primary-foreground bg-primary/80'
          : 'bg-card text-muted-foreground hover:bg-muted/20 bg-card text-foreground/80 hover:bg-accent'}"
      >
        {$t('protection.protectedVars')}
      </button>
      <button
        on:click={() => activeTab = 'paths'}
        class="px-4 py-1.5 text-xs font-medium transition {activeTab === 'paths'
          ? 'bg-primary text-primary-foreground bg-primary/80'
          : 'bg-card text-muted-foreground hover:bg-muted/20 bg-card text-foreground/80 hover:bg-accent'}"
      >
        {$t('protection.protectedPaths')}
      </button>
    </div>

    <select
      bind:value={$selectedScope}
      class="px-2.5 py-1.5 text-xs border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary bg-card border-border/80 text-foreground"
    >
      <option value="all">{$t('table.scope')}</option>
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>

    <button on:click={refresh} class="ml-auto px-3 py-1.5 text-xs text-muted-foreground hover:bg-muted/30 rounded-md text-foreground/80 hover:bg-accent">
      {$t('buttons.refresh')}
    </button>
  </div>

  {#if loading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-5 w-5 border-b-2 border-primary"></div>
    </div>
  {:else if !data}
    <div class="px-4 py-8 text-center text-muted-foreground text-xs">{$t('messages.loading')}</div>
  {:else if activeTab === 'vars'}
    <!-- Protected Variables Tab -->
    <div class="space-y-4">
      <!-- Add custom protected var (searchable CloneCombobox from existing vars) -->
      <div class="flex gap-2">
        <div class="flex-1">
          <CloneCombobox
            items={filteredVars.map(v => ({ name: v.name, value: v.scope }))}
            placeholder={$t('protection.selectVar')}
            on:select={(e) => { selectedVarName = e.detail.name; handleAddVar() }}
          />
        </div>
        <button
          on:click={handleAddVar}
          disabled={!selectedVarName.trim()}
          class="px-3 py-1.5 text-xs font-medium text-primary-foreground bg-primary rounded-md hover:bg-primary transition disabled:opacity-50 bg-primary/80"
        >
          {$t('protection.lockVar')}
        </button>
      </div>

      <!-- Built-in protected vars -->
      <div class="bg-card border border-border rounded-md overflow-hidden">
        <div class="px-4 py-2.5 border-b border-border bg-muted/20 bg-muted">
          <span class="text-xs font-medium text-muted-foreground text-foreground/80">{$t('protection.builtinVars')} ({filteredBuiltinVars.length})</span>
        </div>
        {#if scopeFilter === 'user'}
          <div class="px-4 py-6 text-center text-[10px] text-muted-foreground">{$t('protection.builtinSystemOnly')}</div>
        {:else}
          <div class="max-h-64 overflow-y-auto">
            <div class="grid grid-cols-2 sm:grid-cols-3 gap-1 p-3">
              {#each filteredBuiltinVars as varName (varName)}
                <div class="flex items-center gap-1.5 px-2 py-1 rounded bg-muted/20 bg-muted">
                  <Lock class="w-3 h-3 text-primary/80 flex-shrink-0" />
                  <span class="text-xs font-mono text-foreground/80 text-foreground truncate" title={varName}>{varName}</span>
                </div>
              {/each}
            </div>
          </div>
        {/if}
      </div>
      <div class="text-[10px] text-muted-foreground px-1">{$t('protection.varsNote')}</div>

      <!-- Custom protected vars (user-locked) -->
      <div class="bg-card border border-border rounded-md overflow-hidden">
        <div class="px-4 py-2.5 border-b border-border bg-muted/20 bg-muted">
          <span class="text-xs font-medium text-muted-foreground text-foreground/80">{$t('protection.customVars')} ({filteredCustomVars.length})</span>
        </div>
        {#if filteredCustomVars.length === 0}
          <div class="px-4 py-6 text-center text-[10px] text-muted-foreground">{$t('protection.noCustomVars')}</div>
        {:else}
          <div class="divide-y divide-border/50 divide-border">
            {#each filteredCustomVars as varName (varName)}
              <div class="flex items-center justify-between px-4 py-2">
                <span class="text-xs font-mono text-foreground/80 text-foreground truncate" title={varName}>{varName}</span>
                <button
                  on:click={() => handleRemoveVar(varName)}
                  class="text-destructive hover:text-destructive transition flex-shrink-0 ml-2"
                  title={$t('protection.unlockVar')}
                  aria-label={$t('protection.unlockVar')}
                >
                  <Lock class="w-3.5 h-3.5" />
                </button>
              </div>
            {/each}
          </div>
        {/if}
      </div>
    </div>
  {:else}
    <!-- Protected PATH Entries Tab -->
    <div class="space-y-4">
      <!-- Add custom protected path (searchable CloneCombobox from existing PATH) -->
      <div class="flex gap-2">
        <div class="flex-1">
          <CloneCombobox
            items={filteredPathEntries.map(p => ({ name: p.path, value: p.scope }))}
            placeholder={$t('profiles.cloneSearchPlaceholder')}
            on:select={(e) => { selectedPathEntry = e.detail.name; handleAddPath() }}
          />
        </div>
        <button
          on:click={handleAddPath}
          disabled={!selectedPathEntry.trim()}
          class="px-3 py-1.5 text-xs font-medium text-primary-foreground bg-primary rounded-md hover:bg-primary transition disabled:opacity-50 bg-primary/80"
        >
          {$t('protection.lockPath')}
        </button>
      </div>
      <!-- Built-in protected path entries -->
      <div class="bg-card border border-border rounded-md overflow-hidden">
        <div class="px-4 py-2.5 border-b border-border bg-muted/20 bg-muted">
          <span class="text-xs font-medium text-muted-foreground text-foreground/80">{$t('protection.builtinPaths')}</span>
        </div>
        <div class="divide-y divide-border/50 divide-border">
          {#each filteredBuiltinPaths as entry (entry)}
            <div class="flex items-center justify-between px-4 py-2">
              <span class="text-xs font-mono text-foreground/80 text-foreground truncate" title={entry}>{entry}</span>
              <span class="text-[10px] text-muted-foreground flex-shrink-0 ml-2">{$t('protection.builtin')}</span>
            </div>
          {/each}
        </div>
      </div>

      <!-- Custom protected path entries -->
      <div class="bg-card border border-border rounded-md overflow-hidden">
        <div class="px-4 py-2.5 border-b border-border bg-muted/20 bg-muted flex items-center justify-between">
          <span class="text-xs font-medium text-muted-foreground text-foreground/80">{$t('protection.customPaths')}</span>
        </div>
        {#if filteredCustomPaths.length === 0}
          <div class="px-4 py-6 text-center text-[10px] text-muted-foreground">{$t('protection.noCustomPaths')}</div>
        {:else}
          <div class="divide-y divide-border/50 divide-border">
            {#each filteredCustomPaths as entry (entry)}
              <div class="flex items-center justify-between px-4 py-2">
                <span class="text-xs font-mono text-foreground/80 text-foreground truncate" title={entry}>{entry}</span>
                <button
                  on:click={() => handleRemovePath(entry, false)}
                  class="text-destructive hover:text-destructive transition flex-shrink-0 ml-2"
                  title={$t('protection.unlockPath')}
                  aria-label={$t('protection.unlockPath')}
                >
                  <Lock class="w-3.5 h-3.5" />
                </button>
              </div>
            {/each}
          </div>
        {/if}
      </div>
    </div>
  {/if}
</div>
