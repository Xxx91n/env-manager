import { describe, it, expect } from 'vitest'
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

const locales: Record<string, Record<string, string>> = {
  zh,
  ja,
  ko,
  de,
  fr,
  es,
  pt,
  ru,
  ar,
}

const enKeys = Object.keys(en).sort()

describe('translation completeness', () => {
  it('English has at least 50 keys', () => {
    expect(enKeys.length).toBeGreaterThanOrEqual(50)
  })

  for (const [localeCode, messages] of Object.entries(locales)) {
    it(`${localeCode} has all keys present in English`, () => {
      const localeKeys = Object.keys(messages).sort()
      const missing = enKeys.filter((k) => !localeKeys.includes(k))
      const extra = localeKeys.filter((k) => !enKeys.includes(k))

      expect(missing, `${localeCode} missing keys: ${missing.join(', ')}`).toEqual([])
      // Extra keys are a warning, not a failure, but we log them
      if (extra.length > 0) {
        console.warn(`${localeCode} has extra keys not in en: ${extra.join(', ')}`)
      }
    })

    it(`${localeCode} has no empty string values`, () => {
      for (const [key, value] of Object.entries(messages)) {
        if (typeof value === 'object' && value !== null) continue
        expect(
          value.length > 0,
          `${localeCode}.${key} has an empty value`
        ).toBe(true)
      }
    })
  }
})
