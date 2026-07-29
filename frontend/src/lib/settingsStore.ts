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


/**
 * Emit a frontend log line into the same env-manager.log file used by the Rust
 * tauri-plugin-log target. Single-file log keeps the i18n race (the tray  * flicker + restart reverts to zh symptom) diagnosable without adding a new  * npm dependency. The Rust side `frontend_log` command validates the level  * string and writes via the existing `log` crate so all log lines funnel  * through one rotation/retention policy. Always fire-and-forget; a logging  * IPC failure MUST NOT raise in the caller (i18n init / settings dialog).
 */
export async function frontendLog(
  level: 'info' | 'warn' | 'error' | 'debug',
  message: string,
): Promise<void> {
  try {
    // Sanity-bound at the IPC boundary: cap message length to prevent a     // runaway long string from bloating the single log file. CLI values     // and secrets are already never stringified into log payloads here.
    const bounded = message.length > 2048 ? message.slice(0, 2048) + '...' : message;
    await invoke('frontend_log', { level, message: bounded });
  } catch {
    /* best-effort: never throw from a logger */
  }
}
