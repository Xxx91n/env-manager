import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))

describe('v0.9.20 Tray i18n + lightweight mode regression', () => {
  // Tray translations are inlined in App.svelte as a hardcoded map, not in
  // locale JSON files. This test verifies the inline map covers all 10 locales.
  const locales = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
  const trayFields = ['show', 'lightweight', 'quit', 'tooltip']

  it('App.svelte trayI18n map covers all 10 locales', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    locales.forEach((loc) => {
      // Each locale must have a key in the trayI18n object literal
      expect(appSrc).toContain(`${loc}:`)
    })
  })

  it('App.svelte trayI18n has all 4 fields per locale', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    trayFields.forEach((field) => {
      expect(appSrc).toContain(`${field}:`)
    })
  })

  it('App.svelte has on:contextmenu preventDefault for WebView2 right-click suppression', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toMatch(/on:contextmenu.*preventDefault/)
  })

  it('App.svelte has updateTrayLocale function called from syncTrayLocale', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toContain('function syncTrayLocale')
    expect(appSrc).toContain('updateTrayLocale')
    expect(appSrc).toContain('syncTrayLocale($locale)')
  })
})
