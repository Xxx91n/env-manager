import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { resolve } from 'path'

const pathEditorPath = resolve(__dirname, 'components/PathEditor.svelte')
const src = readFileSync(pathEditorPath, 'utf8')
const cssPath = resolve(__dirname, '..', 'app.css')
const css = readFileSync(cssPath, 'utf8')

function findFn(name: string): string {
  const m = src.match(new RegExp('function ' + name + '[\\s\\S]*?\\n  \\}', 'm'))
  return m ? m[0] : ''
}

describe('v0.9.25 PathEditor move reactivity + highlight (W3C APG listbox-rearrangeable)', () => {
  it('handleMoveUp creates a new array reference (Svelte 4 keyed each reactivity)', () => {
    const fn = findFn('handleMoveUp')
    expect(fn.length).toBeGreaterThan(0)
    expect(fn.includes('stagedEntries = [...arr]')).toBe(true)
  })

  it('handleMoveDown creates a new array reference', () => {
    const fn = findFn('handleMoveDown')
    expect(fn.length).toBeGreaterThan(0)
    expect(fn.includes('stagedEntries = [...arr]')).toBe(true)
  })

  it('handleMoveUp sets lastMovedKey for transient highlight', () => {
    const fn = findFn('handleMoveUp')
    expect(fn.includes('lastMovedKey = movedKey')).toBe(true)
  })

  it('handleMoveDown sets lastMovedKey for transient highlight', () => {
    const fn = findFn('handleMoveDown')
    expect(fn.includes('lastMovedKey = movedKey')).toBe(true)
  })

  it('move buttons have data-move-up-key / data-move-down-key for focus targeting', () => {
    expect(src.includes('data-move-up-key=')).toBe(true)
    expect(src.includes('data-move-down-key=')).toBe(true)
  })

  it('<tr> adds just-moved class binding which drives highlight animation', () => {
    expect(src.includes("lastMovedKey === entry.index ? 'just-moved'")).toBe(true)
  })

  it('app.css defines tr.just-moved animation', () => {
    expect(css.includes('tr.just-moved')).toBe(true)
    expect(css.includes('@keyframes just-moved-fade')).toBe(true)
  })

  it('move handler moves focus to the moved row (W3C APG: focus follows the moved option)', () => {
    const fn = findFn('handleMoveUp')
    expect(fn.includes('btn.focus()')).toBe(true)
  })
})

describe('v0.9.25 PathEditor dead-entries button reactive disabled (WCAG 4.1.2)', () => {
  it('has $: liveDeadCount derived from displayEntries', () => {
    expect(src.includes('$: liveDeadCount = displayEntries.filter')).toBe(true)
  })

  it('button disabled condition uses liveDeadCount (not stale healthSummary)', () => {
    expect(src.includes('disabled={actionLoading || liveDeadCount === 0}')).toBe(true)
  })

  it('button label shows liveDeadCount instead of healthSummary.dead', () => {
    expect(src.includes('{liveDeadCount}')).toBe(true)
    expect(src.includes('({healthSummary.dead})')).toBe(false)
  })
})
