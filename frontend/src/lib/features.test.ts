import { describe, expect, it } from 'vitest'
import { hasVariableConflict, highlightParts, moveItem } from './features'

describe('feature helpers', () => {
  it('splits all case-insensitive search matches without HTML injection', () => {
    expect(highlightParts('Path %PATH% value', 'path')).toEqual([
      { text: 'Path', match: true },
      { text: ' %', match: false },
      { text: 'PATH', match: true },
      { text: '% value', match: false },
    ])
  })

  it('moves an item without mutating the source list', () => {
    const source = ['a', 'b', 'c']
    expect(moveItem(source, 0, 2)).toEqual(['b', 'c', 'a'])
    expect(source).toEqual(['a', 'b', 'c'])
  })

  it('detects same-scope variable conflicts and excludes the original name', () => {
    const variables = [{ name: 'JAVA_HOME', scope: 'user' }]
    expect(hasVariableConflict(variables, 'java_home', 'user')).toBe(true)
    expect(hasVariableConflict(variables, 'JAVA_HOME', 'user', 'JAVA_HOME')).toBe(false)
    expect(hasVariableConflict(variables, 'JAVA_HOME', 'system')).toBe(false)
  })
})
