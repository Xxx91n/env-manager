import { describe, it, expect } from 'vitest'
// The single-file ESM bundle avoids intl-messageformat's extensionless lib/ imports,
// which Vite's SSR resolver rejects in vitest.
import IntlMessageFormat from 'intl-messageformat/intl-messageformat.esm.js'
import en from './translations/en.json'
import zh from './translations/zh.json'
import ja from './translations/ja.json'
import ko from './translations/ko.json'
import de from './translations/de.json'
import fr from './translations/fr.json'
import es from './translations/es.json'
import pt from './translations/pt.json'
import ru from './translations/ru.json'
import ar from './translations/ar.json'

// Architecture-recovery issue 14: i18n "human-readable contract" layer.
// - Structural integrity (hard assertions): every locale carries the full recursive leaf
//   key set of en, no type conflicts, no empty values, and identical ICU placeholder sets.
// - Per-locale full-key rendered snapshots: every leaf of every locale, deterministically
//   ordered, rendered through intl-messageformat (the same ICU engine svelte-i18n uses)
//   with fixed interpolation parameters. Any wording change in any language now surfaces
//   as an explicit diff in review.
// ICU note: single quotes are escape characters in ICU MessageFormat. The renderer below
// never wraps a {placeholder} in single quotes; fixtures pass values, not quoted templates.

const messages: Record<string, Record<string, unknown>> = {
  en, zh, ja, ko, de, fr, es, pt, ru, ar,
}

const localeCodes = Object.keys(messages)

function flatten(obj: Record<string, unknown>, prefix = '', out: Record<string, string> = {}): Record<string, string> {
  for (const [k, v] of Object.entries(obj)) {
    const key = prefix ? `${prefix}.${k}` : k
    if (v && typeof v === 'object') {
      flatten(v as Record<string, unknown>, key, out)
    } else {
      out[key] = String(v)
    }
  }
  return out
}

function placeholderNames(s: string): string[] {
  return (s.match(/\{[^{}]+\}/g) ?? []).map((p) => p.slice(1, -1).trim()).sort()
}

const enFlat = flatten(en as Record<string, unknown>)
const enKeys = Object.keys(enFlat).sort()

// Fixed interpolation fixtures: one deterministic value per placeholder name found in en.
// The full placeholder inventory across all 10 locales is exactly:
// conflicts, count, dead, dup, ext, failed, name, path, reason, rotated, total, upstream, version.
const fixtureValues: Record<string, string> = {
  count: '3',
  conflicts: '1',
  name: 'Example',
  reason: 'test reason',
  upstream: 'upstream detail',
  ext: '.txt',
  version: '1.2.3',
  dead: '2',
  dup: '4',
  failed: '1',
  path: 'C:\\example\\path',
  total: '10',
  rotated: '7',
}

function renderFixture(placeholder: string): string {
  const known = fixtureValues[placeholder]
  if (known !== undefined) return known
  // Unknown placeholder names must fail loudly, not silently render as "".
  throw new Error(`no interpolation fixture for placeholder {${placeholder}} - add it to fixtureValues`)
}

function renderMessage(text: string, locale: string): string {
  if (!placeholderNames(text).length) return text
  const params: Record<string, string> = {}
  for (const p of placeholderNames(text)) params[p] = renderFixture(p)
  const formatted = new IntlMessageFormat(text, locale).format(params)
  return String(formatted)
}

describe('translation completeness (recursive)', () => {
  it('English has at least 400 leaf keys', () => {
    expect(enKeys.length).toBeGreaterThanOrEqual(400)
  })

  for (const [localeCode, msgs] of Object.entries(messages)) {
    const flat = flatten(msgs as Record<string, unknown>)

    it(`${localeCode} has every en leaf key (recursive)`, () => {
      const localeKeys = Object.keys(flat).sort()
      const missing = enKeys.filter((k) => !localeKeys.includes(k))
      const extra = localeKeys.filter((k) => !enKeys.includes(k))
      expect(missing, `${localeCode} missing leaf keys: ${missing.join(', ')}`).toEqual([])
      expect(extra, `${localeCode} extra leaf keys not in en: ${extra.join(', ')}`).toEqual([])
    })

    it(`${localeCode} has no empty or non-string leaf values`, () => {
      for (const [key, value] of Object.entries(flat)) {
        expect(typeof value === 'string' && value.length > 0, `${localeCode}.${key} is empty or non-string`).toBe(true)
      }
    })

    it(`${localeCode} keeps ICU placeholder sets identical to en`, () => {
      const diffs: string[] = []
      for (const key of enKeys) {
        const enSet = placeholderNames(enFlat[key]).join('|')
        const locSet = placeholderNames(flat[key] ?? '').join('|')
        if (enSet !== locSet) diffs.push(`${key}: en=[${enSet}] ${localeCode}=[${locSet}]`)
      }
      expect(diffs, `${localeCode} ICU placeholder drift:\n${diffs.join('\n')}`).toEqual([])
    })

    it(`${localeCode} renders every leaf key through the ICU engine`, () => {
      // Renders ALL keys once (no snapshot) purely to prove the engine parses every
      // message of every locale - including the 25 ICU-placeholder keys. Rendering
      // failures (malformed ICU, single-quote escapes wrapping placeholders) fail here.
      for (const key of enKeys) {
        expect(() => renderMessage(flat[key] ?? '', localeCode), `${localeCode}.${key} failed to render`).not.toThrow()
      }
    })
  }
})

describe('per-locale full-key rendered snapshots', () => {
  for (const [localeCode, msgs] of Object.entries(messages)) {
    it(`${localeCode} full-key rendered snapshot`, () => {
      const flat = flatten(msgs as Record<string, unknown>)
      const rendered: Record<string, string> = {}
      for (const key of enKeys) {
        rendered[key] = renderMessage(flat[key] ?? '', localeCode)
      }
      // Vitest serializes the object with sorted, stable key order; a wording change in
      // any locale now appears as an explicit reviewable diff.
      expect(rendered).toMatchSnapshot()
    })
  }
})
