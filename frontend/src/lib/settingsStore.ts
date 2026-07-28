// Persistent settings store backed by Rust IPC commands that read/write
// %LOCALAPPDATA%\EnvManager\gui-settings.json directly. Same proven path as
// profiles.json/audit.json. Independent of WebView2 localStorage flush timing.
import { invoke } from '@tauri-apps/api/core'

export async function getSetting(key: string): Promise<string | null> {
  try {
    const val = await invoke('read_gui_setting', { key })
    if (val === null || val === undefined) return localStorageFallback(key)
    return String(val)
  } catch {
    return localStorageFallback(key)
  }
}

export async function setSetting(key: string, value: string): Promise<void> {
  // Also write to localStorage as a sync fallback for instant first render.
  try { if (typeof localStorage !== 'undefined') localStorage.setItem(key, value) } catch { /* ignore */ }
  try { await invoke('write_gui_setting', { key, value }) } catch { /* ignore */ }
}

function localStorageFallback(key: string): string | null {
  try { return typeof localStorage !== 'undefined' ? localStorage.getItem(key) : null }
  catch { return null }
}
