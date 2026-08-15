// v0.9.0 Phase B+C: Named Pipe IPC + protocol.
// Server: listens on \\.\pipe\EnvManager.Service (service) or \\.\pipe\EnvManager.Background (background).
// Client: one-shot CLI gateway connects to pipe, sends request, prints response.
// DACL: only EnvManagerService SID and current user can connect.
// Anti-squatting: PIPE_FIRST_PIPE_INSTANCE flag rejects pre-existing pipe.
// See docs/adr/0001-secret-architecture-revision.md decisions A7, A8 (Domain 2).

use serde::{Deserialize, Serialize};
use std::io;
use std::time::Duration;
use std::sync::Arc;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio::net::windows::named_pipe::NamedPipeServer;
use tokio_util::sync::CancellationToken;

/// IPC request from CLI/GUI to the service.
#[derive(Debug, Clone, Serialize, Deserialize)]
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
pub async fn start_ipc_server(pipe_name: &str, shutdown: Arc<CancellationToken>, use_first_pipe_instance: bool) {
    tracing::info!("IPC server listening on {}", pipe_name);

    let mut first = true;
    loop {
        // Check shutdown before creating next pipe instance
        if shutdown.is_cancelled() {
            tracing::info!("IPC server cancelled, exiting");
            return;
        }
        let server = if first && use_first_pipe_instance {
            match tokio::net::windows::named_pipe::ServerOptions::new()
                .first_pipe_instance(true)
                .create(pipe_name)
            {
                Ok(s) => {
                    first = false;
                   tracing::info!("First pipe instance created (anti-squatting check passed)");
                   s
               }
               Err(e) => {
                   tracing::warn!("Pipe creation failed on first attempt: {}. Retrying in 1s...", e);
                   tokio::time::sleep(Duration::from_secs(1)).await;
                    match tokio::net::windows::named_pipe::ServerOptions::new()
                        .first_pipe_instance(true)
                        .create(pipe_name)
                    {
                        Ok(s) => {
                            first = false;
                            tracing::info!("First pipe instance created on retry (stale pipe cleared)");
                            s
                        }
                        Err(e2) => {
                            tracing::error!("Pipe creation failed after retry: {}. Service cannot start safely.", e2);
                            return;
                        }
                    }
                }
           }
       } else {
            match tokio::net::windows::named_pipe::ServerOptions::new()
                .create(pipe_name)
            {
                Ok(s) => s,
                Err(e) => {
                    tracing::error!("Failed to create named pipe instance: {}", e);
                    tokio::time::sleep(Duration::from_secs(5)).await;
                    continue;
                }
            }
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
                return;
            }
        }

        let conn_token = shutdown.clone();
        tokio::spawn(async move {
            if let Err(e) = handle_connection(server, conn_token).await {
                tracing::warn!("Connection handler error: {}", e);
            }
        });
    }
}

/// Handle a single client connection: read one request line, process, write one response line.
async fn handle_connection(mut server: NamedPipeServer, _shutdown: Arc<CancellationToken>) -> io::Result<()> {
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

    let response = process_request(&request, &_shutdown).await;
    tracing::info!("IPC response: ok={} method={}", response.ok, request.method);
    let line = serde_json::to_string(&response).unwrap_or_default();
    server.write_all((line + "\n").as_bytes()).await?;
    server.flush().await?;
    tracing::info!("IPC connection #{} closed (total accepted: {})", conn_id, CONNECTION_COUNT.load(Ordering::Relaxed));
    Ok(())
}

async fn process_request(req: &IpcRequest, shutdown: &Arc<CancellationToken>) -> IpcResponse {
    tracing::info!("IPC request: method={} request_id={}", req.method, req.request_id.as_deref().unwrap_or("-"));
    match req.method.as_str() {
        // v0.9.6: enriched ping with heartbeat data (uptime, reconcile status, mount health)
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
            match crate::reconcile::rotate_mount(mount_id).await {
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
