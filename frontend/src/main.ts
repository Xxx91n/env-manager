import App from './App.svelte'
import { setupI18n } from './lib/i18n'

// Global error handler - catches uncaught errors and promise rejections.
// In production under Tauri's custom protocol, these would otherwise be silent.
function logError(label: string, err: unknown): void {
  const msg = err instanceof Error ? `${err.message}\n${err.stack ?? ''}` : String(err)
  // Write to console (visible in WebView2 DevTools)
  console.error(`[env-manager] ${label}:`, msg)
  // Also surface in the app DOM so the user sees something instead of a blank screen
  const root = document.getElementById('app')
  if (root && root.children.length === 0) {
    root.innerHTML = `
      <div style="padding:2rem;font-family:sans-serif;color:#1a1a1a">
        <h2 style="margin:0 0 0.5rem">Env Manager failed to start</h2>
        <p style="color:#666;margin:0 0 1rem">${label}</p>
        <pre style="background:#f4f4f5;padding:1rem;border-radius:8px;overflow:auto;font-size:13px">${msg}</pre>
      </div>`
  }
}

window.addEventListener('error', (e) => {
  logError('Unhandled error', e.error ?? e.message)
})

window.addEventListener('unhandledrejection', (e) => {
  logError('Unhandled promise rejection', e.reason)
})

// Initialize i18n before rendering. Always starts with 'en' synchronously.
try {
  setupI18n()
} catch (err) {
  logError('i18n initialization failed', err)
}

// Mount the Svelte app. The default export must be at the top level.
const app = new App({
  target: document.getElementById('app')!,
})

export default app
