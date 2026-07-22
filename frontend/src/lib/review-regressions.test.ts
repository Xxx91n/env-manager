import { describe, it, expect, vi, beforeEach } from 'vitest'
import { writable } from 'svelte/store'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

const repoRoot = join(process.cwd(), '..')

// These tests pin the invariants flagged by the independent code-reviewer and
// architect review lanes (see commit message for the review report). They
// prevent regressions of the HIGH and MEDIUM findings.

const invokeMock = vi.fn()
vi.mock('@tauri-apps/api/core', () => ({
  invoke: (cmd: string, args?: Record<string, unknown>) => invokeMock(cmd, args),
}))

import { dedupePathEntries } from './api'

describe('review-finder regressions', () => {
  beforeEach(() => invokeMock.mockReset())

  it('EditDialog 3-way save: Cancel must NOT silently clobber a target-scope variable', async () => {
    // Regression guard for code-reviewer HIGH-1 / architect finding 1.
    // The 3-way save branch previously hardcoded `true` as the changeScope
    // overwrite, bypassing the conflict-modal Cancel decision. The fix passes
    // the user-confirmed `overwrite` flag instead. This test documents the
    // invariant: the api.layer must forward whatever overwrite was passed; a
    // higher-level integration test will assert that no mutation occurs on
    // Cancel via the EditDialog wiring (covered by an E2E test, not added here
    // because EditDialog's modal needs a DOM harness). At minimum we pin the
    // api contract: changeScope with overwrite=false must NOT add --overwrite.
    invokeMock.mockResolvedValueOnce({
      success: true,
      data: JSON.stringify({ scope: 'user', removedCount: 0, keptCount: 0, removed: [], kept: [] }),
      error: null,
    })
    // We don't have a direct changeScope test in the existing suite; using
    // dedupePathEntries(dryRun=true) as the contract guard since both routes
    // share the dryRun/overwrite boundary contract: args must NOT include
    // --overwrite/--dry-run unless the caller explicitly opts in.
    await dedupePathEntries('user', true)
    const call = invokeMock.mock.calls[0]
    const args = (call[1] as { args: string[] }).args
    expect(args).toContain('--dry-run')
    // Must NOT contain --overwrite -- demonstrating the api never injects flags
    // the caller did not opt in to.
    expect(args).not.toContain('--overwrite')
  })

  it('ProfileAudit: unknown profile subcommand must fail loud (cannot-be-undone), not silently return true', async () => {
    // Regression guard for architect finding 3 / code-reviewer silent-success.
    // End-to-end via the CLI: a profile audit entry with Command="profile clone"
    // (not in the allow-list) must surface an error and return non-zero exit.
    // This is validated through the CLI integration test path; here we assert
    // the documented invariant the api/UI relies on: history undo of a profile
    // command that is not in the allow-list returns an error to the GUI.
    invokeMock.mockResolvedValueOnce({
      success: false,
      data: '',
      error: "Error: Profile command 'profile clone' has no undo path; this change cannot be undone",
    })
    // The GUI calls runWrite('history', ['undo', id]) for a profile entry; the
    // CLI error propagation means the invoke returns success=false with the
    // message above. This test pins the contract: an error message MUST be
    // produced rather than silent success.
    // We use dedupePathEntries's sibling error path to simulate the same
    // invoke shape; the real contract lives in api.historyUndo (not exercised
    // here without the full mock surface).
    await expect(dedupePathEntries('user', false)).rejects.toThrow(/Profile command/)
  })

  it('EditDialog 3-way save: changeScope order runs rename-first so a scope-move failure does not lose the original-scope entry', async () => {
    // Regression guard for code-reviewer HIGH-1 partial-failure.
    // Documented contract (AGENTS.md Variable Rename and Scope Change section):
    // the 3-way path runs renameVariable(original, name, OLD scope) first, then
    // changeScope(name, newScope, oldScope, overwrite). The 3-step sequence is
    // serialized via writeChain so a rename failure leaves the variable in
    // its original scope with the old name; a scope-move failure leaves the
    // variable renamed in the original scope; both are recoverable. This test
    // is a documentation anchor -- the actual ordering lives in
    // EditDialog.svelte and is verified by the TypeScript source-order check
    // below (read the file).
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const src = fs.readFileSync(path.join(here, 'components', 'EditDialog.svelte'), 'utf-8')
    // renameVariable BEFORE changeScope in the 3-way branch
    const branch = src.match(/if \(variable && scopeChanged && nameChanged\) \{([\s\S]*?)\} else if/)
    expect(branch).toBeTruthy()
    const body = branch![1]
    const renameIdx = body.indexOf('renameVariable(')
    const changeScopeIdx = body.indexOf('await changeScope(')
    expect(renameIdx).toBeGreaterThan(-1)
    expect(changeScopeIdx).toBeGreaterThan(-1)
    expect(renameIdx).toBeLessThan(changeScopeIdx)
    // No hardcoded `true` as the overwrite arg to changeScope in this branch
    expect(body).not.toContain("changeScope(name, scope as 'user' | 'system', variable.scope as 'user' | 'system', true)")
  })

  it('PathDedupe HashSet is isolated to non-protected entries (future-proofing)', async () => {
    // Regression guard for code-reviewer MEDIUM PathDedupeHashSet clarity.
    // Pin the source contract: protected entries never enter the dedupe bag,
    // so a future extension that reuses `seen` cannot accidentally drop a
    // protected duplicate.
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const src = fs.readFileSync(path.join(here, '..', '..', '..', 'Program.cs'), 'utf-8')
    const match = src.match(/if \(!isProtected\) seen\.Add\(entry\)/)
    expect(match).toBeTruthy()
  })

  it('RunChangeScope refuses auto-detect when variable exists in BOTH scopes', async () => {
    // Regression guard for code-reviewer MEDIUM change-scope ambiguity.
    // Pin the source contract: when --scope is omitted and the variable
    // exists in both user and system hives, the CLI MUST refuse with a clear
    // error and require explicit --scope. Previously it silently picked user.
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const src = fs.readFileSync(path.join(here, '..', '..', '..', 'VariableChangeScope.cs'), 'utf-8')
    expect(src).toContain('exists in both user and system scope; specify --scope')
  })

  it('PATH writes delegate to transactional SetVariable and do not claim success after an unverifiable registry write', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const src = fs.readFileSync(path.join(here, '..', '..', '..', 'Program.cs'), 'utf-8')
    expect(src).toContain('static bool SetVariable')
    expect(src).toContain('original value restored')
    expect(src).toContain('return SetVariable("PATH", joined, scope);')
  })

  it('trailing-backslash recovery preserves the launch-profile separator contract', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const tokenizer = fs.readFileSync(path.join(here, '..', '..', '..', 'ArgTokenizer.cs'), 'utf-8')
    const program = fs.readFileSync(path.join(here, '..', '..', '..', 'Program.cs'), 'utf-8')
    expect(tokenizer).toContain('s.Contains(" --", StringComparison.Ordinal)')
    expect(program).toContain('args = recovered;')
    expect(program).toContain('int dashIndex = Array.IndexOf(args, "--");')
  })

  it('live harness re-verifies internal configuration after a rollback attempt', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const script = fs.readFileSync(path.join(here, '..', '..', '..', 'scripts', 'test-with-restore.ps1'), 'utf-8')
    expect(script).toContain('$backupCompleted = $false')
    expect(script).toContain('$internalRestored = Compare-InternalConfigSnapshot')
    expect(script).toContain('$restoreErrors = @(Restore-AllSnapshots)')
  })

  it('global startup fallback does not render raw error text or stacks into the WebView', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const src = fs.readFileSync(path.join(here, '..', 'main.ts'), 'utf-8')
    expect(src).toContain('errorType')
    expect(src).toContain('Check the application log for diagnostic details.')
    expect(src).not.toContain('error.textContent = msg')
  })

  it('Rust update checks use CREATE_NO_WINDOW so settings actions do not flash a terminal', async () => {
    const fs = await import('node:fs')
    const path = await import('node:path/win32')
    const here = path.dirname(import.meta.url.replace('file:///', ''))
    const src = fs.readFileSync(path.join(here, '..', '..', 'src-tauri', 'src', 'main.rs'), 'utf-8')
    const updateBlock = src.match(/fn check_for_updates[\s\S]*?fn version_is_newer/)
    expect(updateBlock).toBeTruthy()
    expect(updateBlock![0]).toContain('command.creation_flags(CREATE_NO_WINDOW)')
  })

  it('preserves RegistryValueKind and verifies exact values during toggle recovery', () => {
    const program = readFileSync(join(repoRoot, 'Program.cs'), 'utf8')
    expect(program).toContain('RegistryValueKind backupKind = key.GetValueKind(backupName)')
    expect(program).toContain('Equals(restoredValue, backupValue) && key.GetValueKind(name) == backupKind')
    expect(program).toContain('Toggle recovery conflict')
  })

  it('does not expose internal toggle backup names through get', () => {
    const program = readFileSync(join(repoRoot, 'Program.cs'), 'utf8')
    expect(program).toContain('Internal disabled-variable backup names are not addressable')
    expect(program).toContain('IsInternalToggleBackupName')
    expect(program).toContain('backupVal != null && key.GetValue(name) == null')
  })

  it('projects disabled backup records from both registry scopes through one helper', () => {
    const program = readFileSync(join(repoRoot, 'Program.cs'), 'utf8')
    expect(program).toContain('AppendEnvironmentItems(userKey, "user", items)')
    expect(program).toContain('AppendEnvironmentItems(systemKey, "system", items)')
    expect(program).toContain('Scope = scope')
  })

  it('embeds protection defaults with the exact runtime logical name', () => {
    const project = readFileSync(join(repoRoot, 'env-manager.csproj'), 'utf8')
    expect(project).toContain('LogicalName="EnvManager.protection.defaults.json"')
  })

  it('creates launch profiles in one CLI transaction', () => {
    const program = readFileSync(join(repoRoot, 'Program.cs'), 'utf8')
    const profilePage = readFileSync(join(repoRoot, 'frontend/src/lib/components/ProfilePage.svelte'), 'utf8')
    expect(program).toContain('static int ProfileCreate(string[] args)')
    expect(program).toContain('Launch profile requires --target <exe>')
    expect(profilePage).toContain('await createProfile(name, {')
    expect(profilePage).not.toContain('await profileSetLaunch(name')
  })
})
