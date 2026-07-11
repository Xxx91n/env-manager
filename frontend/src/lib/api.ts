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

export interface ProfileVariable {
  name: string
  value: string
}

export interface ProfileData {
  id: string
  name: string
  isEnabled: boolean
  variables: ProfileVariable[]
}

export interface PathEntry {
  index: number
  path: string
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

// --- Profile API ---

export async function listProfiles(): Promise<ProfileData[]> {
  try {
    const output = await runCommand('profile', ['list'])
    return JSON.parse(output) as ProfileData[]
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to list profiles')
    return []
  }
}

export async function createProfile(name: string): Promise<string> {
  try {
    return await runCommand('profile', ['create', name])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to create profile')
    throw err
  }
}

export async function deleteProfile(name: string): Promise<string> {
  try {
    return await runCommand('profile', ['delete', name])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to delete profile')
    throw err
  }
}

export async function applyProfile(name: string): Promise<string> {
  try {
    const result = await runCommand('profile', ['apply', name])
    await listVariables()
    return result
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to apply profile')
    throw err
  }
}

export async function unapplyProfile(name: string): Promise<string> {
  try {
    const result = await runCommand('profile', ['unapply', name])
    await listVariables()
    return result
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to unapply profile')
    throw err
  }
}

export async function showProfile(name: string): Promise<ProfileData | null> {
  try {
    const output = await runCommand('profile', ['show', name])
    return JSON.parse(output) as ProfileData
  } catch {
    return null
  }
}

export async function addProfileVar(
  profileName: string,
  varName: string,
  varValue: string
): Promise<string> {
  try {
    return await runCommand('profile', ['add-var', profileName, varName, varValue])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to add variable to profile')
    throw err
  }
}

export async function removeProfileVar(
  profileName: string,
  varName: string
): Promise<string> {
  try {
    return await runCommand('profile', ['remove-var', profileName, varName])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to remove variable from profile')
    throw err
  }
}

// --- Path API ---

export async function listPathEntries(scope: 'user' | 'system' = 'user'): Promise<PathEntry[]> {
  try {
    const output = await runCommand('path', ['list', '--scope', scope])
    return JSON.parse(output) as PathEntry[]
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to list PATH entries')
    return []
  }
}

export async function addPathEntry(
  dir: string,
  scope: 'user' | 'system' = 'user',
  index?: number
): Promise<string> {
  try {
    const args = ['add', dir, '--scope', scope]
    if (index !== undefined) {
      args.push('--index', String(index))
    }
    return await runCommand('path', args)
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to add PATH entry')
    throw err
  }
}

export async function removePathEntry(
  dir: string,
  scope: 'user' | 'system' = 'user'
): Promise<string> {
  try {
    return await runCommand('path', ['remove', dir, '--scope', scope])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to remove PATH entry')
    throw err
  }
}

export async function movePathEntryUp(
  index: number,
  scope: 'user' | 'system' = 'user'
): Promise<string> {
  try {
    return await runCommand('path', ['move-up', String(index), '--scope', scope])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to move PATH entry')
    throw err
  }
}

export async function movePathEntryDown(
  index: number,
  scope: 'user' | 'system' = 'user'
): Promise<string> {
  try {
    return await runCommand('path', ['move-down', String(index), '--scope', scope])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to move PATH entry')
    throw err
  }
}
