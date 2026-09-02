// Shared by the secret-provider gate tests (architecture-recovery issue 09): the
// engine's secret provider sources live in per-symbol modules under src/, listed
// here in the original single-file order so slice-based assertions keep working.
import { readFileSync } from 'fs'
import { resolve } from 'path'

const SECRET_PROVIDER_MODULES = [
  'SecretEnvelope.cs',
  'SecretEnvelopeJsonContext.cs',
  'ProviderConfigJsonContext.cs',
  'ISecretProvider.cs',
  'DpapiCurrentUserProvider.cs',
  'CredentialManagerProvider.cs',
  'PowerShellSecretManagementProvider.cs',
  'VaultKV2Provider.cs',
  'SopsProvider.cs',
  'AzureKeyVaultProvider.cs',
  'OnePasswordProvider.cs',
  'AwsSecretsManagerProvider.cs',
  'SecretProviderManager.cs',
] as const

export function readSecretProviderSources(): string {
  return SECRET_PROVIDER_MODULES.map((f) =>
    readFileSync(resolve(__dirname, '..', '..', '..', 'src', f), 'utf8'),
  ).join('\n')
}
