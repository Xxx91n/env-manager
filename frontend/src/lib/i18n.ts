import { register, init, addMessages, locale as localeStore } from 'svelte-i18n'
import { getSetting, setSetting, frontendLog } from './settingsStore'
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

  // IMPORTANT: setupI18n does NOT read localStorage to derive the first-paint
  // locale anymore. In the portable Tauri build WebView2 localStorage turned out
  // to be unreliable across restarts (an explicit setItem('locale','en') made in
  // a prior session was NOT visible on the next boot - it inertly persisted the
  // OLD 'zh' that was there before, so first paint kept resurrecting the stale
  // 'zh' briefly before applyPersistedLocale flipped to 'en'). We now treat the
  // durable IPC store (gui-settings.json via getSetting/setSetting) as the ONLY
  // authoritative source. First paint is ALWAYS the default locale (en), which
  // is also svelte-i18n initialLocale; applyPersistedLocale() in App.svelte
  // onMount performs the single authoritative localeStore.set once the durable
  // read resolves. localStorage is written best-effort only as a forward echo
  // (kept so existing tests that assert localStorage is populated still pass),
  // never as a startup source of truth. This eliminates both the first-paint
  // Chinese flicker AND the intermittent 'reverts to zh after restart' symptom:
  // when localStorage holds a stale 'zh', first paint is 'en' (not 'zh'), and
  // the durable 'en' wins authoritatively a few ms later.
  const hint = defaultLocale
  let stored: string | null = null
  try { stored = typeof localStorage !== 'undefined' ? localStorage.getItem('locale') : null } catch { /* ignore */ }
  // Best-effort forward-echo: never read back during startup. Tolerant of failure.
  try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', hint) } catch { /* ignore */ }
  // Removed queueMicrotask(() => localeStore.set(hint)): it caused a first-paint
  // FLICKER (en -> hint) on every boot and re-emitted the locale change so the
  // reactive tray listener fired multiple times (the tray-flicker symptom in the
  // log). applyPersistedLocale() in App.svelte onMount is now the ONLY setter of
  // localeStore during startup; a persisted non-en locale flips the store exactly
  // once when the durable IPC read resolves. No flicker, no duplicate tray updates.
  // Fire-and-forget diagnostic; frontendLog never throws.
  void frontendLog('info', 'setupI18n: hint=' + hint + ' stored=' + JSON.stringify(stored)).catch(() => {});

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
  // Log the durable-vs-fallback decision so the intermittent zh-revert symptom
  // is traceable from the single env-manager.log file (per AGENTS.md).
  void frontendLog('debug', 'applyPersistedLocale: durable=' + JSON.stringify(durable) + ' localStorage=' + JSON.stringify(stored)).catch(() => {})
  if (durable && durable !== 'null' && durable !== 'undefined') {
    const authoritative = normalizeLocale(durable)
    try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', authoritative) } catch { /* ignore */ }
    localeStore.set(authoritative)
    void frontendLog('info', 'applyPersistedLocale: authoritative=' + authoritative + ' (durable path taken)').catch(() => {})
  } else {
    // Do NOT seed from localStorage. The portable WebView2 localStorage is
    // unreliable across restarts (an observed real bug: a stale 'zh' persisted
    // in localStorage even after the user switched to 'en'). Seeding from such
    // a stale localStorage value would resurrect the exact 'reverts to zh on
    // restart' symptom the durable IPC store exists to prevent. When there is no
    // durable value, the correct action is to STAY at the default locale (en) so
    // first paint matches and the user re-picks their language in Settings if
    // needed. We still re-echo 'en' into the durable store so the next run sees a
    // populated durable and does not re-enter this branch.
    try { if (typeof localStorage !== 'undefined') localStorage.setItem('locale', defaultLocale) } catch { /* ignore */ }
    localeStore.set(defaultLocale)
    void setSetting('locale', defaultLocale)
    void frontendLog('warn', 'applyPersistedLocale: durable empty; staying at default (' + defaultLocale + ') rather than seeding from unreliable localStorage=' + JSON.stringify(stored)).catch(() => {})
  }
}

export const locales = supportedLocales
export const defaultLanguage = defaultLocale
