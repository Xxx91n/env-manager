// Regression test for v0.7.6/v0.7.7 History operations column staying on
// Chinese after a locale switch. setup.ts now provides a real per-locale
// dictionary; this test directly exercises the $t store to verify the
// mock resolves to the language-specific dictionary value and that
// switching locale changes the resolved string. This is the minimal
// invariant that the user-reported bug ("operations stay on Chinese
// regardless of locale") violates. HistoryPage's `getOperationLabel`
// implementation delegates to `$t('history.op.<command>'); this test
// reproduces the exact reactive contract.

import { describe, expect, it, beforeEach, vi } from 'vitest'
import { get } from 'svelte/store'
import { t, locale } from 'svelte-i18n'
import enMessages from './translations/en.json'
import zhMessages from './translations/zh.json'

const en = enMessages as Record<string, string>
const zh = zhMessages as Record<string, string>

// Representative audit commands spanning both full-key and head-key fallback
// paths. Each command string is exactly what the CLI writes to audit.json.
const cases: Array<{ command: string; enKey: keyof typeof en; zhKey: keyof typeof zh; head: string }> = [
  { command: 'profile apply',       enKey: 'history.op.profile apply',       zhKey: 'history.op.profile apply',       head: 'history.op.profile' },
  { command: 'path add',            enKey: 'history.op.path add',            zhKey: 'history.op.path add',            head: 'history.op.path' },
  { command: 'profile create',      enKey: 'history.op.profile create',      zhKey: 'history.op.profile create',      head: 'history.op.profile' },
  { command: 'set',                 enKey: 'history.op.set',                 zhKey: 'history.op.set',                 head: 'history.op.set' },
  { command: 'change-scope',        enKey: 'history.op.change-scope',        zhKey: 'history.op.change-scope',        head: 'history.op.change-scope' },
  { command: 'profile remove-secret', enKey: 'history.op.profile remove-secret', zhKey: 'history.op.profile remove-secret', head: 'history.op.profile' },
]

describe('history op labels -- $t resolves locale-specifically (real dictionary reactive switch)', () => {
  beforeEach(() => {
    locale.set('en')
  })

  it('under en, every history.op key resolves to its English value', () => {
    const tFn = get(t) as unknown as (k: string) => string
    for (const c of cases) {
      const got = tFn('history.op.' + c.command)
      // The reactive $t MUST not return the key string itself when the en
      // dictionary has a value for it -- otherwise getOperationLabel falls
      // through to the raw English command and the user-visible label is
      // not localized at all.
      expect(got).toBe(en[c.enKey])
      expect(got).not.toBe('history.op.' + c.command)
    }
  })

  it('under zh, every history.op key resolves to its Chinese value (not English)', () => {
    locale.set('zh')
    const tFn = get(t) as unknown as (k: string) => string
    for (const c of cases) {
      const got = tFn('history.op.' + c.command)
      expect(got).toBe(zh[c.zhKey])
      expect(got).not.toBe(en[c.enKey])
    }
  })

  it('switching en -> zh -> en flips the resolved string each time', () => {
    const tFn = get(t) as unknown as (k: string) => string
    const key = 'history.op.profile apply'
    expect(tFn(key)).toBe(en[key])
    locale.set('zh')
    expect(tFn(key)).toBe(zh[key])
    locale.set('en')
    expect(tFn(key)).toBe(en[key])
  })
})