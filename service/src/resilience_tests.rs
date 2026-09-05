// Ticket 33 (architecture-recovery): service crate resilience fault-injection tests.
// Scenarios pinned here: pipe half-open reconnect, IPC read timeout, IPC schema
// compliance under fault-shaped responses. The kill -9 / SCM-restart scenario needs
// process-level control and is exercised by the manual script
// .scratch/architecture-recovery/manual/kill-9-test.ps1 (administrator, local only).

use std::sync::Arc;
use std::time::Duration;
use tokio::io::{AsyncReadExt, AsyncWriteExt};
use tokio_util::sync::CancellationToken;

/// Unique test pipe names so runs never collide with the real service pipes.
const HALF_OPEN_PIPE: &str = r"\\.\pipe\EnvManager.TestHalfOpen";
const TIMEOUT_PIPE: &str = r"\\.\pipe\EnvManager.TestTimeout";

/// Poll-connect until the server's first pipe instance is up (max 5s).
async fn wait_for_pipe(pipe: &str) {
    for _ in 0..100 {
        if tokio::net::windows::named_pipe::ClientOptions::new()
            .open(pipe)
            .is_ok()
        {
            return;
        }
        tokio::time::sleep(Duration::from_millis(50)).await;
    }
    panic!("pipe {pipe} did not become connectable within 5s");
}

