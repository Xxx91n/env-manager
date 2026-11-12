// Parser for `profile secret-provider list` output.
// The GUI derives providers and their availability from CLI output only —
// no hardcoded provider id list lives here.
// Line contract: "  <name> [(active)] [(unavailable) | (unavailable: <reason>)]
// [refresh=yes|no certauth=yes|no network=yes|no]"; the trailing capability
// block is tolerated, not parsed — the GUI has no consumer for it.
// Output without the bracket block parses the same way.
//
// The reason code is a fixed vocabulary the CLI emits (`auth`, `permission`,
// `network`, `timeout`, `unknown`) — never a sentence. The GUI maps the code to a
// translated message so the CLI carries no localizable prose, and an unrecognized
// code degrades to the plain unavailable label instead of leaking raw text.

export const PROVIDER_REASON_CODES = ['auth', 'permission', 'network', 'timeout', 'unknown'] as const

export type ProviderReasonCode = (typeof PROVIDER_REASON_CODES)[number]

export interface SecretProviderListEntry {
  name: string
  active: boolean
  available: boolean
  /** Why the probe failed; null when the provider is available or the gate alone decided. */
  reason: ProviderReasonCode | null
}

export interface SecretProviderList {
  activeProvider: string | null
  providers: SecretProviderListEntry[]
}

// Matches the marker with any reason token, not just the known vocabulary: an
// unrecognized code must still mark the provider unavailable. Narrowing the pattern
// to the known codes would let an unknown code read as "available", which is worse
// than falling back to the plain label.
const REASON_PATTERN = /\(unavailable(?::\s*([^)\s]+))?\)/

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
    const unavailable = rest.match(REASON_PATTERN)
    const rawReason = unavailable?.[1] ?? null
    providers.push({
      name,
      active: rest.includes('(active)'),
      available: !unavailable,
      reason: rawReason !== null && (PROVIDER_REASON_CODES as readonly string[]).includes(rawReason)
        ? (rawReason as ProviderReasonCode)
        : null,
    })
  }
  return { activeProvider, providers }
}
