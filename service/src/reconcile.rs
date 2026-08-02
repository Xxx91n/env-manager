// v0.9.0 Phase B+C: Reconcile loop.
// tokio::time::interval(300s) periodic full-scan.
// Idempotent per-item handler: read observed state, fetch desired, write only if diff.
// Crash resume on next start by reading file state.
// No SQLite queue, no file watcher, no WAL.

use std::time::Duration;

/// Main reconcile loop. Runs indefinitely (300s interval).
/// On each tick: load secretMount.json, for each mount with refreshPolicy="Periodic"
/// and refreshIntervalSeconds elapsed since lastFetchedAt, call the provider to refresh.
pub async fn reconcile_loop() {
    let interval = Duration::from_secs(300);
    loop {
        tokio::time::sleep(interval).await;
        if let Err(e) = run_reconcile_tick().await {
            log::error!("reconcile tick failed: {}", e);
        }
    }
}

async fn run_reconcile_tick() -> Result<(), String> {
    // v0.9.0 skeleton: read secretMount.json, filter Periodic mounts,
    // check if refreshIntervalSeconds has elapsed, call provider Decrypt+Encrypt
    // to refresh the cached envelope. For now, just log the tick.
    log::debug!("reconcile tick: scanning secretMount.json");
    // TODO: implement full reconcile logic in v0.9.0 Phase B+C I2.
    Ok(())
}
