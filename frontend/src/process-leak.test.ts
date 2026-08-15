import { describe, it, expect } from 'vitest'
import { execSync } from 'child_process'
import * as fs from 'fs'
import * as path from 'path'

/**
 * Phase 2: Process leak prevention tests.
 * These tests verify that the process lifecycle is properly managed:
 * 1. Job Object is initialized on startup (KILL_ON_JOB_CLOSE for WebView2 children)
 * 2. Stale PID cleanup works
 * 3. shutdown_background_service cleans up properly
 *
 * Note: These are static/source-level tests. Full E2E process leak tests
 * require a running build and are executed separately via Playwright.
 */
describe('Phase 2: Process leak prevention — source verification', () => {
  it('job_object.rs has KILL_ON_JOB_CLOSE flag', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'job_object.rs'),
      'utf8'
    )
    expect(src).toContain('JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE')
    expect(src).toContain('AssignProcessToJobObject')
    expect(src).toContain('init_job_object')
  })

  it('main.rs calls init_job_object on startup', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    expect(src).toContain('job_object::init_job_object()')
  })

  it('main.rs has shutdown_background_service with PID cleanup', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    expect(src).toContain('shutdown_background_service()')
    expect(src).toContain('[shutdown_service]')
    expect(src).toContain('SERVICE_PID')
    // Should clear PID after shutdown
    expect(src).toContain('*guard = None')
  })

  it('main.rs has stale PID cleanup on startup', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    expect(src).toContain('stale service pid')
    expect(src).toContain('taskkill')
    expect(src).toContain('/T')
    expect(src).toContain('/F')
  })

  it('service is detached (std::mem::forget or detach pattern)', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    // Service should be detached from GUI process so GUI exit doesn't kill it
    // (unless GUI sends explicit shutdown)
    expect(src).toContain('detaching')
    expect(src).toContain('CREATE_NO_WINDOW')
  })

  it('ipc.rs has connection counter for leak diagnostics', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', '..', 'service', 'src', 'ipc.rs'),
      'utf8'
    )
    expect(src).toContain('CONNECTION_COUNT')
    expect(src).toContain('fetch_add')
    expect(src).toContain('connection #')
  })

  it('ipc.rs handle_connection uses RAII (no explicit resource leak)', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', '..', 'service', 'src', 'ipc.rs'),
      'utf8'
    )
    // handle_connection should return Ok(()) or Err, letting the task drop
    expect(src).toContain('async fn handle_connection')
    expect(src).toContain('Ok(0) => break')
    // On read error, should return Err (not silently continue)
    expect(src).toContain('Err(e) => return Err(e)')
  })
})
