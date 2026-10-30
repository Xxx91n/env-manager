// Named Pipe IPC + protocol.
// Server: listens on \\.\pipe\EnvManager.Service (service) or \\.\pipe\EnvManager.Background (background).
// Client: one-shot CLI gateway connects to pipe, sends request, prints response.
// DACL: every instance is created with `PIPE_SDDL` below (Administrators, LOCAL SYSTEM,
// owner). Anyone else is refused at the first CreateFile, so "who may connect" does not
// depend on a per-method check inside the dispatcher.
// Anti-squatting: PIPE_FIRST_PIPE_INSTANCE flag rejects pre-existing pipe.
// Design: the SCM runs the service as LOCAL SYSTEM (the installer's `ServiceInstall` sets
// no `Account=`), so the named pipe is the only channel - the CLI stays a thin gateway and
// the GUI stays UI-only. Under that identity the owner ACE in `PIPE_SDDL` is redundant with
// the SYSTEM one; it earns its place on the Background pipe, whose owner is the launching user.

use serde::{Deserialize, Serialize};
use std::io;
use std::time::Duration;
use std::sync::Arc;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::windows::named_pipe::NamedPipeServer;
use tokio::task::JoinSet; // in-flight connection handlers, joined by the stop path (#153)
use tokio_util::sync::CancellationToken;

// Architecture-recovery: these two structs are THE authoritative IPC
// schema (single source of truth). JSON Schemas and sample payloads are exported
// from them by the golden test in `tests::export_contract_golden` into docs/schemas/
// for the C# and TS contract tests to consume. Field renames must regenerate the
// golden files explicitly (see docs/architecture.md "IPC Schema Contract"), so
// accidental drift fails the contract tests instead of shipping.
#[cfg(test)]
use schemars::JsonSchema;

/// IPC request from CLI/GUI to the service.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[cfg_attr(test, derive(JsonSchema))]
pub struct IpcRequest {
    pub method: String,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub mount_id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub request_id: Option<String>,
}

/// IPC response from the service to CLI/GUI.
#[derive(Debug, Clone, Serialize, Deserialize)]
#[cfg_attr(test, derive(JsonSchema))]
pub struct IpcResponse {
    pub ok: bool,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub data: Option<serde_json::Value>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub message: Option<String>,
    #[serde(skip_serializing_if = "Option::is_none")]
    pub id: Option<String>,
}

impl IpcResponse {
    pub fn ok(data: serde_json::Value) -> Self {
        Self { ok: true, data: Some(data), message: None, id: None }
    }
    pub fn err(msg: &str) -> Self {
        Self { ok: false, data: None, message: Some(msg.into()), id: None }
    }
}

/// Start the IPC named pipe server. Runs indefinitely.
/// `use_first_pipe_instance`: true for Service mode (SCM guarantees single instance),
/// false for Background mode (previous instance pipe handle may linger in OS kernel,
/// PIPE_FIRST_PIPE_INSTANCE gets os error 5).

// Phase 3: connection counter for leak diagnostics
use std::sync::atomic::{AtomicU64, Ordering};
static CONNECTION_COUNT: AtomicU64 = AtomicU64::new(0);

/// The declared DACL of every pipe instance, in both modes.
///
/// - `FA` (FILE_ALL_ACCESS) for `BA` (Built-in Administrators) and `SY` (LOCAL SYSTEM);
/// - `GA` (GENERIC_ALL) for `OW` (OWNER RIGHTS), the SID of whichever principal created
///   the instance.
///
/// The owner ACE is what keeps Background mode usable: that pipe is created by the user
/// who launched the GUI, so the owner is exactly the identity that must reach it. It is
/// also why this crate has no "current interactive user" concept - a service runs in
/// session 0, where that user either does not exist or is the wrong answer under RDP and
/// fast user switching, and asking would grant the wrong principal.
///
/// The accepted cost: a non-administrator can no longer open the machine-level
/// `EnvManager.Service` pipe at all. Before this descriptor they could open it (the
/// default descriptor granted Everyone and Anonymous read access) but never complete a
/// request, so the tightening removes a read channel rather than a capability.
const PIPE_SDDL: &str = "D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;GA;;;OW)";

