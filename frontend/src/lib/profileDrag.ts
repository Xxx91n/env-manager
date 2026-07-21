import { moveItem } from './features'

const PROFILE_ORDER_KEY = 'envManager_profileOrder'

export function loadProfileOrder(): string[] {
  try {
    const raw = localStorage.getItem(PROFILE_ORDER_KEY)
    return raw ? (JSON.parse(raw) as string[]) : []
  } catch {
    return []
  }
}

export function saveProfileOrder(names: string[]): void {
  try {
    localStorage.setItem(PROFILE_ORDER_KEY, JSON.stringify(names))
  } catch {
    // ignore quota errors
  }
}

export function applyStoredOrder<T extends { name: string }>(list: T[]): T[] {
  const order = loadProfileOrder()
  if (order.length === 0 || list.length < 2) return list

  const byName = new Map(list.map((profile) => [profile.name, profile]))
  const orderedNames = new Set<string>()
  const ordered: T[] = []

  for (const name of order) {
    const profile = byName.get(name)
    if (profile && !orderedNames.has(name)) {
      ordered.push(profile)
      orderedNames.add(name)
    }
  }

  for (const profile of list) {
    if (!orderedNames.has(profile.name)) ordered.push(profile)
  }

  return ordered
}

export interface DragState {
  dragIndex: number | null
  dragOverIndex: number | null
  isDragging: boolean
}

export function createDragState(): DragState {
  return { dragIndex: null, dragOverIndex: null, isDragging: false }
}

export function beginDrag(state: DragState, index: number, event?: { button?: number }): void {
  if (event?.button !== undefined && event.button !== 0) return
  if (event && 'preventDefault' in event && typeof event.preventDefault === 'function') event.preventDefault()
  state.dragIndex = index
  state.dragOverIndex = index
  state.isDragging = true
}

export function enterTarget(state: DragState, index: number): void {
  if (state.isDragging) state.dragOverIndex = index
}

export function finishDrag<T extends { name: string }>(state: DragState, index: number, list: T[]): T[] {
  if (!state.isDragging || state.dragIndex === null) return list
  const targetIndex = state.dragOverIndex ?? index
  let newList = list
  if (state.dragIndex !== targetIndex) {
    newList = moveItem(list, state.dragIndex, targetIndex)
    saveProfileOrder(newList.map((p) => p.name))
  }
  cancelDrag(state)
  return newList
}

export function cancelDrag(state: DragState): void {
  state.dragIndex = null
  state.dragOverIndex = null
  state.isDragging = false
}
