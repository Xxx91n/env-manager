# Secret Providers Setup Guide

> **Official upstream docs**: this guide mirrors vendor-canonical configuration. Per-provider primary references are linked inline at each section: [Microsoft.PowerShell.SecretManagement](https://www.powershellgallery.com/packages/Microsoft.PowerShell.SecretManagement) + [SecretStore](https://www.powershellgallery.com/packages/Microsoft.PowerShell.SecretStore); [HashiCorp Vault KV v2](https://developer.hashicorp.com/vault/docs/secrets/kv/kv-v2); [Mozilla SOPS](https://getsops.io/) and [age](https://github.com/FiloSottile/age); [Azure Key Vault REST API](https://learn.microsoft.com/en-us/rest/api/keyvault/) and [az CLI managed identity / SP](https://learn.microsoft.com/en-us/azure/key-vault/general/authentication); [1Password CLI](https://developer.1password.com/docs/cli/getting-started); [AWS Secrets Manager REST API + SigV4](https://docs.aws.amazon.com/secretsmanager/latest/userguide/); [IAM Roles Anywhere signing process](https://docs.aws.amazon.com/rolesanywhere/latest/userguide/authentication-sign-process.html).

Env Manager ships nine secret providers. Secrets belong to Launch profiles only, because those put values into one child process; a Global profile cannot hold a secret. The active provider is selected in the Profile editor > Active provider dropdown. The activation probe runs once at selection time; if your environment is not configured, the selection is rejected with an inline error under the dropdown plus a toast -- you must fix the environment and retry.

Three providers need no cloud account:

1. **dpapi-current-user** (default) - Windows DPAPI CurrentUser encryption. Secrets are encrypted in the user profile; only the same Windows user on the same machine can decrypt. Zero configuration; recommended for single-user machines.
2. **credential-manager** - Windows Credential Manager (advapi32 CredWriteW/CredReadW). Stores secrets in the Windows credential vault, blob encrypted via DPAPI before persist. No setup; the credential appears in Control Panel > Credential Manager.
3. **powershell-secretmanagement** - PowerShell SecretStore. Needs the PowerShell 7 modules listed in its own section (a one-time `Install-Module`), but no cloud account.

The six external providers below require host-side setup. Each section lists the exact prereqs, the error Env Manager surfaces when a prereq is missing, and the fix.

---

### Capability matrix

Every provider declares its capability descriptors on `ISecretProvider`, and `profile secret-provider list` reports them per line as `[refresh=yes|no certauth=yes|no network=yes|no]` plus real availability (`(unavailable)` when the cheap environment gate fails). The `Periodic refresh` column is what the provider declares, and it is what the Service page in the GUI shows read-only next to each mount (there is no policy picker in the GUI); the service-side loop that actually re-reads a `Periodic` mount is still tracked in [this issue](https://github.com/Xxx91n/env-manager/issues/276), so a declared `yes` does not yet mean values refresh on their own.

| Provider | Periodic refresh | Cert-bound auth | Network | Cheap availability gate |
|----------|------------------|-----------------|---------|--------------------------|
| dpapi-current-user | no (CreatedOnly) | no | no | always usable on Windows |
| credential-manager | no (CreatedOnly) | no | no | always usable on Windows |
| powershell-secretmanagement | no (CreatedOnly) | no | no | declared usable; module check fails closed at activation |
| vault-kv2 | yes (Periodic) | yes (AppRole/TLS) | yes | VAULT_ADDR + VAULT_TOKEN set |
| sops | no (CreatedOnly) | no | no | resolved sops binary exists |
| azure-keyvault | yes (Periodic) | yes (SP certificate) | yes | AZURE_KEYVAULT_URI set |
| 1password | yes (Periodic) | no | yes | resolved op binary exists |
| aws-secretsmanager | yes (Periodic) | no (static access keys) | yes | region + access key + secret key set |
| aws-roles-anywhere | yes (Periodic) | yes (certificate) | yes | region + role, profile and trust-anchor ARNs + certificate SHA-1 set |

Network providers additionally survive a probe before `list` reports them available (the same check `set` has always run); local providers never probe. The probe kind differs by where the provider keeps its data: an ordinary network provider runs a sentinel Encrypt/Decrypt/Delete round-trip, while a provider that declares `ProbeWritesData` as false - `aws-roles-anywhere`, whose store is your AWS account - only exchanges its certificate for temporary credentials and writes nothing.

---

## PowerShell SecretManagement

Official docs: [Microsoft.PowerShell.SecretManagement (Gallery)](https://www.powershellgallery.com/packages/Microsoft.PowerShell.SecretManagement) · [Microsoft.PowerShell.SecretStore (Gallery)](https://www.powershellgallery.com/packages/Microsoft.PowerShell.SecretStore) · [SecretManagement module guide (learn.microsoft.com)](https://learn.microsoft.com/powershell/module/microsoft.powershell.secretmanagement/).

Vault name used by Env Manager: `EnvManager` (auto-registered on first use).

### Prerequisites

- PowerShell 7 or later (`pwsh`) installed and on PATH. Install via `winget install Microsoft.PowerShell` or download from [PowerShell GitHub Releases](https://github.com/PowerShell/PowerShell/releases).
- `Microsoft.PowerShell.SecretManagement` module (v1.1.2 latest) installed for the current user.
- `Microsoft.PowerShell.SecretStore` module (v1.0.6 latest) installed for the current user.

### One-time install

Open pwsh and run:

```powershell
Install-Module Microsoft.PowerShell.SecretManagement, Microsoft.PowerShell.SecretStore -Scope CurrentUser -Force
```

### SecretStore no-password bootstrap (required before switching to the provider headlessly)

The `Microsoft.PowerShell.SecretStore` vault defaults to an interactive password
prompt on every read/write. The Env Manager activation probe runs headless
(no terminal to type a password into), so a fresh SecretStore will hang the
probe until the 30s timeout and report a `Set-Secret is not recognized` /
`CLIXML` failure. Configure the store for no interactive password once before
switching to the provider:

```powershell
Set-SecretStoreConfiguration -Scope CurrentUser -Authentication None `
  -Password (ConvertTo-SecureString -String 'none' -AsPlainText -Force) -Confirm:$false
```

This disables the password prompt (acceptable if your Windows profile is
already access-controlled). If you require an interactive password, configure
it once in a real terminal, then activation will use the cached vault.

The EnvManager vault is auto-registered as `Register-SecretVault -Name EnvManager -ModuleName Microsoft.PowerShell.SecretStore -AllowClobber` on the first `Set-Secret`/`Get-Secret` call, so you do not need to register it manually.

### Activation error and fix

Error: `Cannot activate provider 'powershell-secretmanagement': PowerShell SecretManagement module is not installed. Run: pwsh -Command "Install-Module Microsoft.PowerShell.SecretManagement, Microsoft.PowerShell.SecretStore -Scope CurrentUser -Force" then retry. (Vault: EnvManager). Fix the provider environment first (e.g. install pwsh modules, set VAULT_ADDR, or configure cloud credentials).`

Fix: install the two modules (command above), then in Env Manager re-select the provider.

---

## HashiCorp Vault (KV v2)

Official docs: [Vault KV v2 secrets engine](https://developer.hashicorp.com/vault/docs/secrets/kv/kv-v2) · [Vault HTTP API](https://developer.hashicorp.com/vault/api-docs) · [Vault CLI](https://developer.hashicorp.com/vault/docs/commands) · [Vault Downloads](https://developer.hashicorp.com/vault/downloads).

### Prerequisites

- A reachable Vault server with the KV v2 secrets engine mounted (e.g. `vault secrets enable -path=secret kv-v2`).
- `VAULT_ADDR` env var set to the Vault URL. TLS is mandatory for non-localhost addresses; `http://` is only permitted for `127.0.0.1` / `localhost` / `[::1]`.
- `VAULT_TOKEN` env var set to a token with read/write on `secret/data/<path>`.

### One-time setup (local dev server)

```bash
vault server -dev -dev-root-token-id=root &
export VAULT_ADDR=http://127.0.0.1:8200
export VAULT_TOKEN=root
vault secrets enable -path=secret kv-v2
```

### Activation error and fix

Error (missing address): `Cannot activate provider 'vault-kv2': VAULT_ADDR environment variable not set.`

Fix: set `VAULT_ADDR` (and `VAULT_TOKEN`) in your shell, restart Env Manager, then re-select the provider.

Error (TLS): `Cannot activate provider vault-kv2: VAULT_ADDR must use https:// for non-localhost addresses.`

Fix: use `https://` for any non-localhost Vault, or tunnel through `127.0.0.1` / `localhost` / `[::1]` where `http://` is permitted.

Error (token): `Cannot activate provider vault-kv2: VAULT_TOKEN is not set.`

Fix: `vault login` (interactive) or `export VAULT_TOKEN=<token>` then retry.

---

## SOPS (Mozilla SOPS / getsops)

Official docs: [SOP homepage + age provider](https://getsops.io/) · [SOP age provider config (.sops.yaml)](https://getsops.io/docs/usage/identities/age/) · [age keygen / recipient docs](https://github.com/FiloSottile/age).

### Prerequisites

- `sops` binary on PATH (or `SOPS_PATH` env var pointing to the full binary path).
- A key-provider config: either a `.sops.yaml` file in the project root, or SOPS env vars for one of `age`, `pgp`, `aws_kms`, `azure_kv`, `gcp_kms`, or `vault`.

### One-time install

```powershell
winget install SOPS.SOPS2   # or: choco install sops   # or download from https://github.com/getsops/sops/releases

# Pick exactly one key provider and set its env vars:

# Age (recommended, simplest):
#   generate an age key: age-keygen -o age.key.txt
#   set:  $env:SOPS_AGE_KEY_FILE = "$PWD\age.key.txt"  (and SOPS_AGE_RECIPIENTS to the recipient line)

# PGP:
#   set:  $env:SOPS_PGP_FP = <your-key-fingerprint>

# AWS KMS:
#   set:  AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY, AWS_REGION
#   reference the kms key arn in .sops.yaml

# Azure Key Vault (KMS): set AZURE_TENANT_ID, AZURE_CLIENT_ID, AZURE_CLIENT_SECRET
# GCP KMS: set GOOGLE_APPLICATION_CREDENTIALS path and reference key in .sops.yaml
# Vault Transit: set VAULT_ADDR and VAULT_TOKEN and reference key in .sops.yaml
```

### Sample .sops.yaml (age)

```yaml
creation_rules:
  - path_regex: .*
    age:
      - age1<your-recipient-key>
```

### Activation error and fix

Error (binary missing): `Cannot activate provider 'sops': sops binary not found. Install sops and ensure it is on PATH, or set SOPS_PATH env var..`

Fix: install sops (winget/choco) or set `SOPS_PATH` to the full binary, then retry.

Error (no keys): `Cannot activate provider 'sops': sops encryption failed (exit 1): config file not found and no keys provided through command line options.`

Fix: create `.sops.yaml` with at least one creation rule pointing to a key, or set the corresponding SOPS provider env vars (e.g. `SOPS_AGE_KEY_FILE`), then retry.

---

## Azure Key Vault

Official docs: [Key Vault REST API](https://learn.microsoft.com/en-us/rest/api/keyvault/) · [Key Vault authentication (managed identity / SP)](https://learn.microsoft.com/en-us/azure/key-vault/general/authentication) · [az CLI (az keyvault / az role assignment)](https://learn.microsoft.com/en-us/cli/azure/keyvault) · [Azure CLI Downloads](https://learn.microsoft.com/en-us/cli/azure/install-azure-cli).

### Prerequisites

- An Azure Key Vault created in your subscription. Note its URI, e.g. `https://myvault.vault.azure.net`.
- `AZURE_KEYVAULT_URI` env var set to that URI (https only).
- Authentication: either a managed identity (default on Azure VMs / via IMDS at 169.254.169.254), OR a service principal with the env vars `AZURE_CLIENT_ID`, `AZURE_CLIENT_SECRET`, `AZURE_TENANT_ID`.
- The identity / SP must have the Key Vault Secrets User and Secrets Contributor roles (or an access policy granting get/set on secrets).

### Activation error and fix

Error (URI missing): `Cannot activate provider 'azure-keyvault': AZURE_KEYVAULT_URI environment variable not set (e.g. https://myvault.vault.azure.net).`

Fix: `setx AZURE_KEYVAULT_URI "https://<your-vault>.vault.azure.net"` then configure either managed identity (default on Azure VMs / via IMDS at 169.254.169.254) or service principal env vars `AZURE_CLIENT_ID` / `AZURE_CLIENT_SECRET` / `AZURE_TENANT_ID`. Restart Env Manager and re-select the provider.

---

## 1Password (op CLI)

Official docs: [1Password CLI getting started](https://developer.1password.com/docs/cli/getting-started) · [op account add / sign in](https://developer.1password.com/docs/cli/signin) · [desktop app integration](https://developer.1password.com/docs/cli/app-integration) · [service accounts](https://developer.1password.com/docs/service-accounts) · [Connect server](https://developer.1password.com/docs/connect).

### Prerequisites

- `op` (1Password CLI) on PATH, or `OP_PATH` env var pointing to the binary.
- Auth: one of
  - **Desktop app integration** (recommended): turn on 1Password desktop app > Settings > Developer > Integrate with 1Password CLI. No token needed.
  - **Manual account**: `op account add` and sign in by entering the account password.
  - **Service account** (headless/automation): set `OP_SERVICE_ACCOUNT_TOKEN` env var to the service account token.
  - **Connect server** (self-hosted): set `OP_CONNECT_HOST` and `OP_CONNECT_TOKEN`.


### One-time install

```powershell
winget install AgileBits.1Password.CLI   # or download from https://1password.com/downloads/cli/
op account add  # interactive
# or for service account:
#  setx OP_SERVICE_ACCOUNT_TOKEN "ops_xxx"
```

### Activation error and fix

Error (binary missing): `Cannot activate provider 1password: 1Password CLI (op) not found. Install op and ensure it is on PATH, or set OP_PATH env var.`

Fix: install the `op` CLI (`winget install AgileBits.1Password.CLI`) or set `OP_PATH` to the full binary path, then retry.

Error (no accounts): `Cannot activate provider 1password: 1Password CLI create failed (exit 1): No accounts configured for use with 1Password CLI.`

Fix: either turn on the desktop app integration, run `op account add`, or set `OP_SERVICE_ACCOUNT_TOKEN` (or `OP_CONNECT_HOST`/`OP_CONNECT_TOKEN`), then retry.

---

## AWS Secrets Manager

Official docs: [Secrets Manager REST API](https://docs.aws.amazon.com/secretsmanager/latest/userguide/) · [SigV4 signing process](https://docs.aws.amazon.com/general/latest/gr/sigv4_signing.html) · [IAM credentials / STS assume-role](https://docs.aws.amazon.com/IAM/latest/UserGuide/security-creds.html) · [AWS CLI Downloads](https://aws.amazon.com/cli/).

### Prerequisites

- `AWS_REGION` or `AWS_DEFAULT_REGION` env var set to the region hosting your secret (e.g. `us-east-1`).
- Credentials: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN` (for STS-derived credentials).
- IAM permission to `secretsmanager:GetSecretValue` / `PutSecretValue` / `DeleteSecretValue` for the secret.

### One-time setup

```powershell
setx AWS_REGION us-east-1
setx AWS_ACCESS_KEY_ID AKIAxxxx
setx AWS_SECRET_ACCESS_KEY xxx
# For STS sessions also:
#  setx AWS_SESSION_TOKEN xxx
```

Refresh env vars (restart the shell / Env Manager), then re-select the provider.

### Activation error and fix

Error: `Cannot activate provider 'aws-secretsmanager': AWS_REGION or AWS_DEFAULT_REGION not set.`

Fix: set `AWS_REGION` (and credentials) env vars, restart Env Manager, then retry.

### Local emulator (LocalStack and similar)

Set `AWS_ENDPOINT_URL_SECRETS_MANAGER` to the emulator address to send every Secrets Manager call there instead of AWS. This is the official AWS service-specific endpoint override.

```powershell
setx AWS_ENDPOINT_URL_SECRETS_MANAGER http://127.0.0.1:4566
```

`https://` works for any address. `http://` works only for a host on this machine (the loopback range, `localhost`, `[::1]`); plain http to any other host is refused, because the signed request and its session token would travel in clear text. An address that is neither `http://` nor `https://` is refused too. Production deployments leave this variable unset.

---

## AWS IAM Roles Anywhere

Official docs: [Signing process for IAM Roles Anywhere](https://docs.aws.amazon.com/rolesanywhere/latest/userguide/authentication-sign-process.html) · [CreateSession API](https://docs.aws.amazon.com/rolesanywhere/latest/userguide/authentication-create-session.html) · [Trust model and certificate requirements](https://docs.aws.amazon.com/rolesanywhere/latest/userguide/trust-model.html).

This provider reads the same remote Secrets Manager secrets as `aws-secretsmanager`, but it never keeps a long-lived access key. It presents a certificate from the machine certificate store, exchanges it for short-lived credentials, and uses those to read or write the secret. The two providers use the same envelope payload shape (`region|secretId`), but the envelope records which provider wrote it and a mismatch is rejected, so switching providers means re-adding the secret rather than just changing the dropdown.

Re-adding under a new name is what `add-secret` does: it always calls `CreateSecret`, so reusing the same name that `aws-secretsmanager` already created fails with `ResourceExistsException`. To move an existing secret, pick a new name with the new provider and delete the old remote secret by hand; there is no in-product migration between the two providers.

### How the exchange works

The request is a signed `POST /sessions`. The signature differs from ordinary SigV4 in four ways, all handled inside Env Manager: the algorithm name is `AWS4-X509-RSA-SHA256` or `AWS4-X509-ECDSA-SHA256`, the credential scope names the certificate serial number in decimal instead of an access key, the certificate itself travels in a signed `X-Amz-X509` header (with intermediates in `X-Amz-X509-Chain`), and the signature is made with the certificate's private key instead of a derived HMAC key.

That exchange always goes to the real AWS address. `AWS_ENDPOINT_URL_SECRETS_MANAGER` does not redirect it, only the Secrets Manager calls that follow.

### Prerequisites

- `AWS_REGION` or `AWS_DEFAULT_REGION` set to the region of the trust anchor.
- `AWS_ROLES_ANYWHERE_ROLE_ARN`, `AWS_ROLES_ANYWHERE_PROFILE_ARN`, `AWS_ROLES_ANYWHERE_TRUST_ANCHOR_ARN`.
- `AWS_ROLES_ANYWHERE_CERT_SHA1`: the SHA-1 fingerprint of the certificate, with or without spaces.
- Optional `AWS_ROLES_ANYWHERE_DURATION_SECONDS`: how long to ask for, default 3600. Values outside the 900-43200 range AWS accepts are clamped.
- The certificate in `Cert:\LocalMachine\My`, with its private key readable by the account that runs Env Manager. An interactive session uses your own account; the service uses `NT SERVICE\EnvManagerService`.
- The role must allow `secretsmanager:GetSecretValue`, `CreateSecret`, `PutSecretValue` and `DeleteSecret` for the secrets you use. Creating a secret is needed because that is what storing one does.

Machine-level environment variables are required, not per-user ones. `setx` without `/M` writes to your own account, which the service cannot see.

### Certificate requirements

- X.509 v3, `CA=false`, key usage including digital signature.
- The private key must **not** be marked "strong private key protection". That flag makes Windows show a confirmation prompt on every key use, and a service running in session 0 has nobody to click it, so every call fails.
- A non-exportable software key is not an absolute barrier: an administrator can still extract it. For real key isolation, generate the key on a TPM. Env Manager's threat model already excludes the local administrator, so the software key is the documented default.

### One-time setup

1. Obtain a certificate issued by the CA your trust anchor trusts.
2. Import it into the machine store: `certutil -f -importpfx My identity.pfx`, or `Import-PfxCertificate -CertStoreLocation Cert:\LocalMachine\My`.
3. Give the running account read access to the private key: `certlm.msc` > certificate > All Tasks > Manage Private Keys > add `NT SERVICE\EnvManagerService` with Read.
4. Register the CA as a trust anchor, create a profile that allows the role, and allow the role to be assumed from `rolesanywhere.amazonaws.com`.
5. Set the environment variables machine-wide and restart Env Manager.

### Activation error and fix

Error: `Cannot activate provider 'aws-roles-anywhere': IAM Roles Anywhere is not configured; missing AWS_ROLES_ANYWHERE_ROLE_ARN, AWS_ROLES_ANYWHERE_CERT_SHA1.`

Fix: the message names every missing variable. Set them machine-wide, restart Env Manager, then retry.

Error: `certificate <fingerprint> is not in Cert:\LocalMachine\My` (or `expired`, or `has no private key`).

Fix: import the certificate into the machine store, or reissue and update `AWS_ROLES_ANYWHERE_CERT_SHA1`. Env Manager never falls back to a different certificate.

Error: `CreateSession rejected certificate <fingerprint> (401): check the trust anchor contains the issuing CA`.

Fix: check that the trust anchor bundle includes the intermediate CA that issued your certificate, and that the certificate is not revoked.

### What the probe does, and what it does not

Selecting this provider exchanges your certificate for temporary credentials once, to prove the certificate, role, profile and trust anchor all line up. It does not create, read or delete any secret, so opening the profile editor never writes to your AWS account.

### What is not covered by automated tests

No emulator implements `CreateSession`, so the wire protocol is not exercised in CI. The offline suite proves the signature, the header set, the decimal serial in the credential scope and the envelope shape against a locally generated certificate, but only AWS can accept a real signature. Before relying on this provider, run it once against a throwaway trust anchor:

1. Register a disposable CA as a trust anchor, a profile, and a role allowed to assume from `rolesanywhere.amazonaws.com` with `secretsmanager:CreateSecret` and `secretsmanager:GetSecretValue`.
2. Import the test certificate into `Cert:\LocalMachine\My`, note its SHA-1, and set the five `AWS_ROLES_ANYWHERE_*` variables machine-wide.
3. Add a secret to a Launch profile with `env-manager-cli profile add-secret <profile> <name> <value>`; this is the call that creates the envelope and, through the provider, the remote secret.
4. Read it back with `env-manager-cli profile reveal-secret <profile> <name>` and confirm the value matches. `profile apply` cannot serve this step: it refuses Launch profiles outright, because applying would put a Launch profile's secrets into the registry.
5. Delete the remote secret, the profile, the trust anchor and the certificate. Nothing about the run needs to stay.

If step 4 fails with `CreateSession rejected certificate`, the failure is on the AWS side (trust anchor, revocation, role trust policy). A local signature check does not exonerate the request that actually left the machine: it proves the signature matches the string this code built, not the request AWS received. Env Manager logs the upstream error and the certificate fingerprint; compare the response's complaint against the trust anchor and the role's trust policy before assuming the signing code is wrong.

### The service does not refresh this provider yet

`RefreshCapable` is `true`, but the Rust service's periodic pass only stamps mount metadata; it does not call `Decrypt`. That gap is tracked in [#276](https://github.com/Xxx91n/env-manager/issues/276) and applies to every provider, not just this one. Until it lands, a `Periodic` mount keeps its last value until the CLI reads it again on launch.

---

## Troubleshooting

- After installing modules / binaries / env vars, **restart Env Manager** before retrying. Providers read env vars at activation time, not at secret-store time.
- If the inline banner under the provider selector still shows the error after the env is fixed, click the Close button on the banner and re-select the provider to re-run the activation probe.
- The CLI command `env-manager-cli profile secret-provider list` lists available providers with the active one marked `(active)`.
- Audit entries for Rotation / Export / Import never log plaintext or ciphertext; only provider name and counts.
- Provider activation errors are emitted to `logs/env-manager.log` with a truncated, scrubbed `stderr_hint` line (any `Bearer`, `token=`, `password=`, `setx`, `VAULT_TOKEN=`, `AWS_SECRET_ACCESS_KEY=`, `AWS_SESSION_TOKEN=`, or `OP_SERVICE_ACCOUNT_TOKEN=` segment is masked to `<redacted>`). The log records the error message shape, never a secret value. If the inline banner disappears, the `logs/env-manager.log` line still shows the upstream error to copy into a support request.


### v0.9.12 Expanded Redaction

The redaction vocabulary was expanded from 10 to 22 patterns. In addition to the original Bearer/token/password/setx patterns, the following are now masked: client_secret=, connection_string=, subscription_key=, api_key=, apikey=, client_id=, tenant_id=, access_token=, refresh_token=, Authorization:, X-Vault-Token:, x-api-key:. All three tiers (CLI stderr, Rust tracing logs, service tracing logs) use the identical vocabulary. The complete list lives in `ScrubExceptionMessage` (`src/CliRuntime.cs`), `scrub_stderr` (`frontend/src-tauri/src/main.rs`), and `service/src/redaction.rs`.
