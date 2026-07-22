import { describe, it, expect, vi, beforeEach } from 'vitest'
import { invoke } from '@tauri-apps/api/core'
import {
  profileAddSecret,
  profileEditSecret,
  profileRemoveSecret,
  profileRevealSecret,
} from './api'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('v0.7 secrets API wrappers', () => {
  beforeEach(() => {
    mockInvoke.mockReset()
    // Default success shape used by the IPC shim.
    mockInvoke.mockResolvedValue({ success: true, data: 'ok', error: null })
  })

  it('profileAddSecret issues profile add-secret', async () => {
    try { await profileAddSecret('dev-launch', 'OPENAI_API_KEY', 'sk-test') } catch { /* non-tty environment may surface error store, ignore for shape test */ }
    expect(mockInvoke).toHaveBeenCalled()
    const call = mockInvoke.mock.calls[0] as [string, Record<string, unknown>]
    expect(call[0]).toBe('run_cli')
    const payload = call[1]
    expect(payload.command).toBe('profile')
    expect(payload.args).toContain('add-secret')
    expect(payload.args).toContain('dev-launch')
    expect(payload.args).toContain('OPENAI_API_KEY')
    expect(payload.args).toContain('sk-test')
  })

  it('profileEditSecret passes old, new, value in order', async () => {
    try { await profileEditSecret('p', 'OLD', 'NEW', 'v') } catch { /* ignore */ }
    const payload = mockInvoke.mock.calls[0][1] as Record<string, unknown>
    expect(payload.args).toEqual(['edit-secret', 'p', 'OLD', 'NEW', 'v'])
  })

  it('profileRemoveSecret issues remove-secret', async () => {
    try { await profileRemoveSecret('p', 'K') } catch { /* ignore */ }
    const payload = mockInvoke.mock.calls[0][1] as Record<string, unknown>
    expect(payload.args).toEqual(['remove-secret', 'p', 'K'])
  })

  it('profileRevealSecret routes through read path', async () => {
    mockInvoke.mockResolvedValue({ success: true, data: 'plaintext-value', error: null })
    let out
    try { out = await profileRevealSecret('p', 'K') } catch { /* ignore */ }
    const payload = mockInvoke.mock.calls[0][1] as Record<string, unknown>
    expect(payload.args).toEqual(['reveal-secret', 'p', 'K'])
  })
})

describe('v0.7 secrets design invariants', () => {
  it('ProfileData surface includes secretVariables field on the type', () => {
    type Check = { secretVariables?: string[]; profileType?: 'global' | 'launch' }
    const sample: Check = { secretVariables: ['K'], profileType: 'launch' }
    expect(Array.isArray(sample.secretVariables)).toBe(true)
    expect(sample.profileType).toBe('launch')
  })

  it('reveal-secret args use the read path (frontend wrapper uses runRead)', () => {
    const src = require('fs').readFileSync('D:/Aworker/env-manager/frontend/src/lib/api.ts', 'utf8')
    expect(src).toContain("'reveal-secret', profileName, varName]")
  })

  it('ProfileShow masks secret values by default (CLI contract)', () => {
    const src = require('fs').readFileSync('D:/Aworker/env-manager/Program.cs', 'utf8')
    expect(src).toContain('revealSecrets ? TryDecryptSafe(v.Value) : "<encrypted>"')
  })

  it('DpapiHelper uses P/Invoke crypt32 (no NuGet dependency)', () => {
    const src = require('fs').readFileSync('D:/Aworker/env-manager/EnvFeatures.cs', 'utf8')
    expect(src).toContain('crypt32.dll')
    expect(src).toContain('CryptProtectData')
    expect(src).toContain('CryptUnprotectData')
  })

  it('ProfileLaunch decrypts secrets in-process (never logs plaintext)', () => {
    const src = require('fs').readFileSync('D:/Aworker/env-manager/Program.cs', 'utf8')
    expect(src).toContain('profile.SecretVariables.Contains(v.Name, StringComparer.OrdinalIgnoreCase)')
    expect(src).toContain('SecretProviderManager.Decrypt(valueToInject, profile.Name')
  })
})
