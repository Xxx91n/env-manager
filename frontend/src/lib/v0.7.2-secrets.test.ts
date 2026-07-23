import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'

describe('v0.7.2 Phase 6-7 secret provider implementations', () => {
  it('SecretProvider.cs contains SopsProvider class', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('class SopsProvider : ISecretProvider')
    expect(src).toContain('public string Name => "sops"')
  })

  it('SopsProvider uses CREATE_NO_WINDOW and shells out to sops binary', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('CreateNoWindow = true')
    expect(src).toContain('Arguments = "-e --output')
    expect(src).toContain('Arguments = "-d --output')
  })

  it('SopsProvider supports age, pgp, and kms decryptors via env vars', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('SOPS_AGE_RECIPIENT')
    expect(src).toContain('SOPS_PGP_FP')
    expect(src).toContain('SOPS_KMS_ARN')
  })

  it('SopsProvider DiscoverSopsBinary checks SOPS_PATH, PATH, and common locations', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('SOPS_PATH')
    expect(src).toContain('sops.exe')
  })

  it('SopsProvider Delete is a no-op (self-contained envelope)', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('Delete(string envelope, string? context = null) { }')
  })

  it('SecretProvider.cs contains AzureKeyVaultProvider class', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('class AzureKeyVaultProvider : ISecretProvider')
    expect(src).toContain('public string Name => "azure-keyvault"')
  })

  it('AzureKeyVaultProvider enforces TLS (HTTPS only)', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'))
    expect(azureSection).toContain('https://')
    expect(azureSection).toContain('TLS mandatory')
  })

  it('AzureKeyVaultProvider uses REST API v7.4', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('api-version=" + API_VERSION')
  })

  it('AzureKeyVaultProvider supports managed identity and service principal', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('TryGetManagedIdentityToken')
    expect(src).toContain('TryGetServicePrincipalToken')
    expect(src).toContain('169.254.169.254')
    expect(src).toContain('AZURE_CLIENT_ID')
    expect(src).toContain('AZURE_CLIENT_SECRET')
    expect(src).toContain('AZURE_TENANT_ID')
  })

  it('AzureKeyVaultProvider CanRotate is true', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'))
    expect(azureSection).toContain('public bool CanRotate => true')
  })

  it('AzureKeyVaultProvider stores only vaultUri|secretName in TargetName', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('TargetName = vaultUri.TrimEnd(\'/\') + "|" + secretName')
  })

  it('AzureKeyVaultProvider sanitizes secret names to alphanumeric + hyphens', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('SanitizeSecretName')
    expect(src).toContain('char.IsLetterOrDigit(c) || c == \'-\'')
  })

  it('Both providers registered in SecretProviderManager._providers', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('["sops"] = new SopsProvider()')
    expect(src).toContain('["azure-keyvault"] = new AzureKeyVaultProvider()')
  })

  it('Total 6 providers in _providers dictionary', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    const managerSection = src.slice(src.indexOf('internal static class SecretProviderManager'))
    const providerEntries = managerSection.match(/\["\w+-?[\w-]*"\] = new \w+Provider\(\)/g)
    expect(providerEntries).not.toBeNull()
    expect(providerEntries!.length).toBe(6)
  })

  it('i18n key secrets.providerSops exists in all 10 translation files', () => {
    const langs = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
    for (const lang of langs) {
      const json = JSON.parse(readFileSync(`D:/Aworker/env-manager/frontend/src/lib/translations/${lang}.json`, 'utf8'))
      expect(json['secrets.providerSops'], `${lang}.json missing secrets.providerSops`).toBeTruthy()
    }
  })

  it('i18n key secrets.providerAzure exists in all 10 translation files', () => {
    const langs = ['en', 'zh', 'ja', 'ko', 'de', 'fr', 'es', 'pt', 'ru', 'ar']
    for (const lang of langs) {
      const json = JSON.parse(readFileSync(`D:/Aworker/env-manager/frontend/src/lib/translations/${lang}.json`, 'utf8'))
      expect(json['secrets.providerAzure'], `${lang}.json missing secrets.providerAzure`).toBeTruthy()
    }
  })

  it('AzureKeyVaultProvider token is cached in memory only (not persisted)', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'))
    expect(azureSection).toContain('_cachedToken')
    expect(azureSection).toContain('_tokenExpiry')
    expect(azureSection).toContain('DateTimeOffset.UtcNow < _tokenExpiry.AddMinutes(-5)')
  })

  it('AzureKeyVaultProvider has 15s HTTP timeout', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    expect(src).toContain('TimeSpan.FromSeconds(15)')
  })

  it('SopsProvider temp files are cleaned up in finally block', () => {
    const src = readFileSync('D:/Aworker/env-manager/SecretProvider.cs', 'utf8')
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('Directory.Delete(tempDir, true)')
  })
})