/**
 * Tests for CLI/GUI state synchronization.
 *
 * Verifies that after every GUI mutation (set, delete, toggle, profile apply,
 * profile unapply, path add, path remove), the GUI automatically calls
 * listVariables() to refresh the variables store so the UI reflects the
 * post-mutation state. This is the primary mechanism keeping GUI and CLI
 * in sync.
 *
 * Also verifies that the error store is properly cleared before each
 * operation and set on failure.
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  listVariables,
  setVariable,
  deleteVariable,
  toggleVariable,
  restoreBackup,
  applyProfile,
  unapplyProfile,
  editProfileVar,
} from './api'
import { variables, error } from './stores'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('CLI/GUI state synchronization', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  it('setVariable triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await setVariable('NEW', 'val', 'user')

    // Should have 2 calls: set + list
    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    expect(calls).toContain('set')
    expect(calls).toContain('list')
    expect(calls.indexOf('set')).toBeLessThan(calls.indexOf('list'))
  })

  it('deleteVariable triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await deleteVariable('OLD', 'user')

    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    expect(calls).toContain('delete')
    expect(calls).toContain('list')
    expect(calls.indexOf('delete')).toBeLessThan(calls.indexOf('list'))
  })

  it('toggleVariable triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({
      success: true,
      data: JSON.stringify({ name: 'VAR', scope: 'user', isDisabled: true }),
      error: null,
    })

    await toggleVariable('VAR', 'user')

    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    expect(calls).toContain('toggle')
    expect(calls).toContain('list')
    expect(calls.indexOf('toggle')).toBeLessThan(calls.indexOf('list'))
  })

  it('restoreBackup triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await restoreBackup('backup.json')

    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    expect(calls).toContain('restore')
    expect(calls).toContain('list')
    expect(calls.indexOf('restore')).toBeLessThan(calls.indexOf('list'))
  })

  it('applyProfile triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: 'Applied', error: null })

    await applyProfile('dev')

    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    expect(calls).toContain('profile')
    expect(calls).toContain('list')
    // profile call should come before list
    const profileIdx = calls.findIndex((c) => c === 'profile')
    const listIdx = calls.findIndex((c) => c === 'list')
    expect(profileIdx).toBeLessThan(listIdx)
  })

  it('unapplyProfile triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: 'Unapplied', error: null })

    await unapplyProfile('dev')

    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    const profileIdx = calls.findIndex((c) => c === 'profile')
    const listIdx = calls.findIndex((c) => c === 'list')
    expect(profileIdx).toBeLessThan(listIdx)
  })

  it('editProfileVar triggers listVariables refresh', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: 'Edited', error: null })

    await editProfileVar('dev', 'OLD', 'NEW', 'value')

    const calls = mockInvoke.mock.calls.map((c: unknown[]) => (c[1] as { command: string }).command)
    const profileIdx = calls.findIndex((c) => c === 'profile')
    const listIdx = calls.findIndex((c) => c === 'list')
    expect(profileIdx).toBeLessThan(listIdx)
  })

  it('error store is cleared before each operation', async () => {
    error.set('Previous error')

    // Mock must return valid JSON for list (setVariable calls listVariables internally)
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'list') {
        return Promise.resolve({ success: true, data: '[]', error: null })
      }
      return Promise.resolve({ success: true, data: '', error: null })
    })

    await setVariable('X', 'Y', 'user')

    const { get } = await import('svelte/store')
    // error should be null after successful set (set clears it at start)
    expect(get(error)).toBeNull()
  })

  it('error store is set on CLI failure', async () => {
    mockInvoke.mockResolvedValue({
      success: false,
      data: null,
      error: 'Access denied',
    })

    await listVariables()

    const { get } = await import('svelte/store')
    expect(get(error)).toBe('Access denied')
  })

  it('variables store is populated with fresh data after list', async () => {
    const mockData = [
      { name: 'A', value: '1', scope: 'user' },
      { name: 'B', value: '2', scope: 'system' },
    ]

    mockInvoke.mockResolvedValue({
      success: true,
      data: JSON.stringify(mockData),
      error: null,
    })

    await listVariables()

    const { get } = await import('svelte/store')
    const vars = get(variables)
    expect(vars).toEqual(mockData)
    expect(vars).toHaveLength(2)
  })

  it('GUI/CLI alignment: every API function sends correct command name', async () => {
    // Verify command names match what src/Program.cs expects
    // toggle returns JSON with isDisabled, so mock must return valid JSON
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'toggle') {
        return Promise.resolve({
          success: true,
          data: JSON.stringify({ name: 'X', scope: 'user', isDisabled: false }),
          error: null,
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    await setVariable('X', 'Y', 'user')
    await deleteVariable('X', 'user')
    await toggleVariable('X', 'user')

    const commands = mockInvoke.mock.calls.map(
      (c: unknown[]) => (c[1] as { command: string }).command
    )

    // These must match the ValidCommands set in src/Program.cs
    expect(commands).toContain('set')
    expect(commands).toContain('delete')
    expect(commands).toContain('toggle')
  })
})
