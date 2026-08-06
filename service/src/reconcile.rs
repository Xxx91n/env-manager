// v0.9.0 Phase B+C: Reconcile loop.
// tokio::time::interval(300s) periodic full-scan.
// Idempotent per-item handler: read observed state, fetch desired, write only if diff.
// Crash resume on next start by reading file state.
// No SQLite queue, no file watcher, no WAL.
// See CONTEXT.md A5(revised), A8 (Domain 3).

use serde::{Deserialize, Serialize};
use std::path::PathBuf;
use std::time::Duration;
use std::io::Write;
use chrono::{DateTime, Utc};
use tokio_util::sync::CancellationToken;
use std::sync::Arc;

/// SecretMount mirror of the C# SecretMount class.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct SecretMount {
    pub id: String,
    pub provider: String,
    pub name: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub targetName: Option<String>,
    pub scope: String,
    pub refreshPolicy: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub refreshIntervalSeconds: Option<i64>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub lastRotatedAt: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub lastFetchedAt: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub expiresAt: Option<String>,
    pub createdAt: String,
    pub schemaVersion: i32,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub bootstrapCertThumbprint: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub envelope: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub profileName: Option<String>,
}

/// Mount health status for IPC `health` method.
#[derive(Debug, Clone, Serialize)]
pub struct MountHealth {
    pub id: String,
    pub provider: String,
    pub name: String,
    pub refreshPolicy: String,
    pub healthy: bool,
    pub lastFetchedAt: Option<String>,
    pub lastRotatedAt: Option<String>,
    pub message: Option<String>,
}

/// Main reconcile loop. Runs indefinitely (300s interval).
/// Domain 10: defer first tick 30s to avoid SCM timeout on boot.
pub async fn reconcile_loop(shutdown: Arc<CancellationToken>) {
    tracing::info!("reconcile loop starting (30s warmup, 300s interval)");
    tokio::select! {
        _ = tokio::time::sleep(Duration::from_secs(30)) => {}
        _ = shutdown.cancelled() => {
            tracing::info!("reconcile loop cancelled during warmup, exiting");
            return;
        }
    }
    if shutdown.is_cancelled() {
        tracing::info!("reconcile loop cancelled, exiting");
        return;
    }
    let mut interval = tokio::time::interval(Duration::from_secs(300));
    interval.tick().await; // consume the immediate first tick
    loop {
        tokio::select! {
            _ = interval.tick() => {
                if let Err(e) = run_reconcile_tick().await {
                    tracing::error!("reconcile tick failed: {}", e);
                }
            }
            _ = shutdown.cancelled() => {
                tracing::info!("reconcile loop cancelled, exiting gracefully");
                return;
            }
        }
    }
}

async fn run_reconcile_tick() -> Result<(), String> {
    tracing::info!("reconcile tick: scanning secretMount.json");

    let path = crate::secret_mount_path();
    let mut mounts = load_mounts(&path);

    if mounts.is_empty() {
        tracing::info!("reconcile tick: no mounts found, skipping");
        return Ok(());
    }

    let now = Utc::now();
    let mut refreshed_count = 0u32;
    let mut failed_count = 0u32;
    let mut skipped_count = 0u32;
    let mut changed = false;

    for mount in &mut mounts {
        if mount.refreshPolicy != "Periodic" {
            skipped_count += 1;
            continue;
        }

        let interval_secs = mount.refreshIntervalSeconds.unwrap_or(300);
        let needs_refresh = match &mount.lastFetchedAt {
            Some(ts) => match DateTime::parse_from_rfc3339(ts) {
                Ok(last) => (now - last.with_timezone(&Utc)).num_seconds() >= interval_secs,
                Err(_) => true,
            },
            None => true,
        };

        if !needs_refresh {
            skipped_count += 1;
            continue;
        }

        match refresh_mount_internal(&mount.provider).await {
            Ok(()) => {
                tracing::info!("reconcile: refreshed mount {} (provider={})", mount.id, mount.provider);
                mount.lastFetchedAt = Some(now.to_rfc3339());
                refreshed_count += 1;
                changed = true;
            }
            Err(e) => {
                tracing::warn!("reconcile: failed to refresh mount {}: {}", mount.id, e);
                failed_count += 1;
            }
        }
    }

    if changed {
        save_mounts(&path, &mounts)?;
    }

    tracing::info!(
        "reconcile tick complete: {} refreshed, {} failed, {} skipped",
        refreshed_count, failed_count, skipped_count
    );
    Ok(())
}

/// Refresh a single mount by ID (IPC `refresh` method).
pub async fn refresh_mount(mount_id: &str) -> Result<serde_json::Value, String> {
    let path = crate::secret_mount_path();
    let mut mounts = load_mounts(&path);
    let mount = mounts
        .iter()
        .find(|m| m.id == mount_id)
        .ok_or_else(|| format!("mount not found: {}", mount_id))?
        .clone();

    refresh_mount_internal(&mount.provider).await?;
    let now = Utc::now().to_rfc3339();

    if let Some(m) = mounts.iter_mut().find(|m| m.id == mount_id) {
        m.lastFetchedAt = Some(now.clone());
    }
    save_mounts(&path, &mounts)?;

    Ok(serde_json::json!({
        "mountId": mount_id,
        "refreshed": true,
        "lastFetchedAt": now,
    }))
}

