<script lang="ts">
  import { t } from 'svelte-i18n'
  import { variables, selectedScope, error, showModal } from '../stores'
  import { deleteVariable, createBackup, toggleVariable, setVariable, deleteVariable as delVar } from '../api'
  import EditDialog from './EditDialog.svelte'
  import BackupDialog from './BackupDialog.svelte'

  let filteredVars = $variables
  let search = ''
  let editingVar = null
  let showEditDialog = false
  let showBackupDialog = false
  let togglingKeys: Record<string, boolean> = {}
  let copyFeedback = ''
  let localError = ''

  // Clear persistent error store on mount; we use localError for transient errors
  $: {
    let filtered = $variables

    if ($selectedScope !== 'all') {
      filtered = filtered.filter((v) => v.scope === $selectedScope)
    }

    if (search) {
      filtered = filtered.filter(
        (v) =>
          v.name.toLowerCase().includes(search.toLowerCase()) ||
          v.value.toLowerCase().includes(search.toLowerCase())
      )
    }

    filteredVars = filtered
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

  function handleDelete(name: string, scope: string) {
    showModal({
      title: $t('dialogs.deleteConfirm'),
      message: $t('messages.deleteConfirmText', { values: { name } }),
      confirmLabel: $t('buttons.delete'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
        localError = ''
        try {
          await deleteVariable(name, scope as 'user' | 'system')
        } catch (err) {
          localError = err instanceof Error ? err.message : String(err)
          setTimeout(() => { localError = '' }, 3000)
        }
      }
    })
  }

  async function handleToggle(name: string, scope: string) {
    const key = name + ':' + scope
    // Use object assignment for Svelte reactivity (not Set)
    togglingKeys = { ...togglingKeys, [key]: true }
    localError = ''
    // Clear the global error store to prevent duplicate display
    error.set(null)
    try {
      await toggleVariable(name, scope as 'user' | 'system')
    } catch (err) {
      // Show error as a transient toast, not persistent banner
      localError = err instanceof Error ? err.message : String(err)
      setTimeout(() => { localError = '' }, 3000)
    } finally {
      const next = { ...togglingKeys }
      delete next[key]
      togglingKeys = next
    }
  }

  function handleEdit(v) {
    editingVar = v
    showEditDialog = true
  }

  function handleCloseEdit() {
    showEditDialog = false
    editingVar = null
  }

  function handleShowBackup() {
    showBackupDialog = true
  }

  function handleCloseBackup() {
    showBackupDialog = false
  }
</script>

<div class="space-y-3">
  <!-- Toolbar -->
  <div class="flex gap-2 items-center">
    <div class="relative flex-1">
      <svg class="absolute left-2.5 top-1/2 -translate-y-1/2 w-3.5 h-3.5 text-gray-400" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
      </svg>
      <input
        type="text"
        placeholder={$t('messages.searchPlaceholder')}
        bind:value={search}
        class="w-full pl-8 pr-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
      />
    </div>

    <select
      bind:value={$selectedScope}
      class="px-2.5 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
    >
      <option value="all">{$t('table.scope')}</option>
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>

    <button
      on:click={() => (showEditDialog = true)}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition dark:bg-blue-500 dark:hover:bg-blue-600"
    >
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
        <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" />
      </svg>
      {$t('buttons.add')}
    </button>

    <button
      on:click={handleShowBackup}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:bg-gray-800 dark:border-gray-600 dark:hover:bg-gray-700"
    >
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M5 8h14M5 8a2 2 0 110-4h14a2 2 0 110 4M5 8v10a2 2 0 002 2h10a2 2 0 002-2V8m-9 4h4" />
      </svg>
      {$t('buttons.backup')}
    </button>
  </div>

  {#if localError}
    <div class="bg-red-50 border border-red-200 text-red-800 px-3 py-2 rounded-md text-xs dark:bg-red-900/30 dark:border-red-700 dark:text-red-300">
      {localError}
    </div>
  {/if}

  {#if copyFeedback}
    <div class="fixed top-4 left-1/2 -translate-x-1/2 px-3 py-1.5 bg-gray-800 text-white text-xs rounded-md shadow-lg z-50 dark:bg-gray-700">
      {copyFeedback}
    </div>
  {/if}

  <div class="overflow-x-auto bg-white rounded-md border border-gray-200 dark:bg-gray-800 dark:border-gray-700">
    {#if filteredVars.length === 0}
      <div class="px-4 py-8 text-center text-gray-400 text-xs dark:text-gray-500">
        {$t('messages.noData')}
      </div>
    {:else}
      <table class="w-full">
        <thead class="bg-gray-50 border-b border-gray-200 dark:bg-gray-750 dark:border-gray-600">
          <tr>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-12">
              {$t('table.enabled')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">
              {$t('table.name')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-20">
              {$t('table.scope')}
            </th>
            <th class="px-3 py-2 text-left text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide">
              {$t('table.value')}
            </th>
            <th class="px-3 py-2 text-right text-xs font-medium text-gray-500 dark:text-gray-400 uppercase tracking-wide w-24">
              {$t('table.actions')}
            </th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700">
          {#each filteredVars as variable (variable.name + variable.scope)}
            <tr class="hover:bg-gray-50 transition dark:hover:bg-gray-750 {variable.isDisabled ? 'opacity-50' : ''}">
              <td class="px-3 py-2">
                <button
                  on:click={() => handleToggle(variable.name, variable.scope)}
                  disabled={togglingKeys[variable.name + ':' + variable.scope] === true}
                  class="relative inline-flex h-4 w-7 items-center rounded-full transition disabled:opacity-50 {variable.isDisabled ? 'bg-gray-300 dark:bg-gray-600' : 'bg-blue-600 dark:bg-blue-500'}"
                  role="switch"
                  aria-checked={!variable.isDisabled}
                  title={variable.isDisabled ? $t('messages.clickToEnable') : $t('messages.clickToDisable')}
                >
                  <span
                    class="inline-block h-3 w-3 transform rounded-full bg-white shadow transition {variable.isDisabled ? 'translate-x-0.5' : 'translate-x-3.5'}"
                  ></span>
                </button>
              </td>
              <td
                class="px-3 py-2 text-xs font-mono text-gray-900 dark:text-gray-100 cursor-pointer hover:text-blue-600 dark:hover:text-blue-400 transition select-none"
                title={$t('messages.clickToCopy')}
                on:click={() => copyToClipboard(variable.name)}
              >
                {variable.name}
              </td>
              <td class="px-3 py-2 text-xs">
                <span
                  class="inline-flex px-1.5 py-0.5 rounded text-xs font-medium {variable.scope ===
                  'user'
                    ? 'bg-blue-50 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300'
                    : 'bg-amber-50 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300'}"
                >
                  {variable.scope === 'user' ? $t('scope.user') : $t('scope.system')}
                </span>
              </td>
              <td
                class="px-3 py-2 text-xs text-gray-600 font-mono dark:text-gray-300 max-w-xs truncate cursor-pointer hover:text-blue-600 dark:hover:text-blue-400 transition select-none"
                title={`${$t('messages.clickToCopy')} ${variable.value}`}
                on:click={() => copyToClipboard(variable.value)}
              >
                {variable.value}
              </td>
              <td class="px-3 py-2 text-right text-xs">
                <button
                  on:click={() => handleEdit(variable)}
                  class="inline-flex p-1 text-gray-400 hover:text-blue-600 hover:bg-blue-50 rounded transition dark:hover:text-blue-400 dark:hover:bg-blue-900/30"
                  title={$t('buttons.edit')}
                  aria-label={$t('buttons.edit')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                  </svg>
                </button>
                <button
                  on:click={() => handleDelete(variable.name, variable.scope)}
                  class="inline-flex p-1 text-gray-400 hover:text-red-600 hover:bg-red-50 rounded transition dark:hover:text-red-400 dark:hover:bg-red-900/30"
                  title={$t('buttons.delete')}
                  aria-label={$t('buttons.delete')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                  </svg>
                </button>
              </td>
            </tr>
          {/each}
        </tbody>
      </table>
    {/if}
  </div>
</div>

{#if showEditDialog}
  <EditDialog
    variable={editingVar}
    on:close={handleCloseEdit}
    on:save={() => handleCloseEdit()}
  />
{/if}

{#if showBackupDialog}
  <BackupDialog on:close={handleCloseBackup} />
{/if}
