// v0.9.0 Phase B+C: env-manager-service.exe
// Windows system service for secret mount lifecycle management.
// See docs/adr/0001-secret-architecture-revision.md decisions A5-A8, A11.

use std::env;
use std::path::PathBuf;
use tokio_util::sync::CancellationToken;
use std::sync::Arc;

mod reconcile;
mod ipc;
mod cert_bootstrap;
mod audit_ledger;
mod redaction;
mod pipe_dacl;
mod process_guard;
#[cfg(test)]
mod resilience_tests;

#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub enum RuntimeMode {
    Service,
    Background,
    Cli,
}

impl RuntimeMode {
    /// Resolve from --mode=<x> argv or SCM service args.
    /// Pattern borrowed from D:\Aworker\photo RuntimeModeResolver.
    pub fn resolve(args: &[String]) -> Self {
        for arg in args {
            if let Some(val) = arg.strip_prefix("--mode=") {
                return match val.to_lowercase().as_str() {
                    "service" => RuntimeMode::Service,
                    "background" => RuntimeMode::Background,
                    "cli" => RuntimeMode::Cli,
                    _ => RuntimeMode::Service,
                };
            }
        }
        // Default: Background mode (user-launched).
        RuntimeMode::Background
    }

    pub fn pipe_endpoint(&self) -> &'static str {
        match self {
            RuntimeMode::Service => r"\\.\pipe\EnvManager.Service",
            RuntimeMode::Background => r"\\.\pipe\EnvManager.Background",
            RuntimeMode::Cli => r"\\.\pipe\EnvManager.Background",
        }
    }
}

/// v0.9.6: Process uptime in seconds for heartbeat enrichment.
/// Used by IPC ping/status to help the GUI watchdog distinguish "busy" vs "deadlocked".
pub fn get_process_uptime() -> u64 {
    static START: std::sync::OnceLock<std::time::Instant> = std::sync::OnceLock::new();
    let start = START.get_or_init(|| std::time::Instant::now());
    start.elapsed().as_secs()
}

