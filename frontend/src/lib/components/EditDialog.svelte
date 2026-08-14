<script lang="ts">
  import { createEventDispatcher } from 'svelte'
  import { frontendLog } from '../settingsStore'
  import { t } from 'svelte-i18n'
  import { setVariable, renameVariable, changeScope } from '../api'
  import { showModal, variables } from '../stores'
  import { hasVariableConflict } from '../features'
  import type { EnvVariable } from '../api'  // for typed param'

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

    // Rely on the CLI-driven isProtected flag (kept in sync with the
    // protection list JSON files) instead of a hardcoded list that would
    // drift from the config-driven protection rules. A protected variable
    // cannot be edited: the GUI lock button is the single path to unlock.
    if (variable && variable.isProtected) {
      localError = $t('protection.protectedCannotEdit', { values: { name: variable.name } })
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
    const scopeChanged = !!variable && scope !== variable.scope
    saving = true
    try {
      if (variable && scopeChanged && nameChanged) {
        // 3-way mutation: scope + name + (optional) value. Order matters for
        // partial-failure safety. Rename FIRST in the original scope so a
        // failure here leaves the variable untouched (no cross-scope damage).
        // Then changeScope moves the already-renamed variable to the new scope,
        // passing the user-confirmed overwrite flag (NEVER a hardcoded true) so
        // a conflict-modal Cancel is honored. A failure on this step leaves the
        // variable renamed in the original scope -- a safe, recoverable state
        // (the user can re-open EditDialog and retry the scope move).
        // value-only edit, if needed, runs last by falling into the value-change
        // path below. This ordering was reviewed by the code-reviewer and
        // architect lanes and resolves the silent-clobber + non-rollback
        // findings (HIGH severity) reported against the previous order.
        await renameVariable(originalName, name, variable.scope as 'user' | 'system', overwrite)
        await changeScope(name, scope as 'user' | 'system', variable.scope as 'user' | 'system', overwrite)
        if (value !== variable.value) {
          await setVariable(name, value, scope as 'user' | 'system', true)
        }
      } else if (variable && scopeChanged) {
        // Scope changed, name unchanged. Only clobber an existing target
        // variable when the user confirmed via the conflict modal; the CLI
        // change-scope command itself rejects target collisions without
        // --overwrite, preserving the safety contract.
        await changeScope(name, scope as 'user' | 'system', variable.scope as 'user' | 'system', overwrite)
        if (value !== variable.value) {
          await setVariable(name, value, scope as 'user' | 'system', true)
        }
      } else if (variable && nameChanged) {
        await renameVariable(originalName, name, scope as 'user' | 'system', overwrite)
        await setVariable(name, value, scope as 'user' | 'system', true)
      } else {
        // Normal set (new variable or just value change)
        await setVariable(name, value, scope as 'user' | 'system', overwrite || !!variable)
      }
      dispatch('save')
    } catch (err) { void frontendLog('error', '[EditDialog] ' + (err instanceof Error ? err.message : String(err))).catch(() => {});
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
  <div class="bg-card rounded-lg shadow-xl max-w-md w-full mx-4 bg-card" on:click|stopPropagation>
    <div class="px-5 py-3 border-b border-border border-border">
      <h2 class="text-sm font-semibold text-foreground text-foreground">
        {variable ? $t('dialogs.editVariable') : $t('dialogs.addVariable')}
      </h2>
    </div>

    <div class="px-5 py-4 space-y-3">
      {#if localError}
        <div class="bg-destructive/10 border border-red-200 text-red-800 px-3 py-2 rounded-md text-xs bg-destructive/15 border-destructive text-destructive">
          {localError}
        </div>
      {/if}

      <div>
        <label for="edit-name" class="block text-xs font-medium text-muted-foreground text-muted-foreground mb-1">
          {$t('labels.name')}
        </label>
        <input
          id="edit-name"
          type="text"
          bind:value={name}
          spellcheck="false"
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary font-mono bg-accent border-border/80 text-foreground"
        />
        {#if nameChanged}
          <p class="mt-1 text-[10px] text-primary/80 text-primary/80">
            {$t('messages.renameWarning')}
          </p>
        {/if}
      </div>

      <div>
        <label for="edit-value" class="block text-xs font-medium text-muted-foreground text-muted-foreground mb-1">
          {$t('labels.value')}
        </label>
        <textarea
          id="edit-value"
          bind:value={value}
          rows="4"
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-primary focus:border-primary font-mono bg-accent border-border/80 text-foreground"
        />
      </div>

      <div>
        <label for="edit-scope" class="block text-xs font-medium text-muted-foreground text-muted-foreground mb-1">
          {$t('labels.scope')}
        </label>
        <select
          id="edit-scope"
          bind:value={scope}
          disabled={!!(variable && variable.isProtected)}
          class="w-full px-3 py-1.5 text-xs border border-gray-300 rounded-md focus:outline-none focus:ring-1 focus:ring-primary bg-card disabled:bg-muted/30 bg-accent border-border/80 text-foreground"
        >
          <option value="user">{$t('scope.user')}</option>
          <option value="system">{$t('scope.system')}</option>
        </select>
      </div>
    </div>

    <div class="px-5 py-3 border-t border-border flex gap-2 justify-end border-border">
      <button
        on:click={handleClose}
        disabled={saving}
        class="px-4 py-1.5 text-xs text-foreground/80 border border-gray-300 rounded-md hover:bg-muted/20 transition disabled:opacity-50 text-foreground border-border/80 hover:bg-accent"
      >
        {$t('buttons.cancel')}
      </button>
      <button
        on:click={handleSave}
        disabled={saving}
        class="px-4 py-1.5 text-xs text-white bg-primary rounded-md hover:bg-blue-700 transition disabled:opacity-50 bg-primary/80 hover:bg-primary"
      >
        {saving ? $t('messages.loading') : $t('buttons.save')}
      </button>
    </div>
  </div>
</div>
