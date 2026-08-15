<script lang="ts">
  import { Plus, Download, MapPin, Play, Pencil, Trash2, X, Power } from 'lucide-svelte'
  import { onMount } from 'svelte'
  import { saveProfileOrder as saveStoredOrder, applyStoredOrder as applyStored } from '../profileDrag'
  
  import { t } from 'svelte-i18n'
  import CloneCombobox from './CloneCombobox.svelte'
  import { profiles, showModal, refreshTrigger, openInputDialog, pathProfileIndex } from '../stores'
  import { showToast } from '../stores'
  import { frontendLog } from '../settingsStore'
  import {
    listProfiles,
    createProfile,
    deleteProfile,
    applyProfile,
    unapplyProfile,
    addProfileVar,
    removeProfileVar,
    listVariablesRaw,
    listPathEntries,
    exportProfile,
    importProfile,
    renameProfile,
    pickOpenFile,
    pickSaveFile,
    setProfileInheritance,
    addProfilePath,
    removeProfilePath,
    pickExecutableFile,
    profileLaunch,
    profileAddSecret,
    profileRemoveSecret,
    secretProviderList,
    secretProviderSet,
  } from '../api'
  import type { ProfileData, EnvVariable } from '../api'
  let profileList: ProfileData[] = []
  let selectedProfile: ProfileData | null = null
  let loading = false
  let actionLoading = false
  let newProfileName = ''
  let newVarName = ''
  let newVarValue = ''
  let newVarScope: 'user' | 'system' = 'user'
  let showAddVarPanel = false
  let allVars: EnvVariable[] = []
  let newPathEntry = ''
  let newPathScope: 'user' | 'system' = 'user'
  let pathPool: { name: string; value?: string }[] = []
  let newProfileType: 'global' | 'launch' = 'global'
  let newProfileTarget = ''
  let newProfileArgs = ''
  let newProfileCwd = ''
  let showAddSecretPanel = false
  let newSecretName = ''
  let newSecretValue = ''
  // v0.8 secret provider state
  let activeProvider = 'dpapi-current-user'
  let availableProviders: string[] = []


  // Input validation for variable names and PATH entries (v0.7.5 hard boundary).
  // Rejects characters that could cause CLI parsing issues or registry corruption.
  function validateVarNameInput(name: string): string | null {
    if (/ [=\0\r\n\t]/.test(name)) return $t('errors.varNameInvalid')
    if (name.length > 255) return $t('errors.varNameTooLong')
    return null
  }
  function validatePathInput(p: string): string | null {
    if (/[;\0\r\n]/.test(p)) return $t('errors.pathInvalidChars')
    if (p.length > 32767) return $t('errors.pathTooLong')
    return null
  }    // Pointer drag state remains local. Registry/profile persistence is never
  // touched by a GUI-only ordering change.
  let dragIndex: number | null = null
  let isDragging = false
  let pointerId: number | null = null
  onMount(() => {
    void refreshProfiles()
    void loadProviderInfo()
  })
  // Map provider name to i18n key
  function providerDisplayName(name: string): string {
    const map: Record<string, string> = {
      'dpapi-current-user': $t('secrets.providerDpapi'),
      'credential-manager': $t('secrets.providerCredMan'),
      'powershell-secretmanagement': $t('secrets.providerPsm'),
      'vault-kv2': $t('secrets.providerVault'),
      'sops': $t('secrets.providerSops'),
      'azure-keyvault': $t('secrets.providerAzure'),
      '1password': $t('secrets.provider1Password'),
      'aws-secretsmanager': $t('secrets.providerAws'),
    }
    return map[name] ?? name
  }
  async function loadProviderInfo() {
    try {
      const result = await secretProviderList()
      const lines = result.split('\n')
      for (const line of lines) {
        if (line.startsWith('Active provider:')) {
          activeProvider = line.split(':')[1].trim()
        } else if (line.trim().startsWith('dpapi-current-user') ||
                   line.trim().startsWith('credential-manager') ||
                   line.trim().startsWith('powershell-secretmanagement') ||
                   line.trim().startsWith('vault-kv2') ||
                   line.trim().startsWith('sops') ||
                   line.trim().startsWith('azure-keyvault') ||
                   line.trim().startsWith('1password') ||
                   line.trim().startsWith('aws-secretsmanager')) {
          // This is an available provider line (possibly with "(active)" suffix)
          const name = line.trim().replace('(active)', '').trim()
          if (name && !availableProviders.includes(name)) {
            availableProviders = [...availableProviders, name]
          }
        }
      }
    } catch {
      // keep defaults
    }
  }
  // Watch refreshTrigger from App.svelte: refresh profiles when header
  // refresh button is clicked, regardless of active view.
  $: if ($refreshTrigger > 0) {
    refreshProfiles()
  }
  let profileRefreshEpoch = 0
  async function refreshProfiles() {
    const requestEpoch = ++profileRefreshEpoch
    loading = true
    try {
      const [nextProfiles, nextVariables, nextPathEntriesUser, nextPathEntriesSystem] = await Promise.all([
        listProfiles(),
        listVariablesRaw(),
        listPathEntries('user').catch(() => []),
        listPathEntries('system').catch(() => []),
      ])
      if (requestEpoch !== profileRefreshEpoch) return
      profileList = applyStored(nextProfiles)
      profiles.set(profileList)
      // Build reverse index: normalized path -> contributing profile names
      const idx = new Map<string, string[]>()
      for (const p of profileList) {
        for (const pe of (p.pathEntries ?? [])) {
          const norm = pe.toLowerCase().replace(/\\+$/, "")
          const arr = idx.get(norm) ?? []
          arr.push(p.name)
          idx.set(norm, arr)
        }
      }
      pathProfileIndex.set(idx)
      allVars = nextVariables
      // Build a searchable pool of existing PATH entries for the add-path
      // CloneCombobox. Deduplicate by path so entries appearing in both
      // scopes show once; keep scope as the value preview.
      const seenPaths = new Set<string>()
      pathPool = []
      for (const e of (nextPathEntriesUser || [])) {
        if (e && e.path && !seenPaths.has(e.path)) {
          seenPaths.add(e.path)
          pathPool.push({ name: e.path, value: 'user' })
        }
      }
      for (const e of (nextPathEntriesSystem || [])) {
        if (e && e.path && !seenPaths.has(e.path)) {
          seenPaths.add(e.path)
          pathPool.push({ name: e.path, value: 'system' })
        }
      }
      if (selectedProfile) {
        const updated = profileList.find((profile) => profile.name === selectedProfile?.name)
        selectedProfile = updated || null
      }
    } catch {
      // API helpers publish the relevant error state.
    } finally {
      if (requestEpoch === profileRefreshEpoch) loading = false
    }
  }
  function showMessage(msg: string, type: string) {
    showToast(msg, type === 'success' ? 'success' : type === 'error' ? 'error' : 'info')
  }
  // Map CLI hardcoded error messages to i18n keys for localization
  // v0.7.6: alias for the existing providerDisplayName() so localizeError
  // can render the provider-name placeholder in i18n templates. We reuse the
  // already-defined function instead of duplicating the provider mapping.
  function providerDisplayNameFn(name: string): string {
    try { return providerDisplayName(name) } catch { return name }
  }
  function localizeError(errMsg: string): string {
    // Profile already exists
    if (/already exists/i.test(errMsg) && /profile/i.test(errMsg)) {
      return $t('messages.profileAlreadyExists')
    }
    // PATH duplicate
    if (/already exists in PATH/i.test(errMsg)) {
      return $t('messages.pathDuplicate')
    }
    // Launch target validation errors (EnvFeatures.ValidateLaunchTarget)
    if (/Launch target is empty/i.test(errMsg)) return $t('errors.launchTargetEmpty')
    {
      const m = errMsg.match(/Launch target does not exist:\s*([^\n]+)/i)
      if (m) return $t('errors.launchTargetMissing', { values: { path: m[1].trim() } })
    }
    {
      const m = errMsg.match(/Launch target must be an \.(exe|bat|cmd|ps1) file \(got:\s*([^)]+)\)/i)
      if (m) return $t('errors.launchTargetInvalidExt', { values: { ext: m[2].trim() } })
    }
    if (/System32 are rejected/i.test(errMsg)) return $t('errors.launchTargetSystem32')
    // -------------------------------------------------------------------
    // v0.7.6: activation errors from SecretProviderManager.SetActiveProvider.
    // The CLI throws: "Cannot activate provider '<name>': <upstream>.
    //   Fix the provider environment first (e.g. install pwsh modules,
    //   set VAULT_ADDR, or configure cloud credentials)."
    // <upstream> is provider-specific. We split it out and map each provider's
    //   upstream message to its own i18n key, then assemble a localized error
    //   using the generic 'errors.activateProvider' template with placeholders
    //   {name} and {reason}. Falls back to the raw msg if the upstream doesn't
    //   match any known pattern (defense-in-depth; the user still gets CLI text).
    // -------------------------------------------------------------------
    const actMatch = errMsg.match(/^Cannot activate provider '([^']+)':\s*(.+?)\s*\.\s*Fix the provider environment first/i);
    if (actMatch) {
      const providerName = actMatch[1];
      const upstream = actMatch[2];
      let reasonKey: string | null = null;
      let reasonValues: Record<string, string> = {};
      // PowerShell SecretManagement: module missing + Install-Module hint.
      if (/PowerShell SecretManagement module is not installed/i.test(upstream)) {
        reasonKey = 'errors.activate.pwsh';
      }
      // SOPS: binary not found + SOPS_PATH hint.
      else if (/sops binary not found/i.test(upstream)) {
        reasonKey = 'errors.activate.sops';
      }
      // SOPS: encryption failed because no key configuration was provided
      // (.sops.yaml missing and no key provider env vars set).
      else if (/sops encryption failed/i.test(upstream) && /config file not found|no keys provided/i.test(upstream)) {
        reasonKey = 'errors.activate.sopsConfig';
      }
      // Azure Key Vault: AZURE_KEYVAULT_URI not set.
      else if (/AZURE_KEYVAULT_URI environment variable not set/i.test(upstream)) {
        reasonKey = 'errors.activate.azure';
      }
      // 1Password: op CLI missing.
      else if (/1Password CLI \(op\) not found/i.test(upstream)) {
        reasonKey = 'errors.activate.op';
      }
      // 1Password: op CLI present but no account configured (desktop app,
      // 'op account add', or OP_SERVICE_ACCOUNT_TOKEN env var).
      else if (/No accounts configured for use with 1Password CLI/i.test(upstream) ||
               /1Password CLI .*No accounts configured/i.test(upstream) ||
               /1Password CLI .*failed.*No accounts configured/i.test(upstream)) {
        reasonKey = 'errors.activate.opAccounts';
      }
      // AWS Secrets Manager: AWS_REGION / AWS_DEFAULT_REGION missing.
      else if (/AWS_REGION or AWS_DEFAULT_REGION not set/i.test(upstream)) {
        reasonKey = 'errors.activate.aws';
      }
      // Vault: VAULT_ADDR missing or wrong scheme.
      else if (/VAULT_ADDR environment variable not set/i.test(upstream)) {
        reasonKey = 'errors.activate.vaultAddr';
      }
      else if (/VAULT_ADDR must use https/i.test(upstream)) {
        reasonKey = 'errors.activate.vaultTls';
      }
      else if (/VAULT_TOKEN environment variable not set/i.test(upstream)) {
        reasonKey = 'errors.activate.vaultToken';
      }
      // Generic fallback for any future provider error: surface upstream
      // as a localized "fix provider environment" hint so the user at least
      // sees a non-English action rather than the raw CLI text.
      else {
        reasonKey = 'errors.activate.generic';
        reasonValues = { upstream };
      }
      const providerDisplayName = providerDisplayNameFn(providerName);
      const reason = $t(reasonKey, { values: reasonValues });
      return $t('errors.activateProvider', { values: { name: providerDisplayName, reason } });
    }
    // v0.7.7: inheritance boundary errors from ProfileSetInherits.
    if (/A Global profile cannot inherit from a Launch profile/i.test(errMsg)) {
      return $t('errors.globalInheritsLaunch')
    }
    if (/A Launch profile cannot inherit from another Launch profile that already carries secrets/i.test(errMsg)) {
      return $t('errors.launchInheritsSecret')
    }
    if (/no longer applicable after the inheritance change/i.test(errMsg)) {
      return $t('warnings.profileDisabledAfterInherit')
    }
    // Original Vault errors (non-activation path; e.g. Decrypt at runtime).
    if (/VAULT_ADDR environment variable not set/i.test(errMsg)) return $t('errors.vaultAddrNotSet')
    if (/VAULT_ADDR must use https/i.test(errMsg)) return $t('errors.vaultTlsRequired')
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
      await createProfile(name, {
        type: newProfileType,
        target: newProfileType === 'launch' ? newProfileTarget.trim() : undefined,
        args: newProfileType === 'launch' ? newProfileArgs || undefined : undefined,
        cwd: newProfileType === 'launch' ? newProfileCwd || undefined : undefined,
      })
      newProfileName = ''
      newProfileTarget = ''
      newProfileArgs = ''
      newProfileCwd = ''
      await refreshProfiles()
      showMessage($t('messages.profileCreated'), 'success')
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      const msg_ = localizeError(err instanceof Error ? err.message : String(err))
      providerErrorMessage = msg_
      showMessage(msg_, 'error')
    } finally {
      actionLoading = false
    }
  }
  async function handleBrowseTarget() {
    try {
      const picked = await pickExecutableFile($t('profiles.selectExecutable'))
      if (picked) newProfileTarget = picked
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showMessage(err instanceof Error ? err.message : String(err), 'error')
    } finally {
      actionLoading = false
    }
  }
  // v0.7.4: switching the active secret provider used to silently succeed and
  // leave all pre-existing secrets still encrypted with the OLD provider. The user
  // got no warning and could not recover the olds if the old provider was removed
  // (the DPAPI fallback can't decrypt CredMan/SOPS/etc). Now we show a confirm
  // modal: the user must acknowledge that existing secrets are NOT auto-migrated
  // and that they should run "Rotate all secrets" afterwards.
  let pendingProvider: string | null = null
  // Inline provider activation error (displayed under the selector, not as modal toast)
  let providerErrorMessage: string | null = null
  let providerChanging = false
  function requestChangeProvider(newProvider: string) {
    if (newProvider === activeProvider) return
    pendingProvider = newProvider
    providerErrorMessage = null
  }
  function cancelChangeProvider() {
    pendingProvider = null
    providerErrorMessage = null
  }
  async function confirmChangeProvider() {
    if (!pendingProvider || providerChanging) return
    const target = pendingProvider
    pendingProvider = null
    providerChanging = true
    try {
      await secretProviderSet(target)
      activeProvider = target
      providerErrorMessage = null
      showMessage($t('secrets.providerChanged'), 'success')
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      // v0.7.10: show provider activation errors as an inline amber banner
      // directly under the selector (providerErrorMessage) instead of a
      // transient toast, so the user can read the full actionable fix without
      // copy-pasting from a fading toast.
      providerErrorMessage = localizeError(err instanceof Error ? err.message : String(err))
    } finally {
      providerChanging = false
    }
  }
  async function handleChangeProvider(newProvider: string) {
    requestChangeProvider(newProvider)
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
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      // v0.7.10: surface secret-add errors (provider activation failures,
      // CLIXML probes, upstream provider issues) as an inline banner under
      // the selector so the actionable fix is visible without a fading toast.
      providerErrorMessage = localizeError(err instanceof Error ? err.message : String(err))
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
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }
  async function handleRename(profile: ProfileData) {
    const newName = await openInputDialog({
        title: $t('profiles.renameTitle'),
        defaultValue: profile.name,
        placeholder: $t('profiles.renamePlaceholder'),
      })
    if (!newName || newName === profile.name) return
    actionLoading = true
    try {
      await renameProfile(profile.name, newName.trim())
      await refreshProfiles()
      showMessage($t('messages.profileRenamed'), 'success')
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
        } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }
  async function handleAddVar() {
    if (!selectedProfile || !newVarName.trim()) {
      void frontendLog('warn', 'handleAddVar early-return: profile=' + (selectedProfile?.name ?? 'null') + ' name=[' + (newVarName || '<empty>') + ']').catch(() => {})
      return
    }
    const nameErr = validateVarNameInput(newVarName.trim())
    if (nameErr) { showToast(nameErr, 'error'); return }
    actionLoading = true
    try {
      void frontendLog('info', 'handleAddVar: calling addProfileVar profile=' + selectedProfile.name + ' var=' + newVarName.trim()).catch(() => {})
      await addProfileVar(selectedProfile.name, newVarName.trim(), newVarValue, newVarScope)
      newVarName = ''
      newVarValue = ''
      newVarScope = 'user'
            showAddVarPanel = false
      await refreshProfiles()
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }
  function handleCloneSelectFrom(v: { name: string; value: string }) {
    // Bug 6/7: CloneCombobox auto-clears its own state on select, so the user can
    // immediately search again without clicking empty space first. The selected
    // variable name/value flows into the next-step inputs below; we do NOT echo
    // the name back into the combobox (bug 7: avoidance of duplicate display).
    newVarName = v.name
    newVarValue = v.value
  }
  async function handleRemoveVar(varName: string) {
    if (!selectedProfile) return
    actionLoading = true
    try {
      await removeProfileVar(selectedProfile.name, varName)
      await refreshProfiles()
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    } finally {
      actionLoading = false
    }
  }
  // Instant swap approach: on pointerenter, immediately swap the dragged item
  // with the target item. No animation, no placeholder, no bounce-back.
  function handleDragPointerEnter(index: number): void {
    if (!isDragging || dragIndex === null || dragIndex === index) return
    // Instant swap: exchange the two items in the list
    const newList = [...profileList]
    const tmp = newList[dragIndex]
    newList[dragIndex] = newList[index]
    newList[index] = tmp
    profileList = newList
    profiles.set(newList)
    // The dragged item is now at the new index
    dragIndex = index
  }
  function beginPointerDrag(event: PointerEvent, index: number): void {
    if (event.button !== 0) return
    event.preventDefault()
    dragIndex = index
    isDragging = true
    pointerId = event.pointerId
  }
  function finishPointerDrag(event?: PointerEvent): void {
    if (!isDragging || dragIndex === null) {
      cancelPointerDrag()
      return
    }
    // Persist the final order
    saveStoredOrder(profileList.map((profile) => profile.name))
    cancelPointerDrag()
  }
  function cancelPointerDrag(): void {
    dragIndex = null
    isDragging = false
    pointerId = null
  }
  async function handleInheritance(parent: string, enabled: boolean) {
    if (!selectedProfile) return
    const parents = enabled
      ? [...(selectedProfile.inherits ?? []), parent]
      : (selectedProfile.inherits ?? []).filter(name => name !== parent)
    try {
      await setProfileInheritance(selectedProfile.name, parents)
      await refreshProfiles()
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
      showMessage(localizeError(err instanceof Error ? err.message : String(err)), 'error')
    }
  }
  async function handleAddPath() {
    if (!selectedProfile || !newPathEntry.trim()) return
    const pathErr = validatePathInput(newPathEntry.trim())
    if (pathErr) { showToast(pathErr, 'error'); return }
    try {
      await addProfilePath(selectedProfile.name, newPathEntry.trim(), newPathScope)
      newPathEntry = ''
      newPathScope = 'user'
      await refreshProfiles()
    } catch (err) { void frontendLog('error', '[ProfilePage] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
      }
</script>
<svelte:window
  on:pointerup={finishPointerDrag}
  on:pointercancel={cancelPointerDrag}
/>
<div class="space-y-3">
  <!-- Create profile bar -->
  <div class="space-y-2">
   <!-- Choose scope of effect before naming the profile. Global writes the user registry;
        Launch applies only to the selected child process. -->
    <div class="flex items-start gap-1 flex-wrap py-0.5 min-h-[36px]">
     <label class="text-[11px] font-medium text-muted-foreground">
       <input type="radio" bind:group={newProfileType} value="global" class="mr-1" /> {$t('profiles.typeGlobal')}
      </label>
      <label class="text-[11px] font-medium text-muted-foreground">
        <input type="radio" bind:group={newProfileType} value="launch" class="mr-1" /> {$t('profiles.typeLocal')}
      </label>
      {#if newProfileType === 'launch'}
        <input
          type="text"
          placeholder={$t('profiles.targetExecutable')}
          bind:value={newProfileTarget}
          class="flex-1 min-w-[200px] px-2 py-1 text-xs border border-border rounded-md font-mono bg-card border-border/80 text-foreground"
        />
        <button
          on:click={handleBrowseTarget}
          class="px-2 py-1 text-xs text-primary bg-blue-50 rounded-md hover:bg-primary/15 bg-primary/10"
          title={$t('profiles.browse')}
        >
          {$t('profiles.browse')}
        </button>
        <input
          type="text"
          placeholder={$t('profiles.localArgs')}
          bind:value={newProfileArgs}
          class="flex-1 min-w-[160px] px-2 py-1 text-xs border border-border rounded-md font-mono bg-card border-border/80 text-foreground"
        />
        <input
          type="text"
          placeholder={$t('profiles.workingDir')}
          bind:value={newProfileCwd}
          class="flex-1 min-w-[160px] px-2 py-1 text-xs border border-border rounded-md font-mono bg-card border-border/80 text-foreground"
        />
      {/if}
    </div>
    <div class="flex gap-2">
      <input
        type="text"
        placeholder={$t('profiles.createPrompt')}
        bind:value={newProfileName}
        on:keydown={(event) => { if (event.key === 'Enter') handleCreate() }}
        class="flex-1 px-3 py-1.5 text-xs border border-border rounded-md focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary bg-card border-border/80 text-foreground"
      />
      <button
        on:click={handleCreate}
        disabled={actionLoading || !newProfileName.trim()}
        class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-primary-foreground bg-primary rounded-md hover:bg-primary transition disabled:opacity-50 bg-primary/80"
      >
          <Plus class="w-3.5 h-3.5" />
        {$t('dialogs.createProfile')}
      </button>
      <button
        on:click={handleImport}
        disabled={actionLoading}
        class="flex items-center gap-1 px-3 py-1.5 text-xs font-medium text-foreground/80 bg-card border border-border rounded-md hover:bg-muted/50 transition disabled:opacity-50 text-foreground border-border/80 hover:bg-accent"
        title={$t('profiles.import')}
      >
          <Download class="w-3.5 h-3.5" />
        {$t('profiles.import')}
      </button>
    </div>
  </div>
  {#if loading}
    <div class="flex justify-center py-8">
      <div class="animate-spin rounded-full h-5 w-5 border-b-2 border-primary"></div>
    </div>
  {:else if profileList.length === 0}
    <div class="px-4 py-8 text-center text-muted-foreground text-xs">
      {$t('profiles.empty')}
    </div>
  {:else}
    <!-- Profile list with toggle switches (like PowerToys) -->
    <div class="space-y-2" role="list" data-testid="profile-list">
      {#each profileList as profile, i (profile.name)}
        <div
          class="bg-card rounded-md border border-border bg-card border-border transition-all duration-150 hover:shadow-md hover:ring-2 hover:ring-blue-300 {isDragging && dragIndex === i ? 'opacity-80 ring-2 ring-primary shadow-lg' : ''}"
          on:pointerenter={() => handleDragPointerEnter(i)}
          role="listitem"
          data-profile-name={profile.name}
          data-profile-index={i}
        >
          <!-- Profile row with toggle -->
          <div class="flex items-center justify-between px-4 py-2.5">
            <button
              type="button"
              class="profile-drag-handle flex items-center gap-1 cursor-grab active:cursor-grabbing touch-none text-border hover:text-muted-foreground text-muted-foreground/80"
              title={$t('profiles.dragToSort')}
              aria-label={$t('profiles.dragToSort')}
              data-testid={`profile-drag-handle-${i}`}
              on:pointerdown={(event) => beginPointerDrag(event, i)}
              on:pointerup={(event) => finishPointerDrag(event)}
            >
          <MapPin class="w-3 h-3 pointer-events-none" />
            </button>
            <button
              on:click={() => selectProfile(profile)}
              class="flex items-center gap-3 flex-1 text-left"
            >
              <span class="text-xs font-medium text-foreground">{profile.name}</span>
              {#if profile.profileType === 'launch'}
                <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-purple-50 text-purple-700 bg-primary/15 text-primary" title={profile.targetExecutable || ''}>
                  {$t('profiles.typeLocal')}
                </span>
              {:else}
                <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-blue-50 text-primary bg-primary/20/40">
                  {$t('profiles.typeGlobal')}
                </span>
              {/if}
              <span class="text-[10px] text-muted-foreground">{profile.variables.length} {$t('profiles.variables')}</span>
              {#if profile.isEnabled}
                <span class="inline-flex px-1.5 py-0.5 rounded text-[10px] font-medium bg-primary/10 text-green-700 bg-primary/15 text-primary">
                  {$t('profiles.applied')}
                </span>
              {/if}
            </button>
            <div class="flex items-center gap-3">
              <!-- Toggle switch -->
              <button
                on:click={() => handleToggleProfile(profile)}
                disabled={actionLoading || profile.profileType === 'launch'}
                class="relative inline-flex h-4 w-7 items-center rounded-full transition disabled:opacity-40 disabled:cursor-not-allowed {profile.isEnabled ? 'bg-primary bg-primary/80' : 'bg-border bg-muted'}"
                role="switch"
                aria-checked={profile.isEnabled}
                title={profile.profileType === 'launch' ? $t('profiles.launchApplyDisabled') : (profile.isEnabled ? $t('profiles.unapply') : $t('profiles.apply'))}
              >
                <span class="inline-block h-3 w-3 transform rounded-full bg-card shadow transition {profile.isEnabled ? 'translate-x-3.5' : 'translate-x-0.5'}"></span>
              </button>
              {#if profile.profileType === 'launch' && profile.targetExecutable}
                <!-- Launch button (Launch profiles only) -->
                <button
                  on:click={() => handleLaunchProfile(profile)}
                  disabled={actionLoading}
                  class="p-1 text-primary hover:bg-primary/10 rounded transition hover:bg-primary/15"
                  title={$t('profiles.localLaunch')}
                  aria-label={$t('profiles.localLaunch')}
                >
          <Play class="w-3.5 h-3.5" />
                </button>
              {/if}
              <!-- Export button -->
              <button
                on:click={() => handleExport(profile)}
                disabled={actionLoading}
                class="p-1 text-muted-foreground hover:text-primary rounded transition"
                title={$t('profiles.export')}
                aria-label={$t('profiles.export')}
              >
          <Download class="w-3.5 h-3.5" />
              </button>
              <!-- Rename button -->
              <button
                on:click={() => handleRename(profile)}
                disabled={actionLoading}
                class="p-1 text-muted-foreground hover:text-primary rounded transition"
                title={$t('profiles.rename')}
                aria-label={$t('profiles.rename')}
              >
          <Pencil class="w-3.5 h-3.5" />
              </button>
              <!-- Delete button -->
              <button
                on:click={() => handleDelete(profile)}
                disabled={actionLoading}
                class="p-1 text-muted-foreground hover:text-destructive rounded transition"
                title={$t('buttons.delete')}
                aria-label={$t('buttons.delete')}
              >
          <Trash2 class="w-3.5 h-3.5" />
              </button>
            </div>
          </div>
          <!-- Expanded detail panel -->
          {#if selectedProfile?.name === profile.name}
            <div class="border-t border-gray-100 px-4 py-3 border-border">
              <div class="grid grid-cols-2 gap-4 mb-3 pb-3 border-b border-gray-100 border-border">
                <div>
                  <div class="text-[10px] font-medium text-muted-foreground mb-1">{$t('profiles.inherits')}</div>
                  <div class="space-y-1 max-h-20 overflow-y-auto">
                    {#each profileList.filter(candidate => candidate.name !== profile.name) as parent (parent.name)}
                    {@const inheritBlocked = (profile.profileType === 'global' && parent.profileType === 'launch')
                      || (profile.profileType === 'launch' && parent.profileType === 'launch' && (parent.secretVariables?.length ?? 0) > 0)}
                      <div class="flex items-center gap-1.5 text-[10px] text-muted-foreground text-foreground/80" title={inheritBlocked ? $t('errors.inheritBlocked') : ''}>
                        <input type="checkbox" class="cursor-pointer" checked={profile.inherits?.includes(parent.name)} disabled={inheritBlocked} on:change={(event) => handleInheritance(parent.name, event.currentTarget.checked)} />
                        <span class="truncate" class:opacity-40={inheritBlocked} class:cursor-not-allowed={inheritBlocked}>{parent.name}</span>
                        {#if parent.profileType === 'launch'}
                          <span class="text-[8px] px-0.5 rounded bg-amber-100 text-amber-800 bg-primary/15 text-primary/80">{$t('profiles.typeLaunch')}</span>
                        {/if}
                      </div>
                    {/each}
                  </div>
                </div>
                <div>
                  <div class="text-[10px] font-medium text-muted-foreground mb-1">{$t('profiles.pathEntries')}</div>
                  <div class="space-y-1 mb-1">
                     {#each profile.pathEntries ?? [] as path, i (path)}
                       <div class="flex items-center gap-1 text-[10px] font-mono">
                         <span class="truncate flex-1" title={path}>{path}</span>
                         {#if (profile.pathScopes?.[i] ?? 'user') === 'system'}
                           <span class="text-[9px] px-1 rounded-full bg-orange-100 text-orange-800 bg-primary/15 text-primary/80">{$t('scope.system')}</span>
                         {:else}
                           <span class="text-[9px] px-1 rounded-full bg-blue-100 text-blue-800 bg-primary/20/40 text-primary">{$t('scope.user')}</span>
                         {/if}
                         <button on:click={() => handleRemovePath(path)} disabled={profile.isEnabled} class="text-destructive disabled:opacity-30" aria-label={$t('buttons.delete')}>×</button>
                       </div>
                     {/each}
                  </div>
                 <div class="flex gap-1 items-center">
                   <select bind:value={newPathScope} disabled={profile.isEnabled} class="px-1.5 py-1 text-[10px] border border-border rounded focus:outline-none focus:ring-1 focus:ring-primary bg-accent border-border/80 text-foreground">
                     <option value="user">{$t('scope.user')}</option>
                     <option value="system">{$t('scope.system')}</option>
                   </select>
                   <div class="min-w-0 flex-1">
                     <CloneCombobox
                       items={pathPool}
                       placeholder={$t('profiles.cloneSearchPlaceholder')}
                       keepQueryOnSelect={true}
                       on:input={(e) => { newPathEntry = e.detail }}
                       on:select={(e) => { newPathEntry = e.detail.name }}
                     />
                   </div>
                   <button on:click={handleAddPath} disabled={profile.isEnabled || !newPathEntry.trim()} class="px-2 text-[10px] text-primary disabled:opacity-30">{$t('buttons.add')}</button>
                 </div>
                  {#if profile.isEnabled}
                    <p class="text-[9px] text-primary/80 mt-1">{$t('profiles.unapplyToEdit')}</p>
                  {/if}
               </div>
             </div>
             {#if profile.variables.length === 0}
               <p class="text-[10px] text-muted-foreground py-2">{$t('profiles.noVariables')}</p>
             {:else}
                <!-- Regular variables section -->
                {#if profile.variables.filter(pv => !selectedProfile?.secretVariables?.includes(pv.name)).length > 0}
                  <div class="mt-1 mb-1">
                    <div class="text-[10px] font-semibold text-muted-foreground mb-1">{$t('profiles.regularVariables')}</div>
                    <div class="space-y-1 mb-2">
                      {#each profile.variables.filter(pv => !selectedProfile?.secretVariables?.includes(pv.name)) as pv (pv.name)}
                        <div class="flex items-center gap-2 px-2 py-1 rounded bg-muted/50 bg-accent/50">
                          <span class="text-[10px] font-mono text-foreground/80 flex-1 truncate">{pv.name}</span>
                          <span class="text-[10px] font-mono text-muted-foreground flex-1 truncate">{pv.value}</span>
                          {#if pv.scope === "system"}
                            <span class="text-[9px] px-1 rounded-full bg-orange-100 text-orange-800 bg-primary/15 text-primary/80">{$t("scope.system")}</span>
                          {:else}
                            <span class="text-[9px] px-1 rounded-full bg-blue-100 text-blue-800 bg-primary/20/40 text-primary">{$t("scope.user")}</span>
                          {/if}
                          {#if pv.sourceProfile && pv.sourceProfile !== profile.name}
                            <span class="text-[9px] text-muted-foreground" title={pv.sourceProfile}>{$t("profiles.inheritsFrom")} {pv.sourceProfile}</span>
                          {/if}
                          <button
                            on:click={() => handleRemoveVar(pv.name)}
                            disabled={actionLoading}
                            class="p-0.5 text-muted-foreground hover:text-destructive rounded transition"
                            title={$t('buttons.delete')}
                            aria-label={$t('buttons.delete')}
                          >
          <X class="w-3 h-3" />
                          </button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/if}
                <!-- Secret variables section -->
                {#if profile.variables.filter(pv => selectedProfile?.secretVariables?.includes(pv.name)).length > 0}
                  <div class="mt-1 mb-1">
                    <div class="text-[10px] font-semibold text-primary/80 mb-1 flex items-center gap-1">
          <Power class="w-3 h-3" />
                      {$t('profiles.secretVariables')}
                    </div>
                    <div class="space-y-1 mb-2">
                      {#each profile.variables.filter(pv => selectedProfile?.secretVariables?.includes(pv.name)) as pv (pv.name)}
                        <div class="flex items-center gap-2 px-2 py-1 rounded bg-primary/10">
                          <span class="text-[10px] font-mono text-foreground/80 flex-1 truncate">{pv.name}</span>
                          <span class="text-[10px] font-mono text-muted-foreground flex-1 truncate">{'<encrypted>'}</span>
          <Power class="w-3 h-3 text-amber-600" />
                          <button
                            on:click={() => handleRemoveSecret(pv.name)}
                            disabled={actionLoading}
                            class="p-0.5 text-muted-foreground hover:text-destructive rounded transition"
                            title={$t('buttons.delete')}
                            aria-label={$t('buttons.delete')}
                          >
          <X class="w-3 h-3" />
                          </button>
                        </div>
                      {/each}
                    </div>
                  </div>
                {/if}
              {/if}
              <!-- Add variable button -->
              {#if !showAddVarPanel}
                <button
                  on:click={() => (showAddVarPanel = true)}
                  disabled={profile.isEnabled}
                  class="flex items-center gap-1 text-[10px] font-medium text-primary hover:text-primary disabled:opacity-40 disabled:cursor-not-allowed"
                >
          <Plus class="w-3 h-3" />
                  {$t('profiles.addVariable')}
                </button>
              {:else}
                <!-- Add variable panel with clone-from-existing dropdown -->
                <div class="space-y-1.5 pt-2 border-t border-gray-100 border-border">
                  {#if profile.isEnabled}
                    <p class="text-[9px] text-primary/80">{$t('profiles.unapplyToEdit')}</p>
                  {/if}
                  <!-- Clone from existing variable (reusable CloneCombobox) -->
                  <CloneCombobox
                    items={allVars}
                    placeholder={$t('profiles.cloneSearchPlaceholder')}
                    on:select={(e) => { handleCloneSelectFrom(e.detail) }}
                  />
                                    <!-- Scope selector: user vs system -->
                  <div class="flex items-center gap-2 mb-1">
                    <label for="profile-var-scope" class="text-[10px] font-medium text-muted-foreground">
                      {$t('labels.scope')}
                    </label>
                    <select
                      id="profile-var-scope"
                      value={newVarScope}
                      on:change={(e) => newVarScope = e.currentTarget.value}
                      class="px-2 py-1 text-[10px] border border-border rounded focus:outline-none focus:ring-1 focus:ring-primary bg-accent border-border/80 text-foreground"
                    >
                      <option value="user">{$t('scope.user')}</option>
                      <option value="system">{$t('scope.system')}</option>
                    </select>
                  </div>
                  <input
                    type="text"
                    placeholder={$t('labels.name')}
                    bind:value={newVarName}
                    class="w-full px-2 py-1 text-[10px] border border-border rounded focus:outline-none focus:ring-1 focus:ring-primary bg-accent border-border/80 text-foreground"
                  />
                  <input
                    type="text"
                    placeholder={$t('labels.value')}
                    bind:value={newVarValue}
                    class="w-full px-2 py-1 text-[10px] border border-border rounded focus:outline-none focus:ring-1 focus:ring-primary bg-accent border-border/80 text-foreground"
                  />
                  <div class="flex gap-1">
                    <button
                      on:click={handleAddVar}
                      disabled={actionLoading || !newVarName.trim() || profile.isEnabled}
                      class="flex-1 px-2 py-1 text-[10px] font-medium text-primary-foreground bg-primary rounded hover:bg-primary transition disabled:opacity-50 bg-primary/80"
                    >
                      {$t('buttons.save')}
                    </button>
                    <button
                      on:click={() => { showAddVarPanel = false; newVarName = ''; newVarValue = ''; newVarScope = 'user' }}
                      class="px-2 py-1 text-[10px] text-muted-foreground border border-border rounded hover:bg-muted/50 transition text-foreground/80 border-border/80 hover:bg-accent"
                    >
                      {$t('buttons.cancel')}
                    </button>
                  </div>
                </div>
              {/if}
              {#if profile.profileType === 'launch' && !profile.isEnabled}
                <!-- v0.8 secret provider indicator -->
                <div class="flex items-center gap-1.5 mt-2">
                 <span class="text-[9px] text-muted-foreground">{$t('secrets.activeProvider')}:</span>
                     <select
                     class="text-[9px] px-1.5 py-0.5 rounded bg-muted/30 bg-accent text-muted-foreground text-foreground/80 border border-border border-border/80 focus:outline-none focus:ring-1 focus:ring-primary"
                     value={activeProvider}
                     on:change={(e) => {
                       const next = e.currentTarget.value;
                       e.currentTarget.value = activeProvider;
                       requestChangeProvider(next);
                     }}
                   >
                     {#each availableProviders as prov}
                       <option value={prov} selected={prov === activeProvider}>
                         {providerDisplayName(prov)}{prov === activeProvider ? ' (active)' : ''}
                       </option>
                     {/each}
                   </select>
                    {#if providerChanging}
          <Power class="animate-spin inline-block w-3 h-3" />
                    {/if}
                   {#if providerErrorMessage}
                      <div class="mt-1 p-1.5 rounded-md bg-primary/10 border border-amber-200 text-[10px] text-amber-700 border-primary/40 text-primary/80">
                        <span class="font-medium">{$t('secrets.activeProvider')}:</span> {providerErrorMessage}
                        <button type="button" class="ml-1 underline text-primary/80" on:click={() => providerErrorMessage = null}>{$t('buttons.close')}</button>
                      </div>
                    {/if}
                </div>
                {#if !showAddSecretPanel}
                  <button
                    on:click={() => (showAddSecretPanel = true)}
                    class="flex items-center gap-1 text-[10px] font-medium text-primary/80 hover:text-amber-700 mt-2"
                  >
          <Plus class="w-3 h-3" />
                    {$t('profiles.addSecret')}
                  </button>
                {:else}
                  <div class="space-y-1.5 pt-2 border-t border-gray-100 border-border">
                    <input
                      type="text"
                      placeholder={$t('labels.name')}
                      bind:value={newSecretName}
                      class="w-full px-2 py-1 text-[10px] border border-border rounded focus:outline-none focus:ring-1 focus:ring-amber-500 bg-accent border-border/80 text-foreground"
                    />
                    <input
                      type="password"
                      placeholder={$t('profiles.secretValue')}
                      bind:value={newSecretValue}
                      class="w-full px-2 py-1 text-[10px] border border-border rounded focus:outline-none focus:ring-1 focus:ring-amber-500 bg-accent border-border/80 text-foreground"
                    />
                    <div class="flex gap-1">
                      <button
                        on:click={handleAddSecret}
                        disabled={actionLoading || !newSecretName.trim() || !newSecretValue}
                        class="flex-1 px-2 py-1 text-[10px] font-medium text-primary-foreground bg-primary/80 rounded hover:bg-amber-700 transition disabled:opacity-50 bg-primary/60 hover:bg-primary/80"
                      >
                        {$t('profiles.addSecret')}
                      </button>
                      <button
                        on:click={() => { showAddSecretPanel = false; newSecretName = ''; newSecretValue = '' }}
                        class="px-2 py-1 text-[10px] text-muted-foreground border border-border rounded hover:bg-muted/50 transition text-foreground/80 border-border/80 hover:bg-accent"
                      >
                        {$t('buttons.cancel')}
                      </button>
                    </div>
                    <p class="text-[9px] text-muted-foreground">{$t('profiles.secretHint')}</p>
                  </div>
                {/if}
              {/if}
            </div>
          {/if}
        </div>
      {/each}
    </div>
  {/if}
  {#if pendingProvider}
    <div class="fixed inset-0 z-50 flex items-center justify-center bg-background/40" role="dialog" aria-modal="true">
      <div class="bg-card rounded-lg shadow-xl max-w-sm w-full mx-4 p-4">
        <h3 class="text-sm font-semibold text-foreground mb-2">{$t('secrets.providerChangeTitle')}</h3>
        <p class="text-[11px] text-muted-foreground text-foreground/80 mb-3">{$t('secrets.providerChangeWarning')}</p>
        <div class="flex justify-end gap-2">
          <button
            type="button"
            on:click={cancelChangeProvider}
            class="px-3 py-1 text-[11px] text-muted-foreground border border-border rounded hover:bg-muted/50 transition text-foreground/80 border-border/80 hover:bg-accent"
          >
            {$t('buttons.cancel')}
          </button>
          <button
            type="button"
            on:click={confirmChangeProvider}
            disabled={providerChanging}
            class="px-3 py-1 text-[11px] font-medium text-primary-foreground bg-primary/80 rounded hover:bg-amber-700 transition bg-primary/60 hover:bg-primary/80 disabled:opacity-50 disabled:cursor-wait"
          >
            {#if providerChanging}
          <Power class="animate-spin inline-block w-3 h-3 mr-1" />
              {$t('secrets.confirmChange')}...
            {:else}
              {$t('secrets.confirmChange')}
            {/if}
          </button>
        </div>
      </div>
    </div>
  {/if}
</div>
