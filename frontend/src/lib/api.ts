import { invoke } from '@tauri-apps/api/core'
import { variables, loading, error } from './stores'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
}

export interface CLIResponse {
  success: boolean
  data?: string
  error?: string
}

export interface Diagnostics {
  resolved_cli_path: string
  gui_exe_dir: string
  cwd: string
}

async function runCommand(cmd: string, args: string[] = []): Promise<string> {
  try {
    const result = await invoke<CLIResponse>('run_cli', {
      command: cmd,
      args: args,
    })

    if (!result.success) {
      throw new Error(result.error || 'Unknown CLI error')
    }

    return result.data || ''
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err)
    throw new Error(msg)
  }
}

export async function getDiagnostics(): Promise<Diagnostics> {
  try {
    return await invoke<Diagnostics>('cli_diagnostics')
  } catch {
    return {
      resolved_cli_path: 'UNAVAILABLE',
      gui_exe_dir: 'UNAVAILABLE',
      cwd: 'UNAVAILABLE',
    }
  }
}

export async function listVariables(): Promise<void> {
  loading.set(true)
  error.set(null)

  try {
    const output = await runCommand('list')
    const parsed: EnvVariable[] = JSON.parse(output)
    variables.set(parsed)
  } catch (err) {
    const msg = err instanceof Error ? err.message : 'Failed to list variables'
    error.set(msg)
  } finally {
    loading.set(false)
  }
}

export async function getVariable(name: string): Promise<EnvVariable | null> {
  try {
    const output = await runCommand('get', [name])
    const parsed = JSON.parse(output) as { name: string; value: string; scope: 'user' | 'system' }
    return parsed
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to get variable')
    return null
  }
}

export async function setVariable(
  name: string,
  value: string,
  scope: 'user' | 'system' = 'user'
): Promise<void> {
  error.set(null)

  try {
    await runCommand('set', [name, value, '--scope', scope])
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to set variable')
    throw err
  }
}

export async function deleteVariable(
  name: string,
  scope: 'user' | 'system' = 'user'
): Promise<void> {
  error.set(null)

  try {
    await runCommand('delete', [name, '--scope', scope])
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to delete variable')
    throw err
  }
}

export async function createBackup(outputFile?: string): Promise<string> {
  error.set(null)

  try {
    const args = outputFile ? ['--output', outputFile] : []
    const output = await runCommand('backup', args)
    return output
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Backup failed')
    throw err
  }
}

export async function restoreBackup(
  inputFile: string,
  scope?: 'user' | 'system'
): Promise<void> {
  error.set(null)

  try {
    const args = scope ? [inputFile, '--scope', scope] : [inputFile]
    await runCommand('restore', args)
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Restore failed')
    throw err
  }
}
