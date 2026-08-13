import { invoke } from '@tauri-apps/api/core'

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

let cache: NotesData | null = null
let writeTimer: ReturnType<typeof setTimeout> | null = null
let pendingData: NotesData | null = null

export async function loadNotes(): Promise<NotesData> {
  if (cache) return cache
  try {
    const raw = await invoke('read_var_notes') as NotesData
    cache = raw || { version: 1, notes: {} }
  } catch {
    cache = { version: 1, notes: {} }
  }
  return cache
}

export function getNoteSync(key: string): VarNote | null {
  if (!cache) return null
  return cache.notes[key] || null
}

export async function upsertNote(key: string, noteText: string): Promise<void> {
  const data = await loadNotes()
  const trimmed = noteText.trim()
  if (trimmed) {
    data.notes[key] = {
      key,
      note: trimmed,
      updatedAt: new Date().toISOString(),
    }
  } else {
    delete data.notes[key]
  }
  scheduleWrite(data)
}

export async function deleteNote(key: string): Promise<void> {
  const data = await loadNotes()
  delete data.notes[key]
  scheduleWrite(data)
}

function scheduleWrite(data: NotesData): void {
  pendingData = data
  if (writeTimer) clearTimeout(writeTimer)
  writeTimer = setTimeout(async () => {
    if (!pendingData) return
    try {
      await invoke('write_var_notes', { notesJson: JSON.stringify(pendingData) })
    } catch { /* best-effort */ }
  }, 200)
}