fn main() {
    // Resolve mode FIRST so we know where to put the log file.
    let args: Vec<String> = env::args().collect();
    let mode = RuntimeMode::resolve(&args);

    // v0.9.1: Write service logs to a file so Session 0 service output is
    // not lost (stderr goes nowhere in Session 0). In Background mode, write
    // to %LOCALAPPDATA% alongside env-manager.log; in Service mode, write to
    // %ProgramData% alongside secretMount.json/audit-ledger.jsonl.
    let log_dir = match mode {
        RuntimeMode::Service => {
            let pd = std::env::var("ProgramData").unwrap_or_else(|_| "C:\\ProgramData".to_string());
            std::path::PathBuf::from(pd).join("EnvManager")
        }
        _ => {
            let la = std::env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".to_string());
            std::path::PathBuf::from(la).join("EnvManager")
        }
    };
    let _ = std::fs::create_dir_all(&log_dir);
    let log_file = log_dir.join("env-manager-service.log");
    // ponytail: best-effort file open; if it fails, fall back to stderr only.
// v0.9.8: tracing + tracing-appender unified logging backend.
// Daily rotation + per-module EnvFilter via RUST_LOG.
let file_appender = tracing_appender::rolling::daily(&log_dir, "env-manager-service.log");
let (non_blocking_file, guard) = tracing_appender::non_blocking(file_appender);
let filter = tracing_subscriber::EnvFilter::try_from_default_env()
    .unwrap_or_else(|_| tracing_subscriber::EnvFilter::new("info"));
tracing_subscriber::fmt()
    .with_env_filter(filter)
    .with_writer(non_blocking_file)
    .with_ansi(false)
    .with_target(true)
    .with_file(false)
    .with_line_number(false)
    .init();
std::mem::forget(guard);
    tracing::info!("env-manager-service starting in {:?} mode", mode);

    // v0.9.13 Phase 2D/2B/2C/2E/4B: process hardening (WER, hash, debugger, modules, lock)
    process_guard::init();

    // Entry guard (A7): refuse direct double-click launch with no --mode.
    // If interactive session and no --mode flag, print guidance and exit.
    if matches!(mode, RuntimeMode::Background) && !args.iter().any(|a| a.starts_with("--mode=")) {
        // Check if we're in an interactive session (console attached).
        // In service mode, there's no console. In background mode launched by GUI,
        // the GUI passes --mode=background explicitly.
        // If someone double-clicks with no args at all, we land here as Background default.
        // Allow it but log a warning — background mode is user-launchable.
        tracing::info!("Background mode started by user (no --mode flag). This is acceptable for foreground GUI testing.");
    }

    let rt = match tokio::runtime::Builder::new_multi_thread()
        .enable_all()
        .build()
    {
        Ok(rt) => rt,
        Err(e) => {
            tracing::error!("failed to create tokio runtime: {}", e);
            std::process::exit(1);
        }
    };

    rt.block_on(async {
        match mode {
            RuntimeMode::Service | RuntimeMode::Background => {
                let pipe = mode.pipe_endpoint();
                tracing::info!("IPC pipe: {}", pipe);

                // CancellationToken for graceful shutdown — shared between IPC server
                // and reconcile loop. When IPC receives "shutdown", it cancels the token,
                // which signals both tasks to exit cleanly.
                let shutdown_token = Arc::new(CancellationToken::new());
                let ipc_token = shutdown_token.clone();
                let reconcile_token = shutdown_token.clone();

                let reconcile_handle = tokio::spawn(reconcile::reconcile_loop(reconcile_token));
                // Service mode uses PIPE_FIRST_PIPE_INSTANCE (anti-squatting, SCM guarantees single instance).
                // Background mode skips it: previous instance pipe handle may linger in OS kernel after shutdown,
                // causing os error 5 "拒绝访问" on restart. v0.9.2 fix.
                let use_fpi = matches!(mode, RuntimeMode::Service);
                let ipc_handle = tokio::spawn(ipc::start_ipc_server(pipe, ipc_token, use_fpi));

                tokio::select! {
                    _ = reconcile_handle => tracing::warn!("reconcile loop exited"),
                    _ = ipc_handle => tracing::warn!("IPC server exited"),
                }
                tracing::info!("service shutting down (all tasks exited)");
            }
            RuntimeMode::Cli => {
                let pipe = mode.pipe_endpoint();
                if let Err(e) = ipc::cli_gateway(pipe).await {
                    eprintln!("Error: service IPC failed: {}", e);
                    std::process::exit(1);
                }
            }
        }
    });
}

/// Resolve the secretMount.json path.
/// Machine-level: %ProgramData%\EnvManager\secretMount.json (service identity).
/// User-level: %LOCALAPPDATA%\EnvManager\secretMount.json (background/CLI mode).
pub fn secret_mount_path() -> PathBuf {
    // Service mode uses ProgramData (machine-level, A8).
    // Background/CLI mode uses LocalAppData (user-level, compatibility with v0.8.0).
    let args: Vec<String> = env::args().collect();
    let mode = RuntimeMode::resolve(&args);

    match mode {
        RuntimeMode::Service => {
            let program_data = env::var("ProgramData")
                .unwrap_or_else(|_| "C:\\ProgramData".to_string());
            let dir = PathBuf::from(program_data).join("EnvManager");
            std::fs::create_dir_all(&dir).ok();
            dir.join("secretMount.json")
        }
        RuntimeMode::Background | RuntimeMode::Cli => {
            let local_app = env::var("LOCALAPPDATA")
                .unwrap_or_else(|_| ".".to_string());
            let dir = PathBuf::from(local_app).join("EnvManager");
            std::fs::create_dir_all(&dir).ok();
            dir.join("secretMount.json")
        }
    }
}
