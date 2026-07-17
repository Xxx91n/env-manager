import { describe, it, expect, vi, beforeEach } from 'vitest'

// Mock the invoke bridge before importing the api module
const invokeMock = vi.fn()
vi.mock('@tauri-apps/api/core', () => ({
  invoke: (cmd: string, args?: Record<string, unknown>) => invokeMock(cmd, args),
}))

import { dedupePathEntries } from './api'

describe('dedupePathEntries', () => {
  beforeEach(() => invokeMock.mockReset())

  it('sends correct args for a real dedupe run on user scope', async () => {
    invokeMock.mockResolvedValueOnce({
      success: true,
      data: JSON.stringify({
        scope: 'user',
        removedCount: 2,
        keptCount: 3,
        removed: ['D:\\dup1', 'D:\\dup2'],
        kept: ['A', 'D:\\dup1', 'D:\\dup2'],
      }),
      error: null,
    })
    const result = await dedupePathEntries('user', false)
    expect(invokeMock).toHaveBeenCalledWith('run_cli', {
      command: 'path',
      args: ['dedupe', '--scope', 'user'],
    })
    expect(result.removedCount).toBe(2)
    expect(result.keptCount).toBe(3)
    expect(result.removed).toHaveLength(2)
    expect(result.dryRun).toBeUndefined()
  })

  it('sends --dry-run for a preview and keeps the flag in the result', async () => {
    invokeMock.mockResolvedValueOnce({
      success: true,
      data: JSON.stringify({
        scope: 'system',
        dryRun: true,
        removedCount: 0,
        keptCount: 5,
        removed: [],
        kept: ['a', 'b', 'c', 'd', 'e'],
      }),
      error: null,
    })
    const result = await dedupePathEntries('system', true)
    expect(invokeMock).toHaveBeenCalledWith('run_cli', {
      command: 'path',
      args: ['dedupe', '--scope', 'system', '--dry-run'],
    })
    expect(result.dryRun).toBe(true)
    expect(result.removedCount).toBe(0)
  })

  it('defaults to user scope without dry-run', async () => {
    invokeMock.mockResolvedValueOnce({
      success: true,
      data: JSON.stringify({
        scope: 'user',
        removedCount: 0,
        keptCount: 4,
        removed: [],
        kept: ['w', 'x', 'y', 'z'],
      }),
      error: null,
    })
    await dedupePathEntries()
    expect(invokeMock).toHaveBeenCalledWith('run_cli', {
      command: 'path',
      args: ['dedupe', '--scope', 'user'],
    })
  })

  it('propagates CLI failure as an error and sets the error store', async () => {
    invokeMock.mockResolvedValueOnce({
      success: false,
      data: '',
      error: 'Scope must be user or system',
    })
    await expect(dedupePathEntries('system' as 'user', false)).rejects.toThrow(
      'Scope must be user or system'
    )
  })
})
