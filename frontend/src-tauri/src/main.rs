use log::info;
use serde::Serialize;
use std::path::PathBuf;
use std::process::Command;

#[derive(Serialize)]
#[serde(crate = "serde")]
struct CliResponse {
    success: bool,
    data: Option<String>,
    error: Option<String>,
}

#[tauri::command]
fn run_cli(command: String, args: Vec<String>) -> CliResponse {
    info!("run_cli invoked with command: {} args: {:?}", command, args);

    // 获取当前执行文件的目录
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|p| p.to_path_buf()));

    info!("Executable directory: {:?}", exe_dir);

    // 尝试多个可能的 CLI 位置
    let mut possible_paths = vec![
        // 绝对路径（开发和生产都适用）
        PathBuf::from("D:\\Aworker\\env-manager\\bin\\Release\\net10.0\\env-manager.exe"),
        // 相对路径：从 Tauri 可执行文件所在目录向上找
        if let Some(ref dir) = exe_dir {
            dir.join("../../../../bin/Release/net10.0/env-manager.exe")
        } else {
            PathBuf::from("")
        },
        // 当前工作目录
        std::env::current_dir()
            .ok()
            .map(|d| d.join("../../bin/Release/net10.0/env-manager.exe"))
            .unwrap_or_default(),
        // 直接搜索
        PathBuf::from("env-manager.exe"),
    ];

    // 移除空路径
    possible_paths.retain(|p| !p.as_os_str().is_empty());

    info!("Searching for CLI in paths: {:?}", possible_paths);

    let mut exe_path = None;
    for path in possible_paths.iter() {
        info!("Checking path: {}", path.display());
        if path.exists() {
            info!("Found CLI at: {}", path.display());
            exe_path = Some(path.clone());
            break;
        }
    }

    let exe_path = match exe_path {
        Some(p) => p,
        None => {
            let error_msg = format!(
                "env-manager.exe not found. Tried: {:?}. CWD: {:?}",
                possible_paths,
                std::env::current_dir()
            );
            info!("ERROR: {}", error_msg);
            return CliResponse {
                success: false,
                data: None,
                error: Some(error_msg),
            };
        }
    };

    let mut cmd = Command::new(&exe_path);
    cmd.arg(&command);
    for arg in args {
        cmd.arg(arg);
    }

    info!("Executing: {} {}", exe_path.display(), command);

    match cmd.output() {
        Ok(output) => {
            let stdout = String::from_utf8_lossy(&output.stdout).to_string();
            let stderr = String::from_utf8_lossy(&output.stderr).to_string();

            if output.status.success() {
                info!("Command succeeded");
                CliResponse {
                    success: true,
                    data: Some(stdout),
                    error: None,
                }
            } else {
                let error_msg = if !stderr.is_empty() { stderr } else { stdout };
                info!("Command failed: {}", error_msg);
                CliResponse {
                    success: false,
                    data: None,
                    error: Some(error_msg),
                }
            }
        }
        Err(e) => {
            let error_msg = format!("Failed to execute CLI: {}", e);
            info!("ERROR: {}", error_msg);
            CliResponse {
                success: false,
                data: None,
                error: Some(error_msg),
            }
        }
    }
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_log::Builder::default()
            .level(log::LevelFilter::Info)
            .build())
        .invoke_handler(tauri::generate_handler![run_cli])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