/// Create one pipe instance. `first_pipe_instance` sets PIPE_FIRST_PIPE_INSTANCE.
///
/// The descriptor travels in `lpSecurityAttributes` at creation time, which is the only
/// route that works. Both post-creation routes were measured and rejected:
///
/// - `SetNamedSecurityInfoW` by name answers `87 ERROR_INVALID_PARAMETER` for a named
///   pipe, so the "apply after create" helper this crate used to carry could never have
///   had an effect - wiring it up would have changed nothing;
/// - setting the DACL on each instance through a handle re-opens the instance the accept
///   loop just created and takes the listener down (every later client gets
///   `ERROR_FILE_NOT_FOUND`, sometimes `ERROR_PIPE_BUSY`).
///
/// `PIPE_FIRST_PIPE_INSTANCE` still applies: the flag and the security attributes are
/// both arguments to the same `CreateNamedPipeW` call.
fn create_instance(pipe_name: &str, first_pipe_instance: bool) -> io::Result<NamedPipeServer> {
    let mut options = tokio::net::windows::named_pipe::ServerOptions::new();
    if first_pipe_instance {
        options.first_pipe_instance(true);
    }
    create_with_declared_dacl(&options, pipe_name)
}

/// Hand [`PIPE_SDDL`] to `CreateNamedPipeW` as `lpSecurityAttributes`.
///
/// Fail-closed on purpose: a conversion failure returns the OS error instead of falling
/// back to `ServerOptions::create`, because that fallback is the default descriptor -
/// Everyone and Anonymous readable. A constant that cannot be converted is a build-level
/// defect, and the caller's retry loop reports it as "cannot listen" rather than serving
/// on a wider descriptor than the code declares.
fn create_with_declared_dacl(
    options: &tokio::net::windows::named_pipe::ServerOptions,
    pipe_name: &str,
) -> io::Result<NamedPipeServer> {
    use std::ffi::c_void;
    use windows_sys::Win32::Security::SECURITY_ATTRIBUTES;

    let descriptor = DeclaredSecurityDescriptor::new()?;
    let mut attributes = SECURITY_ATTRIBUTES {
        nLength: std::mem::size_of::<SECURITY_ATTRIBUTES>() as u32,
        lpSecurityDescriptor: descriptor.as_ptr(),
        bInheritHandle: 0,
    };
    // Safety: `attributes` outlives the call and its descriptor pointer stays valid for
    // the whole call (`descriptor` is dropped only afterwards). `CreateNamedPipeW` copies
    // the descriptor into the pipe object, so freeing it once the call returns is correct.
    unsafe {
        options.create_with_security_attributes_raw(
            pipe_name,
            &mut attributes as *mut SECURITY_ATTRIBUTES as *mut c_void,
        )
    }
}

/// [`PIPE_SDDL`] converted to a self-freeing security descriptor.
///
/// The guard exists so an early return between conversion and the `CreateNamedPipeW` call
/// cannot leak the descriptor: `LocalFree` is the only way to release it.
struct DeclaredSecurityDescriptor(*mut std::ffi::c_void);

impl DeclaredSecurityDescriptor {
    fn new() -> io::Result<Self> {
        use windows_sys::Win32::Security::Authorization::{
            ConvertStringSecurityDescriptorToSecurityDescriptorW, SDDL_REVISION_1,
        };

        let sddl: Vec<u16> = PIPE_SDDL
            .encode_utf16()
            .chain(std::iter::once(0))
            .collect();
        let mut raw: *mut std::ffi::c_void = std::ptr::null_mut();
        let converted = unsafe {
            ConvertStringSecurityDescriptorToSecurityDescriptorW(
                sddl.as_ptr(),
                SDDL_REVISION_1,
                &mut raw,
                std::ptr::null_mut(),
            )
        };
        if converted == 0 {
            return Err(io::Error::last_os_error());
        }
        Ok(Self(raw))
    }

    fn as_ptr(&self) -> *mut std::ffi::c_void {
        self.0
    }
}

impl Drop for DeclaredSecurityDescriptor {
    fn drop(&mut self) {
        if !self.0.is_null() {
            unsafe {
                windows_sys::Win32::Foundation::LocalFree(self.0);
            }
        }
    }
}

/// Wait out a retry delay. `false` means shutdown cancelled the wait first.
async fn backoff(delay: Duration, shutdown: &CancellationToken) -> bool {
    tokio::select! {
        _ = tokio::time::sleep(delay) => true,
        _ = shutdown.cancelled() => false,
    }
}

