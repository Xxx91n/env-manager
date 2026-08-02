// v0.9.5 Phase D: Certificate-based bootstrap.
// Replaces static cloud credentials (VAULT_TOKEN, AZURE_CLIENT_SECRET, etc.)
// with short-lived certificate auth. Certificates stored in Cert:\LocalMachine\My
// with non-exportable private key ACL'd to per-service SID.
// See docs/adr/0001-secret-architecture-revision.md decision A9.

/// Vault AppRole or TLS client cert bootstrap.
/// The service holds an AppRole role_id + secret_id (rotatable) OR a client cert
/// in its own Cert:\LocalMachine\My store; auto-auth fetches short-lived Vault token.
/// The static VAULT_TOKEN env var is ELIMINATED.
pub fn vault_bootstrap(_cert_thumbprint: &str) -> Result<String, String> {
    // v0.9.5 skeleton: implement Vault Agent Auto-Auth via AppRole or TLS cert.
    // For now, return a placeholder.
    Err("vault_bootstrap: not yet implemented (Phase D v0.9.5)".into())
}

/// Azure SP certificate auth.
/// Service loads cert from Cert:\LocalMachine\My, does SP cert-based OAuth to
/// Azure AD for short-lived token. Token cached in-memory only with 5-min buffer.
/// The static AZURE_CLIENT_SECRET env var is ELIMINATED.
pub fn azure_sp_bootstrap(_cert_thumbprint: &str) -> Result<String, String> {
    // v0.9.5 skeleton: implement Azure AD app certificate authentication.
    Err("azure_sp_bootstrap: not yet implemented (Phase D v0.9.5)".into())
}

/// AWS IAM Roles Anywhere bootstrap.
/// The service presents its cert to get short-lived AWS credentials.
/// No static AWS_ACCESS_KEY_ID file.
/// Deferred to post-v1.0.0 per ADR 0001 A9.
pub fn aws_roles_anywhere_bootstrap() -> Result<String, String> {
    Err("aws_roles_anywhere_bootstrap: deferred per ADR 0001 A9".into())
}
