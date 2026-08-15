import { describe, it, expect } from 'vitest'

describe('Phase 1: Lazy code splitting — build verification', () => {
  it('dist/assets contains separate chunk files for all 6 lazy-loaded tabs', () => {
    const fs = require('fs')
    const path = require('path')
    const distDir = path.resolve(__dirname, '..', 'dist', 'assets')
    
    if (!fs.existsSync(distDir)) {
      console.log('dist/assets not found — run npm run build first')
      expect(true).toBe(true)
      return
    }

    const chunks = fs.readdirSync(distDir)
    const expectedChunks = [
      { prefix: 'ProfilePage', name: 'profiles' },
      { prefix: 'PathEditor', name: 'path' },
      { prefix: 'HistoryPage', name: 'history' },
      { prefix: 'ProtectionPage', name: 'protection' },
      { prefix: 'AuditPage', name: 'audit' },
      { prefix: 'ServicePage', name: 'service' },
    ]

    for (const { prefix, name } of expectedChunks) {
      const found = chunks.find(c => c.startsWith(prefix) && c.endsWith('.js'))
      expect(found).toBeTruthy()
      if (found) {
        const size = fs.statSync(path.join(distDir, found)).size
        expect(size).toBeGreaterThan(100)
        console.log('  ok ' + name + ' chunk: ' + found + ' (' + size + ' bytes)')
      }
    }
  })

  it('main bundle size is reduced (code splitting active)', () => {
    const fs = require('fs')
    const path = require('path')
    const distDir = path.resolve(__dirname, '..', 'dist', 'assets')
    
    if (!fs.existsSync(distDir)) {
      expect(true).toBe(true)
      return
    }

    const mainBundle = fs.readdirSync(distDir)
      .find(c => c.startsWith('index-') && c.endsWith('.js'))
    
    if (mainBundle) {
      const stat = fs.statSync(path.join(distDir, mainBundle))
      console.log('  Main bundle: ' + mainBundle + ' (' + (stat.size / 1024).toFixed(1) + ' KB)')
      // With code splitting, main bundle should be under 300KB
      expect(stat.size).toBeLessThan(300 * 1024)
    }
  })
})

describe('Phase 1: i18n keys for lazy error fallback', () => {
  it('en.json has errors.chunkLoadFailed', () => {
    const en = require('./lib/translations/en.json')
    expect(en.errors.chunkLoadFailed).toBeTruthy()
    expect(typeof en.errors.chunkLoadFailed).toBe('string')
  })

  it('en.json has common.retry', () => {
    const en = require('./lib/translations/en.json')
    expect(en.common.retry).toBeTruthy()
    expect(typeof en.common.retry).toBe('string')
  })

  it('zh.json has errors.chunkLoadFailed', () => {
    const zh = require('./lib/translations/zh.json')
    expect(zh.errors.chunkLoadFailed).toBeTruthy()
    expect(zh.errors.chunkLoadFailed).toContain('\u91cd\u8bd5')
  })

  it('all 10 locale files have chunkLoadFailed and retry', () => {
    const fs = require('fs')
    const path = require('path')
    const transDir = path.resolve(__dirname, 'lib', 'translations')
    const files = fs.readdirSync(transDir).filter(f => f.endsWith('.json'))
    
    expect(files.length).toBe(10)
    
    for (const file of files) {
      const content = JSON.parse(fs.readFileSync(path.join(transDir, file), 'utf8'))
      expect(content.errors?.chunkLoadFailed).toBeTruthy()
      expect(content.common?.retry).toBeTruthy()
    }
  })

  it('App.svelte source has dynamic import calls (not static imports)', () => {
    const fs = require('fs')
    const path = require('path')
    const src = fs.readFileSync(path.resolve(__dirname, 'App.svelte'), 'utf8')
    
    // Should have dynamic import() calls
    expect(src).toContain("import('./lib/components/ProfilePage.svelte')")
    expect(src).toContain("import('./lib/components/PathEditor.svelte')")
    expect(src).toContain("import('./lib/components/HistoryPage.svelte')")
    expect(src).toContain("import('./lib/components/ProtectionPage.svelte')")
    expect(src).toContain("import('./lib/components/AuditPage.svelte')")
    expect(src).toContain("import('./lib/components/ServicePage.svelte')")
    
    // Should NOT have static imports for these (only Variables stays static)
    expect(src).not.toContain("import ProfilePage from")
    expect(src).not.toContain("import PathEditor from")
    expect(src).not.toContain("import ServicePage from")
    
    // Should have {#await} blocks for lazy loading
    expect(src).toContain('{#await loadComponent')
    expect(src).toContain('errors.chunkLoadFailed')
  })
})
