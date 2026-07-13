import { writable } from 'svelte/store'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
  isDisabled?: boolean
  profileSource?: string
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

export const variables = writable<EnvVariable[]>([])
export const loading = writable(false)
export const error = writable<string | null>(null)
export const selectedScope = writable<'user' | 'system' | 'all'>('all')
export const profiles = writable<ProfileData[]>([])
export const activeView = writable<'variables' | 'profiles' | 'path'>('variables')
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
