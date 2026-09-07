// Shared by the secret-provider gate tests (architecture-recovery issue 09): the
// engine's secret provider sources live in per-symbol modules under src/, listed
// here in the original single-file order so slice-based assertions keep working.
import { readFileSync } from 'fs'
import { resolve } from 'path'

const SECRET_PROVIDER_MODULES = [
  'Secrets/Core/SecretEnvelope.cs',
  'Secrets/Core/SecretEnvelopeJsonContext.cs',
  'Secrets/Core/ProviderConfigJsonContext.cs',
  'Secrets/Core/ISecretProvider.cs',
  'Secrets/Providers/DpapiCurrentUserProvider.cs',
  'Secrets/Providers/CredentialManagerProvider.cs',
  'Secrets/Providers/PowerShellSecretManagementProvider.cs',
  'Secrets/Providers/VaultKV2Provider.cs',
  'Secrets/Providers/SopsProvider.cs',
  'Secrets/Providers/AzureKeyVaultProvider.cs',
  'Secrets/Providers/OnePasswordProvider.cs',
  'Secrets/Providers/AwsSecretsManagerProvider.cs',
  'Secrets/Manager/SecretProviderManager.cs',
] as const

export function readSecretProviderSources(): string {
  return SECRET_PROVIDER_MODULES.map((f) =>
    readFileSync(resolve(__dirname, '..', '..', '..', 'src', f), 'utf8'),
  ).join('\n')
}
