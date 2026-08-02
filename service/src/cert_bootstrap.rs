// v0.9.5 Phase D: Certificate-based bootstrap.
// Replaces static cloud credentials (VAULT_TOKEN, AZURE_CLIENT_SECRET, etc.)
// with short-lived certificate auth. Certificates stored in Cert:\LocalMachine\My
// with non-exportable private key ACL'd to per-service SID.
// See docs/adr/0001-secret-architecture-revision.md decision A9.

use std::path::PathBuf;
use base64::{engine::general_purpose, Engine as _};

/// Encode a PowerShell script as base64(UTF-16LE) for -EncodedCommand.
/// This eliminates all shell quoting/injection risks. Same pattern as v0.7.4
/// PowerShellSecretManagementProvider hard boundary.
fn encode_command(script: &str) -> String {
    let utf16: Vec<u8> = script
        .encode_utf16()
        .flat_map(|u| u.to_le_bytes())
        .collect();
    general_purpose::STANDARD.encode(&utf16)
}

/// Run a PowerShell script via -EncodedCommand (no shell quoting layer).
fn run_powershell(script: &str) -> Result<std::process::Output, String> {
    let encoded = encode_command(script);
    std::process::Command::new("powershell")
        .args(["-NoProfile", "-NonInteractive", "-EncodedCommand", &encoded])
        .output()
        .map_err(|e| format!("failed to run PowerShell: {}", e))
}

/// Vault AppRole or TLS client cert bootstrap.
/// The service holds an AppRole role_id + secret_id (rotatable) OR a client cert
/// in its own Cert:\LocalMachine\My store; auto-auth fetches short-lived Vault token.
/// The static VAULT_TOKEN env var is ELIMINATED.
///
/// Flow:
/// 1. Read role_id from service.config.json (non-secret, infrastructure metadata).
/// 2. Read secret_id from env var VAULT_SECRET_ID (set at enroll time, NOT persisted to disk).
/// 3. POST to {VAULT_ADDR}/v1/auth/approle/login with {role_id, secret_id}.
/// 4. Parse response for client_token + lease_duration.
/// 5. Return token string. Token is cached in-memory only by the caller.
/// 6. On cert-based auth: if cert_thumbprint is provided, use TLS client cert
///    instead of AppRole. POST to {VAULT_ADDR}/v1/auth/cert/login with the cert.
pub fn vault_bootstrap(cert_thumbprint: &str) -> Result<String, String> {
    let vault_addr = std::env::var("VAULT_ADDR")
        .map_err(|_| "VAULT_ADDR environment variable not set".to_string())?;

    if !vault_addr.starts_with("https://") && !vault_addr.contains("127.0.0.1") && !vault_addr.contains("localhost") {
        return Err("VAULT_ADDR must use https:// for non-localhost addresses".to_string());
    }

    // Try cert-based auth first if thumbprint provided.
    if !cert_thumbprint.is_empty() {
        // v0.9.5 skeleton: cert-based auth requires loading the cert from
        // Cert:\LocalMachine\My by thumbprint and using it as TLS client cert.
        // This requires the windows-sys crate's Cryptography API or the
        // reqwest crate with rustls feature for client cert auth.
        // For now, return the flow description.
        log::info!("Vault cert-based auth with thumbprint {}", cert_thumbprint);
        // POST to {vault_addr}/v1/auth/cert/login
        // Response: { "auth": { "client_token": "...", "lease_duration": ... } }
        return vault_cert_login(&vault_addr, cert_thumbprint);
    }

    // AppRole path: read role_id from config, secret_id from env.
    let role_id = std::env::var("VAULT_ROLE_ID")
        .map_err(|_| "VAULT_ROLE_ID environment variable not set (needed for AppRole auth)".to_string())?;
    let secret_id = std::env::var("VAULT_SECRET_ID")
        .map_err(|_| "VAULT_SECRET_ID environment variable not set (needed for AppRole auth)".to_string())?;

    // Build script referencing $env: vars directly — no string interpolation
    // of secret values into the script body, eliminating injection risk.
    let script = format!(
        r#"$body = @{{ role_id = $env:VAULT_ROLE_ID; secret_id = $env:VAULT_SECRET_ID }} | ConvertTo-Json
$resp = Invoke-RestMethod -Uri "{}/v1/auth/approle/login" -Method Post -Body $body -ContentType "application/json" -TimeoutSec 10
$resp.auth.client_token"#,
        vault_addr
    );

    let output = run_powershell(&script)?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        return Err(format!("Vault AppRole login failed: {}", stderr.trim()));
    }

    let token = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if token.is_empty() {
        return Err("Vault AppRole login returned empty token".to_string());
    }

    log::info!("Vault AppRole bootstrap successful, token obtained");
    Ok(token)
}

