import { invoke } from '@tauri-apps/api/core'
import { variables, loading, error } from './stores'

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
  console.log(`[api] run_cli: ${cmd}`, args)
  try {
    const result = await invoke<CLIResponse>('run_cli', {
      command: cmd,
      args: args,
    })

    if (!result.success) {
      const errMsg = result.error || 'Unknown CLI error'
      console.error(`[api] CLI error for '${cmd}':`, errMsg)
      throw new Error(errMsg)
    }

    return result.data || ''
  } catch (err) {
    const msg = err instanceof Error ? err.message : String(err)
    console.error(`[api] invoke failed for '${cmd}':`, msg)
    throw new Error(msg)
  }
}

export async function getDiagnostics(): Promise<Diagnostics> {
  try {
    return await invoke<Diagnostics>('cli_diagnostics')
  } catch (err) {
    console.error('[api] cli_diagnostics failed:', err)
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
    const parsed = parseTableOutput(output)
    variables.set(parsed)
  } catch (err) {
    const msg = err instanceof Error ? err.message : 'Failed to list variables'
    error.set(msg)
  } finally {
    loading.set(false)
  }
}

export async function getVariable(name: string): Promise<string | null> {
  try {
    const output = await runCommand('get', [name])
    const match = output.match(/= (.*)/)
    return match ? match[1] : null
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

/**
 * Parse Spectre.Console table output into structured variable objects.
 * The table uses Unicode box-drawing characters as cell separators.
 */
function parseTableOutput(output: string): Array<{ name: string; scope: string; value: string }> {
  const lines = output.split('\n').slice(1)
  const vars: Array<{ name: string; scope: string; value: string }> = []

  for (const line of lines) {
    if (!line.trim()) continue
    // Spectre.Console uses U+2502 as column separator
    const parts = line.split('\u2502').map((s) => s.trim()).filter(Boolean)
    if (parts.length >= 3) {
      vars.push({
        name: parts[0],
        scope: parts[1] as 'user' | 'system',
        value: parts[2],
      })
    }
  }

  return vars
}
