/**
 * Path staged-move resolution logic — extracted for testability.
 *
 * Given a target ordered list (with original .index preserved) and a live
 * registry list, compute the minimal sequence of move-up/move-down calls
 * needed to bring the live registry into the target order.
 *
 * Each move-up(i) swaps entries[i] and entries[i-1].
 * Each move-down(i) swaps entries[i] and entries[i+1].
 *
 * The algorithm processes entries left to right. For each target position i,
 * it finds the entry whose original .index matches target[i].index in the
 * live list, then moves it left (move-up) or right (move-down) until it sits
 * at position i. After each entry is positioned, the live list is refreshed
 * (the caller provides a refreshFn).
 */

export interface PathEntryLike {
  index: number
  path: string
  isProtected: boolean
  [key: string]: unknown
}

export interface MoveCall {
  direction: 'up' | 'down'
  index: number
}

/**
 * Compute the sequence of move calls to bring `live` into `target` order.
 * Returns the move calls + a flag per entry indicating if it was skipped
 * (protected entry that needed to move).
 */
export function resolveStagedMoves(
  target: PathEntryLike[],
  live: PathEntryLike[],
  refreshFn: (currentLive: PathEntryLike[]) => PathEntryLike[]
): { moves: MoveCall[]; skipped: number[] } {
  const moves: MoveCall[] = []
  const skipped: number[] = []
  let currentLive = live.slice()

  for (let i = 0; i < target.length; i++) {
    const wantOrigIdx = target[i].index
    let realPos = currentLive.findIndex(e => e.index === wantOrigIdx)

    if (realPos < 0) {
      throw new Error('PATH entry at original index ' + wantOrigIdx + ' no longer exists; staged move aborted')
    }

    // Protected entries: skip if they need to move
    if (target[i].isProtected && realPos !== i) {
      skipped.push(i)
      currentLive = refreshFn(currentLive)
      continue
    }

    while (realPos > i) {
      moves.push({ direction: 'up', index: realPos })
      // Simulate: swap [realPos] and [realPos-1]
      const tmp = currentLive[realPos]
      currentLive[realPos] = currentLive[realPos - 1]
      currentLive[realPos - 1] = tmp
      realPos--
    }
    while (realPos < i) {
      moves.push({ direction: 'down', index: realPos })
      // Simulate: swap [realPos] and [realPos+1]
      const tmp = currentLive[realPos]
      currentLive[realPos] = currentLive[realPos + 1]
      currentLive[realPos + 1] = tmp
      realPos++
    }

    // Refresh live after each entry is positioned.
    // In production this re-reads the registry (which reflects prior swaps).
    // In tests, identityRefresh returns currentLive as-is (swaps already applied).
    currentLive = refreshFn(currentLive)
  }

  return { moves, skipped }
}