/// Create the first listener, with retries, before anything is served.
///
/// The retry policy differs by mode on purpose:
/// - Service mode (`first_pipe_instance = true`) keeps the anti-squatting flag and
///   retries once, which absorbs a stale instance left by a predecessor that was
///   stopped a moment ago. A second failure is returned to the caller: the process
///   must not claim `SERVICE_RUNNING` without a listener (#149).
/// - Background mode skips the flag (a previous instance's handle can linger in the
///   kernel after shutdown, which makes the flag fail with os error 5 -- v0.9.2 fix)
///   and retries every 5s indefinitely, as it always has.
pub async fn listen(
    pipe_name: &str,
    first_pipe_instance: bool,
    shutdown: &CancellationToken,
) -> io::Result<NamedPipeServer> {
    let mut attempts = 0u32;
    loop {
        match create_instance(pipe_name, first_pipe_instance) {
            Ok(server) => {
                if attempts == 0 && first_pipe_instance {
                    tracing::info!("First pipe instance created (anti-squatting check passed)");
                } else if attempts > 0 {
                    tracing::info!("Pipe listener created after {} failed attempt(s)", attempts);
                }
                return Ok(server);
            }
            Err(e) => {
                attempts += 1;
                let delay = if first_pipe_instance {
                    if attempts > 1 {
                        tracing::error!(
                            "Pipe creation failed after retry: {}. Service cannot start safely.",
                            e
                        );
                        return Err(e);
                    }
                    tracing::warn!(
                        "Pipe creation failed on first attempt: {}. Retrying in 1s...",
                        e
                    );
                    Duration::from_secs(1)
                } else {
                    tracing::error!("Failed to create named pipe instance: {}", e);
                    Duration::from_secs(5)
                };
                if !backoff(delay, shutdown).await {
                    return Err(io::Error::new(
                        io::ErrorKind::Interrupted,
                        "shutdown requested while creating the pipe listener",
                    ));
                }
            }
        }
    }
}

/// Serve an already-created listener. Used by Service mode, which reports
/// `SERVICE_RUNNING` between `listen` and this call (#149).
pub async fn serve(
    pipe_name: &'static str,
    first: NamedPipeServer,
    shutdown: Arc<CancellationToken>,
) {
    accept_loop(pipe_name, shutdown, Some(first)).await;
}

/// `listen` + accept loop in one call, the shape the server had before Service mode
/// needed to report `SERVICE_RUNNING` between the two steps. Background mode calls
/// this directly (`crate::run_background_server`); Service mode calls `listen` and
/// `serve` separately through `crate::run_server`.
pub async fn start_ipc_server(
    pipe_name: &'static str,
    shutdown: Arc<CancellationToken>,
    use_first_pipe_instance: bool,
) {
    tracing::info!("IPC server listening on {}", pipe_name);
    let first = match listen(pipe_name, use_first_pipe_instance, &shutdown).await {
        Ok(server) => server,
        Err(e) => {
            if !shutdown.is_cancelled() {
                tracing::error!("IPC server cannot listen on {}: {}", pipe_name, e);
            }
            return;
        }
    };
    accept_loop(pipe_name, shutdown, Some(first)).await;
}

/// Accept loop: serve `initial` first (when the caller already created the
/// listener), then one fresh instance per connection.
async fn accept_loop(
    pipe_name: &'static str,
    shutdown: Arc<CancellationToken>,
    mut next: Option<NamedPipeServer>,
) {
    // Connection handlers are tracked here rather than left detached. A `rotate` handler
    // owns a CLI child, and the stop path below waits for the handler: reporting
    // `SERVICE_STOPPED` while one is still live is exactly the silent "stopped, but
    // something can still write profiles.json" window #153 closes.
    let mut handlers: JoinSet<()> = JoinSet::new();

    loop {
        // Reclaim finished handlers before anything else: a `JoinSet` only drops completed
        // tasks when it is polled, so without this every handler ever served would stay in
        // the set. While the loop is parked in `connect()` a handler that finishes waits
        // for the next turn (or for the stop drain below), which bounds the set by
        // connection activity rather than by uptime.
        while let Some(finished) = handlers.try_join_next() {
            if let Err(e) = finished {
                tracing::warn!("IPC connection handler task failed: {}", e);
            }
        }
        // Check shutdown before creating next pipe instance
        if shutdown.is_cancelled() {
            tracing::info!("IPC server cancelled, exiting");
            break;
        }
        let server = match next.take() {
            Some(server) => server,
            None => match create_instance(pipe_name, false) {
                Ok(server) => server,
                Err(e) => {
                    tracing::error!("Failed to create named pipe instance: {}", e);
                    if !backoff(Duration::from_secs(5), &shutdown).await {
                        tracing::info!("IPC server cancelled while creating an instance, exiting");
                        break;
                    }
                    continue;
                }
            },
        };

        tokio::select! {
            result = server.connect() => {
                match result {
                    Ok(()) => {}
                    Err(e) => {
                        tracing::warn!("Client connect failed: {}", e);
                        continue;
                    }
                }
            }
            _ = shutdown.cancelled() => {
                tracing::info!("IPC server cancelled during connect, exiting");
                break;
            }
        }

        let conn_token = shutdown.clone();
        handlers.spawn(async move {
            if let Err(e) = handle_connection(server, conn_token).await {
                tracing::warn!("Connection handler error: {}", e);
            }
        });
    }

    // Stop path: join the in-flight handlers before returning, so the caller cannot report
    // a stopped service with a rotation still owned by one of them. Each handler races its
    // child against the same token (`reconcile::run_cli_child`), so this normally returns
    // as soon as the children are terminated and reaped.
    //
    // The window is `SHUTDOWN_DRAIN` - the same budget `crate::run_until_exit` already
    // spends waiting for this task - so the compile-time stop budget in `crate::scm` still
    // bounds the whole chain. If it does expire, dropping the set aborts the handlers and
    // `kill_on_drop` still terminates the child; only the reap becomes best-effort, which
    // is why this join is the guarantee and the timeout is the net.
    let drain = async { while handlers.join_next().await.is_some() {} };
    let timed_out = tokio::time::timeout(crate::SHUTDOWN_DRAIN, drain).await.is_err();
    if timed_out {
        tracing::warn!(
            "IPC connection handlers still running after {:?}",
            crate::SHUTDOWN_DRAIN
        );
    }
}

