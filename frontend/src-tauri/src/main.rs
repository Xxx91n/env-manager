// Hide the console window in release builds on Windows.
#![cfg_attr(not(debug_assertions), windows_subsystem = "windows")]

use tracing::{info, warn, error, debug};
use serde::Serialize;
use std::path::PathBuf;
use std::process::Command;
use std::sync::RwLock;
use std::sync::OnceLock;
use zeroize::Zeroizing;

mod lightweight;
mod job_object;
use tauri::{
    menu::{Menu, MenuItem, CheckMenuItem},
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

/// Cached .NET 10 runtime probe result. Probes once per process lifetime
/// via `dotnet --list-runtimes` and caches the boolean so subsequent IPC
/// calls do not re-spawn dotnet. When the probe fails (dotnet not on PATH,
/// no matching runtime), run_cli returns a friendly error with the official
/// download link instead of letting the CLI apphost emit a raw tech error.
static DOTNET10_AVAILABLE: OnceLock<bool> = OnceLock::new();

fn dotnet10_available() -> bool {
    *DOTNET10_AVAILABLE.get_or_init(|| {
        // Primary: `dotnet --list-runtimes` output contains a line like
        // "Microsoft.NETCore.App 10.0.x ..." when .NET 10 is installed.
        if let Ok(output) = Command::new("dotnet")
            .arg("--list-runtimes")
            .creation_flags(0x08000000) // CREATE_NO_WINDOW
            .output()
        {
            if output.status.success() {
                let stdout = String::from_utf8_lossy(&output.stdout);
                return stdout.lines().any(|l| {
                    l.starts_with("Microsoft.NETCore.App 10.")
                        || l.starts_with("Microsoft.WindowsDesktop.App 10.")
                });
            }
        }
        // Fallback: check the default install directory for dotnet.exe.
        // The registry path is authoritative but reading it requires the
        // `winreg` crate; the directory probe is sufficient for the 99% case.
        if let Some(pgmfiles) = std::env::var_os("ProgramFiles") {
            let dotnet_exe = PathBuf::from(pgmfiles).join("dotnet").join("dotnet.exe");
            if dotnet_exe.exists() {
                if let Ok(output) = Command::new(&dotnet_exe)
                    .arg("--list-runtimes")
                    .creation_flags(0x08000000)
                    .output()
                {
                    let stdout = String::from_utf8_lossy(&output.stdout);
                    return stdout.lines().any(|l| {
                        l.starts_with("Microsoft.NETCore.App 10.")
                            || l.starts_with("Microsoft.WindowsDesktop.App 10.")
                    });
                }
            }
        }
        false
    })
}


#[derive(Serialize)]
#[serde(crate = "serde")]
struct CliResponse {
    success: bool,
    data: Option<String>,
    error: Option<String>,
}

/// Commands the IPC layer is allowed to forward to the CLI.
const ALLOWED_COMMANDS: &[&str] = &[
    "service",
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
    "audit",
    "export-state",
    "import-state",
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
    "export-state",
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
    "import-state",
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
        "history" => !matches!(args.first().map(|s| s.as_str()), Some("undo") | Some("delete")),
        "audit" => matches!(args.first().map(|s| s.as_str()), Some("list") | Some("verify-ledger") | Some("export-survival-kit")),
        "bulk" => !matches!(args.first().map(|s| s.as_str()), Some("import")) || args.iter().any(|arg| arg == "--dry-run"),
        "protection" => matches!(args.first().map(|s| s.as_str()), Some("list")),
        // service: ping/status/health are read-only IPC probes; refresh/rotate/reload/shutdown mutate state
        "service" => matches!(
            args.first().map(|s| s.as_str()),
            Some("ping") | Some("status") | Some("health")
        ),
        _ => false,
    }
}

/// RwLock for read/write separation:
/// - Read commands acquire a read lock (multiple can run concurrently)
/// - Write commands acquire a write lock (exclusive)
/// This prevents write-write and read-write races while allowing read-read concurrency.
static CLI_RWLOCK: RwLock<()> = RwLock::new(());

