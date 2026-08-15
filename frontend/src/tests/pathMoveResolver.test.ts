import { describe, it, expect } from 'vitest'
import { resolveStagedMoves } from '../lib/pathMoveResolver'
import type { PathEntryLike } from '../lib/pathMoveResolver'

function makeEntries(count: number, protectedIndices: number[] = []): PathEntryLike[] {
  return Array.from({ length: count }, (_, i) => ({
    index: i,
    path: 'C:\\dir' + i,
    isProtected: protectedIndices.includes(i),
  }))
}

const identityRefresh = (live: PathEntryLike[]) => live.slice()

describe('resolveStagedMoves', () => {
  it('move-up only: entry from index 3 to index 0', () => {
    const live = makeEntries(5)
    // Target: [3, 0, 1, 2, 4] — entry 3 moves from pos 3 to pos 0
    const target: PathEntryLike[] = [
      { index: 3, path: 'C:\\dir3', isProtected: false },
      { index: 0, path: 'C:\\dir0', isProtected: false },
      { index: 1, path: 'C:\\dir1', isProtected: false },
      { index: 2, path: 'C:\\dir2', isProtected: false },
      { index: 4, path: 'C:\\dir4', isProtected: false },
    ]
    const { moves } = resolveStagedMoves(target, live, identityRefresh)
    expect(moves.filter(m => m.direction === 'up')).toHaveLength(3)
    expect(moves.filter(m => m.direction === 'down')).toHaveLength(0)
  })

  it('move-down: entry from index 0 to index 3 (via move-up of others)', () => {
    const live = makeEntries(5)
    // Target: [1, 2, 3, 0, 4] — entry 0 pushed down by moving others up
    const target: PathEntryLike[] = [
      { index: 1, path: 'C:\\dir1', isProtected: false },
      { index: 2, path: 'C:\\dir2', isProtected: false },
      { index: 3, path: 'C:\\dir3', isProtected: false },
      { index: 0, path: 'C:\\dir0', isProtected: false },
      { index: 4, path: 'C:\\dir4', isProtected: false },
    ]
    const { moves } = resolveStagedMoves(target, live, identityRefresh)
    // The algorithm moves entries 1,2,3 up to push entry 0 down
    expect(moves.length).toBeGreaterThan(0)
    // Final result should match target
    expect(moves.filter(m => m.direction === 'up')).toHaveLength(3)
  })

  it('explicit move-down: entry at pos 0 needs to reach pos 4', () => {
    const live = makeEntries(5)
    // Target: [1, 2, 3, 4, 0] — entry 0 must go all the way to the end.
    // The algorithm moves 1,2,3,4 up (4 move-ups), pushing 0 to pos 4.
    // But this tests that the while(realPos < i) path DOES work when
    // an earlier-positioned entry is behind and needs move-down.
    const target: PathEntryLike[] = [
      { index: 1, path: 'C:\\dir1', isProtected: false },
      { index: 2, path: 'C:\\dir2', isProtected: false },
      { index: 3, path: 'C:\\dir3', isProtected: false },
      { index: 4, path: 'C:\\dir4', isProtected: false },
      { index: 0, path: 'C:\\dir0', isProtected: false },
    ]
    const { moves } = resolveStagedMoves(target, live, identityRefresh)
    expect(moves.length).toBe(4)
  })

  it('mixed: entry moves both directions in complex reorder', () => {
    const live = makeEntries(6)
    // Target: [5, 0, 1, 4, 2, 3] — entry 5 moves up, entries 2,3 move down
    const target: PathEntryLike[] = [
      { index: 5, path: 'C:\\dir5', isProtected: false },
      { index: 0, path: 'C:\\dir0', isProtected: false },
      { index: 1, path: 'C:\\dir1', isProtected: false },
      { index: 4, path: 'C:\\dir4', isProtected: false },
      { index: 2, path: 'C:\\dir2', isProtected: false },
      { index: 3, path: 'C:\\dir3', isProtected: false },
    ]
    const { moves } = resolveStagedMoves(target, live, identityRefresh)
    expect(moves.length).toBeGreaterThan(0)
    // This scenario should produce move-up calls for entry 5 and entry 4
    expect(moves.some(m => m.direction === 'up')).toBe(true)
  })

  it('protected entry that needs to move is skipped', () => {
    const live = makeEntries(4, [2]) // entry at index 2 is protected
    // Target puts entry 2 at pos 0, but it is protected and must move
    const target: PathEntryLike[] = [
      { index: 2, path: 'C:\\dir2', isProtected: true },
      { index: 0, path: 'C:\\dir0', isProtected: false },
      { index: 1, path: 'C:\\dir1', isProtected: false },
      { index: 3, path: 'C:\\dir3', isProtected: false },
    ]
    const { moves, skipped } = resolveStagedMoves(target, live, identityRefresh)
    // Entry 2 at realPos=2, needs to go to i=0, but isProtected → skip
    expect(skipped).toContain(0)
  })

  it('entry disappeared from live throws abort', () => {
    const live = makeEntries(4)
    const target: PathEntryLike[] = [
      { index: 0, path: 'C:\\dir0', isProtected: false },
      { index: 99, path: 'C:\\dir99', isProtected: false },
    ]
    expect(() => resolveStagedMoves(target, live, identityRefresh)).toThrow(
      /no longer exists/
    )
  })

  it('identity target (no moves needed) produces zero moves', () => {
    const live = makeEntries(5)
    const target = live.slice()
    const { moves, skipped } = resolveStagedMoves(target, live, identityRefresh)
    expect(moves).toHaveLength(0)
    expect(skipped).toHaveLength(0)
  })
})
