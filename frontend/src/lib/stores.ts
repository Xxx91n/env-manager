import { writable, derived, readable } from 'svelte/store'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
  isDisabled?: boolean
  profileSource?: string
  isProtected?: boolean
  isBuiltinProtected?: boolean
}

export interface ProfileVariable {
  name: string
  value: string
}

export interface ProfileData {
  id: string
  name: string
  isEnabled: boolean
  variables: ProfileVariable[]
}

export interface PathEntry {
  index: number
  path: string
}

export interface ModalConfig {
  title: string
  message: string
  confirmLabel?: string
  cancelLabel?: string
  variant?: 'danger' | 'warning' | 'info'
  onConfirm?: () => void
}

export interface InputModalConfig {
  title: string
  message?: string
  defaultValue?: string
  placeholder?: string
  maxLength?: number
  allowEmpty?: boolean
}

let inputResolve: ((value: string | null) => void) | null = null
export const inputModal = writable<InputModalConfig | null>(null)

export function openInputDialog(config: InputModalConfig): Promise<string | null> {
  return new Promise((resolve) => {
    inputResolve = resolve
    inputModal.set(config)
  })
}

export function closeInputModal(value: string | null) {
  if (inputResolve) {
    inputResolve(value)
    inputResolve = null
  }
  inputModal.set(null)
}

export const variables = writable<EnvVariable[]>([])
export const loading = writable(false)
export const error = writable<string | null>(null)
export const selectedScope = writable<'user' | 'system' | 'all'>('all')
export const search = writable('')

// Debounced search store: delays propagating search input by 150ms so
// rapid typing does not re-run the filter on every keystroke. This is
// critical for production machines with thousands of environment variables
// where each filter pass can take 10-50ms.
let searchDebounceId: ReturnType<typeof setTimeout> | null = null
export const debouncedSearch = readable('', (set) => {
  const unsubscribe = search.subscribe((value) => {
    if (searchDebounceId) clearTimeout(searchDebounceId)
    searchDebounceId = setTimeout(() => {
      set(value)
    }, 150)
  })
  return () => {
    if (searchDebounceId) clearTimeout(searchDebounceId)
    unsubscribe()
  }
})

// Derived store: caches filtered variables based on scope + debounced search.
// Uses the debounced search so filter only runs after the user stops typing.
// Svelte derived stores memoize: the filter only recomputes when a dependency
// changes, not on every unrelated component update.
// Performance switch: for production machines with 1000+ environment variables,
// we use a single-pass filter that combines scope and search in one iteration
// instead of two separate .filter() calls. For the common case (no filter at all),
// we return the original array reference (zero-copy) so Svelte reconciliation is skipped.
export const filteredVariables = derived(
  [variables, selectedScope, debouncedSearch],
  ([$variables, $selectedScope, $search]) => {
    const q = $search.trim().toLowerCase()
    const needsScopeFilter = $selectedScope !== 'all'
    const needsSearch = !!q

    // Fast path: no filtering needed — return original reference (zero allocation)
    if (!needsScopeFilter && !needsSearch) return $variables

    // Single-pass filter for production performance: combine scope + search in one iteration
    if (needsScopeFilter && needsSearch) {
      return $variables.filter(
        (v) => v.scope === $selectedScope && (v.name.toLowerCase().includes(q) || v.value.toLowerCase().includes(q)),
      )
    }

    // Scope-only filter
    if (needsScopeFilter) {
      return $variables.filter((v) => v.scope === $selectedScope)
    }

    // Search-only filter
    return $variables.filter((v) => v.name.toLowerCase().includes(q) || v.value.toLowerCase().includes(q))
  },
)
export const profiles = writable<ProfileData[]>([])
export const pathProfileIndex = writable<Map<string, string[]>>(new Map())
export const activeView = writable<'variables' | 'profiles' | 'path' | 'history' | 'protection' | 'service' | 'audit'>('variables')
export const modal = writable<ModalConfig | null>(null)
export const debugLogs = writable<DebugLogEntry[]>([])
export const isWriteInProgress = writable(false)
export const refreshTrigger = writable(0)

// Global toast notification store - rendered once in App.svelte
// to prevent layout shifts from per-component toast rendering
export interface Toast {
  id: number
  message: string
  type: 'success' | 'error' | 'info'
  duration: number
}

export const toasts = writable<Toast[]>([])
let toastId = 0

export function showToast(message: string, type: 'success' | 'error' | 'info' = 'info', duration = 3000) {
  const id = ++toastId
  toasts.update(list => [...list, { id, message, type, duration }])
  if (duration > 0) {
    setTimeout(() => {
      toasts.update(list => list.filter(t => t.id !== id))
    }, duration)
  }
}

export function dismissToast(id: number) {
  toasts.update(list => list.filter(t => t.id !== id))
}

export interface DebugLogEntry {
  timestamp: string
  level: 'info' | 'warn' | 'error' | 'debug'
  message: string
  command?: string
}

export function addDebugLog(entry: Omit<DebugLogEntry, 'timestamp'>) {
  const logEntry: DebugLogEntry = {
    ...entry,
    timestamp: new Date().toISOString(),
  }
  debugLogs.update(logs => {
    const updated = [...logs, logEntry]
    // Keep last 200 entries to prevent memory leak
    if (updated.length > 200) {
      return updated.slice(updated.length - 200)
    }
    return updated
  })
}

export function clearDebugLogs() {
  debugLogs.set([])
}

export function showModal(config: ModalConfig) {
  modal.set(config)
}

export function closeModal() {
  modal.set(null)
}
