// Lightweight mode: destroy WebView window to minimize memory when idle.
// Reference: cc-switch src-tauri/src/lightweight.rs + clash-verge-rev lightweight.rs
// State machine: Normal ↔ InLightweight, guarded by AtomicU8 CAS.

use std::sync::atomic::{AtomicU8, Ordering};
use tauri::Manager;
use tracing::{info, warn};

/// 0 = Normal, 1 = InLightweight
const STATE_NORMAL: u8 = 0;
const STATE_LIGHTWEIGHT: u8 = 1;

static LIGHTWEIGHT_STATE: AtomicU8 = AtomicU8::new(STATE_NORMAL);

/// Auto-lightweight timer handle (tokio task). None when no timer is running.
/// Stored as a string PID-like token so we can abort it.
static LIGHTWEIGHT_TIMER: std::sync::RwLock<Option<tokio::task::JoinHandle<()>>> =
    std::sync::RwLock::new(None);

pub fn is_in_lightweight_mode() -> bool {
    LIGHTWEIGHT_STATE.load(Ordering::Acquire) == STATE_LIGHTWEIGHT
}

/// Atomically transition from one state to another. Returns true on success.
fn try_transition(from: u8, to: u8) -> bool {
    LIGHTWEIGHT_STATE
        .compare_exchange(from, to, Ordering::AcqRel, Ordering::Acquire)
        .is_ok()
}

/// Enter lightweight mode: destroy the main WebView window to free memory.
/// The service process (if running) stays alive — this only destroys the GUI.
pub fn enter_lightweight(app: &tauri::AppHandle) -> Result<(), String> {
    if !try_transition(STATE_NORMAL, STATE_LIGHTWEIGHT) {
        info!("[lightweight] already in lightweight mode, skipping");
        return Ok(());
    }

    // Save window state before destroying (for future restore if needed)
    if let Some(window) = app.get_webview_window("main") {
        #[cfg(target_os = "windows")]
        {
            let _ = window.set_skip_taskbar(true);
        }
        window
            .destroy()
            .map_err(|e| format!("destroy window failed: {e}"))?;
    }

    cancel_lightweight_timer();
    info!("[lightweight] entered lightweight mode — window destroyed, service stays alive");
    Ok(())
}

/// Exit lightweight mode: rebuild the main WebView window from config.
pub fn exit_lightweight(app: &tauri::AppHandle) -> Result<(), String> {
    if !try_transition(STATE_LIGHTWEIGHT, STATE_NORMAL) {
        info!("[lightweight] not in lightweight mode, skipping exit");
        return Ok(());
    }

    // If window still exists (edge case), just show it
    if let Some(window) = app.get_webview_window("main") {
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
        #[cfg(target_os = "windows")]
        {
            let _ = window.set_skip_taskbar(false);
        }
        info!("[lightweight] exited — window shown");
        return Ok(());
    }

    // Rebuild window from config
    let window_config = app
        .config()
        .app
        .windows
        .iter()
        .find(|w| w.label == "main")
        .ok_or("main window config not found")?;

    let new_window = tauri::webview::WebviewWindowBuilder::from_config(app, window_config)
        .map_err(|e| format!("rebuild window config failed: {e}"))?
        .build()
        .map_err(|e| format!("rebuild window build failed: {e}"))?;

    let _ = new_window.unminimize();
    let _ = new_window.show();
    let _ = new_window.set_focus();
    #[cfg(target_os = "windows")]
    {
        let _ = new_window.set_skip_taskbar(false);
    }

    cancel_lightweight_timer();
    info!("[lightweight] exited — window rebuilt from config");
    Ok(())
}

/// Start the auto-lightweight countdown timer.
/// After `timeout_minutes` the window is destroyed and the app enters
/// lightweight mode. The timer can be cancelled by user focus or activity.
pub fn start_lightweight_timer(app: tauri::AppHandle, timeout_minutes: u64) {
    if is_in_lightweight_mode() {
        return;
    }

    // Cancel any existing timer first
    cancel_lightweight_timer();

    if timeout_minutes == 0 {
        return; // 0 = disabled
    }

    let handle = tauri::async_runtime::spawn(async move {
        tokio::time::sleep(tokio::time::Duration::from_secs(timeout_minutes * 60)).await;

        // Timer fired — enter lightweight mode
        if let Err(e) = enter_lightweight(&app) {
            warn!("[lightweight] auto-enter failed: {}", e);
        }
    });

    if let Ok(mut guard) = LIGHTWEIGHT_TIMER.write() {
        *guard = Some(handle);
    }
    info!("[lightweight] auto timer started: {} minutes", timeout_minutes);
}

/// Cancel any pending auto-lightweight timer.
pub fn cancel_lightweight_timer() {
    if let Ok(mut guard) = LIGHTWEIGHT_TIMER.write() {
        if let Some(handle) = guard.take() {
            handle.abort();
            info!("[lightweight] auto timer cancelled");
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_state_machine_transitions() {
        LIGHTWEIGHT_STATE.store(STATE_NORMAL, Ordering::Release);
        assert!(try_transition(STATE_NORMAL, STATE_LIGHTWEIGHT));
        assert!(is_in_lightweight_mode());
        assert!(!try_transition(STATE_NORMAL, STATE_LIGHTWEIGHT)); // already in
        assert!(try_transition(STATE_LIGHTWEIGHT, STATE_NORMAL));
        assert!(!is_in_lightweight_mode());
    }

    #[test]
    fn test_concurrent_cas_guards() {
        LIGHTWEIGHT_STATE.store(STATE_NORMAL, Ordering::Release);
        // First transition succeeds
        assert!(try_transition(STATE_NORMAL, STATE_LIGHTWEIGHT));
        // Concurrent second attempt fails (already transitioned)
        assert!(!try_transition(STATE_NORMAL, STATE_LIGHTWEIGHT));
        // Reset
        assert!(try_transition(STATE_LIGHTWEIGHT, STATE_NORMAL));
    }
}