/// Handle a single client connection: read one request line, process, write one response line.
///
/// `shutdown` is the service stop token, forwarded to the verbs that delegate to a CLI
/// subprocess so the child is converged with the stop instead of orphaned by it (#153).
/// `accept_loop` tracks this handler and joins it before the server body returns.
async fn handle_connection(mut server: NamedPipeServer, shutdown: Arc<CancellationToken>) -> io::Result<()> {
    tracing::info!("IPC connection accepted");
    let conn_id = CONNECTION_COUNT.fetch_add(1, Ordering::Relaxed) + 1;
    tracing::info!("IPC connection #{} (total: {})", conn_id, CONNECTION_COUNT.load(Ordering::Relaxed));
    let mut buf = Vec::with_capacity(4096);
    let mut byte = [0u8; 1];
    loop {
        match server.read(&mut byte).await {
            Ok(0) => break,
            Ok(_) => {
                if byte[0] == b'\n' { break; }
                buf.push(byte[0]);
                if buf.len() > 65536 {
                    tracing::warn!("IPC request too large ({} bytes), rejecting", buf.len());
                    return Err(io::Error::new(io::ErrorKind::InvalidInput, "request too large"));
                }
            }
            Err(e) => return Err(e),
        }
    }

    let request: IpcRequest = match serde_json::from_slice(&buf) {
        Ok(r) => r,
        Err(e) => {
            tracing::warn!("IPC invalid request: {}", e);
            let resp = IpcResponse::err(&format!("invalid request: {}", e));
            let line = serde_json::to_string(&resp).unwrap_or_default();
            server.write_all((line + "\n").as_bytes()).await?;
            return Ok(());
        }
    };

    let response = process_request(&request, &shutdown).await;
    tracing::info!("IPC response: ok={} method={}", response.ok, request.method);
    let line = serde_json::to_string(&response).unwrap_or_default();
    server.write_all((line + "\n").as_bytes()).await?;
    server.flush().await?;
    tracing::info!("IPC connection #{} closed (total accepted: {})", conn_id, CONNECTION_COUNT.load(Ordering::Relaxed));
    Ok(())
}

