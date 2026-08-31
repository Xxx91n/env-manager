/**
 * IPC schema contract tests (architecture-recovery issue 08).
 *
 * The authoritative protocol schema lives in the Rust service
 * (service/src/ipc.rs). docs/schemas/ipc-samples.json is a golden file
 * exported from it by the Rust golden test. These tests run every golden
 * response sample through the exact parser the GUI uses
 * (parseServiceResponse) and assert field-level compatibility, so a field
 * rename on the Rust side (or a parser regression here) fails CI instead of
 * corrupting state. See docs/architecture.md "IPC Schema Contract".
 */
import { describe, expect, it } from 'vitest'
import { readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { parseServiceResponse } from './api'

interface SampleFile {
  requests: Array<{ name: string; note?: string; payload: Record<string, unknown> }>
  responses: Array<{ name: string; note?: string; payload: Record<string, unknown> }>
}

function loadSamples(): SampleFile {
  // vitest runs with frontend/ as CWD; golden files live at repo docs/schemas/.
  const repoRoot = resolve(__dirname, '..', '..', '..')
  const raw = readFileSync(resolve(repoRoot, 'docs', 'schemas', 'ipc-samples.json'), 'utf-8')
  return JSON.parse(raw)
}

const samples = loadSamples()

describe('IPC schema contract (golden samples from Rust service)', () => {
  it('golden file exists and contains both request and response sections', () => {
    expect(Array.isArray(samples.requests)).toBe(true)
    expect(Array.isArray(samples.responses)).toBe(true)
    expect(samples.requests.length).toBeGreaterThan(0)
    expect(samples.responses.length).toBeGreaterThan(0)
  })

  it.each(samples.responses)('$name parses through parseServiceResponse', (sample) => {
    const raw = JSON.stringify(sample.payload)
    const parsed = parseServiceResponse(raw)
    const payload = sample.payload as { ok?: boolean; data?: unknown; message?: string }
    expect(parsed.ok).toBe(payload.ok === true)
    if (payload.ok === true) {
      expect(parsed.data).toEqual(payload.data)
    } else {
      expect(parsed.message).toBe(payload.message)
    }
  })

  it('ok_status sample exposes the documented data fields (running/mountFile/mountPath)', () => {
    const sample = samples.responses.find((s) => s.name === 'ok_status')
    expect(sample).toBeDefined()
    const parsed = parseServiceResponse(JSON.stringify(sample!.payload))
    expect(parsed.ok).toBe(true)
    expect(parsed.data).toHaveProperty('running', true)
    expect(parsed.data).toHaveProperty('mountFile')
    expect(parsed.data).toHaveProperty('mountPath')
  })

  it('degraded gateway envelope (state field) keeps message visible to the GUI', () => {
    const sample = samples.responses.find((s) => s.name === 'cli_degraded_not_running')
    expect(sample).toBeDefined()
    const parsed = parseServiceResponse(JSON.stringify(sample!.payload))
    expect(parsed.ok).toBe(false)
    expect(parsed.message).toContain('not_running')
  })

  it('ok_ping sample carries pong=true', () => {
    const sample = samples.responses.find((s) => s.name === 'ok_ping')
    expect(sample).toBeDefined()
    const parsed = parseServiceResponse(JSON.stringify(sample!.payload))
    expect(parsed.ok).toBe(true)
    expect(parsed.data).toHaveProperty('pong', true)
    expect(parsed.data).toHaveProperty('uptime_seconds')
  })

  it('error responses never expose a data payload the GUI could misread as success', () => {
    for (const sample of samples.responses) {
      const parsed = parseServiceResponse(JSON.stringify(sample.payload))
      if (!parsed.ok) {
        expect(parsed.data ?? null).toBeNull()
      }
    }
  })
})
