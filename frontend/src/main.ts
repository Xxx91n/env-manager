import App from './App.svelte'
import { setupI18n, applyPersistedLocale } from './lib/i18n'

// Global error handler - catches uncaught errors and promise rejections.
// In production under Tauri's custom protocol, these would otherwise be silent.
function logError(label: string, err: unknown): void {
  const errorType = err instanceof Error ? err.name : typeof err
  // Production diagnostics intentionally retain only category metadata. CLI
  // errors can include user values, executable paths, and profile names.
  console.error('[env-manager] startup failure', { label, errorType })
  // Also surface a safe generic failure state so the user never sees a blank screen
  const root = document.getElementById('app')
  if (root && root.children.length === 0) {
    const container = document.createElement('div')
    container.style.cssText = 'padding:2rem;font-family:sans-serif;color:#1a1a1a'

    const heading = document.createElement('h2')
    heading.style.cssText = 'margin:0 0 0.5rem'
    heading.textContent = 'Env Manager failed to start'

    const detail = document.createElement('p')
    detail.style.cssText = 'color:#666;margin:0 0 1rem'
    detail.textContent = label

    const error = document.createElement('p')
    error.style.cssText = 'color:#666;margin:0'
    error.textContent = 'Check the application log for diagnostic details.'

    container.append(heading, detail, error)
    root.replaceChildren(container)
  }
}

window.addEventListener('error', (e) => {
  logError('Unhandled error', e.error ?? e.message)
})

window.addEventListener('unhandledrejection', (e) => {
  logError('Unhandled promise rejection', e.reason)
})

// Initialize i18n before rendering. setupI18n sets the default 'en' locale
// synchronously (messages already loaded), then applyPersistedLocale reads the
// durable IPC store (gui-settings.json) and flips the svelte-i18n locale
// store to the user's persisted choice BEFORE the App component mounts. This
// eliminates the first-paint English flash that happened when applyPersistedLocale
// ran later inside App.svelte onMount and re-rendered the whole tree with the
// user's locale. The IPC read is a local file read, typically <50ms.
try {
  setupI18n()
  // Top-level await: Vite target=es2021 supports it. Blocks App mount until
  // the durable locale resolves so the very first Svelte render uses the
  // user's persisted locale rather than the synchronous default 'en'.
  await applyPersistedLocale()
} catch (err) {
  logError('i18n initialization failed', err)
}

// Mount the Svelte app. The default export must be at the top level.
const app = new App({
  target: document.getElementById('app')!,
})

export default app
