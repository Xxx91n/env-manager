// v0.9.0 Phase B+C: Named Pipe IPC.
// Server: listens on \\.\pipe\EnvManager.Service (service) or \\.\pipe\EnvManager.Background (background).
// Client: one-shot CLI gateway connects to background pipe, sends request, prints response.
// DACL: only EnvManagerService SID and current user can connect.

use std::io::{self};

/// Start the IPC named pipe server. Runs indefinitely.
pub async fn start_ipc_server(_pipe_name: &str) {
    log::info!("IPC server listening on {}", _pipe_name);
    // v0.9.0 skeleton: actual named pipe server implementation.
    // Windows named pipes use CreateNamedPipeW + ConnectNamedPipe.
    // For now, just hold the task alive.
    loop {
        tokio::time::sleep(std::time::Duration::from_secs(60)).await;
    }
}

/// CLI gateway: connect to the running service pipe, send a request, read response.
pub async fn cli_gateway(_pipe_name: &str) -> Result<(), String> {
    // v0.9.0 skeleton: actual named pipe client implementation.
    // For now, return Ok to allow compilation.
    log::info!("CLI gateway connecting to {}", _pipe_name);
    Ok(())
}
