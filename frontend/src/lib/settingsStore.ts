// Persistent settings store backed by tauri-plugin-store.
// localStorage in Tauri WebView2 can be unreliable across restarts because
// the WebView2 user-data folder may not flush before the process exits.
// tauri-plugin-store writes to a JSON file in the app-data directory, which
// is durable and independent of WebView2 lifecycle.
import { Store } from '@tauri-apps/plugin-store'

let store: Store | null = null

async function getStore(): Promise<Store> {
  if (!store) {
    store = await Store.load('gui-settings.json')
  }
  return store
}

export async function getSetting(key: string): Promise<string | null> {
  try {
    const s = await getStore()
    return await s.get<string>(key) ?? null
  } catch {
    // Fallback to localStorage if the store plugin is unavailable (e.g. in tests)
    try {
      return typeof localStorage !== 'undefined' ? localStorage.getItem(key) : null
    } catch {
      return null
    }
  }
}

export async function setSetting(key: string, value: string): Promise<void> {
  try {
    const s = await getStore()
    await s.set(key, value)
    await s.save()
  } catch {
    // Fallback to localStorage
    try {
      if (typeof localStorage !== 'undefined') localStorage.setItem(key, value)
    } catch {
      // ignore
    }
  }
}
