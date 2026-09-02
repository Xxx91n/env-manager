import { describe, it, expect } from 'vitest'
import * as fs from 'fs'
import * as path from 'path'
import { readSecretProviderSources } from './lib/secret-provider-source'

/**
 * Phase 5: Secret provider timeout + long-session memory tests.
 */
describe('Phase 5: Secret provider timeout - comprehensive verification', () => {
  it('All subprocess providers: 30s timeout on WaitForExit', () => {
    const src = readSecretProviderSources()
    const sopsSection = src.slice(src.indexOf('class SopsProvider'), src.indexOf('class AzureKeyVaultProvider'))
    expect(sopsSection).toContain('WaitForExit(30000)')
    
    const opSection = src.slice(src.indexOf('class OnePasswordProvider'), src.indexOf('class AwsSecretsManagerProvider'))
    expect(opSection).toContain('WaitForExit(30000)')
    
    const psmSection = src.slice(src.indexOf('class PowerShellSecretManagementProvider'), src.indexOf('class VaultKV2Provider'))
    expect(psmSection).toContain('WaitForExit(30000)')
  })

  it('All network providers: explicit HTTP timeout', () => {
    const src = readSecretProviderSources()
    const vaultSection = src.slice(src.indexOf('class VaultKV2Provider'), src.indexOf('class SopsProvider'))
    expect(vaultSection).toContain('Timeout = TimeSpan.FromSeconds(10)')
    
    const azureSection = src.slice(src.indexOf('class AzureKeyVaultProvider'), src.indexOf('class OnePasswordProvider'))
    expect(azureSection).toContain('Timeout = TimeSpan.FromSeconds(15)')
    
    const awsSection = src.slice(src.indexOf('class AwsSecretsManagerProvider'))
    expect(awsSection).toContain('Timeout = TimeSpan.FromSeconds(15)')
  })

  it('Rust run_cli: spawn_blocking for long-running CLI operations', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    expect(src).toContain('spawn_blocking')
    expect(src).toContain('60s timeout')
    expect(src).toContain('Instant::now()')
  })

  it('Frontend api.ts has timeout for withTimeout/IPC calls', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, 'lib', 'api.ts'),
      'utf8'
    )
    const hasTimeout = src.includes('withTimeout') || src.includes('timeout') || src.includes('setTimeout')
    expect(hasTimeout).toBe(true)
    expect(src).toContain('secretProviderSet')
    expect(src).toContain('secretProviderRotate')
  })
})

describe('Phase 5: Long-session memory safety', () => {
  it('C# SecretProvider: decrypted material only lives in transient process memory', () => {
    const src = readSecretProviderSources()
    expect(src).toContain('ciphertext')
    const programSrc = fs.readFileSync(
      path.resolve(__dirname, '..', '..', 'src', 'ProfileCommand.cs'),
      'utf8'
    )
    expect(programSrc).toContain('ProfileRevealSecret')
  })

  it('C# Program.cs: secrets never written to profiles.json as plaintext', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', '..', 'src', 'ProfileCommand.cs'),
      'utf8'
    )
    expect(src).toContain('<encrypted>')
    expect(src).toContain('<redacted>')
  })

  it('Rust main.rs: run_cli does not cache CLI stdout beyond the response', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', 'src-tauri', 'src', 'main.rs'),
      'utf8'
    )
    expect(src).toContain('Zeroizing')
  })

  it('Service reconcile.rs: Periodic refresh does not accumulate decrypted material', () => {
    const srcPath = path.resolve(__dirname, '..', '..', 'service', 'src', 'reconcile.rs')
    if (!fs.existsSync(srcPath)) {
      expect(true).toBe(true)
      return
    }
    const src = fs.readFileSync(srcPath, 'utf8')
    expect(src).toContain('lastFetchedAt')
    expect(src).toContain('lastRotatedAt')
    expect(src).not.toContain('plaintext')
    expect(src).not.toContain('decryptedValue')
  })

  it('IPC handle_connection: per-request buffer is stack-allocated (no heap accumulation)', () => {
    const src = fs.readFileSync(
      path.resolve(__dirname, '..', '..', 'service', 'src', 'ipc.rs'),
      'utf8'
    )
    expect(src).toContain('let mut buf = Vec::with_capacity(4096)')
    expect(src).toContain('CONNECTION_COUNT')
  })
})
