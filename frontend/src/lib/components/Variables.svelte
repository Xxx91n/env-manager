<script lang="ts">
  import { t } from 'svelte-i18n'
  import { variables, selectedScope, error } from '../stores'
  import { deleteVariable, createBackup } from '../api'
  import EditDialog from './EditDialog.svelte'
  import BackupDialog from './BackupDialog.svelte'

  let filteredVars = $variables
  let search = ''
  let editingVar = null
  let showEditDialog = false
  let showBackupDialog = false

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

  async function handleDelete(name: string, scope: string) {
    if (confirm(`${$t('messages.deleteConfirmText', { values: { name } })}`)) {
      try {
        await deleteVariable(name, scope as 'user' | 'system')
      } catch {
        // Error already set in store
      }
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

<div class="space-y-4">
  <div class="flex gap-4 items-center">
    <input
      type="text"
      placeholder={$t('messages.searchPlaceholder')}
      bind:value={search}
      class="flex-1 px-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500"
    />

    <select
      bind:value={$selectedScope}
      class="px-4 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white"
    >
      <option value="all">{$t('table.scope')}</option>
      <option value="user">{$t('scope.user')}</option>
      <option value="system">{$t('scope.system')}</option>
    </select>

    <button
      on:click={() => (showEditDialog = true)}
      class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition"
    >
      {$t('buttons.add')}
    </button>

    <button
      on:click={handleShowBackup}
      class="px-4 py-2 bg-gray-600 text-white rounded-lg hover:bg-gray-700 transition"
    >
      {$t('buttons.backup')}
    </button>
  </div>

  {#if $error}
    <div class="bg-red-50 border border-red-200 text-red-800 px-4 py-3 rounded">
      {$error}
    </div>
  {/if}

  <div class="overflow-x-auto bg-white rounded-lg border border-gray-200">
    {#if filteredVars.length === 0}
      <div class="px-6 py-8 text-center text-gray-500">
        {$t('messages.noData')}
      </div>
    {:else}
      <table class="w-full">
        <thead class="bg-gray-50 border-b border-gray-200">
          <tr>
            <th class="px-6 py-3 text-left text-sm font-semibold text-gray-900">
              {$t('table.name')}
            </th>
            <th class="px-6 py-3 text-left text-sm font-semibold text-gray-900">
              {$t('table.scope')}
            </th>
            <th class="px-6 py-3 text-left text-sm font-semibold text-gray-900">
              {$t('table.value')}
            </th>
            <th class="px-6 py-3 text-right text-sm font-semibold text-gray-900">
              {$t('table.actions')}
            </th>
          </tr>
        </thead>
        <tbody>
          {#each filteredVars as variable (variable.name + variable.scope)}
            <tr class="border-b border-gray-200 hover:bg-gray-50">
              <td class="px-6 py-3 text-sm font-mono text-gray-900">
                {variable.name}
              </td>
              <td class="px-6 py-3 text-sm">
                <span
                  class="px-2 py-1 rounded text-xs font-semibold {variable.scope ===
                  'user'
                    ? 'bg-blue-100 text-blue-800'
                    : 'bg-red-100 text-red-800'}"
                >
                  {variable.scope === 'user' ? $t('scope.user') : $t('scope.system')}
                </span>
              </td>
              <td class="px-6 py-3 text-sm text-gray-600 font-mono max-w-xs truncate">
                {variable.value}
              </td>
              <td class="px-6 py-3 text-right text-sm space-x-2">
                <button
                  on:click={() => handleEdit(variable)}
                  class="px-3 py-1 text-blue-600 hover:bg-blue-50 rounded transition"
                >
                  {$t('buttons.edit')}
                </button>
                <button
                  on:click={() =>
                    handleDelete(variable.name, variable.scope)}
                  class="px-3 py-1 text-red-600 hover:bg-red-50 rounded transition"
                >
                  {$t('buttons.delete')}
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
