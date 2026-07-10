use serde::Serialize;
use std::process::Command;
use tauri::State;

#[derive(Serialize)]
#[serde(crate = "serde")]
struct CliResponse {
    success: bool,
    data: Option<String>,
    error: Option<String>,
}

#[tauri::command]
fn run_cli(command: String, args: Vec<String>, _state: State<'_, ()>) -> CliResponse {
    let exe_path = match std::env::current_exe() {
        Ok(path) => {
            let parent = path.parent().unwrap();
            parent.join("../env-manager.exe")
        }
        Err(_) => return CliResponse {
            success: false,
            data: None,
            error: Some("Failed to locate CLI executable".to_string()),
        },
    };

    let mut cmd = Command::new(exe_path);
    cmd.arg(&command);
    for arg in args {
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
                CliResponse {
                    success: false,
                    data: None,
                    error: Some(if !stderr.is_empty() { stderr } else { stdout }),
                }
            }
        }
        Err(e) => CliResponse {
            success: false,
            data: None,
            error: Some(format!("Failed to execute CLI: {}", e)),
        },
    }
}

fn main() {
    tauri::Builder::default()
        .invoke_handler(tauri::generate_handler![run_cli])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}
