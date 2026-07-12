import { invoke } from '@tauri-apps/api/core'
import { variables, loading, error } from './stores'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
  isDisabled?: boolean
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

/**
 * Strips the `\\?\` (Windows verbatim/long-path) prefix from a path string.
 * This prefix can appear when Rust resolves paths via Tauri's resource
 * directory or PathBuf on Windows. It must be removed before writing
 * to the registry so PATH entries use clean drive-letter paths.
 */
function stripVerbatimPrefix(path: string): string {
  if (path.startsWith('\\\\?\\')) {
    return path.slice(4)
  }
  if (path.startsWith('\\\\?\\UNC\\')) {
    return '\\' + path.slice(7)
  }
  return path
}

// Write serialization: ensures only one write operation is in-flight at a time
// on the frontend side. This works with the Rust RwLock to prevent UI-level
// races (e.g., double-click triggering two set operations before the first
// completes). Read operations are not serialized.
let writeChain: Promise<void> = Promise.resolve()

/**
 * Executes a write operation in a serialized chain.
 * Each write operation waits for the previous one to complete before starting.
 * Read operations are NOT serialized (they use the Rust read lock).
 */
async function runWriteOperation<T>(fn: () => Promise<T>): Promise<T> {
  const prevChain = writeChain
  let resolveWrite: () => void
  writeChain = new Promise<void>((resolve) => { resolveWrite = resolve! })

  try {
    // Wait for the previous write to complete
    await prevChain
    // Execute the write operation
    return await fn()
  } finally {
    resolveWrite!()
  }
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

/**
 * Read commands can run concurrently. They acquire the Rust read lock.
 */
async function runRead(cmd: string, args: string[] = []): Promise<string> {
  return runCommand(cmd, args)
}

/**
 * Write commands are serialized on the frontend side AND acquire the Rust
 * write lock. This prevents UI-level races and backend-level races.
 */
async function runWrite(cmd: string, args: string[] = []): Promise<string> {
  return runWriteOperation(() => runCommand(cmd, args))
}

export async function getDiagnostics(): Promise<Diagnostics> {
  try {
    const diag = await invoke<Diagnostics>('cli_diagnostics')
    // Defensive: strip any verbatim prefix that might slip through
    return {
      ...diag,
      resolved_cli_path: stripVerbatimPrefix(diag.resolved_cli_path),
      gui_exe_dir: stripVerbatimPrefix(diag.gui_exe_dir),
    }
  } catch {
    return {
      resolved_cli_path: 'UNAVAILABLE',
      gui_exe_dir: 'UNAVAILABLE',
      cwd: 'UNAVAILABLE',
    }
  }
}

/**
 * Updates the system tray menu text and tooltip to match the current GUI locale.
 */
export async function updateTrayLocale(
  showText: string,
  quitText: string,
  tooltip: string
): Promise<void> {
  try {
    await invoke('update_tray_locale', {
      showText,
      quitText,
      tooltip,
    })
  } catch {
    // Non-critical: tray stays in English if update fails
  }
}

export async function listVariables(): Promise<void> {
  loading.set(true)
  error.set(null)

  try {
    const output = await runRead('list')
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
    const output = await runRead('get', [name])
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
    await runWrite('set', [name, value, '--scope', scope])
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
    await runWrite('delete', [name, '--scope', scope])
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to delete variable')
    throw err
  }
}

export async function toggleVariable(
  name: string,
  scope: 'user' | 'system' = 'user'
): Promise<{ isDisabled: boolean }> {
  error.set(null)

  try {
    const output = await runWrite('toggle', [name, '--scope', scope])
    await listVariables()
    return JSON.parse(output)
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to toggle variable')
    throw err
  }
}


export async function createBackup(outputFile?: string): Promise<string> {
  error.set(null)

  try {
    const args = outputFile ? ['--output', outputFile] : []
    const output = await runRead('backup', args)
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
    await runWrite('restore', args)
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Restore failed')
    throw err
  }
}

// --- Profile API ---

export async function listProfiles(): Promise<ProfileData[]> {
  try {
    const output = await runRead('profile', ['list'])
    return JSON.parse(output) as ProfileData[]
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to list profiles')
    return []
  }
}

export async function createProfile(name: string): Promise<string> {
  try {
    return await runWrite('profile', ['create', name])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to create profile')
    throw err
  }
}

export async function deleteProfile(name: string): Promise<string> {
  try {
    return await runWrite('profile', ['delete', name])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to delete profile')
    throw err
  }
}

export async function applyProfile(name: string): Promise<string> {
  try {
    const result = await runWrite('profile', ['apply', name])
    await listVariables()
    return result
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to apply profile')
    throw err
  }
}

export async function unapplyProfile(name: string): Promise<string> {
  try {
    const result = await runWrite('profile', ['unapply', name])
    await listVariables()
    return result
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to unapply profile')
    throw err
  }
}

export async function showProfile(name: string): Promise<ProfileData | null> {
  try {
    const output = await runRead('profile', ['show', name])
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
    return await runWrite('profile', ['add-var', profileName, varName, varValue])
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
    return await runWrite('profile', ['remove-var', profileName, varName])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to remove variable from profile')
    throw err
  }
}

export async function editProfileVar(
  profileName: string,
  oldVarName: string,
  newVarName: string,
  newVarValue: string
): Promise<string> {
  try {
    const result = await runWrite('profile', ['edit-var', profileName, oldVarName, newVarName, newVarValue])
    await listVariables()
    return result
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to edit profile variable')
    throw err
  }
}

export interface ProfileStatus {
  name: string
  isEnabled: boolean
  isCorrectlyApplied: boolean
  isApplicable: boolean
  variableCount: number
}

export async function getProfileStatus(name: string): Promise<ProfileStatus | null> {
  try {
    const output = await runRead('profile', ['status', name])
    return JSON.parse(output) as ProfileStatus
  } catch {
    return null
  }
}

// --- Path API ---

export async function listPathEntries(scope: 'user' | 'system' = 'user'): Promise<PathEntry[]> {
  try {
    const output = await runRead('path', ['list', '--scope', scope])
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
    return await runWrite('path', args)
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
    return await runWrite('path', ['remove', dir, '--scope', scope])
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
    return await runWrite('path', ['move-up', String(index), '--scope', scope])
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
    return await runWrite('path', ['move-down', String(index), '--scope', scope])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to move PATH entry')
    throw err
  }
}

/**
 * Renames a PATH entry: replaces the old directory string with a new one
 * at the same position in the PATH list.
 */
export async function renamePathEntry(
  oldDir: string,
  newDir: string,
  scope: 'user' | 'system' = 'user'
): Promise<string> {
  try {
    return await runWrite('path', ['rename', oldDir, newDir, '--scope', scope])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to rename PATH entry')
    throw err
  }
}

// --- CLI PATH management ---

/**
 * Detects whether the CLI executable directory is in the user PATH.
 * Always checks real system PATH data, never trusts cached GUI state.
 */
export async function isCliInPath(): Promise<boolean> {
  try {
    const diag = await getDiagnostics()
    const cliPath = diag.resolved_cli_path

    if (!cliPath || cliPath === 'NOT FOUND' || cliPath === 'UNAVAILABLE') {
      return false
    }

    const lastSep = Math.max(cliPath.lastIndexOf('\\'), cliPath.lastIndexOf('/'))
    const cliDir = cliPath.substring(0, lastSep)

    if (!cliDir) return false

    const entries = await listPathEntries('user')
    return entries.some(
      (e) => e.path.toLowerCase() === cliDir.toLowerCase()
    )
  } catch {
    return false
  }
}

/**
 * Adds the CLI executable directory to the user PATH.
 * Detects CLI location via diagnostics, checks real PATH before adding.
 */
export async function addCliToPath(): Promise<{ added: boolean; message: string }> {
  try {
    const diag = await getDiagnostics()
    const cliPath = diag.resolved_cli_path

    if (!cliPath || cliPath === 'NOT FOUND' || cliPath === 'UNAVAILABLE') {
      return { added: false, message: 'CLI path not found' }
    }

    const lastSep = Math.max(cliPath.lastIndexOf('\\'), cliPath.lastIndexOf('/'))
    const cliDir = cliPath.substring(0, lastSep)

    if (!cliDir) {
      return { added: false, message: 'Invalid CLI directory' }
    }

    // Check real system PATH
    const entries = await listPathEntries('user')
    const alreadyExists = entries.some(
      (e) => e.path.toLowerCase() === cliDir.toLowerCase()
    )

    if (alreadyExists) {
      return { added: false, message: 'CLI directory already in PATH' }
    }

    await addPathEntry(cliDir, 'user')
    return { added: true, message: cliDir }
  } catch (err) {
    return {
      added: false,
      message: err instanceof Error ? err.message : 'Failed to add CLI to PATH',
    }
  }
}

/**
 * Removes the CLI executable directory from the user PATH.
 * Detects CLI location via diagnostics, checks real PATH before removing.
 */
export async function removeCliFromPath(): Promise<{ removed: boolean; message: string }> {
  try {
    const diag = await getDiagnostics()
    const cliPath = diag.resolved_cli_path

    if (!cliPath || cliPath === 'NOT FOUND' || cliPath === 'UNAVAILABLE') {
      return { removed: false, message: 'CLI path not found' }
    }

    const lastSep = Math.max(cliPath.lastIndexOf('\\'), cliPath.lastIndexOf('/'))
    const cliDir = cliPath.substring(0, lastSep)

    if (!cliDir) {
      return { removed: false, message: 'Invalid CLI directory' }
    }

    // Check real system PATH
    const entries = await listPathEntries('user')
    const exists = entries.some(
      (e) => e.path.toLowerCase() === cliDir.toLowerCase()
    )

    if (!exists) {
      return { removed: false, message: 'CLI directory not in PATH' }
    }

    await removePathEntry(cliDir, 'user')
    return { removed: true, message: cliDir }
  } catch (err) {
    return {
      removed: false,
      message: err instanceof Error ? err.message : 'Failed to remove CLI from PATH',
    }
  }
}

/**
 * Retrieves the CLI AGENTS.md file content.
 */
export async function getCliAgentsSpec(): Promise<string> {
  try {
    return await runRead('agents', [])
  } catch {
    return 'CLI agents spec not available'
  }
}

/**
 * Gets the file path where AGENTS.cli.md is located.
 */
export async function getCliAgentsPath(): Promise<string> {
  try {
    return await runRead('agents', ['--path'])
  } catch {
    return ''
  }
}