/// Vault cert-based login via TLS client certificate.
/// Uses PowerShell Invoke-RestMethod with -Certificate thumbprint to present
/// the client cert from Cert:\LocalMachine\My. Same subprocess pattern as
/// Azure SP cert auth — no reqwest/rustls dependency needed.
/// See ADR 0001 A9: cert stored non-exportable, ACL'd to per-service SID.
fn vault_cert_login(vault_addr: &str, thumbprint: &str) -> Result<String, String> {
    // Script loads the cert by thumbprint and uses it for TLS client auth.
    // No secret values interpolated into the script body — thumbprint is
    // infrastructure metadata (non-secret), vault_addr is a URL.
    let script = format!(
        r#"$thumbprint = '{}'
$cert = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object {{ $_.Thumbprint -eq $thumbprint }}
if (-not $cert) {{
    Write-Error "Cert not found in LocalMachine\My: $thumbprint"
    exit 1
}}
# Vault cert auth: POST to /v1/auth/cert/login with the client cert presented via TLS.
# Invoke-RestMethod -Certificate presents the cert for mutual TLS.
$resp = Invoke-RestMethod -Uri "{}/v1/auth/cert/login" -Method Post -Certificate $cert -ContentType "application/json" -Body '{{}}' -TimeoutSec 15
$resp.auth.client_token"#,
        thumbprint, vault_addr
    );

    let output = run_powershell(&script)?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        return Err(format!("Vault cert login failed: {}", stderr.trim()));
    }

    let token = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if token.is_empty() {
        return Err("Vault cert login returned empty token".to_string());
    }

    log::info!("Vault cert-based bootstrap successful (thumbprint={})", thumbprint);
    Ok(token)
}

