import { describe, it, expect } from 'vitest'
import { parseSecretProviderList } from './secret-provider-list'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

// Ticket 41: the GUI must derive providers + capability flags from CLI output
// only (hard boundary v0.7.3) — these pins hold the parser to that contract.

describe('parseSecretProviderList', () => {
  it('parses capability-tagged lines with availability and flags', () => {
    const raw = [
      'Active provider: dpapi-current-user',
      'Available providers:',
      '  dpapi-current-user (active) [refresh=no certauth=no network=no]',
      '  credential-manager [refresh=no certauth=no network=no]',
      '  powershell-secretmanagement [refresh=no certauth=no network=no]',
      '  vault-kv2 (unavailable) [refresh=yes certauth=yes network=yes]',
      '  sops [refresh=no certauth=no network=no]',
    ].join('\n')
    const parsed = parseSecretProviderList(raw)
    expect(parsed.activeProvider).toBe('dpapi-current-user')
    expect(parsed.providers.map((p) => p.name)).toEqual([
      'dpapi-current-user',
      'credential-manager',
      'powershell-secretmanagement',
      'vault-kv2',
      'sops',
    ])
    const vault = parsed.providers.find((p) => p.name === 'vault-kv2')!
    expect(vault.available).toBe(false)
    expect(vault.refreshCapable).toBe(true)
    expect(vault.certAuthRequired).toBe(true)
    expect(vault.requiresNetwork).toBe(true)
    const dpapi = parsed.providers.find((p) => p.name === 'dpapi-current-user')!
    expect(dpapi.active).toBe(true)
    expect(dpapi.available).toBe(true)
    expect(dpapi.refreshCapable).toBe(false)
  })

  it('parses pre-capability output with all capability flags false', () => {
    const raw = [
      'Active provider: sops',
      'Available providers:',
      '  dpapi-current-user',
      '  sops (active)',
    ].join('\n')
    const parsed = parseSecretProviderList(raw)
    expect(parsed.activeProvider).toBe('sops')
    expect(parsed.providers).toHaveLength(2)
    for (const p of parsed.providers) {
      expect(p.available).toBe(true)
      expect(p.refreshCapable).toBe(false)
      expect(p.certAuthRequired).toBe(false)
      expect(p.requiresNetwork).toBe(false)
    }
    expect(parsed.providers.find((p) => p.name === 'sops')!.active).toBe(true)
  })

  it('ignores noise lines before the provider list header', () => {
    const raw = ['Some other output', '  indented noise', 'Active provider: dpapi-current-user', 'Available providers:', '  dpapi-current-user (active)'].join('\n')
    const parsed = parseSecretProviderList(raw)
    expect(parsed.providers.map((p) => p.name)).toEqual(['dpapi-current-user'])
  })

  it('handles empty and missing sections without throwing', () => {
    expect(parseSecretProviderList('').providers).toEqual([])
    expect(parseSecretProviderList('Active provider: x').activeProvider).toBe('x')
    const noActive = parseSecretProviderList('Available providers:\n  dpapi-current-user')
    expect(noActive.activeProvider).toBeNull()
    expect(noActive.providers).toHaveLength(1)
  })

  it('tolerates CRLF output from the CLI', () => {
    const raw = 'Active provider: sops\r\nAvailable providers:\r\n  sops (active)\r\n'
    const parsed = parseSecretProviderList(raw)
    expect(parsed.providers.map((p) => p.name)).toEqual(['sops'])
    expect(parsed.providers[0].active).toBe(true)
  })
})

describe('i18n provider-name-list gate (ticket 41 AC4)', () => {
  it('no translation string enumerates multiple provider ids', () => {
    const en = JSON.parse(readFileSync(join(process.cwd(), 'src/lib/translations/en.json'), 'utf8')) as Record<string, unknown>
    const ids = ['dpapi-current-user', 'credential-manager', 'powershell-secretmanagement', 'vault-kv2', 'sops', 'azure-keyvault', '1password', 'aws-secretsmanager']
    const offenders: string[] = []
    for (const [key, value] of Object.entries(en)) {
      if (typeof value !== 'string') continue
      const low = value.toLowerCase()
      if (ids.filter((id) => low.includes(id)).length >= 2) offenders.push(key)
    }
    expect(offenders).toEqual([])
  })
})
