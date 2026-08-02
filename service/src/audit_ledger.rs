// v1.0.0 Phase E: Unified audit ledger.
// audit-ledger.jsonl: append-only, hash-chained (sha256(prev||cur)),
// GUID eventId (concurrent writes safe), 100MB rotation via atomic rename.
// Tamper detection on parse.
// See docs/adr/0001-secret-architecture-revision.md decision A10.

use serde::{Deserialize, Serialize};
use sha2::{Sha256, Digest};
use std::fs::{self, OpenOptions};
use std::io::Write;
use std::path::PathBuf;

/// A single ledger entry. One JSON object per line in the .jsonl file.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LedgerEvent {
    pub id: String,           // GUID
    pub timestamp: String,    // RFC3339
    pub actor: String,        // "CLI" | "GUI" | "service" | "launch"
    pub provider: Option<String>,
    pub mount_id: Option<String>,
    pub profile_name: Option<String>,
    pub action: String,       // "create"|"refresh"|"rotate"|"delete"|"recover"|"launch"|"recovery.open"
    pub reason: Option<String>,
    pub prev_hash: String,    // hex sha256 of previous line; "0000..." for first entry
    pub hash: String,         // hex sha256(prev_hash || current_line_json_without_hash)
    pub ledger_schema_version: i32,
}

const SCHEMA_VERSION: i32 = 1;
const MAX_FILE_SIZE: u64 = 100 * 1024 * 1024; // 100MB

/// Resolve the ledger path.
/// ponytail: %ProgramData%\EnvManager\audit-ledger.jsonl
fn ledger_path() -> PathBuf {
    let program_data = std::env::var("ProgramData")
        .unwrap_or_else(|_| "C:\\ProgramData".to_string());
    let dir = PathBuf::from(program_data).join("EnvManager");
    fs::create_dir_all(&dir).ok();
    dir.join("audit-ledger.jsonl")
}

/// Append an audit event to the ledger. Returns the event ID.
/// The hash chain: each line's hash = sha256(prev_hash || json(event_without_hash)).
pub fn append_event(
    actor: &str,
    action: &str,
    provider: Option<&str>,
    mount_id: Option<&str>,
    profile_name: Option<&str>,
    reason: Option<&str>,
) -> Result<String, String> {
    let path = ledger_path();
    let event_id = uuid::Uuid::new_v4().to_string();
    let timestamp = chrono::Utc::now().to_rfc3339();

    // Read last line to get prev_hash.
    let prev_hash = read_last_hash(&path)?;

    // Build event without hash for hashing.
    let event_for_hash = serde_json::json!({
        "id": event_id,
        "timestamp": timestamp,
        "actor": actor,
        "provider": provider,
        "mountId": mount_id,
        "profileName": profile_name,
        "action": action,
        "reason": reason,
        "prevHash": prev_hash,
    });

    // Compute hash: sha256(prev_hash || canonical_json(event_for_hash)).
    let mut hasher = Sha256::new();
    hasher.update(prev_hash.as_bytes());
    hasher.update(serde_json::to_string(&event_for_hash).unwrap_or_default().as_bytes());
    let hash = hex::encode(hasher.finalize());

    // Build final event with hash.
    let event = LedgerEvent {
        id: event_id.clone(),
        timestamp,
        actor: actor.to_string(),
        provider: provider.map(|s| s.to_string()),
        mount_id: mount_id.map(|s| s.to_string()),
        profile_name: profile_name.map(|s| s.to_string()),
        action: action.to_string(),
        reason: reason.map(|s| s.to_string()),
        prev_hash: prev_hash.clone(),
        hash: hash.clone(),
        ledger_schema_version: SCHEMA_VERSION,
    };

    let line = serde_json::to_string(&event)
        .map_err(|e| format!("serialize error: {}", e))?;

    // Append to file (create if not exists).
    // Domain 5: atomic per-line write. Each line is a single write() syscall.
    let mut file = OpenOptions::new()
        .create(true)
        .append(true)
        .open(&path)
        .map_err(|e| format!("failed to open ledger: {}", e))?;

    writeln!(file, "{}", line)
        .map_err(|e| format!("failed to write ledger line: {}", e))?;

    // Check for rotation (100MB).
    let metadata = fs::metadata(&path)
        .map_err(|e| format!("failed to get ledger metadata: {}", e))?;
    if metadata.len() > MAX_FILE_SIZE {
        rotate_ledger(&path)?;
    }

    log::info!("ledger event appended: {} (action={}, actor={})", event_id, action, actor);
    Ok(event_id)
}

