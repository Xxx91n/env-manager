import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { resolve } from 'path'

const notesStorePath = resolve(__dirname, 'notesStore.ts')
const src = readFileSync(notesStorePath, 'utf8')
const variablesPath = resolve(__dirname, 'components/Variables.svelte')
const variablesSrc = readFileSync(variablesPath, 'utf8')
const pathEditorPath = resolve(__dirname, 'components/PathEditor.svelte')
const pathEditorSrc = readFileSync(pathEditorPath, 'utf8')

describe('v0.9.25 notesStore reactive store (Svelte 4)', () => {
  it('exports notesStore as a Readable for $notesStore auto-subscription', () => {
    expect(src.includes('export const notesStore')).toBe(true)
    // must be typed Readable so components cannot .set directly from outside
    expect(src.includes('Readable<Record<string, VarNote>>')).toBe(true)
  })

  it('upsertNote updates the store optimistically before the IPC write', () => {
    // Root cause being fixed: prior getNoteSync + notesTick bump did not track the function return
    // as a reactive dependency (Svelte 4 docs: function bodies not tracked).
    expect(src.includes('(notesStore as any)._update(')).toBe(true)
  })

  it('deleteNote updates the store so subscribers see removal immediately', () => {
    const m = src.match(/export async function deleteNote[\s\S]*?\n\}/)
    expect(m).toBeTruthy()
    expect(m![0].includes('(notesStore as any)._update(')).toBe(true)
  })

  it('loadNotes seeds the store on first load so subscribers see initial notes', () => {
    expect(src.includes('(notesStore as any)._set({ ...cache.notes })')).toBe(true)
  })

  it('keeps debounced IPC write (200ms) for durability, not for UI reflection', () => {
    expect(src.includes('setTimeout(async () =>')).toBe(true)
    expect(src.includes('}, 200)')).toBe(true)
  })
})

describe('v0.9.25 notesStore consumed via $notesStore auto-subscription in components', () => {
  it('Variables.svelte imports notesStore and uses $notesStore[key] in template', () => {
    expect(variablesSrc.includes('import { loadNotes, getNoteSync, upsertNote, notesStore }')).toBe(true)
    expect(variablesSrc.includes('$notesStore[variable.name]')).toBe(true)
  })

  it('Variables.svelte tooltip uses role=tooltip + aria-describedby (W3C WAI-ARIA APG)', () => {
    expect(variablesSrc.includes('role="tooltip"')).toBe(true)
    expect(variablesSrc.includes('aria-describedby=')).toBe(true)
  })

  it('Variables.svelte no longer relies on getNoteSync for tooltip content', () => {
    // The reactive store replaces the stale getNoteSync tooltip read title
    expect(variablesSrc.includes('title={getNoteSync(variable.name)')).toBe(false)
  })

  it('PathEditor.svelte uses $notesStore[pathNoteKey(...)] for tooltip', () => {
    expect(pathEditorSrc.includes('$notesStore[pathNoteKey(')).toBe(true)
    expect(pathEditorSrc.includes('role="tooltip"')).toBe(true)
  })
})
