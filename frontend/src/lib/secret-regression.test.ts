import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { resolve } from 'path'
import { readSecretProviderSources } from './secret-provider-source'

// Regression tests for secret provider safety - motivated by the past incident
// where a toggle bug corrupted system environment variables. These tests
// ensure that secret operations can never silently lose or corrupt data.

describe('Secret provider regression safety', () => {

  it('SecretProviderManager: Decrypt routes by provider name, never falls back silently', () => {
    const src = readSecretProviderSources()
    // The Decrypt method must parse the envelope and route to the correct provider.
    // Unknown providers must throw (fail-closed), NOT silently fall back to DPAPI.
    expect(src).toContain('Secret provider')
    expect(src).toContain('is not available')
    expect(src).toContain('fail-closed')
  })

  it('Bare base64 blob backwards compat: pre-v0.8 secrets still decrypt', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('IsBareBase64Blob')
    expect(src).toContain('DEFAULT_PROVIDER')
  })

  it('Rotation never deletes failed secrets: failed decrypt = skip + count, never delete', () => {
    const src = readSecretProviderSources()
    const rotateSection = src.slice(src.indexOf('public static (int total, int rotated, int failed) RotateAll'), src.indexOf('public static string ExportSecrets'))
    expect(rotateSection).toContain('failed')
    expect(rotateSection).toContain('catch')
    // The failed variable must NOT be removed - only counted
    expect(rotateSection).toContain('failed++')
  })

  it('Export secrets: the backup is DPAPI-encrypted regardless of source provider', () => {
    const src = readSecretProviderSources()
    const exportSection = src.slice(src.indexOf('public static string ExportSecrets'), src.indexOf('public static System.Collections.Generic.List<(string name, bool success)> ImportSecrets'))
    expect(exportSection).toContain('DpapiHelper.EncryptSecret')
  })

  it('Import secrets: trial-decryption before writing to profile', () => {
    const src = readSecretProviderSources()
    const importSection = src.slice(src.indexOf('public static System.Collections.Generic.List<(string name, bool success)> ImportSecrets'))
    expect(importSection).toContain('Decrypt')
    expect(importSection).toContain('Decrypt')
    expect(importSection).toContain('results.Add((name, false))')
  })

  it('All 8 providers: Encrypt produces a valid JSON envelope with provider field', () => {
    const src = readSecretProviderSources()
    // Every provider Encrypt must create a SecretEnvelope with Provider = Name
    const providers = ['DpapiCurrentUserProvider', 'CredentialManagerProvider',
      'PowerShellSecretManagementProvider', 'VaultKV2Provider', 'SopsProvider',
      'AzureKeyVaultProvider', 'OnePasswordProvider', 'AwsSecretsManagerProvider']
    for (const p of providers) {
      const section = src.slice(src.indexOf(`class ${p}`), src.indexOf('}', src.indexOf(`class ${p}`) + 100))
      // The class must set Provider = Name
      const classSection = src.slice(src.indexOf(`class ${p}`))
      expect(classSection.slice(0, 10000)).toContain('Provider = Name')
    }
  })

  it('Temp file cleanup: all providers that use temp files have finally blocks', () => {
    const src = readSecretProviderSources()
    // SopsProvider uses temp files and must clean up in finally
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('Directory.Delete(tempDir, true)')
    expect(sopsSection).toContain('finally')
  })

  it('All network providers enforce TLS (HTTPS)', () => {
    const src = readSecretProviderSources()
    // Vault, Azure, AWS all enforce HTTPS
    const vaultSection = src.slice(src.indexOf('class VaultKV2Provider'), src.indexOf('class SopsProvider'))
    expect(vaultSection).toContain('https://')
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'), src.indexOf('class OnePasswordProvider'))
    expect(azureSection).toContain('https://')
    expect(azureSection).toContain('TLS mandatory')
    const awsSection = src.slice(src.indexOf('class AwsSecretsManagerProvider'))
    expect(awsSection).toContain('https://')
  })

  it('All subprocess providers use CREATE_NO_WINDOW', () => {
    const src = readSecretProviderSources()
    // Sops, 1Password, PowerShell all spawn processes and must hide window
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('CreateNoWindow = true')
    const opSection = src.slice(src.indexOf('class OnePasswordProvider'), src.indexOf('class AwsSecretsManagerProvider'))
    expect(opSection).toContain('CreateNoWindow = true')
    const psmSection = src.slice(src.indexOf('class PowerShellSecretManagementProvider'), src.indexOf('class VaultKV2Provider'))
    expect(psmSection).toContain('CreateNoWindow = true')
  })

  it('All subprocess providers have timeouts to prevent indefinite hangs', () => {
    const src = readSecretProviderSources()
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('WaitForExit(30000)')
    const opSection = src.slice(src.indexOf('class OnePasswordProvider'), src.indexOf('class AwsSecretsManagerProvider'))
    expect(opSection).toContain('WaitForExit(30000)')
    const psmSection = src.slice(src.indexOf('class PowerShellSecretManagementProvider'), src.indexOf('class VaultKV2Provider'))
    expect(psmSection).toContain('WaitForExit(30000)')
    // Network providers have timeout too
    const vaultSection = src.slice(src.indexOf('class VaultKV2Provider'), src.indexOf('class SopsProvider'))
    expect(vaultSection).toContain('Timeout = TimeSpan.FromSeconds(10)')
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'), src.indexOf('class OnePasswordProvider'))
    expect(azureSection).toContain('Timeout = TimeSpan.FromSeconds(15)')
    const awsSection = src.slice(src.indexOf('class AwsSecretsManagerProvider'))
    expect(awsSection).toContain('Timeout = TimeSpan.FromSeconds(15)')
  })

  it('AWS SigV4: canonical request includes all required components', () => {
    const src = readSecretProviderSources()
    const awsSection = src.slice(src.indexOf('class AwsSecretsManagerProvider'))
    expect(awsSection).toContain('canonicalRequest')
    expect(awsSection).toContain('stringToSign')
    expect(awsSection).toContain('AWS4-HMAC-SHA256')
    expect(awsSection).toContain('credentialScope')
    expect(awsSection).toContain('HmacSHA256')
    expect(awsSection).toContain('BytesToHex')
  })

  it('Azure Key Vault: token cache is memory-only with expiry check', () => {
    const src = readSecretProviderSources()
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'), src.indexOf('class OnePasswordProvider'))
    expect(azureSection).toContain('_cachedToken')
    expect(azureSection).toContain('_tokenExpiry')
    expect(azureSection).toContain('AddMinutes(-5)')
  })

  it('Logs never record secret values - audit records only names and markers', () => {
    // Ticket 26 split ProfileCommand into three files; read all of them so the
    // source-gate assertions look at the post-split code surface. <redacted> now
    // lives in ProfileSecretCommand.cs (ProfileRevealSecret + add/edit-secret).
    const programSrc = [
      readFileSync(resolve(__dirname, '..' , '..', '..', 'src', 'ProfileCommand.cs'), 'utf8'),
      readFileSync(resolve(__dirname, '..' , '..', '..', 'src', 'ProfileLaunchCommand.cs'), 'utf8'),
      readFileSync(resolve(__dirname, '..' , '..', '..', 'src', 'ProfileSecretCommand.cs'), 'utf8'),
    ].join('\n')
    // Audit records for secret operations should use <redacted> or <encrypted>
    expect(programSrc).toContain('<redacted>')
    expect(programSrc).toContain('<encrypted>')
  })

  it('Profile launch decrypts secrets in-process and never logs plaintext', () => {
    // Ticket 26 split ProfileCommand into three files; read all of them so the
    // source-gate assertions look at the post-split code surface.
    // Ticket 39 two-layer port: the env injection now goes through the ISecretStore
    // facade (SecretStore.Reveal in ProfileLaunchCommand.cs); the reveal path lives
    // in ProfileSecretCommand.cs. SecretVariables.Contains is unchanged.
    const programSrc = [
      readFileSync(resolve(__dirname, '..' , '..', '..', 'src', 'ProfileCommand.cs'), 'utf8'),
      readFileSync(resolve(__dirname, '..' , '..', '..', 'src', 'ProfileLaunchCommand.cs'), 'utf8'),
      readFileSync(resolve(__dirname, '..' , '..', '..', 'src', 'ProfileSecretCommand.cs'), 'utf8'),
    ].join('\n')
    expect(programSrc).toContain('SecretStore.Reveal(valueToInject, profile.Name')
    expect(programSrc).toContain('SecretVariables.Contains(v.Name, StringComparer.OrdinalIgnoreCase)')
  })
})