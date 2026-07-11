import { writable } from 'svelte/store'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
  isDisabled?: boolean
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

export function showModal(config: ModalConfig) {
  modal.set(config)
}

export function closeModal() {
  modal.set(null)
}
