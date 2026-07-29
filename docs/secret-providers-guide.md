# Secret Providers Setup Guide

Env Manager supports 8 secret-store providers. Secrets are scoped to "Launch (local)" (per-process) profiles only; Global profiles cannot hold secrets. The active provider is selected in the Profile editor > Active provider dropdown. The activation probe runs once at selection time; if your environment is not configured, the selection is rejected with an inline error under the dropdown plus a toast -- you must fix the environment and retry.

Three providers work offline with no setup:

1. **dpapi-current-user** (default) - Windows DPAPI CurrentUser encryption. Secrets are encrypted in the user profile; only the same Windows user on the same machine can decrypt. Zero configuration; recommended for single-user machines.
2. **credential-manager** - Windows Credential Manager (advapi32 CredWriteW/CredReadW). Stores secrets in the Windows credential vault, blob encrypted via DPAPI before persist. No setup; the credential appears in Control Panel > Credential Manager.

The six external providers below require host-side setup. Each section lists the exact prereqs, the error Env Manager surfaces when a prereq is missing, and the fix.

---

## PowerShell SecretManagement

Vault name used by Env Manager: `EnvManager` (auto-registered on first use).

### Prerequisites

- PowerShell 7 or later (`pwsh`) installed and on PATH. Install via `winget install Microsoft.PowerShell`.
- `Microsoft.PowerShell.SecretManagement` module (v1.1.2 latest) installed for the current user.
- `Microsoft.PowerShell.SecretStore` module (v1.0.6 latest) installed for the current user.

### One-time install

Open pwsh and run:

```powershell
Install-Module Microsoft.PowerShell.SecretManagement, Microsoft.PowerShell.SecretStore -Scope CurrentUser -Force
```

The EnvManager vault is auto-registered as `Register-SecretVault -Name EnvManager -ModuleName Microsoft.PowerShell.SecretStore -AllowClobber` on the first `Set-Secret`/`Get-Secret` call, so you do not need to register it manually.

### Activation error and fix

Error: `Cannot activate provider 'powershell-secretmanagement': PowerShell SecretManagement module is not installed. Run: pwsh -Command "Install-Module Microsoft.PowerShell.SecretManagement, Microsoft.PowerShell.SecretStore -Scope CurrentUser -Force" then retry. (Vault: EnvManager). Fix the provider environment first (e.g. install pwsh modules, set VAULT_ADDR, or configure cloud credentials).`

Fix: install the two modules (command above), then in Env Manager re-select the provider.

---

## HashiCorp Vault (KV v2)

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

### Prerequisites

- `sops` binary on PATH (or `SOPS_PATH` env var pointing to the full binary path).
- A key-provider config: either a `.sops.yaml` file in the project root, or SOPS env vars for one of `age`, `pgp`, `aws_kms`, `azure_kv`, `gcp_kms`, or `vault`.

### One-time install

```powershell
winget install SOPS.SOPS2   # or: choco install sops

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

### Prerequisites

- `AWS_REGION` or `AWS_DEFAULT_REGION` env var set to the region hosting your secret (e.g. `us-east-1`).
- Credentials: `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, optional `AWS_SESSION_TOKEN` (for STS-derived credentials). Or an IAM role via instance metadata in an EC2/ECS context.
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

---

## Troubleshooting

- After installing modules / binaries / env vars, **restart Env Manager** before retrying. Providers read env vars at activation time, not at secret-store time.
- If the inline banner under the provider selector still shows the error after the env is fixed, click the Close button on the banner and re-select the provider to re-run the activation probe.
- The CLI command `env-manager-cli profile secret-provider list` lists available providers with the active one marked `(active)`.
- Audit entries for Rotation / Export / Import never log plaintext or ciphertext; only provider name and counts.
