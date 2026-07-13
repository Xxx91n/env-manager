export interface HighlightPart {
  text: string
  match: boolean
}

export function highlightParts(text: string, query: string): HighlightPart[] {
  const needle = query.trim()
  if (!needle) return [{ text, match: false }]
  const lowerText = text.toLocaleLowerCase()
  const lowerNeedle = needle.toLocaleLowerCase()
  const parts: HighlightPart[] = []
  let cursor = 0
  while (cursor < text.length) {
    const index = lowerText.indexOf(lowerNeedle, cursor)
    if (index < 0) {
      parts.push({ text: text.slice(cursor), match: false })
      break
    }
    if (index > cursor) parts.push({ text: text.slice(cursor, index), match: false })
    parts.push({ text: text.slice(index, index + needle.length), match: true })
    cursor = index + needle.length
  }
  return parts.length > 0 ? parts : [{ text, match: false }]
}

export function moveItem<T>(items: T[], from: number, to: number): T[] {
  if (from === to || from < 0 || to < 0 || from >= items.length || to >= items.length) return items
  const result = [...items]
  const [moved] = result.splice(from, 1)
  result.splice(to, 0, moved)
  return result
}

export function hasVariableConflict(
  variables: Array<{ name: string; scope: string }>,
  name: string,
  scope: string,
  originalName?: string
): boolean {
  return variables.some(variable =>
    variable.scope === scope &&
    variable.name.toLocaleLowerCase() === name.toLocaleLowerCase() &&
    variable.name.toLocaleLowerCase() !== (originalName ?? '').toLocaleLowerCase()
  )
}