async fn process_request(req: &IpcRequest, shutdown: &CancellationToken) -> IpcResponse {
    tracing::info!("IPC request: method={} request_id={}", req.method, req.request_id.as_deref().unwrap_or("-"));
    match req.method.as_str() {
        // enriched ping with heartbeat data (uptime, reconcile status, mount health)
        "ping" => {
            let uptime = crate::get_process_uptime();
            IpcResponse::ok(serde_json::json!({
                "pong": true,
                "uptime_seconds": uptime,
            }))
        }
        "status" => {
            let mount_path = crate::secret_mount_path();
            let exists = mount_path.exists();
            IpcResponse::ok(serde_json::json!({
                "running": true,
                "mountFile": exists,
                "mountPath": mount_path.to_string_lossy(),
            }))
        }
        "health" => {
            match crate::reconcile::get_mount_health().await {
                Ok(summary) => IpcResponse::ok(summary),
                Err(e) => IpcResponse::err(&format!("health check failed: {}", e)),
            }
        }
        "refresh" => {
            let mount_id = req.mount_id.as_deref().unwrap_or("");
            if mount_id.is_empty() {
                return IpcResponse::err("refresh requires mountId");
            }
            match crate::reconcile::refresh_mount(mount_id).await {
                Ok(info) => IpcResponse::ok(info),
                Err(e) => IpcResponse::err(&format!("refresh failed: {}", e)),
            }
        }
        "rotate" => {
            let mount_id = req.mount_id.as_deref().unwrap_or("");
            if mount_id.is_empty() {
                return IpcResponse::err("rotate requires mountId");
            }
            match crate::reconcile::rotate_mount(mount_id, shutdown).await {
                Ok(info) => IpcResponse::ok(info),
                Err(e) => IpcResponse::err(&format!("rotate failed: {}", e)),
            }
        }
        "reload" => {
            tracing::info!("Reload requested via IPC");
            IpcResponse::ok(serde_json::json!({"reloaded": true}))
        }
        "shutdown" => {
            tracing::info!("Shutdown requested via IPC — cancelling all tasks");
            shutdown.cancel();
            IpcResponse::ok(serde_json::json!({"shuttingDown": true}))
        }
        _ => IpcResponse::err(&format!("unknown method: {}", req.method)),
    }
}

/// CLI gateway: connect to the running service pipe, send a request, read response.
/// `ClientOptions::open()` is synchronous (returns Result, not Future).
/// We use `blocking_io::spawn_blocking` or just call it directly (it's fast).
pub async fn cli_gateway(pipe_name: &str) -> Result<(), String> {
    use tokio::net::windows::named_pipe::ClientOptions;

    let client = ClientOptions::new()
        .open(pipe_name)
        .map_err(|e| format!("failed to connect to service: {}", e))?;

    let mut client = client;

    let mut input = String::new();
    io::stdin()
        .read_line(&mut input)
        .map_err(|e| format!("failed to read stdin: {}", e))?;

    if input.is_empty() {
        return Err("no request provided on stdin".into());
    }

    client
        .write_all(input.as_bytes())
        .await
        .map_err(|e| format!("failed to write to service: {}", e))?;
    client
        .flush()
        .await
        .map_err(|e| format!("failed to flush to service: {}", e))?;

    let mut resp_buf = Vec::with_capacity(4096);
    let mut byte = [0u8; 1];
    loop {
        match tokio::time::timeout(
            Duration::from_secs(30),
            client.read(&mut byte),
        )
        .await
        {
            Ok(Ok(0)) => break,
            Ok(Ok(_)) => {
                if byte[0] == b'\n' { break; }
                resp_buf.push(byte[0]);
                if resp_buf.len() > 65536 {
                    return Err("response too large".into());
                }
            }
            Ok(Err(e)) => return Err(format!("read error: {}", e)),
            Err(_) => return Err("timeout waiting for service response".into()),
        }
    }

    let resp_str = String::from_utf8_lossy(&resp_buf);
    print!("{}", resp_str);
    if !resp_str.ends_with('\n') {
        println!();
    }

    Ok(())
}

#[cfg(test)]
mod tests {
    use super::*;
    use schemars::schema_for;

    /// Golden files live in the repo docs/schemas/ directory. Resolve by
    /// walking up from CARGO_MANIFEST_DIR (service/) to the repo root, the
    /// only parent that contains docs/schemas/.
    fn golden_dir() -> std::path::PathBuf {
        let mut dir = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"));
        loop {
            let docs = dir.join("docs");
            if docs.is_dir() {
                // Golden files are committed; create_dir_all only matters for
                // first-time generation on a tree without docs/schemas/ yet.
                let schemas = docs.join("schemas");
                std::fs::create_dir_all(&schemas).unwrap();
                return schemas;
            }
            if !dir.pop() {
                panic!("docs/ not found above {}", env!("CARGO_MANIFEST_DIR"));
            }
        }
    }

    fn read_or_none(path: &std::path::Path) -> Option<String> {
        std::fs::read_to_string(path).ok()
    }

    /// Regeneration escape hatch: ENVMANAGER_REGENERATE_IPC_GOLDEN=1 overwrites
    /// the golden files instead of failing on drift. Intended for deliberate
    /// schema changes; every regeneration is a visible diff in review.
    fn regenerate() -> bool {
        std::env::var("ENVMANAGER_REGENERATE_IPC_GOLDEN").ok().as_deref() == Some("1")
    }

