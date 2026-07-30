// Hide the console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use log::{info, warn, error, debug};
use serde::Serialize;
use std::path::PathBuf;
use std::process::Command;
use std::sync::RwLock;
use tauri::{
    menu::{Menu, MenuItem},
    tray::TrayIconBuilder,
    Manager, WindowEvent,
};

#[cfg(windows)]
use std::os::windows::process::CommandExt;

/// Windows process creation flag that prevents a console window from flashing.
/// 0x08000000 = CREATE_NO_WINDOW
#[cfg(windows)]
const CREATE_NO_WINDOW: u32 = 0x08000000;

/// Maximum allowed length for a single CLI argument (prevents buffer-exhaustion DoS).
const MAX_ARG_LEN: usize = 32767;

/// Maximum number of arguments passed to the CLI.
const MAX_ARGS: usize = 64;

#[derive(Serialize)]
#[serde(crate = "serde")]
struct CliResponse {
    success: bool,
    data: Option<String>,
    error: Option<String>,
}

/// Commands the IPC layer is allowed to forward to the CLI.
const ALLOWED_COMMANDS: &[&str] = &[
    "list",
    "get",
    "set",
    "rename",
    "change-scope",
    "delete",
    "toggle",
    "backup",
    "restore",
    "diff",
    "merge",
    "validate",
    "profile",
    "path",
    "agents",
    "history",
    "bulk",
    "expand",
    "help",
    "protection",
    "update",
];

/// Read-only commands that can run concurrently with each other.
/// They never mutate the registry, so concurrent execution is safe.
const READ_COMMANDS: &[&str] = &[
    "list",
    "get",
    "backup",
    "diff",
    "validate",
    "agents",
    "expand",
    "help",
    "update",
];

/// Write commands that mutate the registry. These must hold the write lock
/// to prevent concurrent mutations from interfering with each other.
/// Sub-commands like `profile list` / `path list` are read-only despite
/// using `profile` / `path` as the top-level command, so we also inspect args.
const WRITE_COMMANDS: &[&str] = &[
    "set",
    "rename",
    "change-scope",
    "delete",
    "toggle",
    "restore",
    "merge",
];

/// Determines if a CLI invocation is read-only (can run concurrently) or
/// write (must hold exclusive lock).
///
/// Some commands like `profile` and `path` have both read and write subcommands.
/// We inspect the first arg to determine the subcommand:
///   - `profile list`, `profile show` -> read
///   - `profile create`, `profile delete`, `profile apply`, `profile unapply`,
///     `profile add-var`, `profile remove-var`, `profile edit-var` -> write
///   - `path list` -> read
///   - `path add`, `path remove`, `path move-up`, `path move-down` -> write
fn is_read_only(command: &str, args: &[String]) -> bool {
    // Top-level commands that are always read-only
    if READ_COMMANDS.contains(&command) {
        return true;
    }

    // Top-level commands that are always write
    if WRITE_COMMANDS.contains(&command) {
        return false;
    }

    // Composite commands: inspect subcommand
    match command {
        "profile" => {
            match args.first().map(|s| s.as_str()) {
                Some("list") | Some("show") | Some("status") | Some("preview") | Some("export") | Some("launch") | Some("reveal-secret") | Some("export-secrets") => true,
                // secret-provider list = read; secret-provider set/rotate = write
                Some("secret-provider") => {
                    args.get(1).map(|s| s.as_str()) == Some("list")
                }
                _ => false, // create, delete, apply, unapply, add-var, remove-var, edit-var, set-launch, rename, add-secret, edit-secret, remove-secret, import-secrets
            }
        }
        "path" => {
            match args.first().map(|s| s.as_str()) {
                Some("list") => true,
                Some("health") => {
                    // path health is read-only UNLESS --fix is passed (--fix mutates the registry PATH).
                    !args.iter().any(|a| a == "--fix")
                }
                // dedupe --dry-run is read-only; dedupe without --dry-run mutates PATH
                Some("dedupe") => args.iter().any(|arg| arg == "--dry-run"),
                _ => false, // add, remove, move-up, move-down
            }
        }
        "history" => !matches!(args.first().map(|s| s.as_str()), Some("undo")),
        "bulk" => !matches!(args.first().map(|s| s.as_str()), Some("import")) || args.iter().any(|arg| arg == "--dry-run"),
        "protection" => matches!(args.first().map(|s| s.as_str()), Some("list")),
        _ => false,
    }
}

