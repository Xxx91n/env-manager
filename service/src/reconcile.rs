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
use std::sync::atomic::{AtomicUsize, Ordering}; // in-flight delegations, read under the lock (#161)
use tokio::io::AsyncReadExt; // draining the CLI child's pipes while it runs
use std::process::Stdio; // piped stdout/stderr on the CLI child (tokio takes std's type)
use tokio::sync::Mutex; // Phase 4C: TOCTOU protection
#[cfg(test)]
use tokio::sync::oneshot; // phase-entry latch for post-exit pipe drain tests (#205)

use crate::job_object::KillOnCloseJob; // kill-on-close tree containment for the CLI child
use crate::sops_scratch::{SopsScratch, SCRATCH_ENV}; // service-owned sops working root (#177)

/// `CREATE_NO_WINDOW`: the service has no console, and without the flag the CLI child
/// would flash a window on the interactive desktop. Passed to tokio's own
/// `Command::creation_flags`, so no `CommandExt` import is needed here.
const CREATE_NO_WINDOW: u32 = 0x0800_0000;

/// How long the stop path waits for the killed child's stderr before giving up on it.
/// Long enough for a pipe that closes the moment its writer is reaped, short enough that a
/// helper holding the handle open cannot extend the stop.
const STDERR_DRAIN_ON_STOP: Duration = Duration::from_millis(200);

/// How long the *exit* path waits for the two pipe readers after the child itself has gone
/// (#192). Same shape as `STDERR_DRAIN_ON_STOP` - a bounded `timeout` over a `JoinHandle`,
/// then abort - but applied to both readers against one shared deadline, because the exit
/// branch used to `await` them without any bound at all. It relied on "a provider helper
/// cannot keep the handle open, because each one redirects its own std handles", an assumption
/// no test pinned: one helper that inherits stdout/stderr (`UseShellExecute`, a future adapter,
/// anything the CLI spawns without redirecting) leaves the CLI gone and both pipes open, so
/// `run_cli_child` never returned and `ROTATIONS_IN_FLIGHT` stayed at 1 forever - the tick
/// skipped every 300s and `refresh` answered "a delegated rotation is in flight" forever.
///
/// 200ms is the same size as the stop drain: long enough for a normal pipe to close in the
/// instant after `wait()` reaps the child, short enough that a held handle cannot turn a
/// completed rotation into a permanent one. It does not widen the stop window - the budget
/// lives only in the `child.wait()` branch of the outer `select!`, a cancellation still
/// wins its own branch immediately, and *inside that branch* the drain is also raced
/// against `shutdown.cancelled()`, so a stop that lands mid-drain ends it at once instead
/// of costing the remaining 200ms. `SHUTDOWN_DRAIN + RUNTIME_DRAIN < STOP_WAIT_HINT_MS`
/// is unchanged; the accept-loop join already allows a `SHUTDOWN_DRAIN` window.
pub(crate) const PIPE_DRAIN_ON_EXIT: Duration = Duration::from_millis(200);

/// How long a delegated rotation may stay inside its CLI child before the service treats it
/// as hung, terminates it and reaps it (#161).
///
/// This is a liveness backstop, not a rotation SLA, and that distinction is the decision this
/// ticket asked for rather than a constant. A rotation is decrypt-then-re-encrypt per secret,
/// so its cost is linear in the mount's secret count and dominated by process spawns (sops
/// starts four processes per secret, ~10s/secret cold on the prototype machine, so 50 secrets
/// already pass two minutes). A fixed *performance* limit was therefore rejected: it would
/// fail healthy rotations. This number is instead a fixed ceiling, deliberately not derived from
/// the secret count: a delegated rotate carries no mount id and covers every profile, so N is
/// only estimable, and an estimate that is wrong by a factor kills healthy work. What that buys
/// and what it costs, one sentence each: no mount this product targets (a few dozen secrets)
/// comes near 900s, and a mount large enough to reach it - roughly 90 secrets at the cold-start
/// estimate above - is failed by the ceiling rather than by a hang. That false positive is the
/// recorded cost of having a ceiling at all; the alternative is a child that can hold the
/// rotation, and everything queued behind it, forever.
///
/// It does not widen the stop window: the stop branch of the same `select!` still fires the
/// moment the token is cancelled, so `SHUTDOWN_DRAIN` + `RUNTIME_DRAIN` stay inside
/// `STOP_WAIT_HINT_MS`. What it buys is that a wedged child - a provider round-trip that never
/// returns, a DPAPI prompt nobody can answer, a CLI deadlock - ends the rotation instead of
/// holding it, and the reconcile loop behind it, forever.
pub(crate) const ROTATE_HANG_BUDGET: Duration = Duration::from_secs(15 * 60);

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
///
/// Scope (#161): the lock covers the mount-file read and the write-back, never the wait for a
/// delegated CLI child. `rotate_mount_at` releases it around that wait because the reconcile
/// tick (`run_reconcile_tick`, 300s interval) queues on this same lock: held across a child
/// that never returns, one rotation starved every later `rotate`/`refresh` and the tick
/// itself for as long as the child lived, which is the defect this ticket closes.
///
/// What the lock still buys is that a write-back cannot interleave with another mount-file
/// writer. What it no longer buys is exclusivity between two rotations of the same mount;
/// that is accepted, because a duplicate rotation is harmless by design - `RotateAll`
/// decrypts then re-encrypts per secret, and the CLI replaces the mount file atomically. The
/// other two writers - `run_reconcile_tick` and `refresh_mount` - would otherwise save a
/// pre-rotation snapshot over the envelopes the child just rewrote, so both consult
/// `ROTATIONS_IN_FLIGHT` under this lock and stand down while a delegation runs.
static RECONCILE_LOCK: Mutex<()> = Mutex::const_new(());

