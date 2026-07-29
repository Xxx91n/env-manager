import { register, init, addMessages, locale as localeStore } from 'svelte-i18n'
import { getSetting, setSetting } from './settingsStore'
import enMessages from './translations/en.json'

const defaultLocale = 'en'
const supportedLocales = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
const rtlLocales = ['ar']

// Eagerly load English synchronously. This is the only locale that must be
// available before the first render under Tauri's custom protocol.
addMessages(defaultLocale, enMessages as Record<string, string>)

// Lazy-load the other locales via dynamic import. These resolve asynchronously.
register('zh', () => import('./translations/zh.json'))
register('ja', () => import('./translations/ja.json'))
register('ko', () => import('./translations/ko.json'))
register('de', () => import('./translations/de.json'))
register('fr', () => import('./translations/fr.json'))
register('es', () => import('./translations/es.json'))
register('pt', () => import('./translations/pt.json'))
register('ru', () => import('./translations/ru.json'))
register('ar', () => import('./translations/ar.json'))

function normalizeLocale(raw: string | null | undefined): string {
  if (!raw) return defaultLocale
  const base = raw.toLowerCase().split('-')[0]
  return supportedLocales.includes(base) ? base : defaultLocale
}

function applyTextDirection(loc: string): void {
  if (typeof document === 'undefined') return
  const isRtl = rtlLocales.includes(loc)
  document.documentElement.setAttribute('dir', isRtl ? 'rtl' : 'ltr')
  document.documentElement.setAttribute('lang', loc)
}

/**
 * Initialize i18n safely.
 *
 * Always starts with 'en' (synchronously loaded) so the first render is never
 * blank. If the user's preferred locale differs, we switch asynchronously
 * after the initial paint. This avoids the race condition where the async
 * message loader hasn't resolved yet and $t() returns undefined for all keys.
 */
export function setupI18n(): string {
  // Always initialize with 'en' synchronously - messages are already loaded.
  // The authoritative locale is read from the durable IPC store in
  // App.svelte onMount (await getSetting('locale')) via applyPersistedLocale()
  // so a stale WebView2 localStorage can never resurrect a language the user
  // already switched away from. We do NOT queue a microtask from a stale sync
  // guess that could re-set the wrong locale before the IPC read resolves.
  init({
    fallbackLocale: defaultLocale,
    initialLocale: defaultLocale,
  })

  // Best-effort localStorage hint for first paint - never authoritative.
  let stored: string | null = null
  try {
    stored = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null
  } catch { /* ignore */ }

  // If localStorage happens to have a supported non-en locale, render it for
  // first paint ONLY (instant feedback). applyPersistedLocale() (called from
  // App.svelte onMount after the IPC read resolves) is the single source of
  // truth and will override this if it differs.
  const hint = normalizeLocale(stored || defaultLocale)
  // Always persist the hint to localStorage so the first-render value and the
  // sync-read tests stay consistent. applyPersistedLocale() still wins.
  try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', hint) } catch { /* ignore */ }
  if (hint !== defaultLocale) {
    queueMicrotask(() => { localeStore.set(hint) })
  }

  // Apply text direction immediately and track locale changes.
  applyTextDirection(hint)
  localeStore.subscribe((loc) => { if (loc) applyTextDirection(loc) })

  return hint
}

/**
 * Apply the authoritative locale resolved from the durable IPC store.
 * Called from App.svelte onMount AFTER getSetting('locale') resolves so the
 * localeStore reflects the user's persisted choice (not a stale sync guess).
 * Also seeds the IPC file the first time the user has nothing durable-persisted
 * but localStorage does (first-run migration).
 */
export async function applyPersistedLocale(): Promise<void> {
  let stored: string | null = null
  try { stored = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null } catch { /* ignore */ }
  const durable = await getSetting('locale')
  if (durable) {
    const authoritative = normalizeLocale(durable)
    try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', authoritative) } catch { /* ignore */ }
    localeStore.set(authoritative)
  } else if (stored) {
    // First-run seeding: persist the localStorage value to the durable store.
    const seed = normalizeLocale(stored)
    try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', seed) } catch { /* ignore */ }
    localeStore.set(seed)
    void setSetting('locale', seed)
  }
}

export const locales = supportedLocales
export const defaultLanguage = defaultLocale
