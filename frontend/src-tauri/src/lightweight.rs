// Lightweight mode: hide WebView window to minimize memory when idle.
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
static LIGHTWEIGHT_TIMER: std::sync::RwLock<Option<tauri::async_runtime::JoinHandle<()>>> =
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

/// Enter lightweight mode: hide the main WebView window to free memory.
///
/// We use `hide()` instead of `destroy()` because Tauri 2.x exits the
/// process when all windows are destroyed — and the tray icon is bound
/// to the process.  `hide()` on Windows suspends the WebView2 renderer
/// (zero-pixel, not painted) and keeps the process alive with the tray.
/// This matches the cc-switch / clash-verge-rev lightweight approach.
pub fn enter_lightweight(app: &tauri::AppHandle) -> Result<(), String> {
    if !try_transition(STATE_NORMAL, STATE_LIGHTWEIGHT) {
        info!("[lightweight] already in lightweight mode, skipping");
        return Ok(());
    }

    if let Some(window) = app.get_webview_window("main") {
        let _ = window.hide();
        #[cfg(target_os = "windows")]
        {
            let _ = window.set_skip_taskbar(true);
        }
    }

    cancel_lightweight_timer();
    info!("[lightweight] entered lightweight mode — window hidden, service stays alive");
    Ok(())}

/// Exit lightweight mode: un-hide the main WebView window.
pub fn exit_lightweight(app: &tauri::AppHandle) -> Result<(), String> {
    if !try_transition(STATE_LIGHTWEIGHT, STATE_NORMAL) {
        info!("[lightweight] not in lightweight mode, skipping exit");
        return Ok(());
    }

    if let Some(window) = app.get_webview_window("main") {
        #[cfg(target_os = "windows")]
        {
            let _ = window.set_skip_taskbar(false);
        }
        let _ = window.unminimize();
        let _ = window.show();
        let _ = window.set_focus();
    }

    cancel_lightweight_timer();
    info!("[lightweight] exited — window shown");
    Ok(())}

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
        std::thread::sleep(std::time::Duration::from_secs(timeout_minutes * 60));

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
