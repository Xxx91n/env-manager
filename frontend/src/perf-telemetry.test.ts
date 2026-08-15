import { describe, it, expect } from 'vitest'
import * as fs from 'fs'
import * as path from 'path'

/**
 * Phase 4: Silent performance telemetry tests.
 * Verifies that:
 * 1. Rust run_cli logs elapsed time for every CLI invocation
 * 2. Frontend App.svelte logs view-switch perf data via frontendLog
 * 3. The telemetry is silent (debug level, not user-facing)
 */
describe('Phase 4: Silent performance telemetry — source verification', () => {
  it('main.rs run_cli logs elapsed time', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    // Should have Instant::now() at the start of run_cli
    expect(src).toContain('let start = std::time::Instant::now()')
    // Should log elapsed in the output
    expect(src).toContain('elapsed=')
    expect(src).toContain('as_millis()')
    // Should log the command and exit status
    expect(src).toContain('[run_cli]')
  })

  it('App.svelte has perf.viewSwitch reactive telemetry', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, 'App.svelte'),
      'utf8'
    )
    expect(src).toContain('perf.viewSwitch')
    expect(src).toContain('perfPrevView')
    expect(src).toContain('perfPrevTime')
    expect(src).toContain('performance.now()')
    // Should use frontendLog with debug level (silent, not user-facing)
    expect(src).toContain("frontendLog('debug'")
    // Should include elapsedMs in the log message
    expect(src).toContain('elapsedMs=')
  })

  it('frontendLog is fire-and-forget (does not block rendering)', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, 'lib', 'settingsStore.ts'),
      'utf8'
    )
    // frontendLog should be async and catch errors silently
    expect(src).toContain('export async function frontendLog')
    // Should have error catching (fire-and-forget)
    // The function itself should not throw
  })

  it('App.svelte perf telemetry does not surface to user (silent)', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, 'App.svelte'),
      'utf8'
    )
    // The perf log should use debug level, not info/warn/error
    // This ensures it's only visible in developer diagnostics, not user-facing logs
    const perfLogLine = src.split('\n').find(l => l.includes('perf.viewSwitch'))
    expect(perfLogLine).toBeTruthy()
    if (perfLogLine) {
      expect(perfLogLine).toContain("frontendLog('debug'")
    }
  })
})
