/**
 * Vitest global setup.
 *
 * Provides a minimal mock for @tauri-apps/api/core so that components
 * importing `invoke` can render in jsdom without a running Tauri backend.
 *
 * Mocks svelte-i18n with a small runtime that resolves $t(key) against an
 * in-memory dictionary so that component tests exercising locale switching
 * can actually observe a change in the resolved string. The prior mock
 * returned the key string itself, which masked the v0.7.6/v0.7.7 regression
 * where the History operations column stayed on Chinese after switching
 * locale. This mock carries a *real* per-locale dictionary and locale store
 * so that asserting $t over two locales actually exercises the i18n path.
 */
import { vi, beforeEach } from 'vitest'
import { writable, derived, get } from 'svelte/store'
import enMessages from '../src/lib/translations/en.json'
import zhMessages from '../src/lib/translations/zh.json'

// In-memory GUI settings store for tests. Keyed by string.
const guiSettings = new Map<string, string>()

// Mock Tauri invoke - returns a default empty success response.
// For read_gui_setting/write_gui_setting, route to the in-memory store.
vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn(async (cmd: string, args?: Record<string, unknown>) => {
    if (cmd === 'read_gui_setting' && args?.key) {
      return guiSettings.get(args.key as string) ?? null
    }
    if (cmd === 'write_gui_setting' && args?.key) {
      guiSettings.set(args.key as string, args.value as string)
      return true
    }
    return { success: true, data: '[]', error: null }
  }),
}))

// In-memory locale + dictionary. zhMessages/enMessages are imported at module
// load so the dictionary is populated synchronously -- the per-test locale
// switch exercises the full reactive path.
const dictionaries: Record<string, Record<string, string>> = {
  en: enMessages as Record<string, string>,
  zh: zhMessages as Record<string, string>,
}
const localeStore = writable<string>('en')

// The mock $t resolves against the current locale dictionary; missing keys
// fall back to the English dictionary; if a key is missing in both, the
// caller can detect that and use the raw value (matches getOperationLabel
// contract in HistoryPage.svelte).
function tFn(key: string): string {
  const loc = get(localeStore)
  const dict = dictionaries[loc] || {}
  if (Object.prototype.hasOwnProperty.call(dict, key)) return dict[key]
  if (dictionaries.en && Object.prototype.hasOwnProperty.call(dictionaries.en, key)) {
    return dictionaries.en[key]
  }
  return key
}
// svelte-i18n exposes $t as a readable store whose value is the t() function.
// Svelte derived re-runs when the source store (locale) changes, so on locale
// switch every `$t(...)` expression re-resolves -- exactly the reactive
// contract svelte-i18n itself provides in the browser runtime.
const tStore = derived(localeStore, () => tFn)

vi.mock('svelte-i18n', () => ({
  register: vi.fn(),
  init: vi.fn(),
  getLocaleFromNavigator: vi.fn(() => null),
  addMessages: vi.fn((loc: string, msg: Record<string, string>) => {
    dictionaries[loc] = { ...(dictionaries[loc] || {}), ...msg }
  }),
  locale: localeStore,
  _: derived(localeStore, () => ({})),
  t: tStore,
}))

// Reset localStorage and all mock call counts before each test.
beforeEach(() => {
  if (typeof localStorage !== 'undefined') {
    localStorage.clear()
  }
  vi.clearAllMocks()
  // Reset the mock locale to 'en' before each test so tests start from a known
  // state (rather than leaking the previous test's last locale).
  localeStore.set('en')
})
// Reset the in-memory GUI settings store before each test.
// (The mock invoke in the @tauri-apps/api/core mock above routes
// read_gui_setting/write_gui_setting to this Map.)
beforeEach(() => {
  guiSettings.clear()
})
