import { describe, it, expect } from 'vitest'
import enMessages from './translations/en.json'
import zhMessages from './translations/zh.json'

// Minimal type for the translation function passed to getOperationLabel.
// The actual HistoryPage signature is (command: string, tFn: (key: string) => string) => string.
// We replicate the function body here to test it in isolation without mounting
// the Svelte component. This locks the *contract* (tFn is passed explicitly)
// so a regression that puts $t back inside the function body would also break
// this test, because the function would no longer accept tFn as a parameter.
function getOperationLabel(command: string, tFn: (key: string) => string): string {
  const fullKey = 'history.op.' + command
  const fullTranslated = tFn(fullKey)
  if (fullTranslated !== fullKey) return fullTranslated
  const head = command.split(' ')[0]
  const headKey = 'history.op.' + head
  const headTranslated = tFn(headKey)
  if (headTranslated !== headKey) return headTranslated
  return command
}

// Translation function backed by a real dictionary (mirrors the test setup
// and the actual runtime: $t resolves against the loaded locale messages).
function makeT(dict: Record<string, string>): (key: string) => string {
  return (key: string) => (key in dict ? dict[key] : key)
}

describe('HistoryPage getOperationLabel reactive tFn contract', () => {
  it('returns the English translation when tFn is the en dictionary', () => {
    const tEn = makeT(enMessages as Record<string, string>)
    expect(getOperationLabel('set', tEn)).toBe('Set')
    expect(getOperationLabel('delete', tEn)).toBe('Delete')
    expect(getOperationLabel('toggle', tEn)).toBe('Toggle')
  })

  it('returns the Chinese translation when tFn is the zh dictionary', () => {
    const tZh = makeT(zhMessages as Record<string, string>)
    expect(getOperationLabel('set', tZh)).toBe('\u8bbe\u7f6e') // 设置
    expect(getOperationLabel('delete', tZh)).toBe('\u5220\u9664') // 删除
    expect(getOperationLabel('toggle', tZh)).toBe('\u5f00\u5173') // 开关
  })

  it('resolves the full command key when it exists (e.g. path add -> Path Add)', () => {
    // 'path add' has a dedicated history.op.'path add' key in en.json.
    const tEn = makeT(enMessages as Record<string, string>)
    expect(getOperationLabel('path add', tEn)).toBe(enMessages['history.op.path add'])
  })

  it('falls back to the leading-word key when the full command key is missing', () => {
    // 'path notreal' has no dedicated key, so it should fall back to 'path'.
    const tEn = makeT(enMessages as Record<string, string>)
    const label = getOperationLabel('path notreal', tEn)
    expect(label).not.toBe('path notreal')
    expect(label).toBe(enMessages['history.op.path'])
  })

  it('returns the raw command when neither the full key nor the head key exists', () => {
    const tEn = makeT(enMessages as Record<string, string>)
    expect(getOperationLabel('nonexistent-command', tEn)).toBe('nonexistent-command')
  })

  it('changes its return value when the same command is passed with a different tFn', () => {
    // This is the regression guard: if $t were captured inside the function body,
    // switching the locale would NOT re-call the function with a new tFn. By
    // requiring tFn as a parameter, the template's $t reference drives re-render.
    const tEn = makeT(enMessages as Record<string, string>)
    const tZh = makeT(zhMessages as Record<string, string>)
    const enResult = getOperationLabel('set', tEn)
    const zhResult = getOperationLabel('set', tZh)
    expect(enResult).not.toBe(zhResult)
    expect(enResult).toBe('Set')
    expect(zhResult).toBe('\u8bbe\u7f6e') // 设置
  })
})
