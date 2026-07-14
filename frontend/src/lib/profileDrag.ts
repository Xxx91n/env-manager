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
  if (event?.preventDefault) event.preventDefault()
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
