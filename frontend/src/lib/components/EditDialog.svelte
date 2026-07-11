<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { t } from 'svelte-i18n'
  import { setVariable } from '../api'

  export let variable = null

  const dispatch = createEventDispatcher()

  let name = variable?.name || ''
  let value = variable?.value || ''
  let scope = variable?.scope || 'user'
  let saving = false

  async function handleSave() {
    if (!name.trim()) {
      alert($t('errors.invalidInput'))
      return
    }

    saving = true
    try {
      await setVariable(name, value, scope as 'user' | 'system')
      dispatch('save')
    } finally {
      saving = false
    }
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div class="fixed inset-0 bg-black/40 flex items-center justify-center z-50" on:click={handleClose}>
  <div class="bg-white rounded-lg shadow-xl max-w-md w-full mx-4 dark:bg-gray-800" on:click|stopPropagation>
    <div class="px-5 py-3 border-b border-gray-200 dark:border-gray-700">
      <h2 class="text-sm font-semibold text-gray-900 dark:text-gray-100">
        {variable ? $t('dialogs.editVariable') : $t('dialogs.addVariable')}
      </h2>
    </div>

    <div class="px-5 py-4 space-y-3">
      <div>
        <label for="edit-name" class="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">
          {$t('labels.name')}
        </label>
        <input
          id="edit-name"
          type="text"
          bind:value={name}
          disabled={!!variable}
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 disabled:bg-gray-100 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
        />
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
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 bg-white dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
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
