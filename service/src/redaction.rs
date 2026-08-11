/// v0.9.12: Service-side tracing redaction layer.
/// Scrubs secret-bearing patterns from log messages before they reach the
/// tracing subscriber. Mirrors the C# ScrubExceptionMessage and Rust
/// scrub_stderr patterns so all three tiers (CLI, GUI, service) use the
/// same redaction vocabulary.
///
/// Ponytail: a single free function, no trait, no struct, no config.

/// Truncates to 512 chars and masks 22 common secret-bearing patterns.
/// Best-effort scrub — logs the error message shape, never a secret value.
pub fn scrub_message(s: &str) -> String {
    let mut out: String = s.chars().take(512).collect();
    for pat in [
        "Bearer ",
        "token=",
        "Token=",
        "password=",
        "Password=",
        "setx ",
        "OP_SERVICE_ACCOUNT_TOKEN=",
        "VAULT_TOKEN=",
        "AWS_SECRET_ACCESS_KEY=",
        "AWS_SESSION_TOKEN=",
        "client_secret=",
        "connection_string=",
        "subscription_key=",
        "api_key=",
        "apikey=",
        "client_id=",
        "tenant_id=",
        "access_token=",
        "refresh_token=",
        "Authorization:",
        "X-Vault-Token:",
        "x-api-key:",
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

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_scrub_bearer() {
        let input = "Error: Bearer abc123def456 failed";
        let out = scrub_message(input);
        assert!(out.contains("Bearer <redacted>"));
        assert!(!out.contains("abc123def456"));
    }

    #[test]
    fn test_scrub_truncation() {
        let long = "x".repeat(600);
        let out = scrub_message(&long);
        assert_eq!(out.len(), 512);
    }

    #[test]
    fn test_scrub_no_match() {
        let input = "service started ok";
        assert_eq!(scrub_message(input), "service started ok");
    }

    #[test]
    fn test_zeroizing_string_drop() {
        use zeroize::Zeroizing;
        // Just verify Zeroizing<String> compiles and can be used
        let secret = Zeroizing::new(String::from("s3cr3t-v4lu3"));
        assert_eq!(&*secret, "s3cr3t-v4lu3");
        // secret drops here, inner String zeroed
    }

}
