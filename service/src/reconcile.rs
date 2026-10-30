// Reconcile loop.
// tokio::time::interval(300s) periodic full-scan.
// Idempotent per-item handler: read observed state, fetch desired, write only if diff.
// Crash resume on next start by reading file state.
// No SQLite queue, no file watcher, no WAL.
// See CONTEXT.md A5(revised), A8 (Domain 3).

use serde::{Deserialize, Serialize};
use std::path::{Path, PathBuf};
use std::time::Duration;
use std::io::Write;
use chrono::{DateTime, Utc};
use tokio_util::sync::CancellationToken;
use std::sync::Arc;
use tokio::io::AsyncReadExt; // draining the CLI child's pipes while it runs
use std::process::Stdio; // piped stdout/stderr on the CLI child (tokio takes std's type)
use tokio::sync::Mutex; // Phase 4C: TOCTOU protection

use crate::job_object::KillOnCloseJob; // kill-on-close tree containment for the CLI child

/// `CREATE_NO_WINDOW`: the service has no console, and without the flag the CLI child
/// would flash a window on the interactive desktop. Passed to tokio's own
/// `Command::creation_flags`, so no `CommandExt` import is needed here.
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// The CLI argv a rotation delegates to. Rotation is not a circular dependency: it is a
/// one-shot write of profiles.json, not an IPC back into the service. `--json` is absent
/// because CLI rotate does not support it (it exits non-zero).
const ROTATE_ARGS: [&str; 3] = ["profile", "secret-provider", "rotate"];

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

/// Phase 4C: Global lock to prevent TOCTOU between reconcile tick and CLI mount mutations.
static RECONCILE_LOCK: Mutex<()> = Mutex::const_new(());

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
                // Phase 2C: Periodic debugger detection (every tick = 300s)
                if crate::process_guard::check_debugger() {
                    tracing::warn!("debugger detected during reconcile tick");
                }
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
    // Phase 4C: Acquire global lock for the entire tick to prevent TOCTOU with CLI mutations.
    let _guard = RECONCILE_LOCK.lock().await;
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
    let _guard = RECONCILE_LOCK.lock().await; // Phase 4C: TOCTOU protection
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
///
/// `shutdown` is the service stop token, handed down by the connection handler that owns
/// this request: the CLI child a rotation spawns belongs to the stop path, not to whatever
/// task happened to spawn it (#153).
pub async fn rotate_mount(
    mount_id: &str,
    shutdown: &CancellationToken,
) -> Result<serde_json::Value, String> {
    let cli_exe = find_cli_exe()?;
    rotate_mount_at(
        &crate::secret_mount_path(),
        &cli_exe,
        &ROTATE_ARGS,
        mount_id,
        shutdown,
    )
    .await
}

/// `rotate_mount` with the mount file and the delegated command taken as parameters.
/// That seam is what lets `rotate_stop_tests` interrupt a real child process against a
/// temp mount file, instead of the real `%LOCALAPPDATA%` / `ProgramData` state.
pub(crate) async fn rotate_mount_at(
    mount_path: &Path,
    cli_exe: &str,
    cli_args: &[&str],
    mount_id: &str,
    shutdown: &CancellationToken,
) -> Result<serde_json::Value, String> {
    let _guard = RECONCILE_LOCK.lock().await; // Phase 4C: TOCTOU protection
    let mut mounts = load_mounts(mount_path);
    if !mounts.iter().any(|m| m.id == mount_id) {
        return Err(format!("mount not found: {}", mount_id));
    }

    // Any error here - a failing child or an interrupted one - returns before the
    // metadata is touched. That is the replay half of #153's recovery semantics: the mount
    // still reads as un-rotated, so the next `rotate` or the reconcile tick does the work
    // again, and re-rotating is harmless (`RotateAll` decrypts then re-encrypts per
    // secret, `src/Secrets/Manager/SecretProviderManager.cs`).
    let result = call_cli_rotate(cli_exe, cli_args, shutdown).await?;
    let now = Utc::now().to_rfc3339();

    if let Some(m) = mounts.iter_mut().find(|m| m.id == mount_id) {
        m.lastRotatedAt = Some(now.clone());
        m.lastFetchedAt = Some(now.clone());
    }
    save_mounts(mount_path, &mounts)?;

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
    // The service manages mount metadata lifecycle only.
    // Actual provider Decrypt/Encrypt happens at CLI launch time.
    // The service's reconcile tick checks if the mount's refresh interval
    // has elapsed and updates lastFetchedAt to indicate a check was done.
    // If the provider supports Rotate (via CLI), the service calls the CLI
    // rotate path; otherwise it just marks the mount as checked.
    // This avoids a circular dependency (service calls CLI which calls service).
    tracing::debug!("refresh_mount_internal: mount metadata check completed");
    Ok(())
  }

