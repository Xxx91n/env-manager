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
} from './api'
import { isCliInPath, removeCliFromPath } from './api'
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
})
