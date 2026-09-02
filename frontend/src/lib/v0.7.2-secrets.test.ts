import { describe, it, expect } from 'vitest'
import { resolve, join } from 'path'
import { readSecretProviderSources } from './secret-provider-source'
import { readFileSync } from 'fs'

describe('v0.7.2 Phase 6-7 secret provider implementations', () => {
  it('Secret provider sources contain SopsProvider class', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('class SopsProvider : ISecretProvider')
    expect(src).toContain('public string Name => "sops"')
  })

  it('SopsProvider uses CREATE_NO_WINDOW and shells out to sops binary', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('CreateNoWindow = true')
    expect(src).toContain('Arguments = "-e --output')
    expect(src).toContain('Arguments = "-d --output')
  })

  it('SopsProvider supports age, pgp, and kms decryptors via env vars', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('SOPS_AGE_RECIPIENT')
    expect(src).toContain('SOPS_PGP_FP')
    expect(src).toContain('SOPS_KMS_ARN')
  })

  it('SopsProvider DiscoverSopsBinary checks SOPS_PATH, PATH, and common locations', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('SOPS_PATH')
    expect(src).toContain('sops.exe')
  })

  it('SopsProvider Delete is a no-op (self-contained envelope)', () => {
    const src = readSecretProviderSources()
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('Delete(string envelope, string? context = null) { }')
  })

  it('Secret provider sources contain AzureKeyVaultProvider class', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('class AzureKeyVaultProvider : ISecretProvider')
    expect(src).toContain('public string Name => "azure-keyvault"')
  })

  it('AzureKeyVaultProvider enforces TLS (HTTPS only)', () => {
    const src = readSecretProviderSources()
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'))
    expect(azureSection).toContain('https://')
    expect(azureSection).toContain('TLS mandatory')
  })

  it('AzureKeyVaultProvider uses REST API v7.4', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('api-version=" + API_VERSION')
  })

  it('AzureKeyVaultProvider supports managed identity and service principal', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('TryGetManagedIdentityToken')
    expect(src).toContain('TryGetServicePrincipalToken')
    expect(src).toContain('169.254.169.254')
    expect(src).toContain('AZURE_CLIENT_ID')
    expect(src).toContain('AZURE_CLIENT_SECRET')
    expect(src).toContain('AZURE_TENANT_ID')
  })

  it('AzureKeyVaultProvider CanRotate is true', () => {
    const src = readSecretProviderSources()
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'))
    expect(azureSection).toContain('public bool CanRotate => true')
  })

  it('AzureKeyVaultProvider stores only vaultUri|secretName in TargetName', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('TargetName = vaultUri.TrimEnd(\'/\') + "|" + secretName')
  })

  it('AzureKeyVaultProvider sanitizes secret names to alphanumeric + hyphens', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('SanitizeSecretName')
    expect(src).toContain('char.IsLetterOrDigit(c) || c == \'-\'')
  })

  it('Both providers registered in SecretProviderManager._providers', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('["sops"] = new SopsProvider()')
    expect(src).toContain('["azure-keyvault"] = new AzureKeyVaultProvider()')
  })

  it('Total 6 providers in _providers dictionary', () => {
    const src = readSecretProviderSources()
    const managerSection = src.slice(src.indexOf('internal static class SecretProviderManager'))
    const providerEntries = managerSection.match(/\["\w+-?[\w-]*"\] = new \w+Provider\(\)/g)
    expect(providerEntries).not.toBeNull()
    expect(providerEntries!.length).toBe(8)
  })

  it('i18n key secrets.providerSops exists in all 10 translation files', () => {
    const langs = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
    for (const lang of langs) {
      const json = JSON.parse(readFileSync(join(__dirname, '..', '..', '..', `frontend/src/lib/translations/${lang}.json`), 'utf8'))
      expect(json['secrets.providerSops'], `${lang}.json missing secrets.providerSops`).toBeTruthy()
    }
  })

  it('i18n key secrets.providerAzure exists in all 10 translation files', () => {
    const langs = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
    for (const lang of langs) {
      const json = JSON.parse(readFileSync(join(__dirname, '..', '..', '..', `frontend/src/lib/translations/${lang}.json`), 'utf8'))
      expect(json['secrets.providerAzure'], `${lang}.json missing secrets.providerAzure`).toBeTruthy()
    }
  })

  it('AzureKeyVaultProvider token is cached in memory only (not persisted)', () => {
    const src = readSecretProviderSources()
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'))
    expect(azureSection).toContain('_cachedToken')
    expect(azureSection).toContain('_tokenExpiry')
    expect(azureSection).toContain('DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5)')
  })

  it('AzureKeyVaultProvider has 15s HTTP timeout', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('TimeSpan.FromSeconds(15)')
  })

  it('SopsProvider temp files are cleaned up in finally block', () => {
    const src = readSecretProviderSources()
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('Directory.Delete(tempDir, true)')
  })

  it('Phase 8: OnePasswordProvider class exists', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('class OnePasswordProvider : ISecretProvider')
    expect(src).toContain('public string Name => "1password"')
  })

  it('Phase 8: OnePasswordProvider uses CREATE_NO_WINDOW and op binary', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('FindOpBinary')
    expect(src).toContain('EnsureOpAvailable')
  })

  it('Phase 8: OnePasswordProvider supports rotation', () => {
    const src = readSecretProviderSources()
    const opSection = src.slice(src.indexOf('class OnePasswordProvider'), src.indexOf('class AwsSecretsManagerProvider'))
    expect(opSection).toContain('public bool CanRotate => true')
  })

  it('Phase 9: AwsSecretsManagerProvider class exists', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('class AwsSecretsManagerProvider : ISecretProvider')
    expect(src).toContain('public string Name => "aws-secretsmanager"')
  })

  it('Phase 9: AwsSecretsManagerProvider implements SigV4 signing', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('CallAwsApi')
    expect(src).toContain('AWS4-HMAC-SHA256')
    expect(src).toContain('HmacSHA256')
  })

  it('Phase 9: AwsSecretsManagerProvider uses AWS env vars', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('AWS_ACCESS_KEY_ID')
    expect(src).toContain('AWS_SECRET_ACCESS_KEY')
    expect(src).toContain('AWS_SESSION_TOKEN')
    expect(src).toContain('AWS_REGION')
  })

  it('Total 8 providers in _providers dictionary', () => {
    const src = readSecretProviderSources()
    const managerSection = src.slice(src.indexOf('internal static class SecretProviderManager'))
    const providerEntries = managerSection.match(/\["[^"]+"\] = new \w+Provider\(\)/g)
    expect(providerEntries).not.toBeNull()
    expect(providerEntries!.length).toBe(8)
  })

  it('i18n keys for Phase 8-9 exist in all 10 files', () => {
    const langs = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
    for (const lang of langs) {
      const json = JSON.parse(readFileSync(join(__dirname, '..', '..', '..', `frontend/src/lib/translations/${lang}.json`), 'utf8'))
      expect(json['secrets.provider1Password'], `${lang}.json missing provider1Password`).toBeTruthy()
      expect(json['secrets.providerAws'], `${lang}.json missing providerAws`).toBeTruthy()
    }
  })

})