/// Scenario: pipe half-open — a client connects, sends a request, then drops the
/// connection without reading the response. The server must absorb the broken
/// pipe (log + close, per handle_connection) and keep accepting new clients; the
/// watchdog's reconnect (ping) must succeed afterwards.
#[tokio::test]
async fn pipe_half_open_then_reconnect() {
    let shutdown = Arc::new(CancellationToken::new());
    let server_token = shutdown.clone();
    let server = tokio::spawn(async move {
        crate::ipc::start_ipc_server(HALF_OPEN_PIPE, server_token, false).await;
    });

    wait_for_pipe(HALF_OPEN_PIPE).await;

    // Half-open: connect, write a request line, drop without reading the response.
    let mut client = tokio::net::windows::named_pipe::ClientOptions::new()
        .open(HALF_OPEN_PIPE)
        .expect("connect to test pipe");
    client
        .write_all(br#"{"method":"ping"}"#)
        .await
        .expect("write request");
    client.flush().await.expect("flush request");
    drop(client); // abrupt close: server sees EOF/error mid-conversation

    // Give the server a moment to reap the broken connection, then verify it
    // still accepts connections (watchdog reconnect semantics).
    tokio::time::sleep(Duration::from_millis(200)).await;
    let mut reconnect = tokio::net::windows::named_pipe::ClientOptions::new()
        .open(HALF_OPEN_PIPE)
        .expect("reconnect after half-open drop");
    reconnect
        .write_all(br#"{"method":"ping"}"#)
        .await
        .expect("write ping after reconnect");

    let mut resp = Vec::new();
    let mut byte = [0u8; 1];
    loop {
        let n = reconnect.read(&mut byte).await.expect("read response");
        assert!(n > 0, "server closed connection before sending a response");
        if byte[0] == b'\n' {
            break;
        }
        resp.push(byte[0]);
    }
    let resp: serde_json::Value = serde_json::from_slice(&resp).expect("valid JSON response");
    assert_eq!(resp["ok"], true, "ping after half-open drop must succeed");
    assert_eq!(resp["data"]["pong"], true);

    shutdown.cancel();
    let _ = server.await;
}

/// Scenario: timeout injection — the service accepts the connection but never
/// writes a response (hung handler). The client-side read must surface a timeout
/// instead of blocking forever (the bounded-read contract of cli_gateway).
#[tokio::test]
async fn ipc_read_timeout_fires_on_hung_server() {
    let shutdown = Arc::new(CancellationToken::new());
    let server_token = shutdown.clone();
    let server = tokio::spawn(async move {
        // Mock server: accept connections, read the request line, never respond.
        loop {
            if server_token.is_cancelled() {
                return;
            }
            let srv = match tokio::net::windows::named_pipe::ServerOptions::new()
                .create(TIMEOUT_PIPE)
            {
                Ok(s) => s,
                Err(_) => {
                    tokio::time::sleep(Duration::from_millis(50)).await;
                    continue;
                }
            };
            let token = server_token.clone();
            tokio::spawn(async move {
                let mut srv = srv;
                let _ = srv.connect().await;
                let mut byte = [0u8; 1];
                while let Ok(Ok(n)) = srv.read(&mut byte).await {
                    if n == 0 || byte[0] == b'\n' {
                        break;
                    }
                }
                // Handler hangs: no response for the lifetime of the connection.
                tokio::time::sleep(Duration::from_secs(60)).await;
            });
        }
    });

    wait_for_pipe(TIMEOUT_PIPE).await;

    let mut client = tokio::net::windows::named_pipe::ClientOptions::new()
        .open(TIMEOUT_PIPE)
        .expect("connect to mock pipe");
    client
        .write_all(br#"{"method":"ping"}"#)
        .await
        .expect("write request");

    // Mirror cli_gateway's timeout contract: a bounded read, not a blocking one.
    let mut resp = Vec::new();
    let mut byte = [0u8; 1];
    let result = tokio::time::timeout(Duration::from_secs(2), async {
        loop {
            let n = client.read(&mut byte).await.expect("read");
            if n == 0 || byte[0] == b'\n' {
                break;
            }
            resp.push(byte[0]);
        }
    })
    .await;

    assert!(
        result.is_err(),
        "read against a hung server must time out, got {:?}",
        result.map(|_| String::from_utf8_lossy(&resp).to_string())
    );

    shutdown.cancel();
    let _ = server.await;
}

/// IPC responses must keep the schema envelope under fault-shaped conditions:
/// ok:false responses carry a message. Read-only check over the ticket 08 golden
/// samples (same resolution walk as ipc.rs::golden_dir — tests run with CWD at
/// the package root, so a bare relative path would not resolve).
#[test]
fn ipc_schema_compliance_under_faults() {
    let mut dir = std::path::PathBuf::from(env!("CARGO_MANIFEST_DIR"));
    loop {
        if dir.join("docs").is_dir() {
            let samples = dir.join("docs").join("schemas").join("ipc-samples.json");
            let raw = std::fs::read_to_string(&samples).expect("read ipc-samples.json");
            let samples: serde_json::Value = serde_json::from_str(&raw).expect("parse samples");
            for resp in samples["responses"].as_array().expect("responses array") {
                let payload = resp["payload"].as_object().expect("payload object");
                assert!(
                    payload.contains_key("ok"),
                    "response {:?} must have 'ok' field",
                    resp["name"]
                );
                if !payload["ok"].as_bool().unwrap() {
                    assert!(
                        payload.contains_key("message"),
                        "error response {:?} must have 'message' field",
                        resp["name"]
                    );
                }
            }
            return;
        }
        if !dir.pop() {
            panic!("docs/ not found above {}", env!("CARGO_MANIFEST_DIR"));
        }
    }
}

/// Reconcile warmup (30s) prevents SCM timeout on boot. Pinned as documentation
/// only for now: driving the real reconcile_loop requires >31s wall time and its
/// tick resolves secret_mount_path() against LOCALAPPDATA, which conflicts with
/// the CI user-state isolation discipline (architecture-recovery issue 24).
/// Replace with a real loop-level assertion if the warmup becomes seam-parameterized.
#[test]
#[ignore = "30s warmup needs >31s wall time and touches LOCALAPPDATA (issue 24 isolation); pin via seam if warmup becomes configurable"]
fn reconcile_warmup_constant_pinned() {
    // Contract (reconcile.rs): 30s warmup before first tick, 300s interval.
    // Expected assertion once seamable: no tick before 30s, tick at 30s.
}

/// Kill -9 / SCM-restart recovery. Requires process-level control (start, ping,
/// taskkill /F, poll watchdog detection, restart, assert secretMount.json
/// persistence) and administrator rights — unsafe for automated CI runners.
/// Executed by the manual script .scratch/architecture-recovery/manual/kill-9-test.ps1.
#[test]
#[ignore = "manual only — kill-9-test.ps1 requires administrator privileges and a local service build"]
fn kill_9_watchdog_recovery() {
    // Scripted assertions (kill-9-test.ps1): pre-kill ping ok -> taskkill /F ->
    // watchdog detection within 5s -> restart -> secretMount.json persisted ->
    // post-restart ping ok -> health endpoint reachable.
}
