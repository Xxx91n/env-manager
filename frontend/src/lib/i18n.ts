import { register, init, getLocaleFromNavigator } from 'svelte-i18n'

const defaultLocale = 'en'
const supportedLocales = ['en', 'zh']

// Register locales and load messages
register('en', () => import('./translations/en.json'))
register('zh', () => import('./translations/zh.json'))

// Initialize i18n
export function setupI18n() {
  const locale = localStorage.getItem('locale') || getLocaleFromNavigator() || defaultLocale
  const normalizedLocale = supportedLocales.includes(locale) ? locale : defaultLocale

  init({
    fallbackLocale: defaultLocale,
    initialLocale: normalizedLocale,
  })

  // Save preference
  localStorage.setItem('locale', normalizedLocale)

  return normalizedLocale
}

export const locales = supportedLocales
export const defaultLanguage = defaultLocale
