# Security Policy

## Supported Versions

Env Manager is in active development. Security fixes are applied to the latest release only.

| Version | Supported |
|---------|-----------|
| Latest  | Yes       |
| < Latest| No        |

## Reporting a Vulnerability

If you discover a security vulnerability in Env Manager, please report it responsibly:

1. **Do NOT open a public GitHub issue.**
2. Go to the repository's [Security tab](https://github.com/Xxx91n/env-manager/security/advisories/new) and create a private security advisory using GitHub's built-in feature.
3. Describe the vulnerability with enough detail to reproduce: affected component (CLI, GUI, Service, IPC), attack surface, version, and steps to trigger.
4. If you have a proposed fix, include it in the advisory.

You will receive a response within 72 hours. If the vulnerability is confirmed, a patch will be released as soon as possible depending on severity.

## Security Architecture

Env Manager handles environment variable mutation and secret provider integration. Key security surfaces:

- **Cross-process mutex**: All write operations acquire `Local\EnvManager.RegistryMutation` mutex plus a Rust `CLI_RWLOCK` write lock plus a frontend `writeChain` serialization. Three layers, never bypassed.
- **Secret storage**: Secret values are encrypted via provider-specific mechanisms (DPAPI, CredMan, Vault, SOPS, Azure KV, 1Password, AWS SM). Plaintext never persists to disk or logs.
- **Launch profile isolation**: Launch profiles inject variables into a child process via `env_clear + inject`. Never written to registry, never broadcast `WM_SETTINGCHANGE`.
- **Named pipe IPC**: Anti-squatting (`PIPE_FIRST_PIPE_INSTANCE`) prevents pipe hijacking. 65536-byte request cap. Newline-delimited JSON protocol.
- **Input validation**: CLI command whitelist, max 64 args, max 32767 chars per arg, null bytes and control characters rejected.

See `AGENTS.md` for the complete list of 40+ hard boundaries.