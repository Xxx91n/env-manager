import { beforeEach, describe, expect, it, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  bulkExport,
  bulkImport,
  expandVariableValue,
  listHistory,
  previewProfile,
  renameVariable,
  setProfileInheritance,
} from './api'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('extended API contract', () => {
  beforeEach(() => vi.clearAllMocks())

  it('uses the atomic rename command with explicit overwrite', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })
    await renameVariable('OLD', 'NEW', 'user', true)
    expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
      command: 'rename',
      args: ['OLD', 'NEW', '--scope', 'user', '--overwrite'],
    })
  })

  it('runs bulk import as a read-only dry run before overwrite', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '{"count":2,"conflicts":[]}', error: null })
    const preview = await bulkImport('vars.env', 'user', false, true)
    expect(preview).toEqual({ count: 2, conflicts: [] })
    expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
      command: 'bulk',
      args: ['import', 'vars.env', '--scope', 'user', '--dry-run'],
    })
  })

  it('exports environment files through the CLI', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '{}', error: null })
    await bulkExport('vars.csv', 'system')
    expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
      command: 'bulk',
      args: ['export', 'vars.csv', '--scope', 'system'],
    })
  })

  it('parses expansion, history, and profile preview responses', async () => {
    mockInvoke
      .mockResolvedValueOnce({ success: true, data: '{"expanded":"C:\\\\Temp"}', error: null })
      .mockResolvedValueOnce({ success: true, data: '[]', error: null })
      .mockResolvedValueOnce({ success: true, data: '{"profile":"dev","inherits":[],"variables":[],"pathEntries":[]}', error: null })
    expect(await expandVariableValue('%TEMP%')).toBe('C:\\Temp')
    expect(await listHistory()).toEqual([])
    expect((await previewProfile('dev')).profile).toBe('dev')
  })

  it('serializes profile inheritance updates', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '{}', error: null })
    await setProfileInheritance('child', ['base', 'tools'])
    expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
      command: 'profile',
      args: ['set-inherits', 'child', 'base', 'tools'],
    })
  })
})
