// Hide the console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use log::{info, warn};
use serde::Serialize;
use std::path::PathBuf;
use std::process::Command;
use std::sync::Mutex;
use tauri::Manager;

#[cfg(windows)]
use std::os::windows::process::CommandExt;

/// Windows process creation flag that prevents a console window from flashing.
/// 0x08000000 = CREATE_NO_WINDOW
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x08000000;

#[derive(Serialize)]
#[serde(crate = "serde")]
struct CliResponse {
    success: bool,
    data: Option<String>,
    error: Option<String>,
}

/// Commands the IPC layer is allowed to forward to the CLI.
/// Any command not in this list is rejected before spawning a subprocess.
const ALLOWED_COMMANDS: &[&str] = &[
    "list",
    "get",
    "set",
    "delete",
    "backup",
    "restore",
    "diff",
    "merge",
    "validate",
    "profile",
    "path",
    "help",
];

/// Mutex to serialize CLI invocations.
/// Without this, concurrent calls (e.g. set + listVariables triggered by the
/// frontend) can race: the list call may execute before the set completes,
/// returning stale data. This ensures mutations and reads are ordered.
static CLI_MUTEX: Mutex<()> = Mutex::new(());

fn resolve_cli_path(app: &tauri::AppHandle) -> Option<PathBuf> {
    // 1. Tauri resource directory (production: bundled as flat env-manager-cli.exe)
    if let Ok(resource_path) = app
        .path()
        .resolve("env-manager-cli.exe", tauri::path::BaseDirectory::Resource)
    {
        if resource_path.exists() {
            info!("[resolve] CLI via resource: {}", resource_path.display());
            return Some(resource_path);
        }
    }

    // 2. Adjacent to the GUI exe (portable distribution)
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.to_path_buf()));

    if let Some(ref dir) = exe_dir {
        let adjacent = dir.join("env-manager-cli.exe");
        if adjacent.exists() {
            info!("[resolve] CLI adjacent to GUI: {}", adjacent.display());
            return Some(adjacent);
        }
    }

    // 3. Dev mode: relative to project root.
    //    Covers both net10.0 and net10.0-windows output directories.
    if let Some(ref dir) = exe_dir {
        for rel in [
            "../../../../bin/Release/net10.0/env-manager-cli.exe",
            "../../../../bin/Release/net10.0-windows/env-manager-cli.exe",
            "../../../bin/Release/net10.0/env-manager-cli.exe",
            "../../../bin/Release/net10.0-windows/env-manager-cli.exe",
            "../../bin/Release/net10.0/env-manager-cli.exe",
            "../../bin/Release/net10.0-windows/env-manager-cli.exe",
        ] {
            let dev_path = dir.join(rel);
            if dev_path.exists() {
                info!("[resolve] CLI via dev path: {}", dev_path.display());
                return Some(dev_path);
            }
        }
    }

    // 4. Current working directory
    if let Ok(cwd) = std::env::current_dir() {
        for rel in [
            "bin/Release/net10.0/env-manager-cli.exe",
            "bin/Release/net10.0-windows/env-manager-cli.exe",
            "../../bin/Release/net10.0/env-manager-cli.exe",
        ] {
            let cwd_path = cwd.join(rel);
            if cwd_path.exists() {
                info!("[resolve] CLI via cwd: {}", cwd_path.display());
                return Some(cwd_path);
            }
        }
    }

    // 5. PATH fallback
    let mut where_cmd = Command::new("where");
    where_cmd.arg("env-manager-cli.exe");
    #[cfg(windows)]
    {
        where_cmd.creation_flags(CREATE_NO_WINDOW);
    }
    if let Ok(output) = where_cmd.output() {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            if let Some(line) = stdout.lines().next() {
                let path = PathBuf::from(line);
                if path.exists() {
                    info!("[resolve] CLI via PATH: {}", path.display());
                    return Some(path);
                }
            }
        }
    }

    warn!("[resolve] CLI not found by any method");
    None
}

/// Builds a Command for the CLI with CREATE_NO_WINDOW to prevent console flicker.
/// On non-Windows platforms this is a no-op.
fn build_cli_command(exe_path: &PathBuf, command: &str, args: &[String]) -> Command {
    let mut cmd = Command::new(exe_path);
    cmd.arg(command);
    for arg in args {
        cmd.arg(arg);
    }

    #[cfg(windows)]
    {
        cmd.creation_flags(CREATE_NO_WINDOW);
    }

    cmd
}

#[tauri::command]
fn run_cli(app: tauri::AppHandle, command: String, args: Vec<String>) -> CliResponse {
    info!("[run_cli] command={}, args={:?}", command, args);

    // Reject commands not in the whitelist before spawning any subprocess.
    if !ALLOWED_COMMANDS.contains(&command.as_str()) {
        warn!("[run_cli] rejected unknown command: {}", command);
        return CliResponse {
            success: false,
            data: None,
            error: Some(format!("Unknown command: {}", command)),
        };
    }

    let exe_path = match resolve_cli_path(&app) {
        Some(p) => p,
        None => {
            return CliResponse {
                success: false,
                data: None,
                error: Some("env-manager-cli.exe not found. Check installation.".to_string()),
            };
        }
    };

    // Acquire the serialization lock so that concurrent frontend calls
    // (e.g. set followed immediately by list) execute in order.
    let _guard = CLI_MUTEX.lock().unwrap_or_else(|e| e.into_inner());

    let mut cmd = build_cli_command(&exe_path, &command, &args);

    let start = std::time::Instant::now();

    match cmd.output() {
        Ok(output) => {
            let elapsed = start.elapsed();
            let stdout = String::from_utf8_lossy(&output.stdout).to_string();
            let stderr = String::from_utf8_lossy(&output.stderr).to_string();
            info!(
                "[run_cli] exit={}, stdout_len={}, stderr_len={}, elapsed={}ms",
                output.status,
                stdout.len(),
                stderr.len(),
                elapsed.as_millis()
            );

            if !stderr.is_empty() {
                info!("[run_cli] stderr: {}", stderr.trim());
            }

            if output.status.success() {
                CliResponse {
                    success: true,
                    data: Some(stdout),
                    error: None,
                }
            } else {
                let err = if !stderr.trim().is_empty() {
                    stderr
                } else {
                    stdout
                };
                CliResponse {
                    success: false,
                    data: None,
                    error: Some(err),
                }
            }
        }
        Err(e) => {
            warn!("[run_cli] spawn failed: {}", e);
            CliResponse {
                success: false,
                data: None,
                error: Some(format!("Failed to spawn CLI: {}", e)),
            }
        }
    }
}

/// Returns diagnostic info about the CLI resolution for the frontend.
#[tauri::command]
fn cli_diagnostics(app: tauri::AppHandle) -> serde_json::Value {
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.display().to_string()))
        .unwrap_or_default();

    let resolved = resolve_cli_path(&app)
        .map(|p| p.display().to_string())
        .unwrap_or_else(|| "NOT FOUND".to_string());

    serde_json::json!({
        "resolved_cli_path": resolved,
        "gui_exe_dir": exe_dir,
        "cwd": std::env::current_dir().map(|d| d.display().to_string()).unwrap_or_default(),
    })
}

fn main() {
    tauri::Builder::default()
        .plugin(
            tauri_plugin_log::Builder::default()
                .level(log::LevelFilter::Info)
                .build(),
        )
        .invoke_handler(tauri::generate_handler![run_cli, cli_diagnostics])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
