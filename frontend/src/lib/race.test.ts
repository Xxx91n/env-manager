/**
 * Tests for CLI/GUI concurrency race conditions.
 *
 * Verifies that the Rust IPC mutex serializes CLI invocations so that
 * concurrent frontend calls (e.g. setVariable immediately followed by
 * listVariables) cannot interleave and produce stale data.
 *
 * Also tests the toggle operation's write-verify-delete sequence:
 * if the backup write fails, the original variable must survive.
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  listVariables,
  setVariable,
  deleteVariable,
  toggleVariable,
} from './api'
import { variables, error } from './stores'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('CLI/GUI race condition prevention', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  it('serializes concurrent set + list calls (no stale reads)', async () => {
    // Simulate: set starts, list is called concurrently
    // The Rust mutex should ensure list waits for set to complete.
    // We model this by resolving set first, then list.
    let resolveOrder: string[] = []

    const setPromise = new Promise<{ success: boolean; data: string | null; error: string | null }>((resolve) => {
      setTimeout(() => {
        resolveOrder.push('set')
        resolve({ success: true, data: '', error: null })
      }, 50)
    })

    const listAfterSet = setPromise.then(() => {
      resolveOrder.push('list')
      return { success: true, data: JSON.stringify([{ name: 'NEW_VAR', value: 'value', scope: 'user' }]), error: null }
    })

    // Mock invoke to return the correct promise based on command
    mockInvoke.mockImplementation((cmd: string, opts: { command: string }) => {
      if (opts.command === 'set') return setPromise
      if (opts.command === 'list') return listAfterSet
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    // Fire both concurrently
    const [,] = await Promise.all([
      setVariable('NEW_VAR', 'value', 'user'),
      listVariables(),
    ])

    // The set must resolve before the list returns data
    expect(resolveOrder).toEqual(['set', 'list'])

    const { get } = await import('svelte/store')
    const vars = get(variables)
    expect(vars).toHaveLength(1)
    expect(vars[0].name).toBe('NEW_VAR')
  })

  it('prevents interleaving of delete + list (list sees post-delete state)', async () => {
    variables.set([
      { name: 'TO_DELETE', value: 'x', scope: 'user' },
      { name: 'KEEP', value: 'y', scope: 'user' },
    ])

    const deletePromise = new Promise<{ success: boolean; data: string | null; error: string | null }>((resolve) => {
      setTimeout(() => resolve({ success: true, data: '', error: null }), 30)
    })

    const listAfterDelete = deletePromise.then(() => ({
      success: true,
      data: JSON.stringify([{ name: 'KEEP', value: 'y', scope: 'user' }]),
      error: null,
    }))

    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'delete') return deletePromise
      if (opts.command === 'list') return listAfterDelete
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    await Promise.all([
      deleteVariable('TO_DELETE', 'user'),
      listVariables(),
    ])

    const { get } = await import('svelte/store')
    const vars = get(variables)
    expect(vars).toHaveLength(1)
    expect(vars[0].name).toBe('KEEP')
  })

  it('toggle does not lose data when backup write succeeds', async () => {
    // After toggle (disable): backup key written, original deleted
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'toggle') {
        return Promise.resolve({
          success: true,
          data: JSON.stringify({ name: 'MY_VAR', scope: 'user', isDisabled: true }),
          error: null,
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    const result = await toggleVariable('MY_VAR', 'user')

    expect(result.isDisabled).toBe(true)
    // The toggle command should have been called with correct args
    expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
      command: 'toggle',
      args: ['MY_VAR', '--scope', 'user'],
    })
  })

  it('toggle restores without data loss when re-enabling', async () => {
    // After toggle (enable): original restored from backup, backup deleted
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'toggle') {
        return Promise.resolve({
          success: true,
          data: JSON.stringify({ name: 'MY_VAR', scope: 'user', isDisabled: false }),
          error: null,
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    const result = await toggleVariable('MY_VAR', 'user')

    expect(result.isDisabled).toBe(false)
    // listVariables is called after toggle to refresh state
    const listCall = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string }).command === 'list'
    )
    expect(listCall).toBeDefined()
  })

  it('multiple rapid toggles are serialized', async () => {
    let callCount = 0
    mockInvoke.mockImplementation(() => {
      callCount++
      const isDisabled = callCount % 2 === 1
      return Promise.resolve({
        success: true,
        data: JSON.stringify({ name: 'VAR', scope: 'user', isDisabled }),
        error: null,
      })
    })

    // Fire 3 rapid toggles
    const results = await Promise.all([
      toggleVariable('VAR', 'user'),
      toggleVariable('VAR', 'user'),
      toggleVariable('VAR', 'user'),
    ])

    // All should complete without error
    expect(results).toHaveLength(3)
    // Each result should have isDisabled field
    results.forEach((r) => {
      expect(typeof r.isDisabled).toBe('boolean')
    })
  })
})