    fn check_golden(path: &std::path::Path, generated: &serde_json::Value, label: &str) {
        let generated_str = serde_json::to_string_pretty(generated).unwrap() + "\n";
        if regenerate() || !path.exists() {
            std::fs::write(path, &generated_str).unwrap();
            println!("wrote golden {} ({} bytes)", label, generated_str.len());
            return;
        }
        let on_disk = read_or_none(path).unwrap_or_default();
        assert_eq!(
            on_disk, generated_str,
            "{label} drifted from the authoritative Rust schema in service/src/ipc.rs.\n\
             If the change is deliberate, regenerate with:\n\
             ENVMANAGER_REGENERATE_IPC_GOLDEN=1 cargo test -p env-manager-service ipc\n\
             then update the C#/TS contract tests and docs/architecture.md."
        );
    }

    /// Single source of truth: exports the JSON Schemas for IpcRequest and
    /// IpcResponse plus fixed sample payloads. CI runs this read-only; drift
    /// (e.g. a renamed field) fails here, the C# xUnit contract tests and the
    /// TS vitest contract tests.
    #[test]
    fn export_contract_golden() {
        let schema = serde_json::json!({
            "$schema": "http://json-schema.org/draft-07/schema#",
            "title": "env-manager-service IPC protocol v1",
            "IpcRequest": schema_for!(IpcRequest),
            "IpcResponse": schema_for!(IpcResponse),
        });
        check_golden(&golden_dir().join("env-manager-service-ipc.schema.json"), &schema, "ipc schema");

        let samples = serde_json::json!({
            "requests": [
                { "name": "ping_minimal", "note": "Tauri watchdog + startup probe form", "payload": { "method": "ping" } },
                { "name": "status", "note": "CLI gateway form with request_id", "payload": { "method": "status", "request_id": "1425300042" } },
                { "name": "health", "payload": { "method": "health", "request_id": "1425300042" } },
                { "name": "refresh", "payload": { "method": "refresh", "mount_id": "vault-team", "request_id": "1425300042" } },
                { "name": "rotate", "payload": { "method": "rotate", "mount_id": "vault-team", "request_id": "1425300042" } },
                { "name": "reload", "payload": { "method": "reload", "request_id": "1425300042" } },
                { "name": "shutdown", "payload": { "method": "shutdown" } }
            ],
            "responses": [
                { "name": "ok_status", "payload": { "ok": true, "data": { "running": true, "mountFile": true, "mountPath": "C:\\ProgramData\\EnvManager\\secretMount.json" } } },
                { "name": "ok_ping", "payload": { "ok": true, "data": { "pong": true, "uptime_seconds": 3600 } } },
                { "name": "ok_health", "payload": { "ok": true, "data": { "mounts": [
                    { "id": "vault-team", "provider": "vault-kv2", "name": "TEAM_TOKEN", "refreshPolicy": "Renew", "healthy": true, "lastFetchedAt": "2026-08-31T10:00:00+00:00", "lastRotatedAt": null, "message": null },
                    { "id": "cred-app", "provider": "credential-manager", "name": "APP_KEY", "refreshPolicy": "CreatedOnly", "healthy": false, "message": "expired" }
                ] } } },
                { "name": "ok_refresh", "payload": { "ok": true, "data": { "mountId": "vault-team", "refreshed": true, "lastFetchedAt": "2026-08-31T10:00:00+00:00" } } },
                { "name": "ok_reload", "payload": { "ok": true, "data": { "reloaded": true } } },
                { "name": "ok_shutdown", "payload": { "ok": true, "data": { "shuttingDown": true } } },
                { "name": "err_unknown_method", "payload": { "ok": false, "message": "unknown method: frobnicate" } },
                { "name": "err_refresh_missing_mount", "payload": { "ok": false, "message": "refresh requires mountId" } },
                { "name": "cli_degraded_not_running", "note": "C# gateway-side failure envelope; extra `state` field is tolerated by consumers", "payload": { "ok": false, "state": "not_running", "message": "service not_running at \\\\.\\pipe\\EnvManager.Background" } }
            ]
        });
        check_golden(&golden_dir().join("ipc-samples.json"), &samples, "ipc samples");
    }

    /// Every request sample must round-trip through the authoritative type.
    #[test]
    fn request_samples_round_trip() {
        let raw = std::fs::read_to_string(golden_dir().join("ipc-samples.json")).unwrap();
        let samples: serde_json::Value = serde_json::from_str(&raw).unwrap();
        for sample in samples["requests"].as_array().unwrap() {
            let req: IpcRequest = serde_json::from_value(sample["payload"].clone())
                .unwrap_or_else(|e| panic!("sample {} failed to deserialize: {}", sample["name"], e));
            assert!(!req.method.is_empty());
        }
    }