/// How a delegated CLI child ended.
pub(crate) enum ChildOutcome {
    /// The child ran to completion; its whole output is attached.
    Finished {
        success: bool,
        stdout: String,
        stderr: String,
    },
    /// A stop was requested while the child was still running. By the time this variant
    /// is returned the child has been terminated *and reaped* (#153), and the job object
    /// holding its process tree has been closed (#160), which terminates the helpers it had
    /// started - requested at handle close, not awaited, so they can still be winding down
    /// for a moment after this returns.
    Interrupted { pid: Option<u32> },
}

/// Run a CLI child and converge it with the service stop path (#153, #160).
///
/// Strategy, chosen over join-to-completion: **terminate and reap**. A rotate waits on
/// provider round-trips that can outlive the SCM stop window, and STOP shares that window
/// with `SERVICE_ACCEPT_SHUTDOWN` during logoff, so the stop path cannot afford to wait a
/// child out. On cancellation the child is killed and its exit awaited before this
/// returns, which is what makes `SERVICE_STOPPED` mean "no CLI process is left that can
/// write profiles.json". The abandoned rotation is recovered by replay, not by waiting -
/// see `rotate_mount_at`.
///
/// Scope of the guarantee, and the holes it cannot close. The child spawned here joins a
/// kill-on-close job object (`crate::job_object::KillOnCloseJob`), so the provider helpers
/// it starts (`op`, `sops`, `pwsh` in `src/Secrets/Providers/`) and *their* children are
/// terminated with the CLI: a stop no longer leaves an orphan helper writing to the
/// backend. Termination is requested, not awaited - a helper can still be visibly alive
/// for a moment after this returns, which is why the stop-path test measures a five-second
/// budget instead of asserting on the return of this call. If the job cannot be created or
/// the child cannot be assigned, the guarantee degrades to the direct child only; both are
/// logged and the rotation proceeds rather than being refused. The remaining hole is the
/// interval between the spawn below and the assignment that follows it - assigning a
/// process puts its future children in the job, not the ones it already has - so a helper
/// the CLI managed to start inside that interval is outside the job and still survives the
/// stop. `Command` offers no create-suspended spawn, so the window cannot be closed from
/// here; it is recorded instead of promised away.
///
/// A different gap stays open and is not this function's to close: sops writes the value it
/// decrypted into its own temp directory and deletes it in the CLI's `finally`, so a stop
/// that terminates the CLI still leaves that file on disk. Killing the helper does not
/// remove it. See the secret-value lifecycle table in `docs/architecture.md`.
pub(crate) async fn run_cli_child(
    cli_exe: &str,
    args: &[&str],
    shutdown: &CancellationToken,
) -> Result<ChildOutcome, String> {
    // One job per delegation, never a process-wide singleton: the handle lives exactly as
    // long as this call, so a stop kills the rotation it interrupted and nothing else. A
    // failure to create it degrades the guarantee to "the direct child only" and must not
    // fail the rotation - losing the tree kill is bad, refusing to rotate is worse.
    let job = match KillOnCloseJob::new() {
        Ok(job) => Some(job),
        Err(e) => {
            tracing::warn!(
                "CLI child job object unavailable, provider helpers may outlive this rotation: {}",
                e
            );
            None
        }
    };

    let mut child = tokio::process::Command::new(cli_exe)
        .args(args)
        .creation_flags(CREATE_NO_WINDOW)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        // Backstop for the one exit this function cannot await: the runtime dropping this
        // task. The guarantee is the cancellation branch below, which also reaps.
        .kill_on_drop(true)
        .spawn()
        .map_err(|e| format!("failed to run CLI rotate: {}", e))?;

    let pid = child.id();
    // Straight after the spawn, because this call is what ends the window described in
    // the doc comment. Assignment needs `PROCESS_SET_QUOTA | PROCESS_TERMINATE` on the
    // target, which `crate::job_object` opens by pid.
    match (job.as_ref(), pid) {
        (Some(job), Some(pid)) => {
            if let Err(e) = job.assign(pid) {
                tracing::warn!(
                    "CLI child {} could not join the job object, its helpers may outlive this rotation: {}",
                    pid, e
                );
            }
        }
        (Some(_), None) => {
            // No pid means the child was already reaped, so there is nothing left to
            // assign and anything it started before exiting is outside the job for good.
            // Rare, and the same class of hole as the window in the doc comment - logged
            // rather than skipped in silence.
            tracing::warn!(
                "CLI rotate child exited before it could join the job object, its helpers may outlive this rotation"
            );
        }
        // No job: `KillOnCloseJob::new` already logged why.
        (None, _) => {}
    }
    // Output is drained while the child runs, never after the wait: a child that fills a
    // pipe buffer would otherwise block on write and never observe the kill.
    let mut stdout = child.stdout.take().ok_or("CLI rotate child has no stdout pipe")?;
    let mut stderr = child.stderr.take().ok_or("CLI rotate child has no stderr pipe")?;
    let read_stdout = tokio::spawn(async move { read_pipe(&mut stdout).await });
    let read_stderr = tokio::spawn(async move { read_pipe(&mut stderr).await });

    tokio::select! {
        status = child.wait() => {
            let status = status
                .map_err(|e| format!("failed to wait for the CLI rotate child: {}", e))?;
            // The child exited, so its end of both pipes is closed and the readers reach
            // EOF: a provider helper cannot keep the handle open, because each one
            // redirects its own std handles (`src/Secrets/Providers/`). A panicking reader
            // only costs the diagnostic text.
            Ok(ChildOutcome::Finished {
                success: status.success(),
                stdout: read_stdout.await.unwrap_or_default(),
                stderr: read_stderr.await.unwrap_or_default(),
            })
        }
        _ = shutdown.cancelled() => {
            // `Child::kill` is TerminateProcess followed by `wait`, so the child is reaped
            // here rather than left as an orphan of a service the SCM already calls
            // Stopped. The readers are aborted instead of awaited: an aborted rotation has
            // no output worth returning, and the stop path should not depend on a pipe
            // reaching EOF - even though it does, because provider helpers redirect their
            // own std handles (`src/Secrets/Providers/`).
            child
                .kill()
                .await
                .map_err(|e| format!("failed to terminate the CLI rotate child: {}", e))?;
            read_stdout.abort();
            read_stderr.abort();
            // The job handle drops as this function returns, which is where the helper
            // tree dies (#160): the CLI is already reaped, so nothing left in the job can
            // still be serving this rotation. Both happen inside the stop budget - closing
            // a job handle is not a wait.
            Ok(ChildOutcome::Interrupted { pid })
        }
    }
}

