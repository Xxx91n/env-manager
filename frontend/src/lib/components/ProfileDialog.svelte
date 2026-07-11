<script lang="ts">
  import { createEventDispatcher, onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { profiles } from '../stores'
  import {
    listProfiles,
    createProfile,
    deleteProfile,
    applyProfile,
    unapplyProfile,
    addProfileVar,
    removeProfileVar,
  } from '../api'
  import type { ProfileData } from '../api'

  const dispatch = createEventDispatcher()

  let profileList: ProfileData[] = []
  let selectedProfile: ProfileData | null = null
  let loading = false
  let actionLoading = false
  let message = ''
  let messageType = ''
  let newProfileName = ''
  let newVarName = ''
  let newVarValue = ''

  onMount(async () => {
    await refreshProfiles()
  })

  async function refreshProfiles() {
    loading = true
    try {
      profileList = await listProfiles()
      profiles.set(profileList)
      if (selectedProfile) {
        const updated = profileList.find((p) => p.name === (selectedProfile as ProfileData).name)
        selectedProfile = updated || null
      }
    } catch {
      // error handled in store
    } finally {
      loading = false
    }
  }

  function showMessage(msg: string, type: string) {
    message = msg
    messageType = type
    setTimeout(() => {
      message = ''
      messageType = ''
    }, 3000)
  }

  async function handleCreate() {
    const name = newProfileName.trim()
    if (!name) return
    actionLoading = true
    try {
      await createProfile(name)
      newProfileName = ''
      await refreshProfiles()
      showMessage($t('messages.profileCreated'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleDelete(profile: ProfileData) {
    if (!confirm($t('profiles.confirmDelete'))) return
    actionLoading = true
    try {
      await deleteProfile(profile.name)
      if (selectedProfile?.name === profile.name) selectedProfile = null
      await refreshProfiles()
      showMessage($t('messages.profileDeleted'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleApply(profile: ProfileData) {
    actionLoading = true
    try {
      await applyProfile(profile.name)
      await refreshProfiles()
      showMessage($t('messages.profileApplied'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleUnapply(profile: ProfileData) {
    if (!confirm($t('profiles.confirmUnapply'))) return
    actionLoading = true
    try {
      await unapplyProfile(profile.name)
      await refreshProfiles()
      showMessage($t('messages.profileUnapplied'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleAddVar() {
    if (!selectedProfile || !newVarName.trim()) return
    actionLoading = true
    try {
      await addProfileVar(selectedProfile.name, newVarName.trim(), newVarValue)
      newVarName = ''
      newVarValue = ''
      await refreshProfiles()
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleRemoveVar(varName: string) {
    if (!selectedProfile) return
    actionLoading = true
    try {
      await removeProfileVar(selectedProfile.name, varName)
      await refreshProfiles()
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  function selectProfile(p: ProfileData) {
    selectedProfile = p
  }

  function handleClose() {
    dispatch('close')
  }
</script>

<div class="fixed inset-0 bg-black/40 flex items-center justify-center z-50" on:click={handleClose}>
  <div class="bg-white rounded-lg shadow-xl max-w-2xl w-full mx-4 max-h-[85vh] flex flex-col dark:bg-gray-800" on:click|stopPropagation>
    <div class="px-5 py-3 border-b border-gray-200 dark:border-gray-700">
      <h2 class="text-sm font-semibold text-gray-900 dark:text-gray-100">
        {$t('nav.profiles')}
      </h2>
    </div>

    <div class="flex-1 overflow-y-auto px-5 py-4">
      {#if message}
        <div class="mb-3 p-2.5 rounded-md text-xs {messageType === 'success'
          ? 'bg-green-50 border border-green-200 text-green-800 dark:bg-green-900/30 dark:border-green-700 dark:text-green-300'
          : 'bg-red-50 border border-red-200 text-red-800 dark:bg-red-900/30 dark:border-red-700 dark:text-red-300'}">
          {message}
        </div>
      {/if}

      <!-- Create profile -->
      <div class="flex gap-2 mb-4">
        <input
          type="text"
          placeholder={$t('profiles.createPrompt')}
          bind:value={newProfileName}
          on:keydown={(e) => { if (e.key === 'Enter') handleCreate() }}
          class="flex-1 px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
        />
        <button
          on:click={handleCreate}
          disabled={actionLoading || !newProfileName.trim()}
          class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-white bg-blue-600 rounded-md hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
        >
          <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
            <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" />
          </svg>
          {$t('dialogs.createProfile')}
        </button>
      </div>

      {#if loading}
        <div class="flex justify-center py-8">
          <div class="animate-spin rounded-full h-5 w-5 border-b-2 border-blue-600"></div>
        </div>
      {:else if profileList.length === 0}
        <div class="px-4 py-8 text-center text-gray-400 text-xs dark:text-gray-500">
          {$t('profiles.empty')}
        </div>
      {:else}
        <div class="grid grid-cols-2 gap-4">
          <!-- Profile list -->
          <div class="space-y-1.5">
            {#each profileList as profile (profile.name)}
              <button
                on:click={() => selectProfile(profile)}
                class="w-full text-left px-3 py-2 rounded-md border transition {selectedProfile?.name === profile.name
                  ? 'border-blue-500 bg-blue-50 dark:bg-blue-900/20 dark:border-blue-500'
                  : 'border-gray-200 hover:bg-gray-50 dark:border-gray-600 dark:hover:bg-gray-700'}"
              >
                <div class="flex items-center justify-between">
                  <span class="text-xs font-medium text-gray-900 dark:text-gray-100">{profile.name}</span>
                  {#if profile.isEnabled}
                    <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-50 text-green-700 dark:bg-green-900/40 dark:text-green-300">
                      {$t('profiles.applied')}
                    </span>
                  {/if}
                </div>
                <span class="text-[10px] text-gray-400 dark:text-gray-500">{profile.variables.length} {$t('profiles.variables')}</span>
              </button>
            {/each}
          </div>

          <!-- Profile detail -->
          <div>
            {#if selectedProfile}
              <div class="space-y-3">
                <div class="flex items-center justify-between">
                  <h3 class="text-xs font-semibold text-gray-900 dark:text-gray-100">{selectedProfile.name}</h3>
                  <div class="flex gap-1">
                    {#if selectedProfile.isEnabled}
                      <button
                        on:click={() => { if (selectedProfile) handleUnapply(selectedProfile) }}
                        disabled={actionLoading}
                        class="px-2 py-1 text-[10px] font-medium text-gray-700 border border-gray-300 rounded hover:bg-gray-50 transition dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
                      >
                        {$t('profiles.unapply')}
                      </button>
                    {:else}
                      <button
                        on:click={() => { if (selectedProfile) handleApply(selectedProfile) }}
                        disabled={actionLoading}
                        class="px-2 py-1 text-[10px] font-medium text-white bg-green-600 rounded hover:bg-green-700 transition dark:bg-green-500 dark:hover:bg-green-600"
                      >
                        {$t('profiles.apply')}
                      </button>
                    {/if}
                    <button
                      on:click={() => { if (selectedProfile) handleDelete(selectedProfile) }}
                      disabled={actionLoading}
                      class="px-2 py-1 text-[10px] font-medium text-red-600 border border-red-300 rounded hover:bg-red-50 transition dark:text-red-400 dark:border-red-700 dark:hover:bg-red-900/30"
                    >
                      {$t('buttons.delete')}
                    </button>
                  </div>
                </div>

                <!-- Variables in profile -->
                <div class="space-y-1">
                  {#if selectedProfile.variables.length === 0}
                    <p class="text-[10px] text-gray-400 dark:text-gray-500 py-2">{$t('profiles.empty')}</p>
                  {:else}
                    {#each selectedProfile.variables as pv (pv.name)}
                      <div class="flex items-center gap-2 px-2 py-1 rounded bg-gray-50 dark:bg-gray-700/50">
                        <span class="text-[10px] font-mono text-gray-700 dark:text-gray-300 flex-1 truncate">{pv.name}</span>
                        <span class="text-[10px] font-mono text-gray-400 dark:text-gray-500 flex-1 truncate">{pv.value}</span>
                        <button
                          on:click={() => handleRemoveVar(pv.name)}
                          disabled={actionLoading}
                          class="p-0.5 text-gray-400 hover:text-red-600 rounded transition dark:hover:text-red-400"
                          title={$t('buttons.delete')}
                          aria-label={$t('buttons.delete')}
                        >
                          <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                            <path stroke-linecap="round" stroke-linejoin="round" d="M6 18L18 6M6 6l12 12" />
                          </svg>
                        </button>
                      </div>
                    {/each}
                  {/if}
                </div>

                <!-- Add variable -->
                <div class="space-y-1 pt-2 border-t border-gray-100 dark:border-gray-700">
                  <input
                    type="text"
                    placeholder={$t('labels.name')}
                    bind:value={newVarName}
                    class="w-full px-2 py-1 text-[10px] border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-blue-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
                  />
                  <input
                    type="text"
                    placeholder={$t('labels.value')}
                    bind:value={newVarValue}
                    class="w-full px-2 py-1 text-[10px] border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-blue-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
                  />
                  <button
                    on:click={handleAddVar}
                    disabled={actionLoading || !newVarName.trim()}
                    class="w-full px-2 py-1 text-[10px] font-medium text-white bg-blue-600 rounded hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
                  >
                    {$t('profiles.addVariable')}
                  </button>
                </div>
              </div>
            {:else}
              <div class="px-4 py-8 text-center text-gray-400 text-xs dark:text-gray-500">
                {$t('profiles.selectPrompt')}
              </div>
            {/if}
          </div>
        </div>
      {/if}
    </div>

    <div class="px-5 py-3 border-t border-gray-200 flex justify-end dark:border-gray-700">
      <button
        on:click={handleClose}
        class="px-4 py-1.5 text-xs text-gray-700 border border-gray-300 rounded-md hover:bg-gray-50 transition dark:text-gray-200 dark:border-gray-600 dark:hover:bg-gray-700"
      >
        {$t('buttons.close')}
      </button>
    </div>
  </div>
</div>
