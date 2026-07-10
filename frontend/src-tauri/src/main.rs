use log::info;
use serde::Serialize;
use std::path::PathBuf;
use std::process::Command;
use tauri::Manager;

#[derive(Serialize)]
#[serde(crate = "serde")]
struct CliResponse {
    success: bool,
    data: Option<String>,
    error: Option<String>,
}

fn resolve_cli_path(app: &tauri::AppHandle) -> Option<PathBuf> {
    // 1. Tauri resource directory (production: bundled inside MSI)
    if let Ok(resource_path) = app
        .path()
        .resolve("env-manager.exe", tauri::path::BaseDirectory::Resource)
    {
        if resource_path.exists() {
            info!("Found CLI via resource: {}", resource_path.display());
            return Some(resource_path);
        }
    }

    // 2. Relative to exe dir (dev mode)
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.to_path_buf()));

    if let Some(ref dir) = exe_dir {
        let dev_path = dir.join("../../../../bin/Release/net10.0/env-manager.exe");
        if dev_path.exists() {
            info!("Found CLI via dev path: {}", dev_path.display());
            return Some(dev_path);
        }
    }

    // 3. Current working directory
    if let Ok(cwd) = std::env::current_dir() {
        let cwd_path = cwd.join("../../bin/Release/net10.0/env-manager.exe");
        if cwd_path.exists() {
            info!("Found CLI via cwd: {}", cwd_path.display());
            return Some(cwd_path);
        }
    }

    // 4. PATH fallback
    if let Ok(output) = Command::new("where").arg("env-manager.exe").output() {
        if output.status.success() {
            let stdout = String::from_utf8_lossy(&output.stdout);
            if let Some(line) = stdout.lines().next() {
                let path = PathBuf::from(line);
                if path.exists() {
                    return Some(path);
                }
            }
        }
    }

    None
}

#[tauri::command]
fn run_cli(app: tauri::AppHandle, command: String, args: Vec<String>) -> CliResponse {
    info!("run_cli: command={}, args={:?}", command, args);

    let exe_path = match resolve_cli_path(&app) {
        Some(p) => p,
        None => {
            return CliResponse {
                success: false,
                data: None,
                error: Some("env-manager.exe not found.".to_string()),
            };
        }
    };

    let mut cmd = Command::new(&exe_path);
    cmd.arg(&command);
    for arg in &args {
        cmd.arg(arg);
    }

    match cmd.output() {
        Ok(output) => {
            let stdout = String::from_utf8_lossy(&output.stdout).to_string();
            let stderr = String::from_utf8_lossy(&output.stderr).to_string();

            if output.status.success() {
                CliResponse {
                    success: true,
                    data: Some(stdout),
                    error: None,
                }
            } else {
                let err = if !stderr.trim().is_empty() { stderr } else { stdout };
                CliResponse {
                    success: false,
                    data: None,
                    error: Some(err),
                }
            }
        }
        Err(e) => CliResponse {
            success: false,
            data: None,
            error: Some(format!("Failed to spawn CLI: {}", e)),
        },
    }
}

fn main() {
    tauri::Builder::default()
        .plugin(
            tauri_plugin_log::Builder::default()
                .level(log::LevelFilter::Info)
                .build(),
        )
        .invoke_handler(tauri::generate_handler![run_cli])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
