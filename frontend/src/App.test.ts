import { describe, it, expect, beforeEach, vi } from 'vitest'
import { invoke } from '@tauri-apps/api/core'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

// Phase 1 note: App.svelte now uses dynamic import() for 6 tabs, which
// can hang jsdom because Vite SFC transform doesn't resolve individual
// component files. These tests are limited to source-level checks.
// Full component rendering tests require Playwright E2E.

describe('App.svelte', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
  })

  it('source has static Variables import (always loaded on first paint)', () => {
    const src = require('fs').readFileSync(
      require('path').resolve(__dirname, 'App.svelte'),
      'utf8'
    )
    expect(src).toContain("import Variables from './lib/components/Variables.svelte'")
    // Variables is the default tab — must remain static
  })

  it('source has dynamic import map for 6 lazy tabs', () => {
    const src = require('fs').readFileSync(
      require('path').resolve(__dirname, 'App.svelte'),
      'utf8'
    )
    expect(src).toContain("lazyImporters")
    expect(src).toContain("import('./lib/components/ProfilePage.svelte')")
    expect(src).toContain("import('./lib/components/PathEditor.svelte')")
    expect(src).toContain("import('./lib/components/HistoryPage.svelte')")
    expect(src).toContain("import('./lib/components/ProtectionPage.svelte')")
    expect(src).toContain("import('./lib/components/AuditPage.svelte')")
    expect(src).toContain("import('./lib/components/ServicePage.svelte')")
  })

  it('source has {#await loadComponent} blocks with chunk error fallback', () => {
    const src = require('fs').readFileSync(
      require('path').resolve(__dirname, 'App.svelte'),
      'utf8'
    )
    expect(src).toContain('{#await loadComponent')
    expect(src).toContain('errors.chunkLoadFailed')
    expect(src).toContain('common.retry')
  })
})