/// RwLock for read/write separation:
/// - Read commands acquire a read lock (multiple can run concurrently)
/// - Write commands acquire a write lock (exclusive)
/// This prevents write-write and read-write races while allowing read-read concurrency.
static CLI_RWLOCK: RwLock<()> = RwLock::new(());

/// Strips the `\\?\` (verbatim/long-path) prefix from a Windows path string.
/// Rust's PathBuf::display() can emit this prefix when the path was constructed
/// from a long path or UNC source. We remove it so that registry values and
/// PATH entries use clean drive-letter paths (e.g. `D:\Tools` not `\\?\D:\Tools`).
fn strip_verbatim_prefix(path: &str) -> String {
    if path.starts_with(r"\\?\") {
        path[r"\\?\".len()..].to_string()
    } else if path.starts_with(r"\\?\UNC\") {
        format!("\\{}", &path[r"\\?\UNC\".len()..])
    } else {
        path.to_string()
    }
}

/// Cleans a PathBuf for display: removes verbatim prefix, normalizes separators.
fn clean_path(path: PathBuf) -> String {
    let display = path.display().to_string();
    strip_verbatim_prefix(&display)
}

fn resolve_cli_path(app: &tauri::AppHandle) -> Option<PathBuf> {
    // 1. Tauri resource directory
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

    // 3. Dev mode: relative to project root
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

/// Validates command and args before spawning the CLI subprocess.
/// Returns Ok(()) if safe, Err(message) if rejected.
///
/// Security checks:
/// 1. Command must be in the whitelist (prevents arbitrary command execution)
/// 2. Argument count limit (prevents resource exhaustion)
/// 3. Per-argument length limit (prevents buffer exhaustion)
/// 4. Null byte rejection (prevents argument injection)
/// 5. Control character rejection (prevents terminal injection)
fn validate_cli_input(command: &str, args: &[String]) -> Result<(), String> {
    if !ALLOWED_COMMANDS.contains(&command) {
        return Err(format!("Unknown command: {}", command));
    }

    if args.len() > MAX_ARGS {
        return Err(format!("Too many arguments (max {})", MAX_ARGS));
    }

    for arg in args {
        if arg.len() > MAX_ARG_LEN {
            return Err(format!("Argument too long (max {} chars)", MAX_ARG_LEN));
        }
        // Reject null bytes (injection prevention)
        if arg.contains('\0') {
            return Err("Null bytes in arguments are not allowed".to_string());
        }
        // Reject control characters except tab/newline (terminal injection prevention)
        if arg.chars().any(|c| c.is_control() && c != '\t' && c != '\n') {
            return Err("Control characters in arguments are not allowed".to_string());
        }
    }

    Ok(())
}

#[tauri::command]
fn run_cli(app: tauri::AppHandle, command: String, args: Vec<String>) -> CliResponse {
/// Truncates stderr to 512 chars and masks common secret-bearing patterns
/// so provider-activation failures are traceable in env-manager.log without
/// leaking credentials. Bounded + best-effort scrub; logs the error message
/// shape, never a secret value.
fn scrub_stderr(s: &str) -> String {
    let mut out: String = s.chars().take(512).collect();
    for pat in ["Bearer ", "token=", "Token=", "password=", "Password=",
                "setx ", "OP_SERVICE_ACCOUNT_TOKEN=", "VAULT_TOKEN=",
                "AWS_SECRET_ACCESS_KEY=", "AWS_SESSION_TOKEN="] {
        if let Some(i) = out.find(pat) {
            let start = i + pat.len();
            let tail: String = out.chars().skip(start).take(8).collect();
            if !tail.is_empty() {
                out.replace_range(start..start + tail.len(), "<redacted>");
            }
        }
    }
    out
}

    info!("[run_cli] command={}, argument_count={}", command, args.len());

    // Validate input before doing anything
    if let Err(msg) = validate_cli_input(&command, &args) {
        warn!("[run_cli] validation failed: {}", msg);
        return CliResponse {
            success: false,
            data: None,
            error: Some(msg),
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

    let read_only = is_read_only(&command, &args);

    let mut cmd = build_cli_command(&exe_path, &command, &args);
    let start = std::time::Instant::now();

    // Execute the CLI with the appropriate lock held.
    // We use a closure to capture cmd and start, and return the output.
    // The lock guard is held for the duration of the closure execution.
    let mut exec_with_lock = || -> std::io::Result<std::process::Output> {
        cmd.output()
    };

    let output_result = if read_only {
        info!("[run_cli] acquiring READ lock for command={}, subcommand_present={}", command, args.first().is_some());
        let _guard = CLI_RWLOCK.read().unwrap_or_else(|e| e.into_inner());
        exec_with_lock()
    } else {
        info!("[run_cli] acquiring WRITE lock for command={}, subcommand_present={}", command, args.first().is_some());
        let _guard = CLI_RWLOCK.write().unwrap_or_else(|e| e.into_inner());
        exec_with_lock()
    };

    match output_result {
        Ok(output) => {
            let elapsed = start.elapsed();
            let stdout = String::from_utf8_lossy(&output.stdout).to_string();
            let stderr = String::from_utf8_lossy(&output.stderr).to_string();
            info!(
                "[run_cli] exit={}, stdout_len={}, stderr_len={}, elapsed={}ms, read_only={}",
                output.status,
                stdout.len(),
                stderr.len(),
                elapsed.as_millis(),
                read_only,
            );

            if !stderr.is_empty() {
                info!("[run_cli] stderr_present=true, stderr_len={}", stderr.len());
            }

            if !output.status.success() && !stderr.trim().is_empty() {
                warn!("[run_cli] non-zero exit stderr_hint: {}", scrub_stderr(&stderr));
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

/// Returns the path to gui-settings.json in %LOCALAPPDATA%\EnvManager\.
fn gui_settings_path() -> Option<PathBuf> {
    let local = std::env::var("LOCALAPPDATA").ok()?;
    Some(PathBuf::from(local).join("EnvManager").join("gui-settings.json"))
}

/// Reads a single key from gui-settings.json. Returns null if file/key missing.
/// Rejects keys longer than 128 chars or containing control chars (defense-in-depth).
#[tauri::command]
fn read_gui_setting(key: String) -> serde_json::Value {
    if key.len() > 128 || key.chars().any(|c| c.is_control()) {
        return serde_json::Value::Null;
    }
    match gui_settings_path() {
        Some(path) => match std::fs::read_to_string(&path) {
            Ok(content) => serde_json::from_str::<serde_json::Value>(&content)
                .ok()
                .and_then(|obj| obj.get(&key).cloned())
                .unwrap_or(serde_json::Value::Null),
            Err(_) => serde_json::Value::Null,
        },
        None => serde_json::Value::Null,
    }
}

/// Writes a single key=value pair into gui-settings.json. Creates file if missing.
/// Rejects keys > 128 chars or values > 4096 chars or containing control chars.
#[tauri::command]
fn write_gui_setting(key: String, value: String) -> bool {
    if key.len() > 128 || value.len() > 4096 || key.chars().any(|c| c.is_control()) {
        return false;
    }
    let path = match gui_settings_path() {
        Some(p) => p,
        None => return false,
    };
    let mut obj: serde_json::Value = std::fs::read_to_string(&path)
        .ok()
        .and_then(|c| serde_json::from_str(&c).ok())
        .unwrap_or_else(|| serde_json::json!({}));
    if let Some(map) = obj.as_object_mut() {
        map.insert(key, serde_json::Value::String(value));
    }
    if let Some(parent) = path.parent() {
        let _ = std::fs::create_dir_all(parent);
    }
    let json = match serde_json::to_string_pretty(&obj) {
        Ok(j) => j,
        Err(_) => return false,
    };
    // Atomic + durable write: temp file in same dir, fsync, rename onto target.
    // The previous direct std::fs::write truncated then chunked; if the app was
    // killed mid-write the durable gui-settings.json could be torn,
    // read_gui_setting then parsed null, and the frontend fell back to stale
    // localStorage (the locale-reverts-to-zh-on-restart symptom).
    // Temp+fsync+rename guarantees the file is either fully old or fully new.
    write_atomic(&path, &json)
}

/// Atomic + durable filesystem write used by write_gui_setting so GUI settings
/// persistence cannot be torn by a mid-write kill. Writes to a sibling .tmp file
/// with the current pid, fsyncs it, then atomically renames onto the target.
/// Rename within the same filesystem is atomic on NTFS and POSIX.
fn write_atomic(path: &std::path::Path, data: &str) -> bool {
    use std::fs::File;
    use std::io::Write;
    let parent = match path.parent() {
        Some(p) if !p.as_os_str().is_empty() => p,
        _ => std::path::Path::new("."),
    };
    let _ = std::fs::create_dir_all(parent);
    let stem = path.file_stem().and_then(|s| s.to_str()).unwrap_or("state");
    let pid = std::process::id();
    let tmp_path = parent.join(format!("{}.{}.tmp", stem, pid));
    let mut f = match File::create(&tmp_path) {
        Ok(f) => f,
        Err(_) => return false,
    };
    if f.write_all(data.as_bytes()).is_err() {
        let _ = std::fs::remove_file(&tmp_path);
        return false;
    }
    // fsync before rename so the renamed content is persistent across power loss,
    // not just in the FS page cache. Without this a crash after rename could lose
    // the temp file content and resurrect the stale-state bug this fixes.
    if f.sync_all().is_err() {
        let _ = std::fs::remove_file(&tmp_path);
        return false;
    }
    let ok = std::fs::rename(&tmp_path, path).is_ok();
    if !ok {
        let _ = std::fs::remove_file(&tmp_path);
    }
    ok
}

/// Emit a frontend log line into the same env-manager.log file used by
/// tauri-plugin-log. Keeps all GUI locale / settings decisions traceable in
/// the single log the user already knows from .mo. The level is one of
/// "info" / "warn" / "error" / "debug"; anything else is treated as "info".
/// A bounded message length prevents log bloat; secrets/values are never
/// stringified by the frontend caller.
#[tauri::command]
fn frontend_log(level: String, message: String) -> () {
    let bounded: String = if message.len() > 2048 {
        let mut s = message.chars().take(2048).collect::<String>();
        s.push_str("...");
        s
    } else {
        message
    };
    match level.as_str() {
        "error" => error!("[gui] {}", bounded),
        "warn" => warn!("[gui] {}", bounded),
        "debug" => debug!("[gui] {}", bounded),
        _ => info!("[gui] {}", bounded),
    }
}


#[tauri::command]
fn cli_diagnostics(app: tauri::AppHandle) -> serde_json::Value {
    let exe_dir = std::env::current_exe()
        .ok()
        .and_then(|p| p.parent().map(|d| d.display().to_string()))
        .map(|d| strip_verbatim_prefix(&d))
        .unwrap_or_default();

    let resolved = resolve_cli_path(&app)
        .map(|p| clean_path(p))
        .unwrap_or_else(|| "NOT FOUND".to_string());

    serde_json::json!({
        "resolved_cli_path": resolved,
        "gui_exe_dir": exe_dir,
        "cwd": std::env::current_dir().map(|d| clean_path(d)).unwrap_or_default(),
    })
}

/// Restores the main window from tray: un-minimize, show, bring to front, and focus.
/// Handles all hiding states: minimized, hidden to tray, or behind other windows.
fn restore_window(app: &tauri::AppHandle) {
    if let Some(window) = app.get_webview_window("main") {
        // First ensure the window is not skipped from taskbar
        // (it may have been hidden to tray which can set skip_taskbar)
        let _ = window.set_skip_taskbar(false);
        // Un-minimize if minimized
        let _ = window.unminimize();
        // Show if hidden
        let _ = window.show();
        // Bring to front
        let _ = window.set_always_on_top(true);
        let _ = window.set_always_on_top(false);
        // Focus
        let _ = window.set_focus();
        info!("[tray] restore_window called: window restored and focused");
    } else {
        warn!("[tray] restore_window: main window not found");
    }
}


/// Checks for available updates by querying the GitHub Releases API.
/// Returns the latest release tag name, release URL, and whether an update is available.
#[tauri::command]
fn check_for_updates(current_version: String) -> serde_json::Value {
    // Use PowerShell to fetch the release info (available on all Windows machines)
    let mut command = Command::new("powershell");
    command.args([
        "-NoProfile", "-Command",
        "Invoke-RestMethod -Uri 'https://api.github.com/repos/Xxx91n/env-manager/releases/latest' -Headers @{'User-Agent'='env-manager'} | ConvertTo-Json -Depth 3",
    ]);
    #[cfg(windows)]
    {
        command.creation_flags(CREATE_NO_WINDOW);
    }
    let output = command.output();

    match output {
        Ok(out) if out.status.success() => {
            let stdout = String::from_utf8_lossy(&out.stdout);
            match serde_json::from_str::<serde_json::Value>(&stdout) {
                Ok(release) => {
                    let tag = release.get("tag_name")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .trim_start_matches('v')
                        .to_string();
                    let html_url = release.get("html_url")
                        .and_then(|v| v.as_str())
                        .unwrap_or("")
                        .to_string();
                    let is_update = !tag.is_empty()
                        && tag != current_version
                        && version_is_newer(&tag, &current_version);

                    serde_json::json!({
                        "latestVersion": tag,
                        "releaseUrl": html_url,
                        "isUpdateAvailable": is_update,
                    })
                }
                Err(_) => serde_json::json!({
                    "latestVersion": "",
                    "releaseUrl": "",
                    "isUpdateAvailable": false,
                    "error": "Failed to parse release info",
                }),
            }
        }
        _ => serde_json::json!({
            "latestVersion": "",
            "releaseUrl": "",
            "isUpdateAvailable": false,
            "error": "Failed to check for updates",
        }),
    }
}

/// Compares two semantic version strings (e.g. "0.5.0" vs "0.4.1").
/// Returns true if `remote` is newer than `local`.
fn version_is_newer(remote: &str, local: &str) -> bool {
    let parse = |s: &str| -> Vec<u32> {
        s.split('.')
            .filter_map(|p| p.trim().parse::<u32>().ok())
            .collect()
    };
    let r = parse(remote);
    let l = parse(local);
    for i in 0..r.len().max(l.len()) {
        let rv = r.get(i).copied().unwrap_or(0);
        let lv = l.get(i).copied().unwrap_or(0);
        if rv > lv {
            return true;
        }
        if rv < lv {
            return false;
        }
    }
    false
}

fn main() {
    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            // If a second instance is launched, restore and focus the existing window.
           restore_window(app);
      }))
      .plugin({
          // Configure logging to write to a 'logs' directory adjacent to the exe.
           // This ensures portable versions keep logs alongside the executable,
            // while MSI installs use the standard app data path (default behavior
            // when the custom directory cannot be created).
            let log_dir = std::env::current_exe()
                .ok()
                .and_then(|p| p.parent().map(|d| d.join("logs")))
                .unwrap_or_else(|| std::path::PathBuf::from("logs"));

            // Create logs directory if it doesn't exist
            let _ = std::fs::create_dir_all(&log_dir);

            tauri_plugin_log::Builder::default()
                .level(log::LevelFilter::Info)
                .targets([
                    tauri_plugin_log::Target::new(tauri_plugin_log::TargetKind::Stdout),
                    tauri_plugin_log::Target::new(tauri_plugin_log::TargetKind::Folder {
                        path: log_dir,
                        file_name: Some("env-manager.log".to_string()),
                    }),
                ])
                .build()
        })
        .setup(|app| {
            // Build tray menu items - text will be updated via update_tray_locale
            let show_item = MenuItem::with_id(app, "show", "Show", true, None::<&str>)?;
            let quit_item = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_item, &quit_item])?;

            // Create system tray icon
            let _tray = TrayIconBuilder::with_id("main")
                .icon(app.default_window_icon().unwrap().clone())
                .tooltip("Env Manager")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| {
                    match event.id.as_ref() {
                        "show" => {
                            restore_window(app);
                        }
                        "quit" => {
                            app.exit(0);
                        }
                        _ => {}
                    }
                })
                .on_tray_icon_event(|tray, event| {
                    // Handle left-click and double-click to restore the window.
                    // Right-click is handled by Tauri automatically to show the context menu.
                    let app = tray.app_handle();
                    match event {
                        tauri::tray::TrayIconEvent::Click { button, button_state, .. } => {
                            // Only restore on left-click release (not right-click)
                            if button == tauri::tray::MouseButton::Left
                                && button_state == tauri::tray::MouseButtonState::Up
                            {
                                restore_window(app);
                            }
                        }
                        tauri::tray::TrayIconEvent::DoubleClick { .. } => {
                            restore_window(app);
                        }
                        _ => {}
                    }
                })
                .build(app)?;

            Ok(())
        })
        .on_window_event(|window, event| {
            match event {
                // Close button hides to tray instead of exiting
                WindowEvent::CloseRequested { api, .. } => {
                    let _ = window.hide();
                    api.prevent_close();
                }
                _ => {}
            }
        })
       .invoke_handler(tauri::generate_handler![
            run_cli,
            cli_diagnostics,
            update_tray_locale,
            check_for_updates,
            read_gui_setting,
            write_gui_setting,
            frontend_log,
        ])
        .run(tauri::generate_context!())
        .expect("error while running tauri application");
}

/// Updates the tray menu and tooltip based on the current GUI locale.
/// Rebuilds the menu with new translated text, then swaps it on the tray.
#[tauri::command]
fn update_tray_locale(
    app: tauri::AppHandle,
    show_text: String,
    quit_text: String,
    tooltip: String,
) {
    update_tray_locale_impl(&app, &show_text, &quit_text, &tooltip)
}

fn update_tray_locale_impl(
    app: &tauri::AppHandle,
    show_text: &str,
    quit_text: &str,
    tooltip: &str,
) {
    info!(
        "[tray] update_tray_locale: show='{}', quit='{}', tooltip='{}'",
        show_text, quit_text, tooltip
    );

    if let Some(tray) = app.tray_by_id("main") {
        match MenuItem::with_id(app, "show", show_text, true, None::<&str>) {
            Ok(show_item) => {
                match MenuItem::with_id(app, "quit", quit_text, true, None::<&str>) {
                    Ok(quit_item) => {
                        match Menu::with_items(app, &[&show_item, &quit_item]) {
                            Ok(menu) => {
                                let _ = tray.set_menu(Some(menu));
                                let _ = tray.set_tooltip(Some(tooltip));
                            }
                            Err(e) => warn!("[tray] failed to build menu: {}", e),
                        }
                    }
                    Err(e) => warn!("[tray] failed to create quit item: {}", e),
                }
            }
            Err(e) => warn!("[tray] failed to create show item: {}", e),
        }
    } else {
        warn!("[tray] tray icon not found for locale update");
    }
}
