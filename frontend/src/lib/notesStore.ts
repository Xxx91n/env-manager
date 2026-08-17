import { invoke } from '@tauri-apps/api/core'
import { writable, type Readable } from 'svelte/store'
import { frontendLog } from './settingsStore'

export interface VarNote {
  key: string
  note: string
  updatedAt: string
  color?: string
  pinned?: boolean
}

export interface NotesData {
  version: number
  notes: Record<string, VarNote>
}

// v0.9.25 — Svelte 4 reactive store: $notes auto-subscription guarantees
// immediate UI reflection after async upsert (Svelte 4 svelte/store official).
// Prior getNoteSync + notesTick writable bump did not track the function return
// as a reactive dependency (Svelte 4 docs: function bodies not tracked).
let cache: NotesData | null = null
let writeTimer: ReturnType<typeof setTimeout> | null = null
let pendingData: NotesData | null = null

// Auto-subscription store. Components read $notesStore for reactive re-render.
export const notesStore: Readable<Record<string, VarNote>> = (() => {
  const inner = writable<Record<string, VarNote>>({})
  return {
    subscribe: inner.subscribe,
    // internal setter exposed via _set for upsert/delete only
    _set: inner.set,
    _update: inner.update,
  } as Readable<Record<string, VarNote>> & { _set: (v: Record<string, VarNote>) => void; _update: (fn: (v: Record<string, VarNote>) => Record<string, VarNote>) => void }
})()

export async function loadNotes(): Promise<NotesData> {
  if (cache) return cache
  try {
    const raw = await invoke('read_var_notes') as NotesData
    cache = raw || { version: 1, notes: {} }
    frontendLog('info', '[notesStore] loadNotes: loaded ' + Object.keys(cache.notes).length + ' notes')
    // sync store so subscribers see the initial load
    ;(notesStore as any)._set({ ...cache.notes })
  } catch (e) {
    frontendLog('warn', '[notesStore] loadNotes failed: ' + (e instanceof Error ? e.message : String(e)))
    cache = { version: 1, notes: {} }
    ;(notesStore as any)._set({})
  }
  return cache
}

// Back-compat: reads cache snapshot synchronously. Prefer $notesStore in templates.
export function getNoteSync(key: string): VarNote | null {
  if (!cache) return null
  return cache.notes[key] || null
}

export async function upsertNote(key: string, noteText: string): Promise<void> {
  const data = await loadNotes()
  const trimmed = noteText.trim()
  if (trimmed) {
    frontendLog('info', '[notesStore] upsertNote: key=' + key + ' len=' + trimmed.length)
    const note: VarNote = { key, note: trimmed, updatedAt: new Date().toISOString() }
    data.notes[key] = note
    // optimistic: update store immediately so $notesStore subscribers re-render
    // before the 200ms debounced IPC write (Svelte 4 svelte/store guarantee).
    ;(notesStore as any)._update(s => ({ ...s, [key]: note }))
  } else {
    delete data.notes[key]
    ;(notesStore as any)._update(s => { const ns = { ...s }; delete ns[key]; return ns })
  }
  scheduleWrite(data)
}

export async function deleteNote(key: string): Promise<void> {
  const data = await loadNotes()
  delete data.notes[key]
  frontendLog('info', '[notesStore] deleteNote: key=' + key)
  ;(notesStore as any)._update(s => { const ns = { ...s }; delete ns[key]; return ns })
  scheduleWrite(data)
}

function scheduleWrite(data: NotesData): void {
  pendingData = data
  if (writeTimer) clearTimeout(writeTimer)
  writeTimer = setTimeout(async () => {
    if (!pendingData) return
    try {
      await invoke('write_var_notes', { notesJson: JSON.stringify(pendingData) })
      frontendLog('info', '[notesStore] write debounced: ' + Object.keys(pendingData.notes).length + ' notes')
    } catch (e) {
      frontendLog('error', '[notesStore] write failed: ' + (e instanceof Error ? e.message : String(e)))
    }
  }, 200)
}
