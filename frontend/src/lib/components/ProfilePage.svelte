<script lang="ts">
  import { onMount } from 'svelte'
  import { t } from 'svelte-i18n'
  import { profiles, variables, showModal, refreshTrigger } from '../stores'
  import {
    listProfiles,
    createProfile,
    deleteProfile,
    applyProfile,
    unapplyProfile,
    addProfileVar,
    removeProfileVar,
    listVariables,
  } from '../api'
  import type { ProfileData, EnvVariable } from '../api'

  let profileList: ProfileData[] = []
  let selectedProfile: ProfileData | null = null
  let loading = false
  let actionLoading = false
  let message = ''
  let messageType = ''
  let newProfileName = ''
  let newVarName = ''
  let newVarValue = ''
  let showAddVarPanel = false
  let cloneSource = ''
  let allVars: EnvVariable[] = []

  onMount(async () => {
    await refreshProfiles()
  })

  // Watch refreshTrigger from App.svelte: refresh profiles when header
  // refresh button is clicked, regardless of active view.
  $: if ($refreshTrigger > 0) {
    refreshProfiles()
  }

  async function refreshProfiles() {
    loading = true
    try {
      profileList = await listProfiles()
      profiles.set(profileList)
      await listVariables()
      allVars = $variables
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

  function handleDelete(profile: ProfileData) {
    showModal({
      title: $t('dialogs.deleteConfirm'),
      message: $t('profiles.confirmDelete'),
      confirmLabel: $t('buttons.delete'),
      cancelLabel: $t('buttons.cancel'),
      variant: 'danger',
      onConfirm: async () => {
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
    })
  }

  async function handleToggleProfile(profile: ProfileData) {
    actionLoading = true
    try {
      if (profile.isEnabled) {
        await unapplyProfile(profile.name)
        showMessage($t('messages.profileUnapplied'), 'success')
      } else {
        await applyProfile(profile.name)
        showMessage($t('messages.profileApplied'), 'success')
      }
      await refreshProfiles()
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
      cloneSource = ''
      showAddVarPanel = false
      await refreshProfiles()
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  function handleCloneSelect() {
    if (!cloneSource) return
    const found = allVars.find((v) => v.name === cloneSource)
    if (found) {
      newVarName = found.name
      newVarValue = found.value
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
    // Toggle: if the same profile is already selected, collapse it
    if (selectedProfile?.name === p.name) {
      selectedProfile = null
    } else {
      selectedProfile = p
    }
    showAddVarPanel = false
    newVarName = ''
    newVarValue = ''
    cloneSource = ''
  }
</script>

<div class="space-y-3">
  {#if message}
    <div class="fixed top-4 left-1/2 -translate-x-1/2 px-3 py-1.5 rounded-md text-xs shadow-lg z-50 pointer-events-none transition-opacity {messageType === 'success'
      ? 'bg-green-600 text-white'
      : 'bg-red-600 text-white'}">
      {message}
    </div>
  {/if}

  <!-- Create profile bar -->
  <div class="flex gap-2">
    <input
      type="text"
      placeholder={$t('profiles.createPrompt')}
      bind:value={newProfileName}
      on:keydown={(e) => { if (e.key === 'Enter') handleCreate() }}
      class="flex-1 px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-blue-500 focus:border-blue-500 dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
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
    <!-- Profile list with toggle switches (like PowerToys) -->
    <div class="space-y-2">
      {#each profileList as profile (profile.name)}
        <div class="bg-white rounded-md border border-gray-200 dark:bg-gray-800 dark:border-gray-700">
          <!-- Profile row with toggle -->
          <div class="flex items-center justify-between px-4 py-2.5">
            <button
              on:click={() => selectProfile(profile)}
              class="flex items-center gap-3 flex-1 text-left"
            >
              <span class="text-xs font-medium text-gray-900 dark:text-gray-100">{profile.name}</span>
              <span class="text-[10px] text-gray-400 dark:text-gray-500">{profile.variables.length} {$t('profiles.variables')}</span>
              {#if profile.isEnabled}
                <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-green-50 text-green-700 dark:bg-green-900/40 dark:text-green-300">
                  {$t('profiles.applied')}
                </span>
              {/if}
            </button>

            <div class="flex items-center gap-3">
              <!-- Toggle switch -->
              <button
                on:click={() => handleToggleProfile(profile)}
                disabled={actionLoading}
                class="relative inline-flex h-4 w-7 items-center rounded-full transition disabled:opacity-50 {profile.isEnabled ? 'bg-blue-600 dark:bg-blue-500' : 'bg-gray-300 dark:bg-gray-600'}"
                role="switch"
                aria-checked={profile.isEnabled}
                title={profile.isEnabled ? $t('profiles.unapply') : $t('profiles.apply')}
              >
                <span class="inline-block h-3 w-3 transform rounded-full bg-white shadow transition {profile.isEnabled ? 'translate-x-3.5' : 'translate-x-0.5'}"></span>
              </button>

              <!-- Delete button -->
              <button
                on:click={() => handleDelete(profile)}
                disabled={actionLoading}
                class="p-1 text-gray-400 hover:text-red-600 rounded transition dark:hover:text-red-400"
                title={$t('buttons.delete')}
                aria-label={$t('buttons.delete')}
              >
                <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M19 7l-.867 12.142A2 2 0 0116.138 21H7.862a2 2 0 01-1.995-1.858L5 7m5 4v6m4-6v6m1-10V4a1 1 0 00-1-1h-4a1 1 0 00-1 1v3M4 7h16" />
                </svg>
              </button>
            </div>
          </div>

          <!-- Expanded detail panel -->
          {#if selectedProfile?.name === profile.name}
            <div class="border-t border-gray-100 px-4 py-3 dark:border-gray-700">
              {#if profile.variables.length === 0}
                <p class="text-[10px] text-gray-400 dark:text-gray-500 py-2">{$t('profiles.noVariables')}</p>
              {:else}
                <div class="space-y-1 mb-2">
                  {#each profile.variables as pv (pv.name)}
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
                </div>
              {/if}

              <!-- Add variable button -->
              {#if !showAddVarPanel}
                <button
                  on:click={() => (showAddVarPanel = true)}
                  class="flex items-center gap-1 text-[10px] font-medium text-blue-600 hover:text-blue-700 dark:text-blue-400"
                >
                  <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" />
                  </svg>
                  {$t('profiles.addVariable')}
                </button>
              {:else}
                <!-- Add variable panel with clone-from-existing dropdown -->
                <div class="space-y-1.5 pt-2 border-t border-gray-100 dark:border-gray-700">
                  <!-- Clone from existing variable -->
                  <div>
                    <label class="block text-[10px] font-medium text-gray-500 dark:text-gray-400 mb-0.5">
                      {$t('profiles.cloneFromExisting')}
                    </label>
                    <select
                      bind:value={cloneSource}
                      on:change={handleCloneSelect}
                      class="w-full px-2 py-1 text-[10px] border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-blue-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
                    >
                      <option value="">-- {$t('profiles.selectVariable')} --</option>
                      {#each allVars as v (v.name)}
                        <option value={v.name}>{v.name}</option>
                      {/each}
                    </select>
                  </div>

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
                  <div class="flex gap-1">
                    <button
                      on:click={handleAddVar}
                      disabled={actionLoading || !newVarName.trim()}
                      class="flex-1 px-2 py-1 text-[10px] font-medium text-white bg-blue-600 rounded hover:bg-blue-700 transition disabled:opacity-50 dark:bg-blue-500 dark:hover:bg-blue-600"
                    >
                      {$t('buttons.save')}
                    </button>
                    <button
                      on:click={() => { showAddVarPanel = false; newVarName = ''; newVarValue = ''; cloneSource = '' }}
                      class="px-2 py-1 text-[10px] text-gray-600 border border-gray-300 rounded hover:bg-gray-50 transition dark:text-gray-300 dark:border-gray-600 dark:hover:bg-gray-700"
                    >
                      {$t('buttons.cancel')}
                    </button>
                  </div>
                </div>
              {/if}
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/if}
</div>
