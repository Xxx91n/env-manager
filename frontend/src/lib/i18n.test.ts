import { describe, it, expect } from 'vitest'

// svelte-i18n is mocked globally in tests/setup.ts
import { setupI18n, locales, defaultLanguage } from './i18n'

describe('i18n module', () => {
  it('exports 10 supported locales', () => {
    expect(locales).toHaveLength(10)
    expect(locales).toContain('en')
    expect(locales).toContain('zh')
    expect(locales).toContain('ja')
    expect(locales).toContain('ko')
    expect(locales).toContain('de')
    expect(locales).toContain('fr')
    expect(locales).toContain('es')
    expect(locales).toContain('pt')
    expect(locales).toContain('ru')
    expect(locales).toContain('ar')
  })

  it('default language is en', () => {
    expect(defaultLanguage).toBe('en')
  })

  it('setupI18n returns a 2-letter locale string', () => {
    localStorage.clear()
    const result = setupI18n()
    expect(typeof result).toBe('string')
    expect(result).toHaveLength(2)
  })

  it('setupI18n persists resolved locale to localStorage', () => {
    localStorage.clear()
    setupI18n()
    const stored = localStorage.getItem('locale')
    expect(stored).toBeTruthy()
    expect(stored).toHaveLength(2)
  })
})
