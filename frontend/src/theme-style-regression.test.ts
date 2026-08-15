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
    'settings.themeStyleBlue',
    'settings.themeStyleViolet',
    'settings.themeStyleRose',
    'settings.themeStyleCyan',
    'settings.themeStyleAmber',
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

describe('v0.9.21 Custom titlebar configuration', () => {
  it('tauri.conf.json has decorations:false and label:main', () => {
    const tauriConfig = JSON.parse(readFileSync(join(__dirname2, '..', 'src-tauri', 'tauri.conf.json'), 'utf8'))
    expect(tauriConfig.app.windows[0].decorations).toBe(false)
    expect(tauriConfig.app.windows[0].label).toBe('main')
  })

  it('capabilities/default.json has window control permissions', () => {
    const caps = JSON.parse(readFileSync(join(__dirname2, '..', 'src-tauri', 'capabilities', 'default.json'), 'utf8'))
    expect(caps.permissions).toContain('core:window:allow-close')
    expect(caps.permissions).toContain('core:window:allow-minimize')
    expect(caps.permissions).toContain('core:window:allow-toggle-maximize')
    expect(caps.permissions).toContain('core:window:allow-start-dragging')
  })

  it('App.svelte imports getCurrentWindow from tauri-apps/api/window', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toContain("import { getCurrentWindow } from '@tauri-apps/api/window'")
  })

  it('App.svelte has titlebar with data-tauri-drag-region', () => {
    const appSrc = readFileSync(join(__dirname2, 'App.svelte'), 'utf8')
    expect(appSrc).toContain('class="titlebar"')
    expect(appSrc).toContain('data-tauri-drag-region')
    expect(appSrc).toContain('getCurrentWindow().minimize()')
    expect(appSrc).toContain('getCurrentWindow().toggleMaximize()')
    expect(appSrc).toContain('getCurrentWindow().close()')
  })

  it('app.css has titlebar styles', () => {
    const css = readFileSync(join(__dirname2, 'app.css'), 'utf8')
    expect(css).toContain('.titlebar')
    expect(css).toContain('.titlebar-btn')
    expect(css).toContain('.titlebar-btn.close')
  })
})

describe('v0.9.21 CSS containment for performance', () => {
  it('app.css has containment rules', () => {
    const css = readFileSync(join(__dirname2, 'app.css'), 'utf8')
    expect(css).toContain('.table-container')
    expect(css).toContain('.list-container')
    expect(css).toContain('content-visibility')
  })

  it('HistoryPage has table-container class', () => {
    const src = readFileSync(join(__dirname2, 'lib', 'components', 'HistoryPage.svelte'), 'utf8')
    expect(src).toContain('table-container')
  })

  it('Variables has table-container class', () => {
    const src = readFileSync(join(__dirname2, 'lib', 'components', 'Variables.svelte'), 'utf8')
    expect(src).toContain('table-container')
  })

  it('ProtectionPage has list-container class', () => {
    const src = readFileSync(join(__dirname2, 'lib', 'components', 'ProtectionPage.svelte'), 'utf8')
    expect(src).toContain('list-container')
  })

  it('AuditPage has list-container class', () => {
    const src = readFileSync(join(__dirname2, 'lib', 'components', 'AuditPage.svelte'), 'utf8')
    expect(src).toContain('list-container')
  })
})
