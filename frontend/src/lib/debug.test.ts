/**
 * Tests for the debug/logging system.
 *
 * Verifies:
 * - Debug log entries are added correctly
 * - Log entries are capped at 200 (memory leak prevention)
 * - isWriteInProgress store tracks write operation state
 * - Debug logs capture CLI command names and timing
 */
import { describe, it, expect, beforeEach, vi } from 'vitest'

// Mock invoke so we don't need Tauri runtime
vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(),
}))

import { get } from 'svelte/store'
import { addDebugLog, clearDebugLogs, debugLogs, isWriteInProgress } from './stores'

describe('Debug logging system', () => {
  beforeEach(() => {
    clearDebugLogs()
    isWriteInProgress.set(false)
  })

  it('addDebugLog adds an entry with timestamp', () => {
    addDebugLog({ level: 'info', message: 'Test message', command: 'list' })
    const logs = get(debugLogs)
    expect(logs).toHaveLength(1)
    expect(logs[0].level).toBe('info')
    expect(logs[0].message).toBe('Test message')
    expect(logs[0].command).toBe('list')
    expect(logs[0].timestamp).toBeTruthy()
  })

  it('clearDebugLogs empties the log store', () => {
    addDebugLog({ level: 'info', message: 'Entry 1' })
    addDebugLog({ level: 'warn', message: 'Entry 2' })
    expect(get(debugLogs)).toHaveLength(2)
    clearDebugLogs()
    expect(get(debugLogs)).toHaveLength(0)
  })

  it('isWriteInProgress starts as false', () => {
    expect(get(isWriteInProgress)).toBe(false)
  })

  it('isWriteInProgress can be set to true', () => {
    isWriteInProgress.set(true)
    expect(get(isWriteInProgress)).toBe(true)
  })

  it('isWriteInProgress returns to false after set', () => {
    isWriteInProgress.set(true)
    isWriteInProgress.set(false)
    expect(get(isWriteInProgress)).toBe(false)
  })

  it('addDebugLog accepts all log levels', () => {
    const levels = ['info', 'warn', 'error', 'debug'] as const
    levels.forEach((level) => {
      addDebugLog({ level, message: `Test ${level}` })
    })
    const logs = get(debugLogs)
    expect(logs).toHaveLength(4)
    expect(logs[0].level).toBe('info')
    expect(logs[1].level).toBe('warn')
    expect(logs[2].level).toBe('error')
    expect(logs[3].level).toBe('debug')
  })

  it('debug logs are capped at 200 entries (memory leak prevention)', () => {
    for (let i = 0; i < 250; i++) {
      addDebugLog({ level: 'debug', message: `Entry ${i}` })
    }
    const logs = get(debugLogs)
    expect(logs.length).toBeLessThanOrEqual(200)
    expect(logs.length).toBe(200)
    // Last entry should be the most recent one
    expect(logs[logs.length - 1].message).toBe('Entry 249')
  })
})
