import { describe, it, expect } from 'vitest'
import { parseSecretProviderList } from './secret-provider-list'
import { readFileSync } from 'node:fs'
import { join } from 'node:path'

// Pins the CLI-output seam: the parser derives providers and availability from
// CLI text only and carries no provider id list. These cases cover name,
// active, and available; the capability block stays unparsed.

describe('parseSecretProviderList', () => {
  it('tolerates the capability block while parsing availability', () => {
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
    expect(vault.reason).toBeNull()
    const dpapi = parsed.providers.find((p) => p.name === 'dpapi-current-user')!
    expect(dpapi.active).toBe(true)
    expect(dpapi.available).toBe(true)
  })

  it('carries the reason code when a probe failed', () => {
    // Issue #277: a probe failure names its cause, a gate failure does not. The code
    // is a fixed vocabulary, so the GUI never has to translate CLI prose.
    const raw = [
      'Active provider: dpapi-current-user',
      'Available providers:',
      '  dpapi-current-user (active) [refresh=no certauth=no network=no]',
      '  vault-kv2 (unavailable: permission) [refresh=yes certauth=yes network=yes]',
      '  azure-keyvault (unavailable: auth) [refresh=yes certauth=yes network=yes]',
      '  1password (unavailable: timeout) [refresh=yes certauth=no network=yes]',
      '  aws-secretsmanager (unavailable: network) [refresh=yes certauth=no network=yes]',
      '  aws-roles-anywhere (unavailable: unknown) [refresh=yes certauth=yes network=yes]',
    ].join('\n')
    const parsed = parseSecretProviderList(raw)
    const expected: Array<[string, string]> = [
      ['vault-kv2', 'permission'],
      ['azure-keyvault', 'auth'],
      ['1password', 'timeout'],
      ['aws-secretsmanager', 'network'],
      ['aws-roles-anywhere', 'unknown'],
    ]
    for (const [name, reason] of expected) {
      const entry = parsed.providers.find((p) => p.name === name)!
      expect(entry.available).toBe(false)
      expect(entry.reason).toBe(reason)
    }
  })

  it('ignores a reason code outside the known vocabulary', () => {
    // A future CLI could emit something this build does not know. Showing raw text
    // would put untranslated words in the dropdown, so the code is dropped instead
    // and the provider falls back to the plain "unavailable" label.
    const raw = ['Active provider: sops', 'Available providers:', '  vault-kv2 (unavailable: kms-key-revoked)'].join('\n')
    const parsed = parseSecretProviderList(raw)
    expect(parsed.providers[0].available).toBe(false)
    expect(parsed.providers[0].reason).toBeNull()
  })

  it('parses output without the capability block', () => {
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

describe('i18n provider-name-list gate', () => {
  it('no translation string enumerates multiple provider ids', () => {
    const en = JSON.parse(readFileSync(join(process.cwd(), 'src/lib/translations/en.json'), 'utf8')) as Record<string, unknown>
    const ids = ['dpapi-current-user', 'credential-manager', 'powershell-secretmanagement', 'vault-kv2', 'sops', 'azure-keyvault', '1password', 'aws-secretsmanager', 'aws-roles-anywhere']
    const offenders: string[] = []
    for (const [key, value] of Object.entries(en)) {
      if (typeof value !== 'string') continue
      const low = value.toLowerCase()
      if (ids.filter((id) => low.includes(id)).length >= 2) offenders.push(key)
    }
    expect(offenders).toEqual([])
  })
})