/// How many delegated CLI rotations are running in this service right now (#161).
///
/// It exists because releasing `RECONCILE_LOCK` for the child wait let the other two writers of
/// the mount file - `run_reconcile_tick` and `refresh_mount` - run *while* a rotation's child is
/// rewriting that file in place. Both save a whole snapshot they loaded before the child's
/// writes, which would restore the pre-rotation envelopes; the lock used to exclude them only
/// by keeping them blocked for the entire delegation. The count is taken under the lock (see
/// `rotate_mount_at`), so a reader holding the lock can never see it *rise*: zero means no
/// child can be writing, and a value that falls to zero under the reader means that rotation's
/// child is already reaped and its write-back done - the guard outlives the write-back, which
/// needs the lock itself.
static ROTATIONS_IN_FLIGHT: AtomicUsize = AtomicUsize::new(0);

/// RAII marker for one running delegation; see `ROTATIONS_IN_FLIGHT`. Dropped when the
/// rotation returns - after the child is reaped and the write-back is done - so a writer that
/// sees zero cannot race a child that is still writing.
struct RotationInFlight;

impl RotationInFlight {
    fn enter() -> Self {
        ROTATIONS_IN_FLIGHT.fetch_add(1, Ordering::SeqCst);
        Self
    }
}

impl Drop for RotationInFlight {
    fn drop(&mut self) {
        ROTATIONS_IN_FLIGHT.fetch_sub(1, Ordering::SeqCst);
    }
}

/// Whether a delegated rotation currently owns the mount file's envelopes (#161). Production
/// callers read it while holding `RECONCILE_LOCK`; tests also poll it as a readiness / drain
/// snapshot (#192, #193), keeping `ROTATIONS_IN_FLIGHT` itself private with no test-only
/// accessor on the static.
pub(crate) fn rotation_in_flight() -> bool {
    ROTATIONS_IN_FLIGHT.load(Ordering::SeqCst) > 0
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

/// Production entry called by `reconcile_loop`: binds `run_reconcile_tick_at` to
/// `crate::secret_mount_path()`. That one-line binding stays covered by inspection until
/// `reconcile_loop` itself takes warmup and mount-path parameters (#193).
async fn run_reconcile_tick() -> Result<(), String> {
    run_reconcile_tick_at(&crate::secret_mount_path()).await
}

/// `run_reconcile_tick` with the mount file taken as a parameter (#193).
///
/// Same seam shape as `rotate_mount_at`: production resolves `crate::secret_mount_path()`,
/// while tests pass a temp file under `%TEMP%` so exercising the tick never touches real
/// `%LOCALAPPDATA%` or `ProgramData` state.
pub(crate) async fn run_reconcile_tick_at(mount_path: &Path) -> Result<(), String> {
    // Phase 4C: Acquire global lock for the entire tick to prevent TOCTOU with CLI mutations.
    let _guard = RECONCILE_LOCK.lock().await;
    // A delegated rotation owns the envelopes in the mount file while its child runs (#161):
    // the CLI rewrites them in place, and the snapshot this tick would save predates that.
    // Saving it would restore the pre-rotation envelopes the rotation is about to record, so
    // the tick stands down for this interval instead. Nothing is lost by doing so - the tick
    // only stamps `lastFetchedAt`, and the next one does that - and the rotation cannot start
    // while this tick holds the lock, so the answer read here cannot go stale underneath it.
    if rotation_in_flight() {
        tracing::info!("reconcile tick: a delegated rotation is in flight, skipping this tick");
        return Ok(());
    }
    tracing::info!("reconcile tick: scanning secretMount.json");

    let mut mounts = load_mounts(mount_path);

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
        save_mounts(mount_path, &mounts)?;
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
    refresh_mount_at(&path, mount_id).await
}

/// `refresh_mount` with the mount file taken as a parameter - the same seam
/// `rotate_mount_at` already has. The in-flight gate is read before any file is touched,
/// so a test can pin "stands down, then recovers" on a `%TEMP%` fixture instead of the
/// real `%LOCALAPPDATA%` / `ProgramData` state (#192). Production behaviour is unchanged.
pub(crate) async fn refresh_mount_at(
    path: &Path,
    mount_id: &str,
) -> Result<serde_json::Value, String> {
    let _guard = RECONCILE_LOCK.lock().await; // Phase 4C: TOCTOU protection
    // Same reason as the reconcile tick (#161): while a rotation's child is rewriting the
    // mount file, the snapshot loaded below predates those writes and saving it would undo
    // them. Failing fast also beats what the old lock did here - block until the delegation
    // finished, which for a wedged child meant until the process died. Both this call and
    // `run_reconcile_tick` consult the same `rotation_in_flight()` gate under this lock,
    // so pinning the gate here pins what the tick reads too.
    if rotation_in_flight() {
        return Err("a delegated rotation is in flight; retry once it completes".to_string());
    }
    let mut mounts = load_mounts(path);
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
    save_mounts(path, &mounts)?;

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
        ROTATE_HANG_BUDGET,
    )
    .await
}

