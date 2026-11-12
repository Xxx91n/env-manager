// Parser for `profile secret-provider list` output.
// The GUI derives providers and their availability from CLI output only —
// no hardcoded provider id list lives here.
// Line contract: "  <name> [(active)] [(unavailable)]
// [refresh=yes|no certauth=yes|no network=yes|no]"; the trailing capability
// block is tolerated, not parsed — the GUI has no consumer for it.
// Output without the bracket block parses the same way.

export interface SecretProviderListEntry {
  name: string
  active: boolean
  available: boolean
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
    })
  }
  return { activeProvider, providers }
}
