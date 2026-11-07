import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  listVariables,
  setVariable,
  deleteVariable,
  createBackup,
  listProfiles,
  createProfile,
  applyProfile,
  unapplyProfile,
  listPathEntries,
  addPathEntry,
  removePathEntry,
  movePathEntryUp,
  movePathEntryDown,
  listVariablesRaw,
  invalidateApiCache,
  listHistory,
  clearHistory,
} from './api'
import { isCliInPath, removeCliFromPath, listProtection } from './api'
import { variables, error } from './stores'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('api module', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  describe('listVariables', () => {
    it('parses JSON output and populates store', async () => {
      const mockData = [
        { name: 'PATH', value: 'C:\\Windows', scope: 'user' },
      ]
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify(mockData),
        error: null,
      })

      await listVariables()

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'list',
        args: [],
      })
      const { get } = await import('svelte/store')
      expect(get(variables)).toEqual(mockData)
    })

    it('sets error store on failure', async () => {
      mockInvoke.mockResolvedValue({
        success: false,
        data: null,
        error: 'CLI error',
      })

      await listVariables()

      const { get } = await import('svelte/store')
      expect(get(error)).toBe('CLI error')
    })
  })

  describe('setVariable', () => {
    it('calls run_cli with correct args', async () => {
      mockInvoke.mockResolvedValue({
        success: true,
        data: '',
        error: null,
      })

      await setVariable('JAVA_HOME', 'C:\\Java', 'user')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'set',
        args: ['JAVA_HOME', 'C:\\Java', '--scope', 'user'],
      })
    })

    it('throws on failure', async () => {
      mockInvoke.mockResolvedValue({
        success: false,
        data: null,
        error: 'Access denied',
      })

      await expect(setVariable('X', 'Y', 'system')).rejects.toThrow('Access denied')
    })
  })

  describe('deleteVariable', () => {
    it('calls run_cli with delete command', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

      await deleteVariable('TEMP_VAR', 'user')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'delete',
        args: ['TEMP_VAR', '--scope', 'user'],
      })
    })
  })

  describe('createBackup', () => {
    it('calls backup with no args when no output file', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'backup created', error: null })

      const result = await createBackup()
      expect(result).toBe('backup created')
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'backup',
        args: [],
      })
    })

    it('calls backup with --output when file specified', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await createBackup('my_backup.json')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'backup',
        args: ['--output', 'my_backup.json'],
      })
    })
  })

  describe('Profile API', () => {
    it('listProfiles parses JSON array', async () => {
      const mockProfiles = [
        { id: '1', name: 'dev', isEnabled: false, variables: [] },
      ]
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify(mockProfiles),
        error: null,
      })

      const result = await listProfiles()
      expect(result).toEqual(mockProfiles)
    })

    it('createProfile sends create subcommand', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await createProfile('myprofile')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['create', 'myprofile'],
      })
    })

    it('createProfile forwards launch options in one command', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await createProfile('tool-run', {
        type: 'launch',
        target: 'C:\\Tools\\tool.exe',
        args: '--safe',
        cwd: 'C:\\Tools',
      })

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['create', 'tool-run', '--type', 'launch', '--target', 'C:\\Tools\\tool.exe', '--args', '--safe', '--cwd', 'C:\\Tools'],
      })
    })

    it('applyProfile sends apply subcommand', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await applyProfile('dev')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['apply', 'dev'],
      })
    })

    it('unapplyProfile sends unapply subcommand', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await unapplyProfile('dev')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['unapply', 'dev'],
      })
    })
  })

  describe('Path API', () => {
    it('listPathEntries parses JSON array', async () => {
      const mockEntries = [
        { index: 0, path: 'C:\\Windows' },
        { index: 1, path: 'C:\\System32' },
      ]
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify(mockEntries),
        error: null,
      })

      const result = await listPathEntries('user')
      expect(result).toEqual(mockEntries)
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['list', '--scope', 'user'],
      })
    })

    it('addPathEntry sends add subcommand with scope', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await addPathEntry('C:\\Tools', 'user')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['add', 'C:\\Tools', '--scope', 'user'],
      })
    })

    it('addPathEntry with index sends --index flag', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await addPathEntry('C:\\Tools', 'system', 2)

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['add', 'C:\\Tools', '--scope', 'system', '--index', '2'],
      })
    })

    it('removePathEntry sends remove subcommand', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await removePathEntry('C:\\Tools', 'user')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['remove', 'C:\\Tools', '--scope', 'user'],
      })
    })

    it('movePathEntryUp sends move-up with index', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await movePathEntryUp(1, 'user')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['move-up', '1', '--scope', 'user'],
      })
    })

    it('movePathEntryDown sends move-down with index', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

      await movePathEntryDown(0, 'user')

      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['move-down', '0', '--scope', 'user'],
      })
    })
  })

  describe('CLI PATH management', () => {
    it('isCliInPath returns false when CLI not found', async () => {
      mockInvoke.mockResolvedValue({
        resolved_cli_path: 'NOT FOUND',
        gui_exe_dir: '',
        cwd: '',
      })

      const result = await isCliInPath()
      expect(result).toBe(false)
    })

    it('isCliInPath checks real PATH entries', async () => {
      mockInvoke.mockImplementation((cmd: string) => {
        if (cmd === 'cli_diagnostics') {
          return Promise.resolve({
            resolved_cli_path: 'C:\\Tools\\env-manager-cli.exe',
            gui_exe_dir: 'C:\\Tools',
            cwd: 'C:\\Tools',
          })
        }
        if (cmd === 'run_cli') {
          return Promise.resolve({
            success: true,
            data: JSON.stringify([{ index: 0, path: 'C:\\Tools' }]),
            error: null,
          })
        }
        return Promise.resolve({ success: true, data: '', error: null })
      })

      const result = await isCliInPath()
      expect(result).toBe(true)
    })

    it('removeCliFromPath sends remove command when CLI is in PATH', async () => {
      mockInvoke.mockImplementation((cmd: string, opts?: { command?: string; args?: string[] }) => {
        if (cmd === 'cli_diagnostics') {
          return Promise.resolve({
            resolved_cli_path: 'C:\\Tools\\env-manager-cli.exe',
            gui_exe_dir: 'C:\\Tools',
            cwd: 'C:\\Tools',
          })
        }
        if (cmd === 'run_cli' && opts?.command === 'path' && opts?.args?.[0] === 'list') {
          return Promise.resolve({
            success: true,
            data: JSON.stringify([{ index: 0, path: 'C:\\Tools' }]),
            error: null,
          })
        }
        return Promise.resolve({ success: true, data: 'ok', error: null })
      })

      const result = await removeCliFromPath()
      expect(result.removed).toBe(true)
    })
  })

  describe('read cache generation safety', () => {
    it('coalesces concurrent raw variable reads into one CLI invocation', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: JSON.stringify([{ name: 'ONE', value: '1', scope: 'user' }]), error: null })
      const results = await Promise.all([listVariablesRaw(true), listVariablesRaw(true), listVariablesRaw(true)])
      expect(results[0]).toEqual(results[1])
      expect(results[1]).toEqual(results[2])
      expect(mockInvoke).toHaveBeenCalledTimes(1)
    })

    it('does not allow a stale PATH read to repopulate cache after invalidation', async () => {
      let resolveFirst: ((value: unknown) => void) | undefined
      mockInvoke.mockImplementation(() => new Promise((resolve) => { resolveFirst = resolve }))
      const first = listPathEntries('user', true)
      invalidateApiCache()
      mockInvoke.mockResolvedValue({ success: true, data: JSON.stringify([{ index: 0, path: 'C:\\Fresh' }]), error: null })
      const fresh = await listPathEntries('user', true)
      resolveFirst?.({ success: true, data: JSON.stringify([{ index: 0, path: 'C:\\Stale' }]), error: null })
      await first
      const cached = await listPathEntries('user')
      expect(fresh[0].path).toBe('C:\\Fresh')
      expect(cached[0].path).toBe('C:\\Fresh')
    })
  })

  describe('listProtection IPC unwrap regression', () => {
    // Regression: listProtection previously called invoke<ProtectionData>('run_cli', ...)
    // directly, bypassing runCommand's `result.data` unwrap + JSON.parse. The invoke
    // returned a CliResponse { success, data, error } shape cast to ProtectionData,
    // which left data.protectedVars undefined inside ProtectionPage.refresh. Accessing
    // data!.protectedVars.builtIn.includes() then threw a TypeError on every refresh,
    // the catch block showed a toast but loading was never reset to false, and the
    // page appeared stuck on a spinner. The fix routes listProtection through runRead
    // + JSON.parse the same way listHistory does. This test asserts the new path: a
    // CLI invocation returning the wrapped JSON body is correctly unwrapped into a
    // ProtectionData object whose sub-fields are defined.
    it('unwraps CliResponse.data JSON into ProtectionData (no more undefined sub-fields)', async () => {
      const mockProtectionData = {
        protectedVars: {
          builtIn: ['PATH', 'SystemRoot'],
          custom: ['MY_LOCKED_VAR'],
        },
        protectedPaths: {
          builtIn: ['C:\\Windows\\System32'],
          custom: ['C:\\My\\Locked'],
        },
      }
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify(mockProtectionData),
        error: null,
      })
      const result = await listProtection(true)

      expect(result).toStrictEqual(mockProtectionData)
      expect(result.protectedVars.builtIn).toContain('PATH')
      expect(result.protectedVars.custom).toContain('MY_LOCKED_VAR')
      expect(result.protectedPaths.builtIn).toContain('C:\\Windows\\System32')
      expect(result.protectedPaths.custom).toContain('C:\\My\\Locked')
      expect(mockInvoke).toHaveBeenCalledTimes(1)
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', { command: 'protection', args: ['list'] })
    })

    it('does not leak a CliResponse object shape as ProtectionData', async () => {
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify({
          protectedVars: { builtIn: [], custom: [] },
          protectedPaths: { builtIn: [], custom: [] },
        }),
        error: null,
      })

      const result = await listProtection(true)
      // If the regression returns, result would be the full CliResponse object.
      // Asserting it has NO `success`/`data`/`error` keys keeps the unwrap honest.
      expect(result).not.toHaveProperty('success')
      expect(result).not.toHaveProperty('data')
      expect(result).not.toHaveProperty('error')
      expect(result).toHaveProperty('protectedVars')
      expect(result).toHaveProperty('protectedPaths')
    })
  })

  describe('History API cache invalidation', () => {
    it('clears history cache immediately after clearHistory', async () => {
      const initialHistory = [
        { id: '1', timestamp: '2026-11-06T00:00:00Z', command: 'set', name: 'FOO', scope: 'user', oldValue: null, newValue: 'BAR' },
      ]
      mockInvoke.mockImplementation((_cmd: string, opts?: { command?: string; args?: string[] }) => {
        if (opts?.command === 'history' && opts?.args?.[0] === 'list') {
          return Promise.resolve({
            success: true,
            data: JSON.stringify(initialHistory),
            error: null,
          })
        }
        if (opts?.command === 'history' && opts?.args?.[0] === 'delete' && opts?.args?.[1] === '--all') {
          return Promise.resolve({ success: true, data: '', error: null })
        }
        return Promise.resolve({ success: true, data: '[]', error: null })
      })

      // First fetch populates cache
      const fetched = await listHistory(200, true)
      expect(fetched).toEqual(initialHistory)

      // Next call without force returns cache
      const fromCache = await listHistory(200, false)
      expect(fromCache).toEqual(initialHistory)

      // Update mock to return empty list after clear
      mockInvoke.mockImplementation((_cmd: string, opts?: { command?: string; args?: string[] }) => {
        if (opts?.command === 'history' && opts?.args?.[0] === 'list') {
          return Promise.resolve({
            success: true,
            data: '[]',
            error: null,
          })
        }
        return Promise.resolve({ success: true, data: '', error: null })
      })

      // Clear history
      await clearHistory('all')

      // Subsequent listHistory should hit CLI / return empty, not stale cached items
      const afterClear = await listHistory(200, false)
      expect(afterClear).toEqual([])
    })

    it('does not allow an in-flight history read started before clear to repopulate cache', async () => {
      let resolveFirst: ((value: unknown) => void) | undefined
      mockInvoke.mockImplementation((_cmd: string, opts?: { command?: string; args?: string[] }) => {
        if (opts?.command === 'history' && opts?.args?.[0] === 'list') {
          return new Promise((resolve) => { resolveFirst = resolve })
        }
        if (opts?.command === 'history' && opts?.args?.[0] === 'delete' && opts?.args?.[1] === '--all') {
          return Promise.resolve({ success: true, data: '', error: null })
        }
        return Promise.resolve({ success: true, data: '[]', error: null })
      })

      // Start an in-flight read
      const inFlight = listHistory(200, true)

      // In the meantime, user clears history
      await clearHistory('all')

      // Mock CLI returning empty list for post-clear reads
      mockInvoke.mockImplementation((_cmd: string, opts?: { command?: string; args?: string[] }) => {
        if (opts?.command === 'history' && opts?.args?.[0] === 'list') {
          return Promise.resolve({ success: true, data: '[]', error: null })
        }
        return Promise.resolve({ success: true, data: '', error: null })
      })

      // Post-clear read starts while the pre-clear read is still pending
      const postClear = listHistory(200, false)

      // First (stale) read finishes with old items
      resolveFirst?.({
        success: true,
        data: JSON.stringify([{ id: 'old', timestamp: '2026-11-06T00:00:00Z', command: 'set', name: 'OLD', scope: 'user', oldValue: null, newValue: 'VAL' }]),
        error: null,
      })
      await inFlight
      expect(await postClear).toEqual([])

      // Cache should NOT be repopulated with the stale result
      const fresh = await listHistory(200, false)
      expect(fresh).toEqual([])
    })
  })
})
