import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))

describe('v0.9.20 Tab indicator initial width regression', () => {
  it('initial indicatorStyle is width:0px to prevent first-render oversized bar', () => {
    // Read App.svelte source and verify the initial indicatorStyle value
    // is width:0, not a computed non-zero width. This catches the regression
    // where the indicator bar renders wider than the active tab label text
    // on the very first paint.
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    // The initial value must be width: 0px OR a string that starts with width:0
    const match = appSrc.match(/let\s+indicatorStyle\s*=\s*'([^']*)'/)
    expect(match).toBeTruthy()
    const initialValue = match![1]
    expect(initialValue).toMatch(/^width:\s*0px/)
    expect(initialValue).not.toMatch(/^width:\s*[1-9]/)
  })

  it('indicatorTransition starts as none to avoid animating from width:0', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    const match = appSrc.match(/let\s+indicatorTransition\s*=\s*'([^']*)'/)
    expect(match).toBeTruthy()
    expect(match![1]).toBe('none')
  })
})

describe('v0.9.20 Theme style i18n keys', () => {
  const localeDir = join(__dirname2, 'lib', 'translations')
  const locales = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
  const requiredKeys = [
    'settings.themeStyle',
    'settings.themeStyleSlate',
    'settings.themeStyleZinc',
    'settings.themeStyleNeutral',
  ]

  locales.forEach((loc) => {
    it(`${loc} has all themeStyle i18n keys`, () => {
      const raw = readFileSync(join(localeDir, `${loc}.json`), 'utf8')
      const data = JSON.parse(raw)
      requiredKeys.forEach((key) => {
        const parts = key.split('.')
        let obj: Record<string, unknown> = data
        for (const p of parts) {
          if (!obj || typeof obj !== 'object' || !(p in obj)) {
            throw new Error(`Missing key ${key} in ${loc}.json`)
          }
          obj = obj[p] as Record<string, unknown>
        }
        expect(typeof obj).toBe('string')
        expect((obj as string).length).toBeGreaterThan(0)
      })
    })
  })
})

describe('v0.9.20 handleThemeStyleChange function exists', () => {
  it('App.svelte defines handleThemeStyleChange function', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toContain('function handleThemeStyleChange')
  })

  it('App.svelte binds on:themeStyleChange={handleThemeStyleChange}', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toContain('on:themeStyleChange={handleThemeStyleChange}')
  })

  it('App.svelte reads persisted themeStyle in onMount', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toContain("getSetting('themeStyle')")
  })

  it('SettingsDialog changeThemeStyle dispatches themeStyleChange event', () => {
    const sdSrc = readFileSync(join(__dirname2, 'lib', 'components', 'SettingsDialog.svelte'), 'utf8')
    expect(sdSrc).toContain("dispatch('themeStyleChange'")
  })
})
