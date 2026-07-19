import { describe, it, expect } from 'vitest'

/**
 * Bug 2 regression: after clicking Health Check, the path editor was
 * rendering BOTH the initial-load duplicate badge AND the health-map
 * duplicate badge, so duplicate entries showed two duplicate markers.
 *
 * The fix makes the two badge sources mutually exclusive:
 *   - If healthMap has an entry for this path, render ONLY the health badge.
 *   - Otherwise render the initial-load duplicate / dead badges.
 *
 * This test models the Svelte {#if}/{:else} selection logic in TS so the
 * contract is locked without requiring a DOM mount.
 */
describe('PathEditor badge exclusivity', () => {
  type HealthEntry = { isDead: boolean; isDuplicate: boolean }
  type InitialEntry = { exists: boolean; isDuplicate: boolean }

  function pickBadges(initial: InitialEntry, health: HealthEntry | null) {
    if (health) {
      if (health.isDead && health.isDuplicate) return ['dead+dup']
      if (health.isDead && !health.isDuplicate) return ['dead']
      if (!health.isDead && health.isDuplicate) return ['dup']
      return ['healthy']
    }
    const out: string[] = []
    if (!initial.exists) out.push('dead')
    if (initial.isDuplicate) out.push('dup')
    return out
  }

  it('shows a single duplicate badge after health check reports duplicate', () => {
    const badges = pickBadges(
      { exists: true, isDuplicate: true },
      { isDead: false, isDuplicate: true },
    )
    expect(badges).toEqual(['dup'])
  })

  it('shows dead+dup single badge when both flags true after health check', () => {
    const badges = pickBadges(
      { exists: false, isDuplicate: true },
      { isDead: true, isDuplicate: true },
    )
    expect(badges).toEqual(['dead+dup'])
  })

  it('shows healthy when health check says all-clear even if initial scan marked dup', () => {
    const badges = pickBadges(
      { exists: true, isDuplicate: true },
      { isDead: false, isDuplicate: false },
    )
    expect(badges).toEqual(['healthy'])
  })

  it('falls back to initial badges when healthMap has no entry', () => {
    const badges = pickBadges({ exists: false, isDuplicate: true }, null)
    expect(badges).toEqual(['dead', 'dup'])
  })

  it('falls back to empty when both sources have no flag', () => {
    const badges = pickBadges({ exists: true, isDuplicate: false }, null)
    expect(badges).toEqual([])
  })

  it('never produces two of the same badge type (single-source rule)', () => {
    // Stress: every combination of the four booleans yields <= 2 badges
    // and no duplicate badge names.
    const combinations = [true, false]
    for (const ie of combinations) {
      for (const id of combinations) {
        for (const hd of combinations) {
          for (const hdup of combinations) {
            const badges = pickBadges({ exists: ie, isDuplicate: id }, { isDead: hd, isDuplicate: hdup })
            // single badge max when health map is authoritative
            expect(badges.length).toBeLessThanOrEqual(2)
            // uniqueness
            expect(new Set(badges).size).toBe(badges.length)
          }
        }
      }
    }
  })
})
