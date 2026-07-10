import { writable } from 'svelte/store'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
}

export const variables = writable<EnvVariable[]>([])
export const loading = writable(false)
export const error = writable<string | null>(null)
export const selectedScope = writable<'user' | 'system' | 'all'>('all')
