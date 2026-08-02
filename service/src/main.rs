// v0.9.0 Phase B+C: env-manager-service.exe
// Windows system service for secret mount lifecycle management.
// See docs/adr/0001-secret-architecture-revision.md decisions A5-A8, A11.

use std::env;
use std::path::PathBuf;

mod reconcile;
mod ipc;
mod cert_bootstrap;
mod audit_ledger;

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

fn main() {
    env_logger::init();
    let args: Vec<String> = env::args().collect();
    let mode = RuntimeMode::resolve(&args);
    log::info!("env-manager-service starting in {:?} mode", mode);

    let rt = tokio::runtime::Runtime::new().expect("failed to create tokio runtime");
    rt.block_on(async {
        match mode {
            RuntimeMode::Service | RuntimeMode::Background => {
                // Start reconcile loop + IPC server concurrently.
                let pipe = mode.pipe_endpoint();
                let reconcile_handle = tokio::spawn(reconcile::reconcile_loop());
                let ipc_handle = tokio::spawn(ipc::start_ipc_server(pipe));
                // Wait for either to complete (they run indefinitely).
                tokio::select! {
                    _ = reconcile_handle => log::warn!("reconcile loop exited"),
                    _ = ipc_handle => log::warn!("IPC server exited"),
                }
            }
            RuntimeMode::Cli => {
                // One-shot: connect to background service pipe, send request, print response.
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
pub fn secret_mount_path() -> PathBuf {
    // v0.9.0: machine-level mounts under ProgramData; user-bound stay in LocalAppData.
    // For now, use the same LocalAppData path as the CLI for compatibility.
    let local_app = env::var("LOCALAPPDATA").unwrap_or_else(|_| ".".into());
    PathBuf::from(local_app).join("EnvManager").join("secretMount.json")
}
