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
      alert($t('labels.name'))
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

<div class="fixed inset-0 bg-black bg-opacity-50 flex items-center justify-center z-50">
  <div class="bg-white rounded-lg shadow-lg max-w-md w-full mx-4">
    <div class="p-6 border-b border-gray-200">
      <h2 class="text-lg font-semibold text-gray-900">
        {variable ? $t('dialogs.editVariable') : $t('dialogs.addVariable')}
      </h2>
    </div>

    <div class="p-6 space-y-4">
      <div>
        <label for="edit-name" class="block text-sm font-medium text-gray-700 mb-1">
          {$t('labels.name')}
        </label>
        <input
          id="edit-name"
          type="text"
          bind:value={name}
          disabled={!!variable}
          class="w-full px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 disabled:bg-gray-100"
        />
      </div>

      <div>
        <label for="edit-value" class="block text-sm font-medium text-gray-700 mb-1">
          {$t('labels.value')}
        </label>
        <textarea
          id="edit-value"
          bind:value={value}
          rows="3"
          class="w-full px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 font-mono text-sm"
        />
      </div>

      <div>
        <label for="edit-scope" class="block text-sm font-medium text-gray-700 mb-1">
          {$t('labels.scope')}
        </label>
        <select
          id="edit-scope"
          bind:value={scope}
          class="w-full px-3 py-2 border border-gray-300 rounded-lg focus:outline-none focus:ring-2 focus:ring-blue-500 bg-white"
        >
          <option value="user">{$t('scope.user')}</option>
          <option value="system">{$t('scope.system')}</option>
        </select>
      </div>
    </div>

    <div class="p-6 border-t border-gray-200 flex gap-3 justify-end">
      <button
        on:click={handleClose}
        disabled={saving}
        class="px-4 py-2 text-gray-700 border border-gray-300 rounded-lg hover:bg-gray-50 transition disabled:opacity-50"
      >
        {$t('buttons.cancel')}
      </button>
      <button
        on:click={handleSave}
        disabled={saving}
        class="px-4 py-2 bg-blue-600 text-white rounded-lg hover:bg-blue-700 transition disabled:opacity-50"
      >
        {saving ? $t('messages.loading') : $t('buttons.save')}
      </button>
    </div>
  </div>
</div>
