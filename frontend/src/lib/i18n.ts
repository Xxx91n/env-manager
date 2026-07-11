import { register, init, getLocaleFromNavigator, addMessages, locale as localeStore } from 'svelte-i18n'
import { get } from 'svelte/store'
import enMessages from './translations/en.json'

const defaultLocale = 'en'
const supportedLocales = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']

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

  // Determine the user's preferred locale.
  let stored: string | null = null
  try {
    stored = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null
  } catch {
    // localStorage may be unavailable in some WebView2 contexts
  }

  const browser = (() => {
    try {
      return getLocaleFromNavigator()
    } catch {
      return null
    }
  })()

  const resolved = normalizeLocale(stored || browser || defaultLocale)

  // Persist the resolved locale.
  try {
    localStorage.setItem('locale', resolved)
  } catch {
    // Ignore storage errors
  }

  // If the user's locale is not English, switch asynchronously after render.
  // The page is already visible in English; the switch is seamless.
  if (resolved !== defaultLocale) {
    queueMicrotask(() => {
      localeStore.set(resolved)
    })
  }

  return resolved
}

export const locales = supportedLocales
export const defaultLanguage = defaultLocale
