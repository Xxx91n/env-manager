import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  addPathEntry,
  setVariable,
  renameVariable,
  changeScope,
} from './api'
import { error } from './stores'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

/**
 * The Microsoft .NET runtime argv tokenizer has a known hazard: when a quoted
 * PATH value ends with a trailing backslash, the backslash escapes the closing
 * quote, so the quote and any following arguments (e.g. --scope user) get
 * folded INTO the value. The CLI now detects and recovers this case via
 * LenientArgs.WasArgsCorruptedByTrailingBackslashQuote + LenientArgs.Tokenize.
 *
 * The GUI never goes through a shell. It builds an args[] array and sends it
 * to the Tauri Rust backend, which spawns the CLI with Command::arg(). Each
 * arg is an independent element and is never vulnerable to that tokenizer
 * hazard. These tests lock that invariant in place: a regression that
 * concatenates the value with the scope flag would fail loudly.
 */
describe('quoting safety: GUI value args stay independent', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    error.set(null)
  })

  it('addPathEntry sends trailing-backslash value as its own argv element', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

    await addPathEntry('C:\\Program Files\\PowerShell\\7\\', 'user')

    expect(mockInvoke).toHaveBeenCalledTimes(1)
    const call = mockInvoke.mock.calls[0]
    expect(call[0]).toBe('run_cli')
    const payload = call[1] as { command: string; args: string[] }
    expect(payload.command).toBe('path')
    expect(payload.args).toEqual(['add', 'C:\\Program Files\\PowerShell\\7\\', '--scope', 'user'])
    expect(payload.args.some((a) => a.includes(' --scope '))).toBe(false)
  })

  it('addPathEntry with index keeps value separate from --scope and --index', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })

    await addPathEntry('D:\\lib with space\\', 'system', 3)

    const payload = mockInvoke.mock.calls[0][1] as { command: string; args: string[] }
    expect(payload.command).toBe('path')
    expect(payload.args).toEqual(['add', 'D:\\lib with space\\', '--scope', 'system', '--index', '3'])
    expect(payload.args.some((a) => a.includes(' --scope ') || a.includes(' --index '))).toBe(false)
  })

  it('setVariable keeps trailing-backslash value separate from --scope', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await setVariable('JAVA_HOME', 'D:\\jdk17\\', 'user')

    const payload = mockInvoke.mock.calls[0][1] as { command: string; args: string[] }
    expect(payload.command).toBe('set')
    expect(payload.args).toEqual(['JAVA_HOME', 'D:\\jdk17\\', '--scope', 'user'])
    expect(payload.args.some((a) => a.includes(' --scope '))).toBe(false)
  })

  it('setVariable with overwrite keeps value separate from --scope and --overwrite', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await setVariable('JAVA_HOME', 'C:\\Program Files (x86)\\jdk\\', 'user', true)

    const payload = mockInvoke.mock.calls[0][1] as { command: string; args: string[] }
    expect(payload.command).toBe('set')
    expect(payload.args).toEqual(['JAVA_HOME', 'C:\\Program Files (x86)\\jdk\\', '--scope', 'user', '--overwrite'])
    expect(payload.args.some((a) => a.includes(' --scope '))).toBe(false)
    expect(payload.args).toContain('--overwrite')
    expect(payload.args.some((a) => a.endsWith(' --overwrite'))).toBe(false)
  })

  it('renameVariable keeps both old and new names with trailing backslash separate from --scope', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await renameVariable('OLD_HOME\\', 'NEW_HOME\\', 'user')

    const payload = mockInvoke.mock.calls[0][1] as { command: string; args: string[] }
    expect(payload.command).toBe('rename')
    expect(payload.args).toEqual(['OLD_HOME\\', 'NEW_HOME\\', '--scope', 'user'])
    expect(payload.args.some((a) => a.includes(' --scope '))).toBe(false)
  })

  it('changeScope keeps name separate from --scope and target scope arg', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: '', error: null })

    await changeScope('VAR_NAME', 'system', 'user', true)

    const payload = mockInvoke.mock.calls[0][1] as { command: string; args: string[] }
    expect(payload.command).toBe('change-scope')
    expect(payload.args).toEqual(['VAR_NAME', 'system', '--scope', 'user', '--overwrite'])
    expect(payload.args.some((a) => a.includes(' --scope ') || a.includes(' --overwrite'))).toBe(false)
  })
})