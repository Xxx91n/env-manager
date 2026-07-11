import { register, init, getLocaleFromNavigator, addMessages } from 'svelte-i18n'
import enMessages from './translations/en.json'

const defaultLocale = 'en'
const supportedLocales = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']

// Eagerly load the default locale synchronously so the UI renders immediately
// even under Tauri's custom-protocol (no dev server / no async import).
addMessages('en', enMessages as Record<string, any>)

// Lazy-load the other locales (async, non-blocking).
register('zh', () => import('./translations/zh.json'))
register('ja', () => import('./translations/ja.json'))
register('ko', () => import('./translations/ko.json'))
register('de', () => import('./translations/de.json'))
register('fr', () => import('./translations/fr.json'))
register('es', () => import('./translations/es.json'))
register('pt', () => import('./translations/pt.json'))
register('ru', () => import('./translations/ru.json'))
register('ar', () => import('./translations/ar.json'))

function normalizeLocale(raw: string): string {
  const base = raw.toLowerCase().split('-')[0]
  return supportedLocales.includes(base) ? base : defaultLocale
}

/**
 * Initialize i18n. Returns the resolved locale.
 * The default locale ('en') is already loaded synchronously above,
 * so the UI renders without waiting for async message loaders.
 */
export function setupI18n(): string {
  const stored = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null
  const browser = getLocaleFromNavigator()
  const raw = stored || browser || defaultLocale
  const resolved = normalizeLocale(raw)

  init({
    fallbackLocale: defaultLocale,
    initialLocale: resolved,
  })

  if (typeof localStorage !== 'undefined') {
    localStorage.setItem('locale', resolved)
  }

  return resolved
}

export const locales = supportedLocales
export const defaultLanguage = defaultLocale
