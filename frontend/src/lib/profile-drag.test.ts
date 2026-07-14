import { describe, it, expect, beforeEach } from 'vitest'
import {
  createDragState,
  beginDrag,
  enterTarget,
  finishDrag,
  cancelDrag,
  loadProfileOrder,
  saveProfileOrder,
  applyStoredOrder,
} from './profileDrag'

const PROFILE_ORDER_KEY = 'envManager_profileOrder'

interface Item {
  name: string
}

function makeList(names: string[]): Item[] {
  return names.map((n) => ({ name: n }))
}

describe('Pointer-event drag state machine', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('moves item from index 0 to index 2', () => {
    const state = createDragState()
    const list = makeList(['A', 'B', 'C', 'D'])

    beginDrag(state, 0, { button: 0 })
    enterTarget(state, 2)
    const result = finishDrag(state, 2, list)

    expect(result.map((p) => p.name)).toEqual(['B', 'C', 'A', 'D'])
  })

  it('moves item from last index to first', () => {
    const state = createDragState()
    const list = makeList(['A', 'B', 'C', 'D'])

    beginDrag(state, 3, { button: 0 })
    enterTarget(state, 0)
    const result = finishDrag(state, 0, list)

    expect(result.map((p) => p.name)).toEqual(['D', 'A', 'B', 'C'])
  })

  it('is a no-op when dragging onto the same index', () => {
    const state = createDragState()
    const list = makeList(['A', 'B', 'C'])

    beginDrag(state, 1, { button: 0 })
    enterTarget(state, 1)
    const result = finishDrag(state, 1, list)

    expect(result.map((p) => p.name)).toEqual(['A', 'B', 'C'])
    // localStorage should NOT have been written
    expect(localStorage.getItem(PROFILE_ORDER_KEY)).toBeNull()
  })

  it('ignores right-click (button !== 0)', () => {
    const state = createDragState()
    const list = makeList(['A', 'B'])

    beginDrag(state, 0, { button: 2 })
    // Even if pointer events fire on target, state should not be dragging
    expect(state.isDragging).toBe(false)
    enterTarget(state, 1)
    const result = finishDrag(state, 1, list)

    expect(result.map((p) => p.name)).toEqual(['A', 'B'])
  })

  it('cancels drag without affecting the list', () => {
    const state = createDragState()
    const list = makeList(['A', 'B', 'C'])

    beginDrag(state, 0, { button: 0 })
    expect(state.isDragging).toBe(true)
    cancelDrag(state)
    expect(state.isDragging).toBe(false)
    expect(state.dragIndex).toBeNull()

    const result = finishDrag(state, 2, list)
    expect(result.map((p) => p.name)).toEqual(['A', 'B', 'C'])
    expect(localStorage.getItem(PROFILE_ORDER_KEY)).toBeNull()
  })

  it('persists order to localStorage after successful drag', () => {
    const state = createDragState()
    const list = makeList(['A', 'B', 'C'])

    beginDrag(state, 0, { button: 0 })
    enterTarget(state, 2)
    finishDrag(state, 2, list)

    const stored = loadProfileOrder()
    expect(stored).toEqual(['B', 'C', 'A'])
  })

  it('handles single item (no reorder possible)', () => {
    const state = createDragState()
    const list = makeList(['Only'])

    beginDrag(state, 0, { button: 0 })
    enterTarget(state, 0)
    const result = finishDrag(state, 0, list)

    expect(result.map((p) => p.name)).toEqual(['Only'])
    expect(localStorage.getItem(PROFILE_ORDER_KEY)).toBeNull()
  })

  it('applyStoredOrder restores custom order from localStorage', () => {
    saveProfileOrder(['C', 'A', 'B'])
    const freshList = makeList(['A', 'B', 'C'])
    const result = applyStoredOrder(freshList)
    expect(result.map((p) => p.name)).toEqual(['C', 'A', 'B'])
  })

  it('applyStoredOrder appends new profiles not in stored order', () => {
    saveProfileOrder(['A', 'B'])
    const freshList = makeList(['C', 'A', 'B', 'D'])
    const result = applyStoredOrder(freshList)
    expect(result.map((p) => p.name)).toEqual(['A', 'B', 'C', 'D'])
  })

  it('applyStoredOrder returns list as-is when no stored order', () => {
    const freshList = makeList(['A', 'B'])
    const result = applyStoredOrder(freshList)
    expect(result.map((p) => p.name)).toEqual(['A', 'B'])
  })
})
