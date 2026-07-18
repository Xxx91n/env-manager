import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  profileSetLaunch,
  profileLaunch,
  pathHealth,
} from './api'
import { variables, error } from './stores'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('v0.6.0 launch profile + PATH health', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    variables.set([])
    error.set(null)
  })

  describe('profileSetLaunch', () => {
    it('builds full set-launch args with target/args/cwd/type=launch', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'Updated', error: null })
      await profileSetLaunch('node-dev', {
        target: 'C:\\node\\node.exe',
        args: '--inspect',
        cwd: 'D:\\proj',
        type: 'launch',
      })
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: [
          'set-launch',
          'node-dev',
          '--target',
          'C:\\node\\node.exe',
          '--args',
          '--inspect',
          '--cwd',
          'D:\\proj',
          '--type',
          'launch',
        ],
      })
    })

    it('omits flags when undefined', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })
      await profileSetLaunch('minimal', {})
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['set-launch', 'minimal'],
      })
    })

    it('throws on failure', async () => {
      mockInvoke.mockResolvedValue({ success: false, data: null, error: 'apply state' })
      await expect(profileSetLaunch('applied', { target: 'C:\\a.exe' })).rejects.toThrow('apply state')
    })
  })

  describe('profileLaunch', () => {
    it('sends -- and extra args when provided', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'Launched', error: null })
      await profileLaunch('node-dev', ['app.js', '--port=3000'])
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['launch', 'node-dev', '--', 'app.js', '--port=3000'],
      })
    })

    it('omits -- when no extra args', async () => {
      mockInvoke.mockResolvedValue({ success: true, data: 'Launched', error: null })
      await profileLaunch('node-dev')
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'profile',
        args: ['launch', 'node-dev'],
      })
    })

    it('throws on failure', async () => {
      mockInvoke.mockResolvedValue({ success: false, data: null, error: 'not found' })
      await expect(profileLaunch('missing')).rejects.toThrow('not found')
    })
  })

  describe('pathHealth', () => {
    it('defaults: read-only, scope user, no flags', async () => {
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify({
          scope: 'user',
          dryRun: true,
          totalEntries: 0,
          healthyCount: 0,
          duplicateCount: 0,
          deadCount: 0,
          wouldFix: false,
          results: [],
        }),
        error: null,
      })
      await pathHealth()
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['health', '--scope', 'user'],
      })
    })

    it('fix=true routes through runWrite (adds --fix)', async () => {
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify({
          scope: 'user',
          dryRun: false,
          totalEntries: 2,
          healthyCount: 1,
          duplicateCount: 0,
          deadCount: 1,
          wouldFix: false,
          results: [
            { entry: 'C:\\ok', status: 'healthy', isProtected: false, isDead: false, isDuplicate: false, fullPath: 'C:\\ok' },
            { entry: 'C:\\gone', status: 'dead', isProtected: false, isDead: true, isDuplicate: false, fullPath: 'C:\\gone' },
          ],
        }),
        error: null,
      })
      const r = await pathHealth('user', true, false)
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['health', '--scope', 'user', '--fix'],
      })
      expect(r.deadCount).toBe(1)
      expect(r.results.length).toBe(2)
      expect(r.results[1].status).toBe('dead')
    })

    it('dryRun=true adds --dry-run (still read)', async () => {
      mockInvoke.mockResolvedValue({
        success: true,
        data: JSON.stringify({
          scope: 'system',
          dryRun: true,
          totalEntries: 0,
          healthyCount: 0,
          duplicateCount: 0,
          deadCount: 0,
          wouldFix: false,
          results: [],
        }),
        error: null,
      })
      await pathHealth('system', false, true)
      expect(mockInvoke).toHaveBeenCalledWith('run_cli', {
        command: 'path',
        args: ['health', '--scope', 'system', '--dry-run'],
      })
    })
  })
})
