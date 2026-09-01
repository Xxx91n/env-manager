# ADR 0005: Sensitive Data Redaction Architecture

Date: 2026-08-09
Status: Accepted
Version: v0.9.12

## Context

Logs, stderr, and tracing output across three tiers (C# CLI, Rust Tauri shell, Rust service) can
inadvertently leak secret-bearing strings when exception messages or provider error output contain
credentials, tokens, or connection strings. The existing `scrub_stderr` in Rust had only 10 patterns;
C# had no scrubbing at all — `ex.Message` was written verbatim to stderr at 7 sites.

## Decision

Implement a unified redaction layer across all three tiers:

1. **C# `ScrubExceptionMessage`** (src/Program.cs): Scrubs 22 secret-bearing patterns from exception
   messages before writing to stderr or logs. Applied at all 7 `ex.Message` leak sites.

2. **Rust `scrub_stderr` expansion** (main.rs): Expanded from 10 to 22 patterns, matching the C#
   vocabulary. Covers Bearer tokens, passwords, setx, OP_SERVICE_ACCOUNT_TOKEN, VAULT_TOKEN,
   AWS credentials, client_secret, connection_string, subscription_key, api_key, apikey,
   client_id, tenant_id, access_token, refresh_token, Authorization, X-Vault-Token, x-api-key.

3. **C# `SecretString` ref struct** (src/Program.cs): Wraps decrypted secret values in a zeroing
   container. `Dispose()` clears the underlying char[] so plaintext does not linger in heap
   memory longer than necessary. Used at `ProfileRevealSecret` and `ProfileLaunch` decrypt sites.

4. **Service `redaction.rs`** (service/src/): Mirrors `scrub_stderr` patterns as a standalone
   module (`scrub_message()`) for use in tracing call sites within the service crate.

5. **`secrecy` crate** (both Cargo.toml): Added `secrecy = { version = "0.10", features = ["zeroize"] }`
   to provide `SecretString` zeroing-on-drop semantics in Rust IPC paths.

6. **`Microsoft.Extensions.Compliance.Redaction`** (env-manager.csproj): .NET pipeline redaction
   library for structured DataClassification-based redaction in future logging extensions.

## Consequences

- All stderr/log output across CLI, GUI, and service is scrubbed of known secret patterns.
- Plaintext secret values live in zeroing containers (C# SecretString, Rust secrecy SecretString).
- Pattern coverage is a best-effort allow-list, not a universal filter — unknown secret formats may
  still leak. The 512-char truncation limits blast radius.
- Adding a new secret pattern requires updating the pattern array in 3 places (C# + Rust main.rs +
  Rust service redaction.rs). The vocabulary is intentionally identical across all three.
