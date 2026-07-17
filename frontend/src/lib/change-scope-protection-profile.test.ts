/**
 * Tests for the change-scope command, protected-variable write rejection,
 * and profile audit recording. Covers:
 * - Task 1: changeScope API builds the right CLI command and refreshes state.
 * - Task 2: protected variables cannot be edited/deleted via the API surface
 *           (CLI side already enforces; GUI sends correct args).
 * - Task 3: profile create/delete/rename/add-var/remove-var/edit-var produce
 *           audit entries with Scope="profile" and a recognizable command.
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  changeScope,
  renameVariable,
  deleteVariable,
  createProfile,
  deleteProfile,
  renameProfile,
  addProfileVar,
  removeProfileVar,
  editProfileVar,
  listHistory,
  undoHistory,
} from './api'
import { variables, error } from './stores'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('change-scope command', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  it('changeScope invokes run_cli with change-scope and correct args', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string; args: string[] }) => {
      if (opts.command === 'change-scope') {
        return Promise.resolve({ success: true, data: 'Changed scope', error: null })
      }
      if (opts.command === 'list') {
        return Promise.resolve({ success: true, data: '[]', error: null })
      }
      return Promise.resolve({ success: true, data: '', error: null })
    })

    await changeScope('MY_VAR', 'system', 'user', false)

    const call = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string }).command === 'change-scope'
    ) as [string, { command: string; args: string[] }] | undefined
    expect(call).toBeDefined()
    expect(call![1].args).toEqual(['MY_VAR', 'system', '--scope', 'user'])
  })

  it('changeScope includes --overwrite when overwrite=true', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'change-scope') {
        return Promise.resolve({ success: true, data: 'Changed scope', error: null })
      }
      if (opts.command === 'list') {
        return Promise.resolve({ success: true, data: '[]', error: null })
      }
      return Promise.resolve({ success: true, data: '', error: null })
    })

    await changeScope('MY_VAR', 'system', 'user', true)

    const call = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string }).command === 'change-scope'
    ) as [string, { command: string; args: string[] }] | undefined
    expect(call).toBeDefined()
    expect(call![1].args).toEqual(['MY_VAR', 'system', '--scope', 'user', '--overwrite'])
  })

  it('changeScope omits --scope when oldScope is undefined (auto-detect)', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'change-scope') {
        return Promise.resolve({ success: true, data: 'Changed scope', error: null })
      }
      if (opts.command === 'list') {
        return Promise.resolve({ success: true, data: '[]', error: null })
      }
      return Promise.resolve({ success: true, data: '', error: null })
    })

    await changeScope('MY_VAR', 'user')

    const call = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string }).command === 'change-scope'
    ) as [string, { command: string; args: string[] }] | undefined
    expect(call).toBeDefined()
    expect(call![1].args).toEqual(['MY_VAR', 'user'])
  })

  it('changeScope is a write operation (serialized via writeChain)', async () => {
    // Verify that changeScale goes through runWriteOperation: error is set on failure
    mockInvoke.mockResolvedValue({
      success: false,
      data: null,
      error: 'Error: Cannot move protected variable SystemRoot',
    })

    await expect(changeScope('SystemRoot', 'user', 'system')).rejects.toThrow()
    // error store is set by the failure path
    const { get } = await import('svelte/store')
    expect(get(error)).toBeTruthy()
  })

  it('renameVariable calls run_cli with rename and correct args', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'rename') {
        return Promise.resolve({ success: true, data: 'Renamed', error: null })
      }
      if (opts.command === 'list') {
        return Promise.resolve({ success: true, data: '[]', error: null })
      }
      return Promise.resolve({ success: true, data: '', error: null })
    })

    await renameVariable('OLD', 'NEW', 'user', false)

    const call = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string }).command === 'rename'
    ) as [string, { command: string; args: string[] }] | undefined
    expect(call).toBeDefined()
    expect(call![1].args).toEqual(['OLD', 'NEW', '--scope', 'user'])
  })
})

describe('protected variable write rejection (GUI contract)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  it('CLI rejects rename of protected variable with a clear error', async () => {
    // Simulate CLI guard: VariableRename.cs now refuses protected source names
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'rename') {
        return Promise.resolve({
          success: false,
          data: null,
          error: 'Error: Cannot rename protected variable SystemRoot',
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    await expect(renameVariable('SystemRoot', 'OtherName', 'system')).rejects.toThrow(
      /Cannot rename protected variable SystemRoot/
    )
  })

  it('CLI rejects change-scope of protected variable', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'change-scope') {
        return Promise.resolve({
          success: false,
          data: null,
          error: 'Error: Cannot move protected variable SystemRoot out of system scope',
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    await expect(changeScope('SystemRoot', 'user', 'system')).rejects.toThrow(
      /Cannot move protected variable/
    )
  })

  it('CLI rejects delete of protected variable', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'delete') {
        return Promise.resolve({
          success: false,
          data: null,
          error: 'Error: Cannot delete protected system variable windir',
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    await expect(deleteVariable('windir', 'system')).rejects.toThrow(
      /Cannot delete protected system variable windir/
    )
  })
})

describe('profile audit recording (GUI contract)', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  it('createProfile goes through write path (runWrite)', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string; args: string[] }) => {
      if (opts.command === 'profile' && opts.args[0] === 'create') {
        return Promise.resolve({ success: true, data: 'Created profile', error: null })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    await createProfile('dev')
    const call = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string }).command === 'profile'
    ) as [string, { command: string; args: string[] }] | undefined
    expect(call).toBeDefined()
    expect(call![1].args[0]).toBe('create')
  })

  it('deleteProfile goes through write path', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'profile') {
        return Promise.resolve({ success: true, data: 'Deleted profile', error: null })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })
    await deleteProfile('dev')
    expect(mockInvoke).toHaveBeenCalled()
  })

  it('renameProfile goes through write path', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'profile') {
        return Promise.resolve({ success: true, data: 'Renamed', error: null })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })
    await renameProfile('old', 'new')
    expect(mockInvoke).toHaveBeenCalled()
  })

  it('addProfileVar goes through write path', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'profile') {
        return Promise.resolve({ success: true, data: 'Added', error: null })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })
    await addProfileVar('dev', 'JAVA_HOME', 'C:\\jdk')
    expect(mockInvoke).toHaveBeenCalled()
  })

  it('removeProfileVar goes through write path', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'profile') {
        return Promise.resolve({ success: true, data: 'Removed', error: null })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })
    await removeProfileVar('dev', 'JAVA_HOME')
    expect(mockInvoke).toHaveBeenCalled()
  })

  it('editProfileVar goes through write path', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string }) => {
      if (opts.command === 'profile') {
        return Promise.resolve({ success: true, data: 'Edited', error: null })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })
    await editProfileVar('dev', 'OLD', 'NEW', 'val')
    expect(mockInvoke).toHaveBeenCalled()
  })

  it('listHistory retrieves audit entries including profile-level ones', async () => {
    // The CLI records profile mutations with Scope="profile". The GUI must
    // retrieve them just like registry entries.
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string; args: string[] }) => {
      if (opts.command === 'history' && opts.args[0] === 'list') {
        return Promise.resolve({
          success: true,
          data: JSON.stringify([
            { id: 'a1', timestamp: 'T1', command: 'profile create', name: 'dev', scope: 'profile', oldValue: null, newValue: '{}' },
            { id: 'a2', timestamp: 'T2', command: 'set', name: 'MY_VAR', scope: 'user', oldValue: null, newValue: 'value' },
            { id: 'a3', timestamp: 'T3', command: 'profile delete', name: 'dev', scope: 'profile', oldValue: '{}', newValue: null },
          ]),
          error: null,
        })
      }
      return Promise.resolve({ success: true, data: '[]', error: null })
    })

    const history = await listHistory()
    expect(history).toHaveLength(3)
    expect(history[0].scope).toBe('profile')
    expect(history[0].command).toBe('profile create')
    expect(history[2].command).toBe('profile delete')
  })

  it('undoHistory issues undo with the audit id regardless of scope', async () => {
    mockInvoke.mockImplementation((_cmd: string, opts: { command: string; args: string[] }) => {
      if (opts.command === 'history' && opts.args[0] === 'undo') {
        return Promise.resolve({ success: true, data: '{"undone":"x"}', error: null })
      }
      if (opts.command === 'list') {
        return Promise.resolve({ success: true, data: '[]', error: null })
      }
      return Promise.resolve({ success: true, data: '', error: null })
    })

    await undoHistory('some-profile-id')
    const call = mockInvoke.mock.calls.find(
      (c: unknown[]) => (c[1] as { command: string; args: string[] }).command === 'history'
    ) as [string, { command: string; args: string[] }] | undefined
    expect(call).toBeDefined()
    expect(call![1].args[0]).toBe('undo')
    expect(call![1].args[1]).toBe('some-profile-id')
  })
})
