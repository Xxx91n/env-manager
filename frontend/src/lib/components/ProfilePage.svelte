<script lang="ts">
  import { onMount } from 'svelte'
  import {
    loadProfileOrder as loadStoredOrder, saveProfileOrder as saveStoredOrder,
    applyStoredOrder as applyStored,
  } from '../profileDrag'
  
  import { t } from 'svelte-i18n'
  import { profiles, variables, showModal, refreshTrigger } from '../stores'
  import { showToast } from '../stores'
  import {
    listProfiles,
    createProfile,
    deleteProfile,
    applyProfile,
    unapplyProfile,
    addProfileVar,
    removeProfileVar,
    listVariables,
    exportProfile,
    importProfile,
    renameProfile,
    pickOpenFile,
    pickSaveFile,
    setProfileInheritance,
    addProfilePath,
    removeProfilePath,
    pickExecutableFile,
    profileSetLaunch,
    profileLaunch,
    profileAddSecret,
    profileRemoveSecret,
  } from '../api'
  import type { ProfileData, EnvVariable } from '../api'

  let profileList: ProfileData[] = []
  let selectedProfile: ProfileData | null = null
  let loading = false
  let actionLoading = false
  let newProfileName = ''
  let newVarName = ''
  let newVarValue = ''
  let showAddVarPanel = false
  let cloneSource = ''
  let allVars: EnvVariable[] = []
  let newPathEntry = ''
  let newProfileType: 'global' | 'launch' = 'global'
  let newProfileTarget = ''
  let newProfileArgs = ''
  let newProfileCwd = ''
  let showAddSecretPanel = false
  let newSecretName = ''
  let newSecretValue = ''
  // Drag state as plain component-level variables for Svelte reactivity
  let dragIndex: number | null = null
  let dragOverIndex: number | null = null
  let isDragging = false
  let pointerY = 0
  let startY = 0
  $: deltaY = isDragging ? pointerY - startY : 0

  // loadProfileOrder/saveProfileOrder/applyStoredOrder imported from ../profileDrag
  // as loadStoredOrder/saveStoredOrder/applyStored respectively.

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
      profileList = applyStored(await listProfiles())
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
    showToast(msg, type === 'success' ? 'success' : type === 'error' ? 'error' : 'info')
  }

  // Map CLI hardcoded error messages to i18n keys for localization
  function localizeError(errMsg: string): string {
    // Profile already exists
    if (/already exists/i.test(errMsg) && /profile/i.test(errMsg)) {
      return $t('messages.profileAlreadyExists')
    }
    // PATH duplicate
    if (/already exists in PATH/i.test(errMsg)) {
      return $t('messages.pathDuplicate')
    }
    return errMsg
  }

  async function handleCreate() {
    const name = newProfileName.trim()
    if (!name) return
    if (newProfileType === 'launch' && !newProfileTarget.trim()) {
      showMessage($t('messages.profileTargetRequired'), 'error')
      return
    }
    actionLoading = true
    try {
      await createProfile(name)
      if (newProfileType === 'launch') {
        await profileSetLaunch(name, {
          target: newProfileTarget.trim(),
          args: newProfileArgs || undefined,
          cwd: newProfileCwd || undefined,
          type: 'launch',
        })
      }
      newProfileName = ''
      newProfileTarget = ''
      newProfileArgs = ''
      newProfileCwd = ''
      await refreshProfiles()
      showMessage($t('messages.profileCreated'), 'success')
    } catch (err) {
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleBrowseTarget() {
    try {
      const picked = await pickExecutableFile($t('profiles.selectExecutable'))
      if (picked) newProfileTarget = picked
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    }
  }

  async function handleLaunchProfile(profile: ProfileData) {
    if (profile.profileType !== 'launch' || !profile.targetExecutable) {
      showMessage($t('messages.profileNotLaunch'), 'error')
      return
    }
    actionLoading = true
    try {
      await profileLaunch(profile.name)
      showMessage($t('messages.profileLaunched'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleAddSecret() {
    if (!selectedProfile) return
    const name = newSecretName.trim()
    if (!name || !newSecretValue) return
    actionLoading = true
    try {
      await profileAddSecret(selectedProfile.name, name, newSecretValue)
      newSecretName = ''
      newSecretValue = ''
      showAddSecretPanel = false
      await refreshProfiles()
      showMessage($t('messages.secretAdded'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleRemoveSecret(varName: string) {
    if (!selectedProfile) return
    actionLoading = true
    try {
      await profileRemoveSecret(selectedProfile.name, varName)
      await refreshProfiles()
      showMessage($t('messages.secretRemoved'), 'success')
    } catch (err) {
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleExport(profile: ProfileData) {
    const defaultPath = `${profile.name}.json`
    const fileName = await pickSaveFile($t('profiles.exportFilePrompt'), defaultPath)
    if (!fileName) return
    actionLoading = true
    try {
      await exportProfile(profile.name, fileName)
      showMessage($t('messages.profileExported'), 'success')
    } catch (err) {
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleImport() {
    const fileName = await pickOpenFile($t('profiles.importFilePrompt'), '')
    if (!fileName) return
    actionLoading = true
    try {
      await importProfile(fileName)
      await refreshProfiles()
      showMessage($t('messages.profileImported'), 'success')
    } catch (err) {
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }

  async function handleRename(profile: ProfileData) {
    const newName = prompt($t('profiles.renamePrompt'), profile.name)
    if (!newName || newName === profile.name) return
    actionLoading = true
    try {
      await renameProfile(profile.name, newName.trim())
      await refreshProfiles()
      showMessage($t('messages.profileRenamed'), 'success')
    } catch (err) {
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
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
          showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
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
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
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
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
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
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }

  function beginPointerDrag(event: PointerEvent, index: number): void {
    if (event.button !== 0) return
    event.preventDefault()
    dragIndex = index
    dragOverIndex = index
    isDragging = true
    pointerY = event.clientY
    startY = event.clientY
  }

  function enterPointerTarget(index: number): void {
    if (isDragging) dragOverIndex = index
  }

  function finishPointerDrag(index: number): void {
    if (!isDragging || dragIndex === null) {
      cancelPointerDrag()
      return
    }
    const targetIndex = dragOverIndex ?? index
    if (dragIndex !== targetIndex) {
      const item = profileList[dragIndex]
      const newList = [...profileList]
      newList.splice(dragIndex, 1)
      newList.splice(targetIndex, 0, item)
      profileList = newList
      saveStoredOrder(profileList.map((p) => p.name))
    }
    cancelPointerDrag()
  }

  function cancelPointerDrag(): void {
    dragIndex = null
    dragOverIndex = null
    isDragging = false
    pointerY = 0
    startY = 0
  }

  async function handleInheritance(parent: string, enabled: boolean) {
    if (!selectedProfile) return
    const parents = enabled
      ? [...(selectedProfile.inherits ?? []), parent]
      : (selectedProfile.inherits ?? []).filter(name => name !== parent)
    try {
      await setProfileInheritance(selectedProfile.name, parents)
      await refreshProfiles()
    } catch (err) {
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    }
  }

  async function handleAddPath() {
    if (!selectedProfile || !newPathEntry.trim()) return
    try {
      await addProfilePath(selectedProfile.name, newPathEntry.trim())
      newPathEntry = ''
      await refreshProfiles()
    } catch (err) {
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    }
  }

  async function handleRemovePath(path: string) {
    if (!selectedProfile) return
    await removeProfilePath(selectedProfile.name, path)
    await refreshProfiles()
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

<svelte:window on:pointermove={(e) => { if (isDragging) pointerY = e.clientY }} on:pointerup={cancelPointerDrag} on:pointercancel={cancelPointerDrag} />

<div class="space-y-3">
  <!-- Create profile bar -->
  <div class="space-y-2">
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

    <button
      on:click={handleImport}
      disabled={actionLoading}
      class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-gray-700 bg-white border border-gray-300 rounded-md hover:bg-gray-50 transition disabled:opacity-50 dark:text-gray-200 dark:bg-gray-800 dark:border-gray-600 dark:hover:bg-gray-700"
      title={$t('profiles.import')}
    >
      <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
        <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-8l-4-4m0 0L8 8m4-4v12" />
      </svg>
      {$t('profiles.import')}
    </button>
    </div>
    <!-- Profile type selector + launch-specific fields -->
    <div class="flex items-center gap-2 flex-wrap">
      <label class="text-xs font-medium text-gray-600 dark:text-gray-400">
        <input type="radio" bind:group={newProfileType} value="global" class="mr-1" /> {$t('profiles.typeGlobal')}
      </label>
      <label class="text-xs font-medium text-gray-600 dark:text-gray-400">
        <input type="radio" bind:group={newProfileType} value="launch" class="mr-1" /> {$t('profiles.typeLaunch')}
      </label>
      {#if newProfileType === 'launch'}
        <input
          type="text"
          placeholder={$t('profiles.targetExecutable')}
          bind:value={newProfileTarget}
          class="flex-1 min-w-[200px] px-2 py-1 text-xs border border-gray-300 rounded-md font-mono dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
        />
        <button
          on:click={handleBrowseTarget}
          class="px-2 py-1 text-xs text-blue-600 bg-blue-50 rounded-md hover:bg-blue-100 dark:bg-blue-900/20 dark:text-blue-300"
          title={$t('profiles.browse')}
        >
          {$t('profiles.browse')}
        </button>
        <input
          type="text"
          placeholder={$t('profiles.launchArgs')}
          bind:value={newProfileArgs}
          class="flex-1 min-w-[160px] px-2 py-1 text-xs border border-gray-300 rounded-md font-mono dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
        />
        <input
          type="text"
          placeholder={$t('profiles.workingDir')}
          bind:value={newProfileCwd}
          class="flex-1 min-w-[160px] px-2 py-1 text-xs border border-gray-300 rounded-md font-mono dark:bg-gray-800 dark:border-gray-600 dark:text-gray-100"
        />
      {/if}
    </div>
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
    <div class="space-y-2" role="list" data-testid="profile-list">
      {#each profileList as profile, i (profile.name)}
        <div
          class="bg-white rounded-md border border-gray-200 transition-[border-color,box-shadow,opacity,transform] dark:bg-gray-800 dark:border-gray-700 {isDragging && dragIndex === i ? 'opacity-80 shadow-lg z-10 scale-[1.02]' : ''} {isDragging && dragOverIndex === i && dragIndex !== i ? 'ring-2 ring-blue-500 border-blue-500' : ''}"
          style={isDragging && dragIndex === i ? `transform: translateY(${deltaY}px); cursor: grabbing;` : ''}
          role="listitem"
          data-profile-name={profile.name}
          on:pointerenter={() => enterPointerTarget(i)}
          on:pointerup={() => finishPointerDrag(i)}
        >
          <!-- Profile row with toggle -->
          <div class="flex items-center justify-between px-4 py-2.5">
            <div
              class="profile-drag-handle flex items-center gap-1 cursor-grab active:cursor-grabbing touch-none text-gray-300 hover:text-gray-500 dark:text-gray-600"
              title={$t('profiles.dragToSort')}
              role="button"
              tabindex="0"
              aria-label={$t('profiles.dragToSort')}
              data-testid={`profile-drag-handle-${i}`}
              on:pointerdown={(event) => beginPointerDrag(event, i)}
            >
              <svg class="w-3 h-3 pointer-events-none" draggable="false" fill="currentColor" viewBox="0 0 20 20">
                <path d="M7 4a1 1 0 11-2 0 1 1 0 012 0zM9 4a1 1 0 11-2 0 1 1 0 012 0zM11 4a1 1 0 11-2 0 1 1 0 012 0zM13 4a1 1 0 11-2 0 1 1 0 012 0zM7 8a1 1 0 11-2 0 1 1 0 012 0zM9 8a1 1 0 11-2 0 1 1 0 012 0zM11 8a1 1 0 11-2 0 1 1 0 012 0zM13 8a1 1 0 11-2 0 1 1 0 012 0zM7 12a1 1 0 11-2 0 1 1 0 012 0zM9 12a1 1 0 11-2 0 1 1 0 012 0zM11 12a1 1 0 11-2 0 1 1 0 012 0zM13 12a1 1 0 11-2 0 1 1 0 012 0zM7 16a1 1 0 11-2 0 1 1 0 012 0zM9 16a1 1 0 11-2 0 1 1 0 012 0zM11 16a1 1 0 11-2 0 1 1 0 012 0zM13 16a1 1 0 11-2 0 1 1 0 012 0z" />
              </svg>
            </div>
            <button
              on:click={() => selectProfile(profile)}
              class="flex items-center gap-3 flex-1 text-left"
            >
              <span class="text-xs font-medium text-gray-900 dark:text-gray-100">{profile.name}</span>
              {#if profile.profileType === 'launch'}
                <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-purple-50 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300" title={profile.targetExecutable || ''}>
                  {$t('profiles.typeLaunch')}
                </span>
              {:else}
                <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-blue-50 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300">
                  {$t('profiles.typeGlobal')}
                </span>
              {/if}
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

              {#if profile.profileType === 'launch' && profile.targetExecutable}
                <!-- Launch button (Launch profiles only) -->
                <button
                  on:click={() => handleLaunchProfile(profile)}
                  disabled={actionLoading}
                  class="p-1 text-green-600 hover:bg-green-50 rounded transition dark:text-green-400 dark:hover:bg-green-900/30"
                  title={$t('profiles.launch')}
                  aria-label={$t('profiles.launch')}
                >
                  <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                    <path stroke-linecap="round" stroke-linejoin="round" d="M14.752 11.168l-3.197-2.132A1 1 0 0010 9.87v4.263a1 1 0 001.555.832l3.197-2.132a1 1 0 000-1.664z" />
                    <path stroke-linecap="round" stroke-linejoin="round" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z" />
                  </svg>
                </button>
              {/if}
              <!-- Export button -->
              <button
                on:click={() => handleExport(profile)}
                disabled={actionLoading}
                class="p-1 text-gray-400 hover:text-blue-600 rounded transition dark:hover:text-blue-400"
                title={$t('profiles.export')}
                aria-label={$t('profiles.export')}
              >
                <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M4 16v1a3 3 0 003 3h10a3 3 0 003-3v-1m-4-4l-4 4m0 0l-4-4m4 4V4" />
                </svg>
              </button>
              <!-- Rename button -->
              <button
                on:click={() => handleRename(profile)}
                disabled={actionLoading}
                class="p-1 text-gray-400 hover:text-blue-600 rounded transition dark:hover:text-blue-400"
                title={$t('profiles.rename')}
                aria-label={$t('profiles.rename')}
              >
                <svg class="w-3.5 h-3.5" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2">
                  <path stroke-linecap="round" stroke-linejoin="round" d="M11 5H6a2 2 0 00-2 2v11a2 2 0 002 2h11a2 2 0 002-2v-5m-1.414-9.414a2 2 0 112.828 2.828L11.828 15H9v-2.828l8.586-8.586z" />
                </svg>
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
              <div class="grid grid-cols-2 gap-4 mb-3 pb-3 border-b border-gray-100 dark:border-gray-700">
                <div>
                  <div class="text-[10px] font-medium text-gray-500 mb-1">{$t('profiles.inherits')}</div>
                  <div class="space-y-1 max-h-20 overflow-y-auto">
                    {#each profileList.filter(candidate => candidate.name !== profile.name) as parent (parent.name)}
                      <label class="flex items-center gap-1.5 text-[10px] text-gray-600 dark:text-gray-300">
                        <input type="checkbox" checked={profile.inherits?.includes(parent.name)} on:change={(event) => handleInheritance(parent.name, event.currentTarget.checked)} />
                        <span class="truncate">{parent.name}</span>
                      </label>
                    {/each}
                  </div>
                </div>
                <div>
                  <div class="text-[10px] font-medium text-gray-500 mb-1">{$t('profiles.pathEntries')}</div>
                  <div class="space-y-1 mb-1">
                    {#each profile.pathEntries ?? [] as path (path)}
                      <div class="flex items-center gap-1 text-[10px] font-mono">
                        <span class="truncate flex-1" title={path}>{path}</span>
                        <button on:click={() => handleRemovePath(path)} disabled={profile.isEnabled} class="text-red-500 disabled:opacity-30" aria-label={$t('buttons.delete')}>×</button>
                      </div>
                    {/each}
                  </div>
                  <div class="flex gap-1">
                    <input bind:value={newPathEntry} disabled={profile.isEnabled} on:keydown={(event) => { if (event.key === 'Enter') handleAddPath() }} class="min-w-0 flex-1 px-2 py-1 text-[10px] font-mono border rounded dark:bg-gray-700 dark:border-gray-600" placeholder={$t('path.entryPlaceholder')} />
                    <button on:click={handleAddPath} disabled={profile.isEnabled || !newPathEntry.trim()} class="px-2 text-[10px] text-blue-600 disabled:opacity-30">{$t('buttons.add')}</button>
                  </div>
                </div>
              </div>
              {#if profile.variables.length === 0}
                <p class="text-[10px] text-gray-400 dark:text-gray-500 py-2">{$t('profiles.noVariables')}</p>
              {:else}
                <div class="space-y-1 mb-2">
                  {#each profile.variables as pv (pv.name)}
                    <div class="flex items-center gap-2 px-2 py-1 rounded bg-gray-50 dark:bg-gray-700/50">
                      <span class="text-[10px] font-mono text-gray-700 dark:text-gray-300 flex-1 truncate">{pv.name}</span>
                      <span class="text-[10px] font-mono text-gray-400 dark:text-gray-500 flex-1 truncate">
                        {#if selectedProfile?.secretVariables?.includes(pv.name)}
                          {'<encrypted>'}
                        {:else}
                          {pv.value}
                        {/if}
                      </span>
                      {#if selectedProfile?.secretVariables?.includes(pv.name)}
                        <svg class="w-3 h-3 text-amber-500" fill="currentColor" viewBox="0 0 24 24" title={$t('messages.secretVariable')}>
                          <path d="M12 15v2m-6 4h12a2 2 0 002-2v-6a2 2 0 00-2-2H6a2 2 0 00-2 2v6a2 2 0 002 2zm10-10V7a4 4 0 00-8 0v4h8z" />
                        </svg>
                      {/if}
                      <button
                        on:click={() => selectedProfile?.secretVariables?.includes(pv.name) ? handleRemoveSecret(pv.name) : handleRemoveVar(pv.name)}
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
                    <label for="profile-clone-source" class="block text-[10px] font-medium text-gray-500 dark:text-gray-400 mb-0.5">
                      {$t('profiles.cloneFromExisting')}
                    </label>
                    <select
                      id="profile-clone-source"
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

              {#if profile.profileType === 'launch' && !profile.isEnabled}
                {#if !showAddSecretPanel}
                  <button
                    on:click={() => (showAddSecretPanel = true)}
                    class="flex items-center gap-1 text-[10px] font-medium text-amber-600 hover:text-amber-700 dark:text-amber-400 mt-2"
                  >
                    <svg class="w-3 h-3" fill="none" stroke="currentColor" viewBox="0 0 24 24" stroke-width="2.5">
                      <path stroke-linecap="round" stroke-linejoin="round" d="M12 4v16m8-8H4" />
                    </svg>
                    {$t('profiles.addSecret')}
                  </button>
                {:else}
                  <div class="space-y-1.5 pt-2 border-t border-gray-100 dark:border-gray-700">
                    <input
                      type="text"
                      placeholder={$t('labels.name')}
                      bind:value={newSecretName}
                      class="w-full px-2 py-1 text-[10px] border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-amber-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
                    />
                    <input
                      type="password"
                      placeholder={$t('profiles.secretValue')}
                      bind:value={newSecretValue}
                      class="w-full px-2 py-1 text-[10px] border border-gray-300 rounded focus:outline-none focus:ring-1 focus:ring-amber-500 dark:bg-gray-700 dark:border-gray-600 dark:text-gray-100"
                    />
                    <div class="flex gap-1">
                      <button
                        on:click={handleAddSecret}
                        disabled={actionLoading || !newSecretName.trim() || !newSecretValue}
                        class="flex-1 px-2 py-1 text-[10px] font-medium text-white bg-amber-600 rounded hover:bg-amber-700 transition disabled:opacity-50 dark:bg-amber-500 dark:hover:bg-amber-600"
                      >
                        {$t('profiles.addSecret')}
                      </button>
                      <button
                        on:click={() => { showAddSecretPanel = false; newSecretName = ''; newSecretValue = '' }}
                        class="px-2 py-1 text-[10px] text-gray-600 border border-gray-300 rounded hover:bg-gray-50 transition dark:text-gray-300 dark:border-gray-600 dark:hover:bg-gray-700"
                      >
                        {$t('buttons.cancel')}
                      </button>
                    </div>
                    <p class="text-[9px] text-gray-400 dark:text-gray-500">{$t('profiles.secretHint')}</p>
                  </div>
                {/if}
              {/if}
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/if}
</div>
