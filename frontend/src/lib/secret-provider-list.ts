// Ticket 41: capability-aware parser for `profile secret-provider list` output.
// The GUI derives providers and their capability descriptors from CLI output
// only (hard boundary v0.7.3) — no hardcoded provider id list lives here.
// Line contract (v0.12+): "  <name> [(active)] [(unavailable)]
// [refresh=yes|no certauth=yes|no network=yes|no]"; pre-capability output
// (no bracket block) parses identically with all capability flags false.

export interface SecretProviderListEntry {
  name: string
  active: boolean
  available: boolean
  refreshCapable: boolean
  certAuthRequired: boolean
  requiresNetwork: boolean
}

export interface SecretProviderList {
  activeProvider: string | null
  providers: SecretProviderListEntry[]
}

export function parseSecretProviderList(raw: string): SecretProviderList {
  const lines = raw.split(/\r?\n/)
  let activeProvider: string | null = null
  let inList = false
  const providers: SecretProviderListEntry[] = []
  for (const line of lines) {
    if (line.startsWith('Active provider:')) {
      activeProvider = line.slice('Active provider:'.length).trim()
      continue
    }
    if (line.startsWith('Available providers:')) {
      inList = true
      continue
    }
    if (!inList) continue
    const m = line.match(/^\s+(\S+)\s*(.*)$/)
    if (!m) continue
    const name = m[1]
    const rest = m[2]
    providers.push({
      name,
      active: rest.includes('(active)'),
      available: !rest.includes('(unavailable)'),
      refreshCapable: /refresh=yes/.test(rest),
      certAuthRequired: /certauth=yes/.test(rest),
      requiresNetwork: /network=yes/.test(rest),
    })
  }
  return { activeProvider, providers }
}