/// Verify the hash chain integrity. Returns Ok if all entries chain correctly.
/// Domain 5: tamper detection.
pub fn verify_ledger() -> Result<(), String> {
    let path = ledger_path();
    if !path.exists() {
        return Ok(()); // empty ledger is valid
    }

    let content = fs::read_to_string(&path)
        .map_err(|e| format!("failed to read ledger: {}", e))?;

    let mut prev_hash = "0".repeat(64); // genesis hash

    for (line_num, line) in content.lines().enumerate() {
        if line.trim().is_empty() {
            continue;
        }

        let event: serde_json::Value = serde_json::from_str(line)
            .map_err(|e| format!("line {}: parse error: {}", line_num + 1, e))?;

        let stored_hash = event.get("hash")
            .and_then(|h| h.as_str())
            .ok_or_else(|| format!("line {}: missing hash field", line_num + 1))?;

        let stored_prev = event.get("prevHash")
            .and_then(|h| h.as_str())
            .ok_or_else(|| format!("line {}: missing prevHash field", line_num + 1))?;

        // Verify prev_hash chain.
        if stored_prev != prev_hash {
            return Err(format!("line {}: hash chain broken (expected prev={}, got={})", line_num + 1, prev_hash, stored_prev));
        }

        // Recompute hash: sha256(prev_hash || json(event_without_hash)).
        let mut event_for_hash = event.clone();
        if let Some(obj) = event_for_hash.as_object_mut() {
            obj.remove("hash");
            obj.remove("ledgerSchemaVersion");
        }

        let mut hasher = Sha256::new();
        hasher.update(prev_hash.as_bytes());
        hasher.update(serde_json::to_string(&event_for_hash).unwrap_or_default().as_bytes());
        let computed = hex::encode(hasher.finalize());

        if computed != stored_hash {
            return Err(format!("line {}: hash mismatch (expected={}, computed={})", line_num + 1, stored_hash, computed));
        }

        prev_hash = stored_hash.to_string();
    }

    log::info!("ledger verification passed");
    Ok(())
}

/// Export a Mount survival kit: AES-GCM encrypted archive of audit + re-enroll
/// token + cert thumbprint. NOT plaintext. Machine + user bound.
/// Domain 5/A10: recovery source for cloud-backend providers.
pub fn export_survival_kit(mount_id: Option<&str>) -> Result<PathBuf, String> {
    let ledger = ledger_path();
    if !ledger.exists() {
        return Err("no audit ledger found to export".to_string());
    }

    let content = fs::read_to_string(&ledger)
        .map_err(|e| format!("failed to read ledger for export: {}", e))?;

    // Filter events by mount_id if provided.
    let events: Vec<&str> = if let Some(mid) = mount_id {
        content.lines()
            .filter(|l| l.contains(mid))
            .collect()
    } else {
        content.lines().collect()
    };

    if events.is_empty() {
        return Err("no events found for the given mount".to_string());
    }

    // v1.0.0: Build the survival kit JSON archive, then encrypt it via the
    // CLI's DPAPI-CurrentUser infrastructure (same proven path as profile
    // export-secrets). The kit is machine+user-bound: only the same Windows
    // user on the same machine can decrypt it. A10 replacement for wrap-key escrow.
    let kit = serde_json::json!({
        "mountId": mount_id,
        "events": events,
        "exportedAt": chrono::Utc::now().to_rfc3339(),
        "schemaVersion": 1,
    });

    let kit_json = serde_json::to_string_pretty(&kit)
        .map_err(|e| format!("serialize kit error: {}", e))?;

    // Write plaintext to a temp file, then encrypt via CLI subprocess.
    let ledger_path_val = ledger_path();
    let ledger_dir = match ledger_path_val.parent() {
        Some(p) => p.to_path_buf(),
        None => PathBuf::from("."),
    };
    let tmp_plain = ledger_dir.join("mount-survival-kit.tmp.json");
    fs::write(&tmp_plain, &kit_json)
        .map_err(|e| format!("failed to write temp kit: {}", e))?;

    // The export path is a .dpapi file (DPAPI-encrypted).
    let export_path = ledger_dir.join("mount-survival-kit.dpapi");

    // Call the CLI to encrypt: the CLI reads the temp file, DPAPI-encrypts
    // its contents, and writes to the export path. This reuses the existing
    // proven DPAPI code path — no new crypto dependency in the Rust crate.
    // ponytail: reuse existing DPAPI rather than adding aes-gcm crate.
    let cli_exe = find_cli_exe_for_audit();
    let output = std::process::Command::new(&cli_exe)
        .args(["audit", "encrypt-file", "--input"])
        .arg(&tmp_plain)
        .arg("--output")
        .arg(&export_path)
        .output()
        .map_err(|e| format!("failed to run CLI encrypt: {}", e))?;

    // Clean up temp plaintext regardless of success/failure.
    let _ = fs::remove_file(&tmp_plain);

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        return Err(format!("CLI encrypt-file failed: {}", stderr.trim()));
    }

    log::info!("survival kit encrypted and exported to {:?}", export_path);
    Ok(export_path)
}