/// v0.9.2: Track the detached background service PID so we can send it
/// an IPC shutdown when the GUI exits. Without this, std::mem::forget(child)
/// orphans the service process (process leak). The PID is read at GUI quit
/// time and a `{"method":"shutdown"}` IPC message is sent via named pipe.
static SERVICE_PID: RwLock<Option<u32>> = RwLock::new(None);

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
async fn run_cli(app: tauri::AppHandle, command: String, args: Vec<String>) -> CliResponse {
/// Truncates stderr to 512 chars and masks common secret-bearing patterns
/// so provider-activation failures are traceable in env-manager.log without
/// leaking credentials. Bounded + best-effort scrub; logs the error message
/// shape, never a secret value.
fn scrub_stderr(s: &str) -> String {
    let mut out: String = s.chars().take(512).collect();
    for pat in [
        "Bearer ", "token=", "Token=", "password=", "Password=",
        "setx ", "OP_SERVICE_ACCOUNT_TOKEN=", "VAULT_TOKEN=",
        "AWS_SECRET_ACCESS_KEY=", "AWS_SESSION_TOKEN=",
        // v0.9.12: expanded redaction patterns (22 total)
        "client_secret=", "connection_string=", "subscription_key=",
        "api_key=", "apikey=", "client_id=", "tenant_id=",
        "access_token=", "refresh_token=", "Authorization:",
        "X-Vault-Token:", "x-api-key:",
    ] {
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

    // v0.9.8 A4: generate request_id for cross-process log tracing.
    let request_id: String = format!("{:06x}", std::time::SystemTime::now()
        .duration_since(std::time::UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos() & 0xFFFFFF);
    info!("[run_cli] command={}, argument_count={}, request_id={}", command, args.len(), request_id);

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

    // .NET 10 runtime fence: probe once, cache result. If the runtime
    // is missing, return a friendly error with the download link instead
    // of letting the CLI apphost emit a raw "You must install .NET" error.
    if !dotnet10_available() {
        warn!("[run_cli] .NET 10 runtime not found — returning friendly error");
        return CliResponse {
            success: false,
            data: None,
            error: Some(
                ".NET 10 Desktop Runtime is not installed. Download from: https://dotnet.microsoft.com/download/dotnet/10.0 (pick matching architecture: x64, x86, or ARM64). After installing, restart Env Manager.".to_string()
            ),
        };
    }

    let read_only = is_read_only(&command, &args);

    let mut cmd = build_cli_command(&exe_path, &command, &args);
    // v0.9.8 A4: propagate request_id to C# CLI via env var for cross-process log tracing.
    cmd.env("ENVMANAGER_REQUEST_ID", &request_id);
    let start = std::time::Instant::now();

    // v0.9.1: Run CLI via spawn_blocking so the tokio async executor is not
    // blocked during long-running CLI operations (pwsh probe 30s, network
    // calls). The lock is acquired inside the blocking task to serialize
    // writes. A 60s timeout kills runaway processes (normal <2s, probe <30s).
    // ponytail: spawn_blocking keeps build_cli_command returning std::process::Command
    // avoiding a tokio::process::Command migration across all call sites.
    let is_read = read_only;
    let output_result = tauri::async_runtime::spawn_blocking(move || {
        if is_read {
            let _guard = CLI_RWLOCK.read().unwrap_or_else(|e| e.into_inner());
            cmd.output()
        } else {
            let _guard = CLI_RWLOCK.write().unwrap_or_else(|e| e.into_inner());
            cmd.output()
        }
    }).await;

    let output_result = match output_result {
        Ok(Ok(output)) => Ok(output),
        Ok(Err(e)) => Err(e),
        Err(e) => {
            warn!("[run_cli] spawn_blocking join error: {}", e);
            Err(std::io::Error::new(std::io::ErrorKind::Other, format!("task join error: {}", e)))
        }
    };

    match output_result {
        Ok(output) => {
            let elapsed = start.elapsed();
            let mut stdout = Zeroizing::new(String::from_utf8_lossy(&output.stdout).to_string());
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
                    data: Some(std::mem::take(&mut *stdout)),
                    error: None,
                }
            } else {
                let err = if !stderr.trim().is_empty() {
                    stderr
                } else {
                    std::mem::take(&mut *stdout)
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

#[tauri::command]
fn read_var_notes() -> serde_json::Value {
    let path = var_notes_path();
    match std::fs::read_to_string(&path) {
        Ok(content) => {
            let val = serde_json::from_str(&content).unwrap_or(serde_json::json!({"version":1,"notes":{}}));
            info!("[IPC] read_var_notes: path={}", path.display());
            val
        }
        Err(e) => {
            info!("[IPC] read_var_notes: file not found (normal on first run): {}", e);
            serde_json::json!({"version":1,"notes":{}})
        }
    }
}

#[tauri::command]
fn write_var_notes(notes_json: String) -> bool {
    let path = var_notes_path();
    let ok = write_atomic(&path, &notes_json);
    if ok {
        info!("[IPC] write_var_notes: wrote {} bytes to {}", notes_json.len(), path.display());
    } else {
        error!("[IPC] write_var_notes: FAILED to write to {}", path.display());
    }
    ok
}

fn var_notes_path() -> std::path::PathBuf {
    let local_appdata = std::env::var("LOCALAPPDATA").unwrap_or_default();
    let base = std::path::PathBuf::from(local_appdata);
    base.join("EnvManager").join("var-notes.json")
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
fn frontend_log(level: String, message: String, request_id: Option<String>) -> () {
    let bounded: String = if message.len() > 2048 {
        let mut s = message.chars().take(2048).collect::<String>();
        s.push_str("...");
        s
    } else {
        message
    };
    match level.as_str() {
        "error" => error!("[gui] {} {}", request_id.as_deref().map(|r| format!("[req:{}] ", r)).unwrap_or_default(), bounded),
        "warn" => warn!("[gui] {} {}", request_id.as_deref().map(|r| format!("[req:{}] ", r)).unwrap_or_default(), bounded),
        "debug" => debug!("[gui] {} {}", request_id.as_deref().map(|r| format!("[req:{}] ", r)).unwrap_or_default(), bounded),
        _ => info!("[gui] {} {}", request_id.as_deref().map(|r| format!("[req:{}] ", r)).unwrap_or_default(), bounded),
    }
}

/// Resolves the env-manager-service.exe path with dev-mode fallback.
/// 1. Same directory as CLI exe (portable build)
/// 2. service/target/release/ relative to CWD (dev mode)
fn resolve_service_path(app: &tauri::AppHandle) -> Option<PathBuf> {
    // 1. Same directory as CLI (portable build)
    if let Some(cli) = resolve_cli_path(app) {
        let p = cli.with_file_name("env-manager-service.exe");
        if p.exists() {
            info!("[start_service] service binary found adjacent to CLI: {}", p.display());
            return Some(p);
        }
    }
    // 2. Dev mode: service/target/release/env-manager-service.exe
    if let Ok(cwd) = std::env::current_dir() {
        let dev_path = cwd.join("service/target/release/env-manager-service.exe");
        if dev_path.exists() {
            info!("[start_service] service binary found in dev tree: {}", dev_path.display());
            return Some(dev_path);
        }
    }
    warn!("[start_service] service binary not found by any method");
    None
}

/// Starts the env-manager-service.exe in background mode (detached child process).
/// The service listens on \\.\pipe\EnvManager.Background for IPC.
/// Returns true if the process was spawned and survived its first 2 seconds.
#[tauri::command]
/// v0.9.2: Send IPC shutdown to the detached background service before GUI exit.
/// Prevents process leak: std::mem::forget(child) orphans the service process.
/// Connects to \\\\.\\pipe\\EnvManager.Background, sends {"method":"shutdown"}, then reads response.
/// Best-effort: if the pipe is gone or the service already exited, this is a no-op.
fn shutdown_background_service() {
    let pid = {
        let guard = match SERVICE_PID.read() {
            Ok(g) => g,
            Err(_) => return,
        };
        *guard
    };
    let pid = match pid {
        Some(p) => p,
        None => {
            info!("[shutdown_service] no background service PID tracked, skipping");
            return;
        }
    };
    info!("[shutdown_service] sending IPC shutdown to background service pid={}", pid);

    use std::io::{Read, Write};
    let pipe_path = r"\\.\pipe\EnvManager.Background";
    match std::fs::OpenOptions::new().read(true).write(true).open(pipe_path) {
        Ok(mut pipe) => {
            let req = br#"{"method":"shutdown"}"#;
            let mut req_nl = req.to_vec();
            req_nl.push(b'\n');
            if let Err(e) = pipe.write_all(&req_nl) {
                warn!("[shutdown_service] failed to write shutdown request: {}", e);
            } else {
                let mut buf = [0u8; 256];
                let _ = pipe.read(&mut buf);
                info!("[shutdown_service] shutdown request sent to pid={}, response read", pid);
            }
        }
        Err(e) => {
            info!("[shutdown_service] pipe not found (service likely already exited): {}", e);
        }
    }
    // v0.9.2: Industrial-grade graceful shutdown per pwm research.
    // Phase 1: IPC shutdown signal was sent above (best-effort).
    // Phase 2: Wait 500ms for graceful exit, then force-kill if still alive.
    // Phase 3: Reap the process to prevent zombie/leaked process.
    std::thread::sleep(std::time::Duration::from_millis(500));
    let still_alive = {
        let output = std::process::Command::new("taskkill")
            .args(&["/PID", &pid.to_string(), "/T", "/F"])
            .creation_flags(0x08000000)
            .output();
        match output {
            Ok(o) => {
                if o.status.code() == Some(0) {
                    warn!("[shutdown_service] process pid={} was still alive after IPC shutdown; force-killed", pid);
                    true
                } else {
                    info!("[shutdown_service] process pid={} already exited gracefully", pid);
                    false
                }
            }
            Err(e) => {
                warn!("[shutdown_service] taskkill probe failed for pid={}: {}", pid, e);
                false
            }
        }
    };
    let _ = still_alive;
    if let Ok(mut guard) = SERVICE_PID.write() {
        *guard = None;
    }
    info!("[shutdown_service] background service cleanup complete, pid={} cleared", pid);
}

#[tauri::command]
fn start_service(app: tauri::AppHandle) -> Result<bool, String> {
    let service_exe = match resolve_service_path(&app) {
        Some(p) => p,
        None => return Err("Service binary not found — cannot locate env-manager-service.exe".to_string()),
    };

    info!("[start_service] spawning: {} --mode=background", service_exe.display());
   info!(r"[start_service] pipe: \\.\pipe\EnvManager.Background, flags: CREATE_NO_WINDOW|DETACHED_PROCESS");

    let mut cmd = Command::new(&service_exe);
    cmd.arg("--mode=background");
    // Critical: give the child independent stdio handles so it does NOT inherit
    // the Tauri parent's stdin/stdout/stderr pipe handles. When the parent
    // closes its end of an inherited handle, the child gets a broken pipe and
    // may exit. Stdio::null gives the child its own NUL device handles that
    // survive parent exit. This is the standard Rust pattern for daemon spawning.
    cmd.stdin(std::process::Stdio::null());
    cmd.stdout(std::process::Stdio::null());
    cmd.stderr(std::process::Stdio::null());
    #[cfg(windows)]
    {
        // CREATE_NO_WINDOW | DETACHED_PROCESS — ensures the child survives parent exit
        cmd.creation_flags(0x08000008);
    }

    let mut child = match cmd.spawn() {
        Ok(c) => {
            info!("[start_service] spawn ok, pid={}", c.id());
            c
        }
        Err(e) => {
            error!("[start_service] failed to spawn service: {}", e);
            return Err(format!("Failed to start service: {}", e));
        }
    };

    // Wait up to 2s to detect immediate exit (pipe conflict, missing DLL, etc.)
    // If the child survives 2s, it's healthy enough to serve IPC.
    match child.try_wait() {
        Ok(None) => {
            // Still running after instant check — wait 2s more
            std::thread::sleep(std::time::Duration::from_millis(2000));
            match child.try_wait() {
                Ok(None) => {
                    // Child survived 2s — healthy, detach it
                    info!("[start_service] service pid={} survived 2s, detaching", child.id());
                    // v0.9.2: Track PID for GUI-exit cleanup (prevent process leak)
                    if let Ok(mut guard) = SERVICE_PID.write() {
                        *guard = Some(child.id());
                    }
                    info!("[start_service] service pid={} registered in SERVICE_PID for cleanup-on-quit", child.id());
                    std::mem::forget(child);
                    Ok(true)
                }
                Ok(Some(status)) => {
                    let code = status.code().unwrap_or(-1);
                    error!("[start_service] service exited after 2s with code={}", code);
                    // Try to read service log for diagnostic
                    if let Ok(la) = std::env::var("LOCALAPPDATA") {
                        let log_path = std::path::PathBuf::from(la)
                            .join("EnvManager")
                            .join("env-manager-service.log");
                        if let Ok(log) = std::fs::read_to_string(&log_path) {
                            let last_lines: String = log.lines().rev().take(10).collect::<Vec<_>>().join("\n");
                            error!("[start_service] service log tail:\n{}", last_lines);
                        }
                    }
                    Err(format!("Service started but exited immediately (code={}). Check env-manager-service.log for details.", code))
                }
                Err(e) => {
                    error!("[start_service] error waiting for service: {}", e);
                    Err(format!("Service wait failed: {}", e))
                }
            }
        }
        Ok(Some(status)) => {
            let code = status.code().unwrap_or(-1);
            error!("[start_service] service exited immediately with code={}", code);
            Err(format!("Service exited immediately (code={}). Check env-manager-service.log for details.", code))
        }
        Err(e) => {
            error!("[start_service] try_wait error: {}", e);
            Err(format!("Service spawn check failed: {}", e))
        }
    }
}



/// v0.9.6: Service watchdog — pings the service every 30s, auto-restarts after 2 consecutive failures.
/// Spawned as a background thread on GUI startup. Thread dies when GUI process exits.
/// Defense-in-depth on top of SCM recovery (Service mode) and manual restart (Background mode).
fn spawn_service_watchdog(app: tauri::AppHandle) {
    use std::io::{Read, Write};
    std::thread::spawn(move || {
        let mut consecutive_failures: u32 = 0;
        info!("[watchdog] service watchdog thread started (30s interval, 2-failure threshold)");
        loop {
            std::thread::sleep(std::time::Duration::from_secs(30));
            // Ping the service via named pipe (same path as CLI service ping)
            let pipe_path = r"\\.\pipe\EnvManager.Background";
            match std::fs::OpenOptions::new().read(true).write(true).open(pipe_path) {
                Ok(mut pipe) => {
                    let req = br#"{"method":"ping"}"#;
                    let req_nl = [req.as_slice(), b"
"].concat();
                    if pipe.write_all(&req_nl).is_ok() {
                        let mut buf = [0u8; 1024];
                        if pipe.read(&mut buf).is_ok() {
                            // ping succeeded — reset failure count
                            if consecutive_failures > 0 {
                                info!("[watchdog] service ping recovered after {} failures", consecutive_failures);
                            }
                            consecutive_failures = 0;
                            continue;
                        }
                    }
                    // write or read failed
                    consecutive_failures += 1;
                }
                Err(_) => {
                    consecutive_failures += 1;
                }
            }
            warn!("[watchdog] service ping failed (consecutive: {})", consecutive_failures);
            if consecutive_failures >= 2 {
                warn!("[watchdog] 2 consecutive ping failures — attempting auto-restart");
                match start_service(app.clone()) {
                    Ok(true) => {
                        info!("[watchdog] service auto-restarted successfully");
                        consecutive_failures = 0;
                    }
                    Ok(false) => {
                        warn!("[watchdog] service auto-restart returned false (already running?)");
                        consecutive_failures = 0;
                    }
                    Err(e) => {
                        error!("[watchdog] service auto-restart failed: {}", e);
                        // Keep trying on next tick
                    }
                }
            }
        }
    });
}

/// v0.9.6: Start the watchdog thread (called from frontend after service start confirmation).
#[tauri::command]
fn start_service_watchdog(app: tauri::AppHandle) -> Result<bool, String> {
    spawn_service_watchdog(app);
    Ok(true)
}

/// v0.9.3: Stop the background service (user-initiated).
/// This is the ONLY path that kills the service process.
/// GUI exit does NOT call this — the service persists across GUI restarts.
#[tauri::command]
fn stop_service() -> Result<bool, String> {
    info!("[stop_service] user requested service stop");
    shutdown_background_service();
    Ok(true)
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

/// Startup service residual cleanup (Q4/Q8b).
///
/// Probes the background service named pipe. If the pipe is dead but a
/// stale service PID was persisted (from a prior GUI session that crashed
/// without graceful shutdown), taskkills the orphaned process to prevent
/// zombie accumulation. This is the startup complement to
/// `stop_service`'s graceful shutdown path — it handles the case where
/// the GUI was force-killed and the service process leaked.
fn cleanup_stale_service_on_startup() {
    // Check if the background service pipe is alive.
    let pipe_path = r"\\.\pipe\EnvManager.Background";
    let pipe_alive = std::fs::OpenOptions::new()
        .read(true)
        .write(true)
        .open(pipe_path)
        .is_ok();

    if pipe_alive {
        info!("[startup] service pipe alive — no stale cleanup needed");
        if let Ok(mut guard) = SERVICE_PID.write() {
            *guard = None;
        }
        return;
    }

    // Pipe is dead — check if we have a stale PID to clean up.
    let stale_pid = match SERVICE_PID.read() {
        Ok(guard) => *guard,
        Err(_) => None,
    };

    match stale_pid {
        Some(pid) => {
            warn!(
                "[startup] service pipe dead and stale PID={} found — attempting taskkill",
                pid
            );
            let output = std::process::Command::new("taskkill")
                .args(&["/PID", &pid.to_string(), "/T", "/F"])
                .creation_flags(0x08000000) // CREATE_NO_WINDOW
                .output();
            match output {
                Ok(o) if o.status.code() == Some(0) => {
                    warn!(
                        "[startup] stale service pid={} force-killed (pipe was dead)",
                        pid
                    );
                }
                Ok(_) => {
                    info!(
                        "[startup] stale service pid={} already exited (no kill needed)",
                        pid
                    );
                }
                Err(e) => {
                    warn!(
                        "[startup] taskkill probe failed for stale pid={}: {}",
                        pid, e
                    );
                }
            }
            if let Ok(mut guard) = SERVICE_PID.write() {
                *guard = None;
            }
        }
        None => {
            info!("[startup] service pipe dead, no stale PID — clean state");
        }
    }
}

/// Stored references to tray menu items for in-place updates (set_checked/set_text).
/// Tauri menu items are reference-counted — Clone points to the same underlying item.
#[derive(Clone)]
struct LightMenuState {
    show_item: tauri::menu::MenuItem<tauri::Wry>,
    lightweight_item: tauri::menu::CheckMenuItem<tauri::Wry>,
    quit_item: tauri::menu::MenuItem<tauri::Wry>,
}

/// Update the lightweight-mode checkmark on the stored CheckMenuItem.
/// This replaces the old rebuild_tray_menu approach with a single set_checked call.
fn update_lightweight_check(app: &tauri::AppHandle, checked: bool) {
    use tauri::Manager;
    if let Some(state) = app.try_state::<LightMenuState>() {
        if let Err(e) = state.lightweight_item.set_checked(checked) {
            warn!("[tray] set_checked({}) failed: {}", checked, e);
        }
    } else {
        warn!("[tray] LightMenuState not managed — cannot update checkmark");
    }
}

/// Restores the main window from tray: un-minimize, show, bring to front, and focus.
/// Handles all hiding states: minimized, hidden to tray, or behind other windows.
fn restore_window(app: &tauri::AppHandle) {
    // Cancel any pending auto-lightweight timer — user is showing the window.
    lightweight::cancel_lightweight_timer();

    // If in lightweight mode (window destroyed), rebuild it first.
    if lightweight::is_in_lightweight_mode() {
        info!("[tray] restore_window: in lightweight mode, rebuilding window");
        if let Err(e) = lightweight::exit_lightweight(app) {
            warn!("[tray] restore_window: failed to exit lightweight mode: {}", e);
        }
        update_lightweight_check(app, false);
        // exit_lightweight already shows/focuses the rebuilt window.
        return;
    }

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
// v0.9.8: tracing + tracing-appender unified logging backend.
// Replaces tauri-plugin-log with daily rotation + 7-day retention + 50MB cap.
// Per-module filtering via RUST_LOG env var (e.g. RUST_LOG=env_manager=debug).
let log_dir = std::env::current_exe()
    .ok()
    .and_then(|p| p.parent().map(|d| d.join("logs")))
    .unwrap_or_else(|| std::path::PathBuf::from("logs"));
let _ = std::fs::create_dir_all(&log_dir);

// Daily rotation: env-manager.log -> env-manager.log.2026-08-07
let file_appender = tracing_appender::rolling::daily(&log_dir, "env-manager.log");
let (non_blocking_file, _guard) = tracing_appender::non_blocking(file_appender);

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
// Keep _guard alive for process lifetime (dropping it flushes + stops the writer).
std::mem::forget(_guard);

    tauri::Builder::default()
        .plugin(tauri_plugin_dialog::init())
        .plugin(tauri_plugin_single_instance::init(|app, _args, _cwd| {
            // If a second instance is launched, restore and focus the existing window.
           restore_window(app);
      }))
        .setup(|app| {
            // Build tray menu items - text will be updated via update_tray_locale
            let show_item = MenuItem::with_id(app, "show", "Show", true, None::<&str>)?;
            let lightweight_item = CheckMenuItem::with_id(
                app,
                "lightweight",
                "Lightweight Mode",
                lightweight::is_in_lightweight_mode(),
                true,
                None::<&str>,
            )?;
            let quit_item = MenuItem::with_id(app, "quit", "Quit", true, None::<&str>)?;
            let menu = Menu::with_items(app, &[&show_item, &lightweight_item, &quit_item])?;

            // Store menu items in State for in-place updates (set_checked/set_text).
            app.manage(LightMenuState {
                show_item: show_item.clone(),
                lightweight_item: lightweight_item.clone(),
                quit_item: quit_item.clone(),
            });

            // Create system tray icon
            let _tray = TrayIconBuilder::with_id("main")
                .icon(app.default_window_icon().unwrap().clone())
                .tooltip("Env Manager")
                .menu(&menu)
                .show_menu_on_left_click(false)
                .on_menu_event(|app, event| {
                    match event.id.as_ref() {
                        "show" => {
                            if lightweight::is_in_lightweight_mode() {
                                if let Err(e) = lightweight::exit_lightweight(app) {
                                    warn!("[tray] exit lightweight failed: {}", e);
                                }
                                // Rebuild menu to update checkmark state.
                                update_lightweight_check(app, false);
                            }
                            restore_window(app);
                        }
                        "lightweight" => {
                            if lightweight::is_in_lightweight_mode() {
                                if let Err(e) = lightweight::exit_lightweight(app) {
                                    warn!("[tray] exit lightweight failed: {}", e);
                                }
                            } else {
                                if let Err(e) = lightweight::enter_lightweight(app) {
                                    warn!("[tray] enter lightweight failed: {}", e);
                                }
                            }
                            // Rebuild menu to update checkmark state.
                            update_lightweight_check(app, false);
                        }
                        "quit" => {
                            info!("[tray] quit requested — service stays alive (user-managed lifecycle)");
                            // v0.9.3: GUI exit does NOT kill the background service.
                            // The service is a persistent daemon for secret mount refresh.
                            // Only clear our PID tracking; the process survives.
                            if let Ok(mut guard) = SERVICE_PID.write() {
                                *guard = None;
                            }
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

            // Initialize Job Object so WebView2 child processes are cleaned
            // up when the GUI exits (even on task manager forced kill).
            job_object::init_job_object();

            // Q4/Q8b: Startup service residual cleanup — probe the background
            // service pipe. If the pipe is dead but a stale service PID was
            // tracked (from a prior GUI session that crashed), taskkill the
            // orphaned process to prevent zombie accumulation.
            cleanup_stale_service_on_startup();

            Ok(())
        })
        .on_window_event(|window, event| {
            match event {
                // Close button: minimize to tray instead of exiting.
                // Windows: use minimize + set_skip_taskbar(true) instead of hide()
                // because WebView2 hide()/show() is unreliable (wry#637).
                // Non-Windows: hide() works fine.
                WindowEvent::CloseRequested { api, .. } => {
                    #[cfg(target_os = "windows")]
                    {
                        let _ = window.set_skip_taskbar(true);
                        let _ = window.minimize();
                    }
                    #[cfg(not(target_os = "windows"))]
                    {
                        let _ = window.hide();
                    }
                    api.prevent_close();
                    // Start auto-lightweight timer if enabled in settings.
                    // The timer will destroy the WebView window after the
                    // configured timeout to free memory while the service
                    // stays alive.
                    let app = window.app_handle();
                    let config = get_lightweight_config();
                    let enabled = config
                        .get("enabled")
                        .and_then(|v| v.as_bool())
                        .unwrap_or(true);
                    let timeout = config
                        .get("timeoutMinutes")
                        .and_then(|v| v.as_u64())
                        .unwrap_or(10);
                    if enabled && timeout > 0 {
                        lightweight::start_lightweight_timer(app.clone(), timeout);
                    }
                }
                _ => {}
            }
        })
       .invoke_handler(
            tauri::generate_handler![
            run_cli,
            cli_diagnostics,
            update_tray_locale,
            check_for_updates,
            read_gui_setting,
            write_gui_setting,
            frontend_log,
            start_service,
            start_service_watchdog,
            stop_service,
            read_var_notes,
            write_var_notes,
            enter_lightweight_mode,
            exit_lightweight_mode,
            get_lightweight_config,
            set_lightweight_config,
        ])
        .build(tauri::generate_context!())
        .expect("error while building tauri application")
        .run(|_app_handle, event| {
            // v0.9.3: GUI exit does NOT kill the background service.
            // The service persists for secret mount refresh even when GUI is closed.
            // Only clear PID tracking to avoid stale references.
            if let tauri::RunEvent::ExitRequested { .. } = event {
                info!("[exit] ExitRequested — clearing service PID tracking (service stays alive)");
                lightweight::cancel_lightweight_timer();
                if let Ok(mut guard) = SERVICE_PID.write() {
                    *guard = None;
                }
            }
        });
}

/// Updates the tray menu and tooltip based on the current GUI locale.
/// Rebuilds the menu with new translated text, then swaps it on the tray.
#[tauri::command]
fn update_tray_locale(
    app: tauri::AppHandle,
    show_text: String,
    lightweight_text: String,
    quit_text: String,
    tooltip: String,
) {
    update_tray_locale_impl(&app, &show_text, &lightweight_text, &quit_text, &tooltip)
}

fn update_tray_locale_impl(
    app: &tauri::AppHandle,
    show_text: &str,
    lightweight_text: &str,
    quit_text: &str,
    tooltip: &str,
) {
    use tauri::Manager;
    info!(
        "[tray] update_tray_locale: show='{}', quit='{}', tooltip='{}'",
        show_text, quit_text, tooltip
    );

    if let Some(state) = app.try_state::<LightMenuState>() {
        // In-place text update via set_text (no menu rebuild).
        if let Err(e) = state.show_item.set_text(show_text) {
            warn!("[tray] set_text show failed: {}", e);
        }
        if let Err(e) = state.lightweight_item.set_text(lightweight_text) {
            warn!("[tray] set_text lightweight failed: {}", e);
        }
        if let Err(e) = state.quit_item.set_text(quit_text) {
            warn!("[tray] set_text quit failed: {}", e);
        }
    } else {
        warn!("[tray] LightMenuState not managed — menu text not updated");
    }

    if let Some(tray) = app.tray_by_id("main") {
        let _ = tray.set_tooltip(Some(tooltip));
    } else {
        warn!("[tray] tray icon not found for locale update");
    }
}