/// `rotate_mount` with the mount file and the delegated command taken as parameters.
/// That seam is what lets `rotate_stop_tests` interrupt a real child process against a
/// temp mount file, instead of the real `%LOCALAPPDATA%` / `ProgramData` state.
///
/// The lock is taken twice - around the mount check and around the write-back - and never
/// held while the child runs (#161). See `RECONCILE_LOCK` for what that trades away.
///
/// `hang_budget` is passed through to `run_cli_child` and is a parameter for the same reason
/// it is there: the tests inject a short one, production passes `ROTATE_HANG_BUDGET`. The
/// post-exit pipe drain is fixed here at `PIPE_DRAIN_ON_EXIT` and only injectable through
/// `rotate_mount_at_with_pipe_drain` (#192, #205).
pub(crate) async fn rotate_mount_at(
    mount_path: &Path,
    cli_exe: &str,
    cli_args: &[&str],
    mount_id: &str,
    shutdown: &CancellationToken,
    hang_budget: Duration,
) -> Result<serde_json::Value, String> {
    rotate_mount_at_with_pipe_drain(
        mount_path,
        cli_exe,
        cli_args,
        mount_id,
        shutdown,
        hang_budget,
        PIPE_DRAIN_ON_EXIT,
    )
    .await
}

/// `rotate_mount_at` with the post-exit pipe budget injected (#192, #205); see
/// `run_cli_child`. Production always goes through `rotate_mount_at` so every delegation gets
/// the same bound; the held-handle tests pass a shortened `Duration` or a `#[cfg(test)]`
/// `LatchedPipeDrain` (#205). Every existing stop / hang / write-back fixture stays on the
/// production default with no assertion changed.
pub(crate) async fn rotate_mount_at_with_pipe_drain(
    mount_path: &Path,
    cli_exe: &str,
    cli_args: &[&str],
    mount_id: &str,
    shutdown: &CancellationToken,
    hang_budget: Duration,
    pipe_drain: impl Into<PipeDrain>,
) -> Result<serde_json::Value, String> {
    let pipe_drain: PipeDrain = pipe_drain.into();
    // Check-then-delegate, with the lock released for the delegation itself (#161). The child
    // wait is the only long part of a rotation, and holding the lock across it made one slow
    // or wedged child stall every later `rotate`/`refresh` and the 300s reconcile tick, which
    // queues on this same lock. Two windows open and both are handled: the mount can be
    // deleted while the child runs (the write-back re-reads and skips it), and a second
    // rotation of the same mount can start (harmless - `RotateAll` decrypts then re-encrypts
    // per secret, and the CLI replaces the mount file atomically).
    // The in-flight marker is taken *inside* the locked block (#161): a writer holding the lock
    // that reads zero must not be able to race a child which is about to start, so the count
    // may only go up while the lock is held. It is released when this function returns, after
    // the child is reaped and the write-back is done.
    let _in_flight;
    {
        let _guard = RECONCILE_LOCK.lock().await; // Phase 4C: TOCTOU protection
        let mounts = load_mounts(mount_path);
        if !mounts.iter().any(|m| m.id == mount_id) {
            return Err(format!("mount not found: {}", mount_id));
        }
        _in_flight = RotationInFlight::enter();
    }

    // Any error here - a failing child, an interrupted one, or one that outlived its hang
    // budget - returns before the metadata is touched. That is the replay half of #153's
    // recovery semantics: the mount still reads as un-rotated, so the next `rotate` or the
    // reconcile tick does the work again, and re-rotating is harmless (`RotateAll` decrypts
    // then re-encrypts per secret, `src/Secrets/Manager/SecretProviderManager.cs`).
    let result = call_cli_rotate(cli_exe, cli_args, shutdown, hang_budget, pipe_drain).await?;
    let now = Utc::now().to_rfc3339();

    // Re-acquired for the write-back only, so the read below and the save it feeds cannot be
    // interleaved with another mount-file writer.
    let _guard = RECONCILE_LOCK.lock().await;

    // Re-read what the child left on disk instead of reusing the snapshot from before the
    // call: a delegated rotation writes each rotated envelope back into the mount that owns
    // it (the profile only holds `mount:<id>`), so `mounts` is stale by now and saving it
    // would restore the pre-rotation envelopes - silently undoing the rotation this call is
    // about to record. Only the two metadata stamps are ours to write.
    //
    // The strict read is what keeps "the mount is gone" apart from "the child left a file
    // this build cannot read": the plain loader answers an empty list for both, and the
    // second one must never be reported as a completed rotation.
    let mut rotated = load_mounts_strict(mount_path)?;
    let stamped = match rotated.iter_mut().find(|m| m.id == mount_id) {
        Some(m) => {
            m.lastRotatedAt = Some(now.clone());
            m.lastFetchedAt = Some(now.clone());
            true
        }
        None => {
            // The mount vanished while the rotation ran. Look it up again and skip: the
            // rotation of its secret already happened, and rewriting a file that no longer
            // names the mount would resurrect a list that lost it. Never fail a completed
            // rotation over a mount nobody can act on.
            tracing::warn!(
                "mount {} disappeared during the rotation; leaving the mount file alone",
                mount_id
            );
            false
        }
    };
    if stamped {
        save_mounts(mount_path, &rotated)?;
    }

    Ok(serde_json::json!({
        "mountId": mount_id,
        "rotated": true,
        "lastRotatedAt": stamped.then(|| now.clone()),
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

/// Post-exit pipe drain budget paired with a two-way test handshake right before
/// `drain_pipes_on_exit` starts its deadline (#205).
///
/// Only compiled under `cfg(test)` (#163): `entered` fires once `child.wait()` has won the
/// outer `select!` and `drain_pipes_on_exit` has begun, `release` holds the drain before its
/// deadline starts until the test finishes its mid-drain setup or stand-down probes, and
/// `started` optionally acknowledges the first `Poll::Pending` inside `timeout_at` so a stop
/// test proves cancellation interrupts an active drain wait without relying on `yield_now()`.
/// Assertions remain strictly on external outcomes.
#[cfg(test)]
pub(crate) struct LatchedPipeDrain {
    pub(crate) budget: Duration,
    pub(crate) entered: oneshot::Sender<()>,
    pub(crate) release: oneshot::Receiver<()>,
    pub(crate) started: Option<oneshot::Sender<()>>,
}

#[cfg(test)]
type PipeDrainLatch = (
    oneshot::Sender<()>,
    oneshot::Receiver<()>,
    Option<oneshot::Sender<()>>,
);

/// Post-exit pipe drain configuration passed into `run_cli_child` and
/// `rotate_mount_at_with_pipe_drain` (#192, #205).
///
/// Fields stay private so callers construct it only via `From<Duration>` (production and
/// unlatched tests) or `#[cfg(test)] From<LatchedPipeDrain>` (mid-drain race tests). In
/// non-test builds this compiles to a single `Duration` with zero extra state or runtime
/// cost; under `cfg(test)` it optionally carries the two-way handshake consumed inside
/// `drain_pipes_on_exit` before starting the deadline, while a child stopped or hung in
/// another `select!` arm drops it without firing `entered`.
pub(crate) struct PipeDrain {
    budget: Duration,
    #[cfg(test)]
    latch: Option<PipeDrainLatch>,
}

impl From<Duration> for PipeDrain {
    /// Wrap a plain drain budget with no test latch attached.
    fn from(budget: Duration) -> Self {
        Self {
            budget,
            #[cfg(test)]
            latch: None,
        }
    }
}

#[cfg(test)]
impl From<LatchedPipeDrain> for PipeDrain {
    /// Arm the drain budget with a test-only two-way phase-entry gate (#205).
    fn from(drain: LatchedPipeDrain) -> Self {
        Self {
            budget: drain.budget,
            latch: Some((drain.entered, drain.release, drain.started)),
        }
    }
}

/// How a delegated CLI child ended.
#[derive(Debug)]
pub(crate) enum ChildOutcome {
    /// The child ran to completion; whatever its pipes yielded before the post-exit drain
    /// ended is attached.
    ///
    /// `output_complete` is false when a reader did not reach EOF inside
    /// `PIPE_DRAIN_ON_EXIT` (#192): a process that inherited the CLI's std handles kept
    /// one open after the CLI exited. **The exit status, not the completeness of stdout,
    /// decides whether this rotation succeeded**, and that is the answer the ticket asked
    /// for. `RotateAll` writes the rotated envelopes into `secretMount.json` before it
    /// exits, so a zero status means the work is already on disk; only `stdout` (the
    /// `rotateResult` the client sees) and `stderr` (the provider's fallback warning) can
    /// be truncated. Reporting a *failure* for a truncated diagnostic would leave the
    /// mount unstamped although its secrets were rotated - breaking the "unstamped = not
    /// rotated" replay rule the stop path relies on - and would tell the IPC client that
    /// completed work failed. The alternative ("failure, replay it") buys no recovery the
    /// child has not already performed, and re-rotating is only harmless, not free.
    /// The distinguishable warn at the drain deadline is what makes the loss visible.
    Finished {
        success: bool,
        stdout: String,
        stderr: String,
        output_complete: bool,
    },
    /// A stop was requested while the child was still running. By the time this variant
    /// is returned the child has been terminated *and reaped* (#153), and the job object
    /// holding its process tree has been closed (#160), which terminates the helpers it had
    /// started - requested at handle close, not awaited, so they can still be winding down
    /// for a moment after this returns.
    Interrupted { pid: Option<u32> },
    /// The rotation outlived its hang budget while no stop was requested (#161). Converged
    /// exactly like `Interrupted` - terminated, reaped, job handle closed - so the only
    /// difference is the reason and the message the caller gets. A liveness backstop, not a
    /// rotation SLA: see `ROTATE_HANG_BUDGET`.
    OverHangBudget { pid: Option<u32> },
}

/// Why a live child was ended instead of waited for (#161).
///
/// `ChildOutcome` carries the same distinction, but only once the child has been converged;
/// the `select!` needs to know before that, so it does not have to encode the reason twice
/// (a bool next to a string that says the same thing).
enum Ending {
    /// The service stop token was cancelled.
    Stopped,
    /// The rotation outlived `ROTATE_HANG_BUDGET`.
    Hung,
}

impl Ending {
    /// How the reason reads inside a log line: "before it was terminated (the stop)".
    fn reason(&self) -> &'static str {
        match self {
            Ending::Stopped => "the stop",
            Ending::Hung => "the hang budget",
        }
    }
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
/// The value sops writes to disk does not outlive the delegation either (#177): the root the
/// CLI puts its sops working directories in is created here, handed over through
/// `SCRATCH_ENV`, and deleted once the outcome above is known - including the interrupted
/// case, where the CLI's own `finally` never runs, because the process doing the delete is
/// this one. That delete is retried for a moment (a killed helper can still own a handle
/// for a fraction of a second) and a failure that survives the retries is logged at error
/// level, never swallowed; see `crate::sops_scratch`. What this cannot cover is a service
/// that is killed rather than stopped: the root then stays on disk with a name that says
/// the service wrote it.
///
/// `hang_budget` bounds the wait on a *live* child (#161): whichever of the child's exit, the
/// stop token and that budget comes first decides how the call ends, and the two that are not
/// an exit converge the same way. The budget is a parameter rather than a constant so the
/// tests can inject a short one and never wait a real 15 minutes on a CI runner; production
/// passes `ROTATE_HANG_BUDGET`, which documents why the value is what it is.
///
/// The exit branch has its own bound (#192): `pipe_drain` caps the wait for the two pipe
/// readers after the child is gone, so a helper that inherited the std handles cannot keep
/// `run_cli_child`, and with it `ROTATIONS_IN_FLIGHT`, alive forever. A child that exited 0
/// is still a successful rotation; only its diagnostic text can be incomplete. See
/// `ChildOutcome::Finished` for why that answer, not "failure", is the chosen one.
///
/// Both budgets are parameters for the same reason `hang_budget` always was: a bounded wait
/// CI cannot shorten is a bounded wait CI cannot test. Production passes
/// `ROTATE_HANG_BUDGET` and `PIPE_DRAIN_ON_EXIT`; the held-handle fixtures inject a short
/// pipe drain so CI never waits the helper out. No private lock or job-handle accessor is
/// added (#163); the two mid-drain race tests pass `#[cfg(test)]` `LatchedPipeDrain` (#205),
/// which compiles out completely in non-test builds.
pub(crate) async fn run_cli_child(
    cli_exe: &str,
    args: &[&str],
    shutdown: &CancellationToken,
    hang_budget: Duration,
    pipe_drain: impl Into<PipeDrain>,
) -> Result<ChildOutcome, String> {
    let pipe_drain: PipeDrain = pipe_drain.into();
    // Created before the spawn and reclaimed after the outcome is known, so no early
    // return inside `spawn_cli_child` - a failed spawn, a missing pipe, a failed wait -
    // can skip the delete.
    let scratch = SopsScratch::create();
    let outcome =
        spawn_cli_child(cli_exe, args, shutdown, hang_budget, pipe_drain, scratch.as_ref()).await;
    if let Some(scratch) = scratch {
        scratch.reclaim().await;
    }
    outcome
}

/// The delegation itself: job object, spawn, drain, converge.
///
/// Split out of `run_cli_child` so the reclaim above runs on every exit path. The job
/// handle is dropped when this returns, which is before the caller deletes the scratch
/// root, so the tree is already under termination when its files are removed.
async fn spawn_cli_child(
    cli_exe: &str,
    args: &[&str],
    shutdown: &CancellationToken,
    hang_budget: Duration,
    pipe_drain: PipeDrain,
    scratch: Option<&SopsScratch>,
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

    let mut command = tokio::process::Command::new(cli_exe);
    command
        .args(args)
        .creation_flags(CREATE_NO_WINDOW)
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        // Backstop for the one exit this function cannot await: the runtime dropping this
        // task. The guarantee is the cancellation branch below, which also reaps.
        .kill_on_drop(true);
    // Where the CLI's sops working directories go. Unset, the provider keeps using its own
    // `%TEMP%` directory - the hand-run CLI case, which has no service to reclaim for it.
    if let Some(scratch) = scratch {
        command.env(SCRATCH_ENV, scratch.path());
    }
    let mut child = command
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
    // pipe buffer would otherwise block on write and never observe the kill. Each reader
    // pushes what it sees into a shared buffer as it arrives, so a caller that stops
    // waiting for EOF - the stop drain below, or the post-exit drain in the exit branch
    // (#192) - keeps the text instead of losing it inside the task it aborts. The handles
    // are `mut` because both bounded drains await them through `&mut`: dropping a
    // `JoinHandle` detaches its task without stopping it, and a reader parked on a pipe
    // whose writer never closes would otherwise outlive the delegation.
    let mut stdout = child.stdout.take().ok_or("CLI rotate child has no stdout pipe")?;
    let mut stderr = child.stderr.take().ok_or("CLI rotate child has no stderr pipe")?;
    let stdout_seen = Arc::new(std::sync::Mutex::new(Vec::<u8>::new()));
    let stderr_seen = Arc::new(std::sync::Mutex::new(Vec::<u8>::new()));
    let mut read_stdout = tokio::spawn({
        let seen = Arc::clone(&stdout_seen);
        async move { read_pipe_into(&mut stdout, seen).await }
    });
    let mut read_stderr = tokio::spawn({
        let seen = Arc::clone(&stderr_seen);
        async move { read_pipe_into(&mut stderr, seen).await }
    });

    // Three ways out of the wait. "The child exited" is the only one that reports what the
    // child did; the other two both decide to stop waiting on a live child and owe the same
    // guarantee - terminated *and reaped* here, job handle closed on return, stderr salvaged -
    // so they share one convergence path below (#153, #161). Which of the two wins when both
    // are ready is not a correctness question: either way the child dies the same way, and
    // only the message the caller gets differs.
    let ending = tokio::select! {
        status = child.wait() => {
            let status = status
                .map_err(|e| format!("failed to wait for the CLI rotate child: {}", e))?;
            // The child's own ends of the pipes are closed, but EOF is not a fact to wait
            // on forever (#192). The old unconditional `await` on both readers assumed a
            // provider helper cannot inherit the CLI's std handles; the C# providers do
            // redirect theirs today (`src/Secrets/Providers/`), yet nothing pinned that,
            // and one helper that does not - a future adapter, `UseShellExecute`, anything
            // the CLI spawns without redirecting - leaves the CLI gone and both pipes
            // open. That was a permanent `run_cli_child`, a permanent
            // `ROTATIONS_IN_FLIGHT == 1`, a tick that skipped every 300s and a `refresh`
            // that never stopped answering "in flight". Both readers now share one
            // deadline, so the exit path is bounded by a single number; a reader that
            // panics or errors is reported as incomplete output, not a closed pipe.
            // A stop still wins *immediately* even after this arm of the outer `select!`
            // has won: the drain is raced against `shutdown.cancelled()`, not just given
            // its budget (#192 review). `biased` polls the drain first, so a child whose
            // pipes are already closed cannot be spuriously marked incomplete by a stop
            // cancelled at the same instant; a pending drain loses to the stop. The child
            // has already exited, so the answer stays `Finished` with the exit status
            // intact - there is nothing left to kill or replay.
            // `drain_pipes_on_exit` consumes `pipe_drain`, firing any `#[cfg(test)]`
            // drain-entry latch after `child.wait()` has won the outer `select!` and
            // waiting for test release before starting its deadline (#205).
            let drain_budget = pipe_drain.budget;
            let (stdout_closed, stderr_closed, cause): (bool, bool, &'static str) =
                tokio::select! {
                    biased;
                    drained =
                        drain_pipes_on_exit(&mut read_stdout, &mut read_stderr, pipe_drain) =>
                    {
                        let cause = if drained.2 {
                            "a pipe reader failed before EOF"
                        } else {
                            "the drain deadline expired"
                        };
                        (drained.0, drained.1, cause)
                    }
                    _ = shutdown.cancelled() => {
                        (false, false, "a service stop interrupted the drain")
                    }
                };
            let output_complete = stdout_closed && stderr_closed;
            if !output_complete {
                // Distinguishable from the stop drain ("... after it was terminated") and
                // from a hang budget ("outlived its ... hang budget"): this one says the
                // child *finished* and only its output did not. The phrase is what an
                // operator greps for; the pid, the pipe name, the budget and the cause say
                // which delegation lost which diagnostic, and why.
                read_stdout.abort();
                read_stderr.abort();
                tracing::warn!(
                    "post-exit pipe drain: CLI rotate child {} exited but its {} did not close cleanly (budget {:?}: {}); keeping the output captured so far and deciding the rotation by its exit status",
                    pid_label(pid),
                    unclosed_pipes(stdout_closed, stderr_closed),
                    drain_budget,
                    cause
                );
            }
            let stdout = take_seen(&stdout_seen);
            let stderr = take_seen(&stderr_seen);
            // A child that exits 0 and still wrote to stderr is where the provider's own
            // fallback warning lands (`SopsProvider.CreateWorkingDirectory`): the rotation
            // is fine, but the file sops wrote may sit outside the scratch root this call
            // reclaims, so the line cannot be dropped on the success path. sops writes
            // nothing to stderr on a normal encrypt/decrypt (measured on 3.7.3 and 3.13.3),
            // so this is not a per-rotation line. Through `redaction::scrub_message` before
            // it reaches the log (`docs/agents/hard-boundaries.md`): the CLI scrubs at the
            // source, but a helper's own stderr is not this crate's to vouch for - and the
            // scrub caps the length, so one rotation cannot become a page of log. The level
            // is info because the default filter records it and a chatty provider must not
            // read as a failure. A failing child is left out of this: its stderr is already
            // the substance of the error the client gets.
            if status.success() && !stderr.is_empty() {
                tracing::info!(
                    "delegated CLI wrote to stderr on success: {}",
                    crate::redaction::scrub_message(&stderr)
                );
            }
            return Ok(ChildOutcome::Finished {
                success: status.success(),
                stdout,
                stderr,
                output_complete,
            });
        }
        _ = shutdown.cancelled() => Ending::Stopped,
        _ = tokio::time::sleep(hang_budget) => Ending::Hung,
    };

    converge_killed_child(
        &mut child,
        read_stdout,
        read_stderr,
        &stderr_seen,
        ending.reason(),
    )
    .await?;
    // The job handle drops as this function returns, which is where the helper tree dies
    // (#160): the CLI is already reaped, so nothing left in the job can still be serving this
    // rotation. Both paths reach it inside the stop budget - closing a job handle is not a
    // wait - and the hang-budget path adds no window of its own.
    Ok(match ending {
        Ending::Stopped => ChildOutcome::Interrupted { pid },
        Ending::Hung => {
            // The service log is where a budget expiry has to be visible: the IPC client gave
            // up 30s into the call (`ipc::cli_gateway`), so for a rotation this long nobody is
            // waiting on the response any more.
            tracing::warn!(
                "CLI rotate child {} outlived its {:?} hang budget and was terminated; the mount keeps its old metadata and the next rotate replays it",
                pid_label(pid),
                hang_budget
            );
            ChildOutcome::OverHangBudget { pid }
        }
    })
}

/// Wait for both pipe readers to reach a real EOF, but no longer than one shared
/// `pipe_drain.budget` (#192). Returns `(stdout_eof, stderr_eof, reader_failed)`: a reader
/// that ended on an I/O error or a panic is *not* a closed pipe, so it counts as incomplete
/// output just like a deadline expiry. `timeout_at` over `&mut` `JoinHandle`s keeps the
/// handles so they can still be aborted: dropping a handle detaches its task, and a reader
/// parked on a pipe whose writer never closes would otherwise live until the runtime shuts
/// down. The second wait uses the same deadline as the first, so two pipes cannot cost two
/// budgets. Under `cfg(test)`, an armed `LatchedPipeDrain` signals entry, waits for test
/// release before computing `deadline`, and optionally acknowledges the first active
/// `timeout_at` poll (#205).
async fn drain_pipes_on_exit(
    read_stdout: &mut tokio::task::JoinHandle<bool>,
    read_stderr: &mut tokio::task::JoinHandle<bool>,
    pipe_drain: PipeDrain,
) -> (bool, bool, bool) {
    #[cfg(test)]
    let mut started = if let Some((entered, release, started)) = pipe_drain.latch {
        let _ = entered.send(());
        let _ = release.await;
        started
    } else {
        None
    };
    let deadline = tokio::time::Instant::now() + pipe_drain.budget;
    let stdout = await_pipe_with_deadline(
        &mut *read_stdout,
        deadline,
        #[cfg(test)]
        &mut started,
    )
    .await;
    let stderr = await_pipe_with_deadline(
        &mut *read_stderr,
        deadline,
        #[cfg(test)]
        &mut started,
    )
    .await;
    let stdout_eof = matches!(&stdout, Ok(Ok(true)));
    let stderr_eof = matches!(&stderr, Ok(Ok(true)));
    let reader_failed = matches!(&stdout, Ok(Ok(false)) | Ok(Err(_)))
        || matches!(&stderr, Ok(Ok(false)) | Ok(Err(_)));
    (stdout_eof, stderr_eof, reader_failed)
}

/// Await a single pipe reader up to `deadline` without consuming its `JoinHandle`, firing
/// any armed `cfg(test)` active-wait acknowledgement once `timeout_at` registers `Pending`.
async fn await_pipe_with_deadline(
    reader: &mut tokio::task::JoinHandle<bool>,
    deadline: tokio::time::Instant,
    #[cfg(test)] started: &mut Option<oneshot::Sender<()>>,
) -> Result<Result<bool, tokio::task::JoinError>, tokio::time::error::Elapsed> {
    #[cfg(not(test))]
    {
        tokio::time::timeout_at(deadline, reader).await
    }
    #[cfg(test)]
    {
        let mut wait = std::pin::pin!(tokio::time::timeout_at(deadline, reader));
        std::future::poll_fn(|cx| {
            let poll = std::future::Future::poll(wait.as_mut(), cx);
            if poll.is_pending() {
                if let Some(tx) = started.take() {
                    let _ = tx.send(());
                }
            }
            poll
        })
        .await
    }
}

/// Which pipes the warn has to name. Called only when at least one did not close; the
/// both-closed arm keeps the match exhaustive rather than making the caller unwrap.
fn unclosed_pipes(stdout_closed: bool, stderr_closed: bool) -> &'static str {
    match (stdout_closed, stderr_closed) {
        (false, false) => "stdout and stderr pipes",
        (false, true) => "stdout pipe",
        (true, false) => "stderr pipe",
        (true, true) => "pipes",
    }
}

/// End a rotation whose child the caller has decided not to wait for: terminate, reap, and
/// salvage whatever its stderr already said.
///
/// Shared by the stop branch (#153) and the hang-budget branch (#161) - the two owe the same
/// guarantee, and the only difference between them is what the caller is told afterwards.
/// `reason` names the cause in the log lines, so a service log distinguishes "the SCM asked us
/// to stop" from "this child wedged".
async fn converge_killed_child(
    child: &mut tokio::process::Child,
    read_stdout: tokio::task::JoinHandle<bool>,
    read_stderr: tokio::task::JoinHandle<bool>,
    stderr_seen: &Arc<std::sync::Mutex<Vec<u8>>>,
    reason: &str,
) -> Result<(), String> {
    // `Child::kill` is TerminateProcess followed by `wait`, so the child is reaped here
    // rather than left as an orphan of a service the SCM already calls Stopped. The two pipe
    // readers are then handled differently - stdout is aborted, stderr is drained (below) -
    // and neither path may depend on a pipe reaching EOF. The C# providers redirect their
    // own std handles today (`src/Secrets/Providers/`), but that is not a guarantee this
    // crate can vouch for: a helper that inherits them keeps the pipe open, which is why
    // the drain below is bounded and why the exit branch has its own budget (#192).
    child
        .kill()
        .await
        .map_err(|e| format!("failed to terminate the CLI rotate child: {}", e))?;
    read_stdout.abort();
    // stderr is drained instead of aborted: the provider's fallback warning is written before
    // it does any work, so dropping it would erase the one line that explains why the reclaim
    // that follows may find nothing to delete. Bounded, because the stop path must not depend
    // on a pipe reaching EOF: reaping the child closes its own handles, but a helper that
    // inherited them can still hold the pipe open (#192). The reader accumulates as bytes
    // arrive, so giving up at the budget keeps what was already captured; only bytes written
    // after the budget are lost.
    let mut stderr_reader = read_stderr;
    let closed = tokio::time::timeout(STDERR_DRAIN_ON_STOP, &mut stderr_reader).await;
    if closed.is_err() {
        stderr_reader.abort();
    }
    let stderr = take_seen(stderr_seen);
    if !stderr.is_empty() {
        tracing::info!(
            "delegated CLI wrote to stderr before it was terminated ({}): {}",
            reason,
            crate::redaction::scrub_message(&stderr)
        );
    }
    if closed.is_err() {
        // Logged rather than dropped: a helper that keeps the pipe open past the drain budget
        // is exactly the case where the reclaim may find a held file, and silence here would
        // read as "nothing to say".
        tracing::warn!(
            "delegated CLI stderr did not close within {:?} after it was terminated ({}); what had arrived is logged above, anything later is lost",
            STDERR_DRAIN_ON_STOP,
            reason
        );
    }
    Ok(())
}

/// Read a child pipe to the end, appending each chunk to `seen` as it arrives. The
/// incremental append is what lets a bounded drain keep partial text: the reader task can
/// be aborted mid-pipe, and what it already read stays in `seen`. Bytes accumulate raw and
/// are decoded once, so a multi-byte character split across two reads is not corrupted.
/// "To the end" is a best effort, not a promise - a helper that inherited the handle can
/// keep the pipe open past the child's exit, which is exactly why both callers bound this
/// read (`STDERR_DRAIN_ON_STOP` on the stop path, `PIPE_DRAIN_ON_EXIT` after an exit).
///
/// Returns `true` only on a real EOF. A read error is *not* a closed pipe (#192): it ends
/// the reader with truncated output, so the exit drain must report incomplete output and
/// keep its warn instead of silently calling the diagnostics complete. `pub(crate)` so the
/// #192 drain fixture can inject a failing reader into this exact function.
pub(crate) async fn read_pipe_into<R: tokio::io::AsyncRead + Unpin>(
    pipe: &mut R,
    seen: Arc<std::sync::Mutex<Vec<u8>>>,
) -> bool {
    let mut buf = [0u8; 4096];
    // A child that is gone normally closes its end; an inheriting helper may not. Either
    // way the caller owns a budget for this read, so an absent EOF costs a warn, not a hang.
    loop {
        match pipe.read(&mut buf).await {
            Ok(0) => return true,
            Err(_) => return false,
            Ok(n) => {
                if let Ok(mut out) = seen.lock() {
                    out.extend_from_slice(&buf[..n]);
                }
            }
        }
    }
}

/// What a reader captured so far, lossy-decoded and trimmed the way the single-shot read
/// used to.
fn take_seen(seen: &Arc<std::sync::Mutex<Vec<u8>>>) -> String {
    match seen.lock() {
        Ok(bytes) => String::from_utf8_lossy(&bytes).trim().to_string(),
        Err(_) => String::new(),
    }
}

/// Run a delegated CLI rotation and map its `ChildOutcome` to the IPC caller's `Result`.
async fn call_cli_rotate(
    cli_exe: &str,
    cli_args: &[&str],
    shutdown: &CancellationToken,
    hang_budget: Duration,
    pipe_drain: PipeDrain,
) -> Result<String, String> {
    match run_cli_child(cli_exe, cli_args, shutdown, hang_budget, pipe_drain).await? {
        // `output_complete` is deliberately not consulted here: an exited-0 child is a
        // successful rotation even when a helper kept a pipe open, and the warn at the
        // drain deadline is where that loss is reported. See `ChildOutcome::Finished`.
        ChildOutcome::Finished {
            success: true,
            stdout,
            ..
        } => Ok(stdout),
        ChildOutcome::Finished { stderr, .. } => {
            Err(format!("CLI rotate failed: {}", stderr.trim()))
        }
        ChildOutcome::Interrupted { pid } => Err(format!(
            "CLI rotate interrupted by service stop (child {} terminated and reaped)",
            pid_label(pid)
        )),
        ChildOutcome::OverHangBudget { pid } => {
            // Deliberately not the stop wording: an operator reading the IPC error or the
            // service log has to be able to tell "the SCM stopped us" from "this rotation
            // wedged". The duration is in the message because it is the only thing that
            // makes the number in `ROTATE_HANG_BUDGET` falsifiable from a log line.
            Err(format!(
                "CLI rotate exceeded its {:?} hang budget (child {} terminated and reaped)",
                hang_budget,
                pid_label(pid)
            ))
        }
    }
}

/// A pid for a message, or `unknown` when the child was already reaped (#153, #161).
fn pid_label(pid: Option<u32>) -> String {
    pid.map(|p| p.to_string())
        .unwrap_or_else(|| "unknown".to_string())
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

/// `load_mounts`, but keeping "no mounts" apart from "a file this build cannot read".
/// After a delegated rotation the two mean opposite things: no mounts is a mount that
/// disappeared (skip it), a file that will not parse is a rotation that must not be reported
/// as completed over state nobody can inspect. Only an unparseable or unreadable file is an
/// error - a missing file means the same thing as an empty one.
fn load_mounts_strict(path: &Path) -> Result<Vec<SecretMount>, String> {
    if !path.exists() {
        return Ok(Vec::new());
    }
    let raw = std::fs::read_to_string(path)
        .map_err(|e| format!("failed to read the mount file after the rotation: {}", e))?;
    serde_json::from_str(&raw)
        .map_err(|e| format!("the mount file did not parse after the rotation: {}", e))
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