    /// Every response sample must round-trip through the authoritative type
    /// (unknown extra keys like the CLI gateway `state` field are tolerated).
    #[test]
    fn response_samples_round_trip() {
        let raw = std::fs::read_to_string(golden_dir().join("ipc-samples.json")).unwrap();
        let samples: serde_json::Value = serde_json::from_str(&raw).unwrap();
        for sample in samples["responses"].as_array().unwrap() {
            let resp: IpcResponse = serde_json::from_value(sample["payload"].clone())
                .unwrap_or_else(|e| panic!("sample {} failed to deserialize: {}", sample["name"], e));
            if resp.ok {
                assert!(resp.data.is_some(), "sample {} should carry data", sample["name"]);
            }
        }
    }
}

/// Pipe security: the DACL is a property of the instance, so it is asserted against a real
/// instance rather than against the call path that creates one.
///
/// The effect layer - "a non-privileged identity is refused `err=5`, the two shipped
/// clients still answer pong" - needs a second identity and a LocalSystem service, so it
/// lives in the dispatch-only observation job (`scripts/probe-pipe-dacl.ps1`, wired by
/// `.github/workflows/pipe-dacl-observation.yml`) and not here.
#[cfg(test)]
mod pipe_security_tests {
    use super::*;
    use windows_sys::Win32::Foundation::{CloseHandle, HANDLE, INVALID_HANDLE_VALUE};
    use windows_sys::Win32::Security::Authorization::{
        ConvertSecurityDescriptorToStringSecurityDescriptorW, GetSecurityInfo, SE_KERNEL_OBJECT,
        SDDL_REVISION_1,
    };
    use windows_sys::Win32::Security::DACL_SECURITY_INFORMATION;
    use windows_sys::Win32::Storage::FileSystem::{
        CreateFileW, FILE_SHARE_READ, FILE_SHARE_WRITE, OPEN_EXISTING,
    };

    /// READ_CONTROL, the access `GetSecurityInfo` needs to read a DACL.
    const READ_CONTROL: u32 = 0x0002_0000;

    /// A pipe name no real service and no sibling test uses, so the assertions cannot be
    /// answered by somebody else's instance.
    fn test_pipe(label: &str) -> String {
        format!(r"\\.\pipe\EnvManager.Test{}{}", label, std::process::id())
    }

    /// Read the live DACL of a pipe instance through a handle.
    ///
    /// By name is not an option: `GetNamedSecurityInfoW` answers `87
    /// ERROR_INVALID_PARAMETER` for `\\.\pipe\<name>`, which is the same reason the
    /// deleted post-creation helper could never have taken effect. A handle opened for
    /// READ_CONTROL plus `SE_KERNEL_OBJECT` is the route the pipe tools use.
    ///
    /// Opening the client side consumes the instance the server is waiting on, which is
    /// exactly what a client does; the caller drops the server afterwards either way.
    fn dacl_sddl(pipe: &str) -> String {
        let wide: Vec<u16> = pipe.encode_utf16().chain(std::iter::once(0)).collect();
        let handle: HANDLE = unsafe {
            CreateFileW(
                wide.as_ptr(),
                READ_CONTROL,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                std::ptr::null(),
                OPEN_EXISTING,
                0,
                std::ptr::null_mut(),
            )
        };
        assert!(
            handle != INVALID_HANDLE_VALUE,
            "cannot open {pipe} for READ_CONTROL: {}",
            std::io::Error::last_os_error()
        );
        let result = (|| {
            let (mut owner, mut group, mut dacl, mut sacl) = (
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
                std::ptr::null_mut(),
            );
            let mut sd: *mut std::ffi::c_void = std::ptr::null_mut();
            let rc = unsafe {
                GetSecurityInfo(
                    handle,
                    SE_KERNEL_OBJECT,
                    DACL_SECURITY_INFORMATION,
                    &mut owner,
                    &mut group,
                    &mut dacl,
                    &mut sacl,
                    &mut sd,
                )
            };
            assert_eq!(rc, 0, "GetSecurityInfo on {pipe} failed with {rc}");
            let mut text: *mut u16 = std::ptr::null_mut();
            let mut chars: u32 = 0;
            let converted = unsafe {
                ConvertSecurityDescriptorToStringSecurityDescriptorW(
                    sd,
                    SDDL_REVISION_1,
                    DACL_SECURITY_INFORMATION,
                    &mut text,
                    &mut chars,
                )
            };
            assert!(converted != 0, "SDDL conversion failed on {pipe}");
            let mut buf = Vec::with_capacity(chars as usize);
            let mut cursor = text;
            while unsafe { *cursor } != 0 {
                buf.push(unsafe { *cursor });
                cursor = unsafe { cursor.add(1) };
            }
            unsafe {
                windows_sys::Win32::Foundation::LocalFree(sd);
                windows_sys::Win32::Foundation::LocalFree(text as *mut std::ffi::c_void);
            }
            String::from_utf16(&buf).expect("DACL SDDL is not valid UTF-16")
        })();
        unsafe { CloseHandle(handle) };
        result
    }

