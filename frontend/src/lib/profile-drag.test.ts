import { describe, it, expect, beforeEach } from 'vitest'

/**
 * Tests for profile drag-to-reorder logic.
 * The actual drag implementation is pointer-event-based (pointerdown on handle,
 * pointerenter on cards, pointerup to drop).
 * We test the reorder logic that the component uses internally.
 */

describe('Profile drag-to-reorder logic', () => {
  // Simulate the performReorder function
  function performReorder<T>(list: T[], fromIdx: number, toIdx: number): T[] {
    const moved = list[fromIdx]
    const newList = list.filter((_, idx) => idx !== fromIdx)
    newList.splice(toIdx, 0, moved)
    return newList
  }

  // Simulate localStorage order persistence
  const PROFILE_ORDER_KEY = 'envManager_profileOrder'

  function saveProfileOrder(names: string[]): void {
    localStorage.setItem(PROFILE_ORDER_KEY, JSON.stringify(names))
  }

  function loadProfileOrder(): string[] {
    try {
      const raw = localStorage.getItem(PROFILE_ORDER_KEY)
      return raw ? JSON.parse(raw) : []
    } catch {
      return []
    }
  }

  function applyStoredOrder<T extends { name: string }>(
    list: T[],
    order: string[]
  ): T[] {
    if (order.length === 0) return list
    const ordered: T[] = []
    const remaining: T[] = []
    for (const name of order) {
      const found = list.find((p) => p.name === name)
      if (found) ordered.push(found)
    }
    for (const p of list) {
      if (!order.includes(p.name)) remaining.push(p)
    }
    return [...ordered, ...remaining]
  }

  beforeEach(() => {
    localStorage.clear()
  })

  it('moves profile from index 0 to index 2', () => {
    const list = [
      { name: 'A', variables: [] },
      { name: 'B', variables: [] },
      { name: 'C', variables: [] },
      { name: 'D', variables: [] },
    ]
    const result = performReorder(list, 0, 2)
    expect(result.map((p) => p.name)).toEqual(['B', 'C', 'A', 'D'])
  })

  it('moves profile from index 3 to index 0', () => {
    const list = [
      { name: 'A', variables: [] },
      { name: 'B', variables: [] },
      { name: 'C', variables: [] },
      { name: 'D', variables: [] },
    ]
    const result = performReorder(list, 3, 0)
    expect(result.map((p) => p.name)).toEqual(['D', 'A', 'B', 'C'])
  })

  it('no-op when from and to are the same index', () => {
    const list = [
      { name: 'A', variables: [] },
      { name: 'B', variables: [] },
      { name: 'C', variables: [] },
    ]
    const result = performReorder(list, 1, 1)
    expect(result.map((p) => p.name)).toEqual(['A', 'B', 'C'])
  })

  it('persists order to localStorage', () => {
    const names = ['B', 'C', 'A', 'D']
    saveProfileOrder(names)
    const loaded = loadProfileOrder()
    expect(loaded).toEqual(names)
  })

  it('applies stored order to a fresh profile list', () => {
    const order = ['C', 'A', 'B']
    saveProfileOrder(order)

    // Simulate a fresh fetch from CLI (may return in different order)
    const freshList = [
      { name: 'A', variables: [] },
      { name: 'B', variables: [] },
      { name: 'C', variables: [] },
    ]

    const stored = loadProfileOrder()
    const result = applyStoredOrder(freshList, stored)
    expect(result.map((p) => p.name)).toEqual(['C', 'A', 'B'])
  })

  it('handles profiles not in stored order (appends at end)', () => {
    const order = ['A', 'B']
    saveProfileOrder(order)

    const freshList = [
      { name: 'C', variables: [] },
      { name: 'A', variables: [] },
      { name: 'B', variables: [] },
      { name: 'D', variables: [] },
    ]

    const stored = loadProfileOrder()
    const result = applyStoredOrder(freshList, stored)
    expect(result.map((p) => p.name)).toEqual(['A', 'B', 'C', 'D'])
  })

  it('handles empty stored order (returns list as-is)', () => {
    const freshList = [
      { name: 'A', variables: [] },
      { name: 'B', variables: [] },
    ]
    const result = applyStoredOrder(freshList, loadProfileOrder())
    expect(result.map((p) => p.name)).toEqual(['A', 'B'])
  })

  it('reordering works with single profile', () => {
    const list = [{ name: 'A', variables: [] }]
    const result = performReorder(list, 0, 0)
    expect(result.map((p) => p.name)).toEqual(['A'])
  })
})
