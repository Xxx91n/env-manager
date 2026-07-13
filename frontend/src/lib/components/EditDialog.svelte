<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { t } from 'svelte-i18n'
  import { setVariable, renameVariable } from '../api'
  import { showModal, variables } from '../stores'
  import { hasVariableConflict } from '../features'

  export let variable = null

  const dispatch = createEventDispatcher()

  let name = variable?.name || ''
  let value = variable?.value || ''
  let scope = variable?.scope || 'user'
  let originalName = variable?.name || ''
  let saving = false
  let localError = ''

  // Check if name has changed from original
  $: nameChanged = !!variable && name !== originalName

  async function handleSave() {
    if (!name.trim()) {
      showModal({
        title: $t('errors.invalidInput'),
        message: $t('labels.name') + ' is required',
        confirmLabel: $t('buttons.close'),
        variant: 'warning',
      })
      return
    }

    // Validate: no '=' in name
    if (name.includes('=')) {
      localError = 'Variable name cannot contain "="'
      return
    }

    // Validate: name length
    if (name.length > 255 && scope === 'user') {
      localError = 'Variable name exceeds 255 characters'
      return
    }

    // Check for protected system variable (consistent with CLI security)
    const protectedVars = ['PATH', 'PATHEXT', 'PSMODULEPATH', 'SystemRoot', 'windir',
      'ComSpec', 'TEMP', 'TMP', 'USERPROFILE', 'SystemDrive', 'ProgramFiles',
      'ProgramFiles(x86)', 'ProgramData', 'HOMEDRIVE', 'HOMEPATH',
      'NUMBER_OF_PROCESSORS', 'OS', 'PROCESSOR_ARCHITECTURE']
    if (scope === 'system' && protectedVars.some(v => v.toLowerCase() === name.toLowerCase())) {
      localError = `Cannot modify protected system variable: ${name}`
      return
    }

    const conflict = hasVariableConflict($variables, name, scope, variable ? originalName : undefined)
    if (conflict) {
      showModal({
        title: $t('messages.overwriteTitle'),
        message: $t('messages.overwriteConfirm', { values: { name } }),
        confirmLabel: $t('messages.overwrite'),
        cancelLabel: $t('buttons.cancel'),
        variant: 'warning',
        onConfirm: () => saveValue(true),
      })
      return
    }
    await saveValue(false)
  }

  async function saveValue(overwrite: boolean) {
    localError = ''
    saving = true
    try {
      if (variable && nameChanged) {
        await renameVariable(originalName, name, scope as 'user' | 'system', overwrite)
        await setVariable(name, value, scope as 'user' | 'system', true)
      } else {
        // Normal set (new variable or just value change)
        await setVariable(name, value, scope as 'user' | 'system', overwrite || !!variable)
      }
      dispatch('save')
    } catch (err) {
      localError = err instanceof Error ? err.message : String(err)
      setTimeout(() => { localError = '' }, 4000)
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
        {variable ? $t('dialogs.editVariable') : $t('dialogs.addVariable')}
      </h2>
    </div>

    <div class="px-5 py-4 space-y-3">
      {#if localError}
        <div class="bg-red-50 border border-red-200 text-red-800 px-3 py-2 rounded-md text-xs dark:bg-red-900/30 dark:border-red-700 dark:text-red-300">
          {localError}
        </div>
      {/if}

      <div>
        <label for="edit-name" class="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
          {$t('labels.name')}
        </label>
        <input
          id="edit-name"
          type="text"
          bind:value={name}
          spellcheck="false"
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
        />
        {#if nameChanged}
          <p class="mt-1 text-[10px] text-amber-600 dark:text-amber-400">
            {$t('messages.renameWarning')}
          </p>
        {/if}
      </div>

      <div>
        <label for="edit-value" class="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
          {$t('labels.value')}
        </label>
        <textarea
          id="edit-value"
          bind:value={value}
          rows="4"
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 font-mono dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
        />
      </div>

      <div>
        <label for="edit-scope" class="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
          {$t('labels.scope')}
        </label>
        <select
          id="edit-scope"
          bind:value={scope}
          disabled={!!variable}
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white disabled:bg-gray-100 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
        >
          <option value="user">{$t('scope.user')}</option>
          <option value="system">{$t('scope.system')}</option>
        </select>
      </div>
    </div>

    <div class="px-5 py-3 border-t border-gray-200 flex gap-2 justify-end dark:border-gray-700">
      <button
        on:click={handleClose}
        disabled={saving}
        class="px-4 py-1.5 text-xs text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition disabled:opacity-50 dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
      >
        {$t('buttons.cancel')}
      </button>
      <button
        on:click={handleSave}
        disabled={saving}
        class="px-4 py-1.5 text-xs text-white bg-blue-600 rounded-md hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
      >
        {saving ? $t('messages.loading') : $t('buttons.save')}
      </button>
    </div>
  </div>
</div>