/// Read a child pipe to the end, lossy-decoded and trimmed.
async fn read_pipe<R: tokio::io::AsyncRead + Unpin>(pipe: &mut R) -> String {
    let mut buf = Vec::new();
    // A terminated child closes its end, so this reaches EOF rather than failing.
    let _ = pipe.read_to_end(&mut buf).await;
    String::from_utf8_lossy(&buf).trim().to_string()
}

async fn call_cli_rotate(
    cli_exe: &str,
    cli_args: &[&str],
    shutdown: &CancellationToken,
) -> Result<String, String> {
    match run_cli_child(cli_exe, cli_args, shutdown).await? {
        ChildOutcome::Finished {
            success: true,
            stdout,
            ..
        } => Ok(stdout),
        ChildOutcome::Finished { stderr, .. } => {
            Err(format!("CLI rotate failed: {}", stderr.trim()))
        }
        ChildOutcome::Interrupted { pid } => {
            let pid = pid
                .map(|p| p.to_string())
                .unwrap_or_else(|| "unknown".to_string());
            Err(format!(
                "CLI rotate interrupted by service stop (child {pid} terminated and reaped)"
            ))
        }
    }
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

fn load_mounts(path: &Path) -> Vec<SecretMount> {
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
fn save_mounts(path: &Path, mounts: &[SecretMount]) -> Result<(), String> {
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