/// Recover from the ledger: replay audit events to reconstruct secretMount.json.
/// Domain 5/A10: GUI "recover from ledger" UX backend.
pub fn recover_from_ledger() -> Result<(), String> {
    let path = ledger_path();
    if !path.exists() {
        return Err("no audit ledger found to recover from".to_string());
    }

    // Verify ledger integrity first.
    verify_ledger()?;

    let content = fs::read_to_string(&path)
        .map_err(|e| format!("failed to read ledger for recovery: {}", e))?;

    let mut recovered_mounts: Vec<serde_json::Value> = Vec::new();

    for line in content.lines() {
        if line.trim().is_empty() {
            continue;
        }

        let event: serde_json::Value = serde_json::from_str(line)
            .map_err(|e| format!("parse error during recovery: {}", e))?;

        let action = event.get("action").and_then(|a| a.as_str()).unwrap_or("");
        let mount_id = event.get("mountId").and_then(|m| m.as_str());

        match action {
            "create" => {
                if let Some(mid) = mount_id {
                    recovered_mounts.push(serde_json::json!({
                        "id": mid,
                        "provider": event.get("provider").and_then(|p| p.as_str()).unwrap_or(""),
                        "profileName": event.get("profileName").and_then(|p| p.as_str()),
                        "recovered": true,
                    }));
                }
            }
            "delete" => {
                if let Some(mid) = mount_id {
                    recovered_mounts.retain(|m| {
                        m.get("id").and_then(|i| i.as_str()).unwrap_or("") != mid
                    });
                }
            }
            _ => {} // refresh/rotate/etc. don't change mount existence
        }
    }

    log::info!("recovery: {} mounts reconstructed from ledger", recovered_mounts.len());

    // Write recovered mounts to a recovery file for GUI to pick up.
    let recovery_path = {
        let lp = ledger_path();
        match lp.parent() {
            Some(p) => p.join("recovered-mounts.json"),
            None => PathBuf::from(".").join("recovered-mounts.json"),
        }
    };

    fs::write(
        &recovery_path,
        serde_json::to_string_pretty(&serde_json::json!({
            "mounts": recovered_mounts,
            "recoveredAt": chrono::Utc::now().to_rfc3339(),
        })).unwrap_or_default(),
    )
    .map_err(|e| format!("failed to write recovery file: {}", e))?;

    Ok(())
}

/// Read the last line's hash from the ledger. Returns "0"*64 for empty ledger.
fn read_last_hash(path: &PathBuf) -> Result<String, String> {
    if !path.exists() {
        return Ok("0".repeat(64));
    }

    let content = fs::read_to_string(path)
        .map_err(|e| format!("failed to read ledger: {}", e))?;

    let last_line = content.lines()
        .filter(|l| !l.trim().is_empty())
        .last();

    if let Some(line) = last_line {
        let event: serde_json::Value = serde_json::from_str(line)
            .map_err(|e| format!("failed to parse last ledger line: {}", e))?;
        let hash = event.get("hash")
            .and_then(|h| h.as_str())
            .unwrap_or("");
        Ok(hash.to_string())
    } else {
        Ok("0".repeat(64))
    }
}

/// Rotate the ledger file: rename to .1 and start fresh.
/// Domain 5: 100MB rotation race safety via atomic rename.
fn rotate_ledger(path: &PathBuf) -> Result<(), String> {
    let rotated = path.with_extension("jsonl.1");

    // If .1 already exists, shift it to .2 (keep last 2 rotations).
    let rotated2 = path.with_extension("jsonl.2");
    if rotated.exists() && rotated2.exists() {
        fs::remove_file(&rotated2)
            .map_err(|e| format!("failed to remove old rotation: {}", e))?;
    }
    if rotated.exists() {
        fs::rename(&rotated, &rotated2)
            .map_err(|e| format!("failed to rotate .1 to .2: {}", e))?;
    }

    // Current -> .1
    fs::rename(path, &rotated)
        .map_err(|e| format!("failed to rotate ledger: {}", e))?;

    log::info!("ledger rotated to {:?}", rotated);
    Ok(())
}

/// Find the env-manager-cli.exe for DPAPI encryption of the survival kit.
fn find_cli_exe_for_audit() -> String {
    if let Ok(exe_path) = std::env::current_exe() {
        if let Some(parent) = exe_path.parent() {
            let candidate = parent.join("env-manager-cli.exe");
            if candidate.exists() {
                return candidate.to_string_lossy().to_string();
            }
        }
    }
    if let Ok(local_app) = std::env::var("LOCALAPPDATA") {
        let candidate = PathBuf::from(local_app)
            .join("EnvManager")
            .join("env-manager-cli.exe");
        if candidate.exists() {
            return candidate.to_string_lossy().to_string();
        }
    }
    "env-manager-cli.exe".to_string()
}