    /// Guards the constant itself: widening it (Everyone, Anonymous, Authenticated Users,
    /// Users) or dropping one of the three grantees has to fail here rather than silently
    /// change who may connect to a machine-level pipe.
    #[test]
    fn declared_sddl_grants_only_administrators_system_and_the_owner() {
        assert_eq!(PIPE_SDDL, "D:(A;;FA;;;BA)(A;;FA;;;SY)(A;;GA;;;OW)");
        for granted in ["(A;;FA;;;BA)", "(A;;FA;;;SY)", "(A;;GA;;;OW)"] {
            assert!(
                PIPE_SDDL.contains(granted),
                "declared DACL lost {granted}: {PIPE_SDDL}"
            );
        }
        for widened in [";;;WD", ";;;AN", ";;;AU", ";;;BU", ";;;IU"] {
            assert!(
                !PIPE_SDDL.contains(widened),
                "declared DACL grants {widened}: {PIPE_SDDL}"
            );
        }
    }

    /// The regression this crate actually needed: an instance created by `create_instance`
    /// must carry the declared descriptor, not the token default. Asserted on the live
    /// object, so removing the security attributes (or reverting to the default create)
    /// fails here even though the code still compiles.
    ///
    /// The expectation spells the owner ACE as `FA`, not the declared `GA`: the kernel maps
    /// generic rights onto the object type's specific rights when the descriptor is
    /// assigned, so `GA` on a pipe object is stored as `FILE_ALL_ACCESS` and reads back as
    /// `FA`. Asserting the declared spelling here would fail on a descriptor that was in
    /// fact applied correctly.
    ///
    /// Runs on a tokio runtime because a pipe handle has to register with the reactor to
    /// become a `NamedPipeServer`, not because the assertion needs async.
    #[tokio::test]
    async fn created_instance_carries_the_declared_dacl() {
        let pipe = test_pipe("Dacl");
        let server = create_instance(&pipe, true).expect("create instance");
        let sddl = dacl_sddl(&pipe);
        drop(server);

        assert_eq!(
            sddl.matches("(A;").count(),
            3,
            "the instance DACL is not the three declared ACEs: {sddl}"
        );
        for granted in ["(A;;FA;;;BA)", "(A;;FA;;;SY)", "(A;;FA;;;OW)"] {
            assert!(sddl.contains(granted), "instance DACL lost {granted}: {sddl}");
        }
        for widened in [";;;WD", ";;;AN"] {
            assert!(
                !sddl.contains(widened),
                "instance DACL still grants {widened} (the default descriptor): {sddl}"
            );
        }
    }

    /// Anti-squatting must survive the switch to creation-time security attributes: the
    /// flag and the descriptor are arguments to the same call, so this is the regression
    /// the descriptor change could plausibly have caused.
    ///
    /// The error code is asserted, not just "some error": a failure for any other reason
    /// (a descriptor that no longer converts, a broken option) would otherwise read as a
    /// passing anti-squatting check. `5` is `ERROR_ACCESS_DENIED`, the documented answer
    /// when the name is already owned; `231` is `ERROR_PIPE_BUSY`, which a named pipe can
    /// return instead.
    #[tokio::test]
    async fn first_pipe_instance_flag_still_refuses_a_second_creator() {
        let pipe = test_pipe("Squat");
        let _first = create_instance(&pipe, true).expect("first creator must win");
        let second = create_instance(&pipe, true);
        let code = second.as_ref().err().and_then(|e| e.raw_os_error());
        assert!(
            matches!(code, Some(5) | Some(231)),
            "a second PIPE_FIRST_PIPE_INSTANCE creator was not refused by the name \
             ownership check (os error {:?})",
            code
        );
    }
}