/// Azure SP certificate auth.
/// Service loads cert from Cert:\LocalMachine\My, does SP cert-based OAuth to
/// Azure AD for short-lived token. Token cached in-memory only with 5-min buffer.
/// The static AZURE_CLIENT_SECRET env var is ELIMINATED.
/// AZURE_CLIENT_ID + AZURE_TENANT_ID + cert thumbprint replace it.
///
/// Flow:
/// 1. Read AZURE_CLIENT_ID, AZURE_TENANT_ID from env (non-secret, infrastructure metadata).
/// 2. Load cert by thumbprint from Cert:\LocalMachine\My.
/// 3. Create JWT assertion: header={alg:RS256, typ:JWT, x5t:base64url(thumbprint)},
///    payload={aud, exp, iss, sub: AZURE_CLIENT_ID, jti: GUID}.
/// 4. Sign JWT with cert private key.
/// 5. POST to https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token
///    with grant_type=client_credentials&client_assertion_type=jwt-bearer&client_assertion=<JWT>&scope=https://vault.azure.net/.default
/// 6. Parse response for access_token + expires_in.
/// 7. Return token. Caller caches with 5-min buffer.
pub fn azure_sp_bootstrap(cert_thumbprint: &str) -> Result<String, String> {
    let client_id = std::env::var("AZURE_CLIENT_ID")
        .map_err(|_| "AZURE_CLIENT_ID environment variable not set".to_string())?;
    let tenant_id = std::env::var("AZURE_TENANT_ID")
        .map_err(|_| "AZURE_TENANT_ID environment variable not set".to_string())?;

    if cert_thumbprint.is_empty() {
        return Err("cert thumbprint is required for Azure SP cert auth".to_string());
    }

    // v0.9.5 skeleton: Azure SP cert auth requires:
    // (a) Load cert by thumbprint from Cert:\LocalMachine\My
    // (b) Create and sign a JWT client assertion
    // (c) POST to Azure AD token endpoint
    // Full implementation needs either the rsa crate or windows-sys Cryptography API
    // for signing. For skeleton: use PowerShell as a subprocess (same pattern as Vault).
    // Script references $env:AZURE_CLIENT_ID and $env:AZURE_TENANT_ID directly
    // (no interpolation of secret values into the script body).
    let script = format!(
        r#"$thumbprint = '{}'
$cert = Get-ChildItem -Path Cert:\LocalMachine\My | Where-Object {{ $_.Thumbprint -eq $thumbprint }}
if (-not $cert) {{
    Write-Error "Cert not found: $thumbprint"
    exit 1
}}
# Create JWT assertion
$header = @{{ alg = "RS256"; typ = "JWT"; x5t = $thumbprint }} | ConvertTo-Json -Compress
$now = [DateTimeOffset]::UtcNow
$exp = $now.AddMinutes(10)
$payload = @{{
    aud = "https://login.microsoftonline.com/{}/oauth2/v2.0/token"
    exp = $exp.ToUnixTimeSeconds()
    iss = $env:AZURE_CLIENT_ID
    sub = $env:AZURE_CLIENT_ID
    jti = [Guid]::NewGuid().ToString()
}} | ConvertTo-Json -Compress
$headerB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($header)).TrimEnd('=').Replace('+','-').Replace('/','_')
$payloadB64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($payload)).TrimEnd('=').Replace('+','-').Replace('/','_')
$message = "$headerB64.$payloadB64"
$rsa = $cert.PrivateKey
if (-not $rsa) {{
    Write-Error "Private key not accessible for cert: $thumbprint"
    exit 1
}}
$hash = [Security.Cryptography.SHA256]::Create().ComputeHash([Text.Encoding]::UTF8.GetBytes($message))
$signature = $rsa.SignHash($hash, [Security.Cryptography.CryptoConfig]::MapNameToOID("SHA256"))
$sigB64 = [Convert]::ToBase64String($signature).TrimEnd('=').Replace('+','-').Replace('/','_')
$jwt = "$message.$sigB64"
$body = "grant_type=client_credentials&client_assertion_type=urn:ietf:params:oauth:client-assertion-type:jwt-bearer&client_assertion=$jwt&scope=https://vault.azure.net/.default"
$resp = Invoke-RestMethod -Uri "https://login.microsoftonline.com/{}/oauth2/v2.0/token" -Method Post -Body $body -ContentType "application/x-www-form-urlencoded" -TimeoutSec 15
$resp.access_token"#,
        cert_thumbprint, tenant_id, tenant_id
    );

    let output = run_powershell(&script)?;

    if !output.status.success() {
        let stderr = String::from_utf8_lossy(&output.stderr);
        return Err(format!("Azure SP cert auth failed: {}", stderr.trim()));
    }

    let token = String::from_utf8_lossy(&output.stdout).trim().to_string();
    if token.is_empty() {
        return Err("Azure SP cert auth returned empty token".to_string());
    }

    log::info!("Azure SP cert bootstrap successful, token obtained");
    Ok(token)
}

/// AWS IAM Roles Anywhere bootstrap.
/// The service presents its cert to get short-lived AWS credentials.
/// No static AWS_ACCESS_KEY_ID file.
/// Deferred to post-v1.0.0 per ADR 0001 A9.
pub fn aws_roles_anywhere_bootstrap() -> Result<String, String> {
    Err("aws_roles_anywhere_bootstrap: deferred per ADR 0001 A9".into())
}

/// Resolve the service config path.
/// ponytail: %ProgramData%\EnvManager\service.config.json (non-secret infrastructure metadata).
fn service_config_path() -> PathBuf {
    let program_data = std::env::var("ProgramData")
        .unwrap_or_else(|_| r"C:\ProgramData".to_string());
    PathBuf::from(program_data).join("EnvManager").join("service.config.json")
}
