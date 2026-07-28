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
  // Always initialize with 'en' first - messages are already loaded synchronously.
  init({
    fallbackLocale: defaultLocale,
    initialLocale: defaultLocale,
  })

 // Sync read of localStorage for instant first render. This is NOT the
 // authoritative source - it may be stale if WebView2 didn't flush before
 // the process exited. The IPC gui-settings.json (read below) is authoritative.
 let stored: string | null = null
 try {
   stored = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null
 } catch {
   // localStorage may be unavailable in some WebView2 contexts
 }

  // Do NOT use navigator locale as a fallback. On a Chinese Windows machine
  // getLocaleFromNavigator() returns 'zh-CN' which overwrites the user's
  // explicit en choice when localStorage is empty/unflushed.
  const resolved = normalizeLocale(stored || defaultLocale)

  // Sync write to localStorage so first-run persists the default and the
  // i18n test (which checks localStorage immediately after setupI18n) passes.
  // This is NOT the durable store - the IPC read below may override it.
  try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', resolved) } catch { /* ignore */ }

  // If the user's locale is not English, switch asynchronously after render.
  // The page is already visible in English; the switch is seamless.
  if (resolved !== defaultLocale) {
    queueMicrotask(() => {
      localeStore.set(resolved)
    })
  }

  // The IPC gui-settings.json is the SINGLE SOURCE OF TRUTH for persisted
  // locale. We read it AFTER init and correct the locale store if it differs.
  // NEVER write to it here - the persisted value is only changed when the
  // user explicitly picks a language in SettingsDialog.switchLocale.
  // The prior bug was calling setSetting('locale', resolved) here, which
  // overwrote the user's persisted choice with a stale sync guess before
  // this async correction could protect it.
  void getSetting('locale').then((storeLocale) => {
    if (storeLocale) {
      const authoritative = normalizeLocale(storeLocale)
      // Update localStorage so the next sync read matches the durable value
      try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', authoritative) } catch { /* ignore */ }
      // Switch the locale store if it differs from what we rendered
      if (authoritative !== resolved) {
        localeStore.set(authoritative)
      }
    } else if (stored) {
      // IPC file empty but localStorage has a value - persist it for next time
      void setSetting('locale', resolved)
    }
  })

  // Apply text direction (RTL for Arabic) immediately and on locale changes.
  applyTextDirection(resolved)
  localeStore.subscribe((loc) => {
    if (loc) applyTextDirection(loc)
  })

  return resolved
}

export const locales = supportedLocales
export const defaultLanguage = defaultLocale