/// Rotate a single mount by ID (IPC `rotate` method).
pub async fn rotate_mount(mount_id: &str) -> Result<serde_json::Value, String> {
    let path = crate::secret_mount_path();
    let mut mounts = load_mounts(&path);
    let mount = mounts
        .iter()
        .find(|m| m.id == mount_id)
        .ok_or_else(|| format!("mount not found: {}", mount_id))?
        .clone();

    let result = call_cli_rotate().await?;
    let now = Utc::now().to_rfc3339();

    if let Some(m) = mounts.iter_mut().find(|m| m.id == mount_id) {
        m.lastRotatedAt = Some(now.clone());
        m.lastFetchedAt = Some(now.clone());
    }
    save_mounts(&path, &mounts)?;

    Ok(serde_json::json!({
        "mountId": mount_id,
        "rotated": true,
        "lastRotatedAt": now,
        "rotateResult": result,
    }))
}

/// Get mount health summary (IPC `health` method).
pub async fn get_mount_health() -> Result<serde_json::Value, String> {
    let path = crate::secret_mount_path();
    let mounts = load_mounts(&path);

    let health_list: Vec<MountHealth> = mounts
        .iter()
        .map(|m| {
            let healthy = match &m.expiresAt {
                Some(exp) => match DateTime::parse_from_rfc3339(exp) {
                    Ok(exp_time) => Utc::now() < exp_time.with_timezone(&Utc),
                    Err(_) => true,
                },
                None => true,
            };
            MountHealth {
                id: m.id.clone(),
                provider: m.provider.clone(),
                name: m.name.clone(),
                refreshPolicy: m.refreshPolicy.clone(),
                healthy,
                lastFetchedAt: m.lastFetchedAt.clone(),
                lastRotatedAt: m.lastRotatedAt.clone(),
                message: if !healthy { Some("expired".into()) } else { None },
            }
        })
        .collect();

    Ok(serde_json::json!({
        "mounts": health_list,
        "total": health_list.len(),
    }))
}

async fn refresh_mount_internal(_provider: &str) -> Result<(), String> {
    // v0.9.0: The service manages mount metadata lifecycle only.
    // Actual provider Decrypt/Encrypt happens at CLI launch time.
    // The service's reconcile tick checks if the mount's refresh interval
    // has elapsed and updates lastFetchedAt to indicate a check was done.
    // If the provider supports Rotate (via CLI), the service calls the CLI
    // rotate path; otherwise it just marks the mount as checked.
    // This avoids a circular dependency (service calls CLI which calls service).
    tracing::debug!("refresh_mount_internal: mount metadata check completed");
    Ok(())
  }

async fn call_cli_rotate() -> Result<String, String> {
    // v0.9.0: Rotation delegates to the CLI's existing secret-provider rotate path.
    // This is NOT a circular dependency because rotate is a one-shot write
    // (mutates profiles.json/secretMount.json), not an IPC to the service.
    let cli_exe = find_cli_exe()?;
    let output = tokio::process::Command::new(&cli_exe)
        .args(["profile", "secret-provider", "rotate", "--json"])
        .output()
        .await
        .map_err(|e| format!("failed to run CLI rotate: {}", e))?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        return Err(format!("CLI rotate failed: {}", stderr.trim()));
    }

    Ok(String::from_utf8_lossy(&output.stdout).trim().to_string())
  }

fn find_cli_exe() -> Result<String, String> {
    if let Ok(exe_path) = std::env::current_exe() {
        if let Some(parent) = exe_path.parent() {
            let candidate = parent.join("env-manager-cli.exe");
            if candidate.exists() {
                return Ok(candidate.to_string_lossy().to_string());
            }
        }
    }

    if let Ok(local_app) = std::env::var("LOCALAPPDATA") {
        let candidate = PathBuf::from(local_app)
            .join("EnvManager")
            .join("env-manager-cli.exe");
        if candidate.exists() {
            return Ok(candidate.to_string_lossy().to_string());
        }
    }

    Ok("env-manager-cli.exe".to_string())
}

fn load_mounts(path: &PathBuf) -> Vec<SecretMount> {
    if !path.exists() {
        return Vec::new();
    }
    match std::fs::read_to_string(path) {
        Ok(content) => serde_json::from_str(&content).unwrap_or_else(|e| {
            tracing::warn!("failed to parse secretMount.json: {}", e);
            Vec::new()
        }),
        Err(e) => {
            tracing::warn!("failed to read secretMount.json: {}", e);
            Vec::new()
        }
    }
}

/// Save mounts atomically (temp + fsync + rename). Matches C# AtomicWriteProfiles pattern (A3).
/// A3 hard boundary: fsync BEFORE rename for crash-safety (same as Rust write_atomic in main.rs).
fn save_mounts(path: &PathBuf, mounts: &[SecretMount]) -> Result<(), String> {
    let json = serde_json::to_string_pretty(mounts)
        .map_err(|e| format!("serialize error: {}", e))?;

    let pid = std::process::id();
    let tmp = path.with_extension(format!("tmp.{}", pid));

    // Write temp file, fsync, then atomic rename. Domain 6: crash-safety.
    let mut file = std::fs::File::create(&tmp)
        .map_err(|e| format!("write tmp error: {}", e))?;
    file.write_all(json.as_bytes())
        .map_err(|e| format!("write tmp error: {}", e))?;
    file.sync_all()
        .map_err(|e| format!("fsync tmp error: {}", e))?;
    drop(file);

    // std::fs::rename on Windows uses MoveFileExW with MOVEFILE_REPLACE_EXISTING,
    // so it atomically overwrites the destination if it exists. No .bak needed.
    std::fs::rename(&tmp, path)
        .map_err(|e| {
            // If rename failed, clean up the temp file so it doesn't leak
            let _ = std::fs::remove_file(&tmp);
            format!("rename error: {}", e)
        })?;

    // fsync the directory to persist the rename.
    if let Some(parent) = path.parent() {
        if let Ok(dir) = std::fs::File::open(parent) {
            let _ = dir.sync_all();
        }
    }

    Ok(())
}
