import { invoke } from '@tauri-apps/api/core'
import { open as openDialog, save as saveDialog } from '@tauri-apps/plugin-dialog'
import { variables, loading, error, isWriteInProgress, addDebugLog } from './stores'

export interface EnvVariable {
  name: string
  value: string
  scope: 'user' | 'system'
  isDisabled?: boolean
  profileSource?: string
  isProtected?: boolean
  isBuiltinProtected?: boolean
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
  appliedAt?: number | null
  inherits: string[]
  pathEntries: string[]
  variables: ProfileVariable[]
  profileType?: 'global' | 'launch'
  targetExecutable?: string
  launchArguments?: string
  workingDirectory?: string
  secretVariables?: string[]
}

export interface PathEntry {
  index: number
  path: string
  expandedPath: string
  isDuplicate: boolean
  exists: boolean
  isProtected: boolean
  isBuiltinProtected: boolean
}

export interface AuditEntry {
  id: string
  timestamp: string
  command: string
  name: string
  scope: 'user' | 'system'
  oldValue: string | null
  newValue: string | null
}

export interface ProfilePreview {
  profile: string
  inherits: string[]
  variables: Array<{ name: string; value: string; currentValue: string | null; conflict: boolean }>
  pathEntries: Array<{ path: string; expandedPath: string; exists: boolean }>
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
let pendingWriteCount = 0

/**
 * Executes a write operation in a serialized chain. The busy state is reference-counted
 * so the UI remains locked until every queued mutation completes.
 */
async function runWriteOperation<T>(fn: () => Promise<T>): Promise<T> {
  pendingWriteCount += 1
  isWriteInProgress.set(true)
  const prevChain = writeChain
  let resolveWrite!: () => void
  writeChain = new Promise<void>((resolve) => { resolveWrite = resolve })

  try {
    await prevChain
    const result = await fn()
    invalidateApiCache()
    return result
  } finally {
    resolveWrite()
    pendingWriteCount -= 1
    isWriteInProgress.set(pendingWriteCount > 0)
  }
}

async function runCommand(cmd: string, args: string[] = []): Promise<string> {
  const startTime = Date.now()
  addDebugLog({ level: 'debug', message: `CLI: ${cmd} (${args.length} args)`, command: cmd })
  try {
    const result = await invoke<CLIResponse>('run_cli', {
      command: cmd,
      args: args,
    })

    const elapsed = Date.now() - startTime
    if (!result.success) {
      addDebugLog({ level: 'error', message: `CLI error (${elapsed}ms); details withheld from debug log`, command: cmd })
      throw new Error(result.error || 'Unknown CLI error')
    }

    addDebugLog({ level: 'debug', message: `CLI ok (${elapsed}ms): ${cmd}`, command: cmd })
    return result.data || ''
  } catch (err) {
    const elapsed = Date.now() - startTime
    const msg = err instanceof Error ? err.message : String(err)
    addDebugLog({ level: 'error', message: `CLI exception (${elapsed}ms); details withheld from debug log`, command: cmd })
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

export interface UpdateInfo {
  latestVersion: string
  releaseUrl: string
  isUpdateAvailable: boolean
  error?: string
}

/**
 * Checks for available updates by querying the GitHub Releases API via the Rust backend.
 * Returns the latest version, release URL, and whether an update is available.
 */
export async function checkForUpdates(currentVersion: string): Promise<UpdateInfo> {
  try {
    return await invoke<UpdateInfo>('check_for_updates', { currentVersion })
  } catch {
    return {
      latestVersion: '',
      releaseUrl: '',
      isUpdateAvailable: false,
      error: 'Failed to check for updates',
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

// Cache for the most recently fetched full variable list.
// Used by secondary surfaces (e.g. ProtectionPage) to avoid duplicate CLI calls.
// TTL-based: cached data expires after 5 seconds so secondary pages see fresh
// data without hammering the CLI on every page switch.
const VARIABLES_CACHE_TTL_MS = 5000
const MAX_CACHE_ENTRIES = 4
let lastVariablesRaw: EnvVariable[] = []
let lastVariablesCacheTime = 0
let variablesReadInFlight: { generation: number; promise: Promise<EnvVariable[]> } | null = null
let variablesRequestEpoch = 0
let dataGeneration = 0

// PATH entries cache uses bounded single-flight reads. A generation prevents an
// old in-flight read from repopulating cache after a successful mutation.
const PATH_CACHE_TTL_MS = 5000
const pathEntriesCache: Map<string, { data: PathEntry[]; time: number; generation: number }> = new Map()
const pathReadsInFlight = new Map<string, { generation: number; promise: Promise<PathEntry[]> }>()

/**
 * Invalidate all cached data. Called after any write operation to ensure
 * secondary pages see fresh data on their next read.
 */
export function invalidateApiCache(): void {
  lastVariablesCacheTime = 0
  dataGeneration += 1
  variablesRequestEpoch += 1
  pathEntriesCache.clear()
}

/**
 * Returns the raw variable list without touching the global `variables` store,
 * so secondary pages can read the data independently.
 * If `force` is false and cached data is still fresh (within TTL), returns the cache.
 * If `force` is true, always refetch.
 */
export async function listVariablesRaw(force = false): Promise<EnvVariable[]> {
  const now = Date.now()
  const generation = dataGeneration
  const cacheFresh = !force
    && lastVariablesRaw.length > 0
    && (now - lastVariablesCacheTime) < VARIABLES_CACHE_TTL_MS
  if (cacheFresh) return lastVariablesRaw
  if (variablesReadInFlight?.generation === generation) return variablesReadInFlight.promise

  const requestEpoch = ++variablesRequestEpoch
  const promise = runRead('list')
    .then((output) => JSON.parse(output) as EnvVariable[])
    .then((parsed) => {
      if (generation === dataGeneration && requestEpoch === variablesRequestEpoch) {
        lastVariablesRaw = parsed
        lastVariablesCacheTime = Date.now()
      }
      return parsed
    })
    .finally(() => {
      if (variablesReadInFlight?.promise === promise) variablesReadInFlight = null
    })

  variablesReadInFlight = { generation, promise }
  return promise
}

export async function listVariables(): Promise<void> {
  loading.set(true)
  error.set(null)
  const requestEpoch = ++variablesRequestEpoch

  try {
    const output = await runRead('list')
    const parsed: EnvVariable[] = JSON.parse(output)
    if (requestEpoch !== variablesRequestEpoch) return
    variables.set(parsed)
    lastVariablesRaw = parsed
    lastVariablesCacheTime = Date.now()
  } catch (err) {
    if (requestEpoch === variablesRequestEpoch) {
      const msg = err instanceof Error ? err.message : 'Failed to list variables'
      error.set(msg)
    }
  } finally {
    if (requestEpoch === variablesRequestEpoch) loading.set(false)
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
  scope: 'user' | 'system' = 'user',
  overwrite = false
): Promise<void> {
  error.set(null)

  try {
    const args = [name, value, '--scope', scope]
    if (overwrite) args.push('--overwrite')
    await runWrite('set', args)
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to set variable')
    throw err
  }
}

export async function renameVariable(
  oldName: string,
  newName: string,
  scope: 'user' | 'system' = 'user',
  overwrite = false
): Promise<void> {
  const args = [oldName, newName, '--scope', scope]
  if (overwrite) args.push('--overwrite')
  await runWrite('rename', args)
  await listVariables()
}
/**
 * Atomically changes a variable scope from one hive to another. The CLI
 * refuses to move protected variables and refuses cross-scope collisions
 * unless overwrite is explicit. Use this when the user edits an existing
 * variable and changes its scope in the EditDialog.
 */
export async function changeScope(
  name: string,
  newScope: 'user' | 'system',
  oldScope?: 'user' | 'system',
  overwrite = false
): Promise<void> {
  error.set(null)
  try {
    const args = [name, newScope]
    if (oldScope) {
      args.push('--scope', oldScope)
    }
    if (overwrite) args.push('--overwrite')
    await runWrite('change-scope', args)
    await invalidateApiCache()
    await listVariables()
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to change variable scope')
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

export interface ProfileCreateOptions {
  type?: 'global' | 'launch'
  target?: string
  args?: string
  cwd?: string
}

export async function createProfile(name: string, options: ProfileCreateOptions = {}): Promise<string> {
  try {
    const args = ['create', name]
    if (options.type) args.push('--type', options.type)
    if (options.target !== undefined) args.push('--target', options.target)
    if (options.args !== undefined) args.push('--args', options.args)
    if (options.cwd !== undefined) args.push('--cwd', options.cwd)
    return await runWrite('profile', args)
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

export async function exportProfile(
  profileName: string,
  outputFile: string
): Promise<string> {
  try {
    return await runRead('profile', ['export', profileName, '--output', outputFile])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to export profile')
    throw err
  }
}

export async function importProfile(inputFile: string): Promise<string> {
  try {
    return await runWrite('profile', ['import', inputFile])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to import profile')
    throw err
  }
}

export async function renameProfile(
  oldName: string,
  newName: string
): Promise<string> {
  try {
    return await runWrite('profile', ['rename', oldName, newName])
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to rename profile')
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

export async function listPathEntries(scope: 'user' | 'system' = 'user', force = false): Promise<PathEntry[]> {
  const now = Date.now()
  const generation = dataGeneration
  const cached = pathEntriesCache.get(scope)
  if (!force && cached && cached.generation === generation && (now - cached.time) < PATH_CACHE_TTL_MS) {
    return cached.data
  }
  const inFlight = pathReadsInFlight.get(scope)
  if (inFlight?.generation === generation) return inFlight.promise

  const promise = runRead('path', ['list', '--scope', scope])
    .then((output) => {
      const data = JSON.parse(output) as PathEntry[]
      if (generation === dataGeneration) {
        pathEntriesCache.set(scope, { data, time: Date.now(), generation })
        while (pathEntriesCache.size > MAX_CACHE_ENTRIES) {
          const oldest = pathEntriesCache.keys().next().value
          if (oldest === undefined) break
          pathEntriesCache.delete(oldest)
        }
      }
      return data
    })
    .catch((err) => {
      error.set(err instanceof Error ? err.message : 'Failed to list PATH entries')
      return []
    })
    .finally(() => {
      if (pathReadsInFlight.get(scope)?.promise === promise) pathReadsInFlight.delete(scope)
    })

  pathReadsInFlight.set(scope, { generation, promise })
  return promise
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

export interface PathDedupeResult {
  scope: string
  removedCount: number
  keptCount: number
  removed: string[]
  kept: string[]
  dryRun?: boolean
}

/**
 * Removes duplicate PATH entries (case-insensitive) while preserving the
 * first occurrence. Protected PATH entries are never removed even when
 * duplicated -- mirrors the CLI IsProtectedPathEntry contract. Pass
 * dryRun=true to preview removed entries without modifying PATH.
 */
export async function dedupePathEntries(
  scope: 'user' | 'system' = 'user',
  dryRun = false
): Promise<PathDedupeResult> {
  const args = ['dedupe', '--scope', scope]
  if (dryRun) args.push('--dry-run')
  try {
    const output = dryRun
      ? await runRead('path', args)
      : await runWrite('path', args)
    return JSON.parse(output) as PathDedupeResult
  } catch (err) {
    error.set(err instanceof Error ? err.message : 'Failed to dedupe PATH entries')
    throw err
  }
}


export async function expandVariableValue(value: string): Promise<string> {
  const output = await runRead('expand', [value])
  return (JSON.parse(output) as { expanded: string }).expanded
}

export async function listHistory(limit = 200): Promise<AuditEntry[]> {
  const output = await runRead('history', ['list', '--limit', String(limit)])
  return JSON.parse(output) as AuditEntry[]
}

export async function undoHistory(id: string, force = false): Promise<void> {
  const args = ['undo', id]
  if (force) args.push('--force')
  await runWrite('history', args)
  await listVariables()
}

export async function deleteHistory(id: string): Promise<void> {
  await runWrite('history', ['delete', id])
}

export async function clearHistory(scope: 'user' | 'system' | 'all' = 'all'): Promise<void> {
  const args = ['delete', '--all']
  if (scope !== 'all') args.push('--scope', scope)
  await runWrite('history', args)
}

export async function bulkImport(file: string, scope: 'user' | 'system', overwrite = false, dryRun = false): Promise<Record<string, unknown>> {
  const args = ['import', file, '--scope', scope]
  if (overwrite) args.push('--overwrite')
  if (dryRun) args.push('--dry-run')
  const output = dryRun ? await runRead('bulk', args) : await runWrite('bulk', args)
  if (!dryRun) await listVariables()
  return JSON.parse(output) as Record<string, unknown>
}

export async function bulkExport(file: string, scope: 'user' | 'system'): Promise<void> {
  await runRead('bulk', ['export', file, '--scope', scope])
}

export async function previewProfile(name: string): Promise<ProfilePreview> {
  const output = await runRead('profile', ['preview', name])
  return JSON.parse(output) as ProfilePreview
}

export async function setProfileInheritance(name: string, parents: string[]): Promise<void> {
  await runWrite('profile', ['set-inherits', name, ...parents])
}

export async function addProfilePath(name: string, path: string): Promise<void> {
  await runWrite('profile', ['add-path', name, path])
}

export async function removeProfilePath(name: string, path: string): Promise<void> {
  await runWrite('profile', ['remove-path', name, path])
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

    const entries = await listPathEntries('user', true)
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
/**
 * Opens a native Windows file open dialog for selecting a JSON file.
 */
export async function pickOpenFile(title: string, defaultPath?: string): Promise<string | null> {
  const selected = await openDialog({
    title,
    defaultPath,
    filters: [{ name: 'JSON', extensions: ['json'] }],
    multiple: false,
  })
  if (selected === null) return null
  return typeof selected === 'string' ? selected : null
}

export async function pickExecutableFile(title: string, defaultPath?: string): Promise<string | null> {
  const selected = await openDialog({
    title,
    defaultPath,
    filters: [
      { name: 'Executable', extensions: ['exe', 'bat', 'cmd', 'ps1'] },
    ],
    multiple: false,
  })
  if (selected === null) return null
  return typeof selected === 'string' ? selected : null
}

/**
 * Opens a native Windows file save dialog for selecting an export destination.
 */
export async function pickSaveFile(title: string, defaultPath?: string): Promise<string | null> {
  const selected = await saveDialog({
    title,
    defaultPath,
    filters: [{ name: 'JSON', extensions: ['json'] }],
  })
  return selected === null ? null : selected
}

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

// --- Protection list API ---

export interface ProtectionData {
  protectedVars: {
    builtIn: string[]
    custom: string[]
  }
  protectedPaths: {
    builtIn: string[]
    custom: string[]
  }
}

export async function listProtection(): Promise<ProtectionData> {
  const output = await runRead('protection', ['list'])
  return JSON.parse(output) as ProtectionData
}

export async function addProtectedPath(entry: string): Promise<void> {
  await runWrite('protection', ['add-path', entry])
}

export async function removeProtectedPath(entry: string): Promise<void> {
  await runWrite('protection', ['remove-path', entry])
}

export async function addProtectedVar(name: string): Promise<void> {
  await runWrite('protection', ['add-var', name])
}

export async function removeProtectedVar(name: string): Promise<void> {
  await runWrite('protection', ['remove-var', name])
}

// --- v0.6.0 Launch profile + PATH health API ---

/**
 * Configures a Launch profile: sets the target executable / args / cwd, and optionally
 * converts the profile type between 'global' and 'launch'.
 * CLI: `profile set-launch <name> --target <exe> [--args <args>] [--cwd <dir>] [--type global|launch]`
 */
export interface ProfileLaunchConfig {
  target?: string
  args?: string
  cwd?: string
  type?: 'global' | 'launch'
}

export async function profileSetLaunch(profileName: string, config: ProfileLaunchConfig): Promise<string> {
  const args = ['set-launch', profileName]
  if (config.target !== undefined) { args.push('--target', config.target) }
  if (config.args !== undefined) { args.push('--args', config.args) }
  if (config.cwd !== undefined) { args.push('--cwd', config.cwd) }
  if (config.type !== undefined) { args.push('--type', config.type) }
  return await runWrite('profile', args)
}

/**
 * Spawns the launch profile's target executable with an isolated environment block
 * (env_clear + inject). The child process receives ONLY the profile's variables + PATH entries.
 * Never writes the registry or broadcasts WM_SETTINGCHANGE. Logs nothing about values.
 * CLI: `profile launch <name> [-- <extra-args ...>]`
 * `extraArgs` is optional and passed to the spawned process as additional command-line arguments.
 */
export async function profileLaunch(profileName: string, extraArgs: string[] = []): Promise<string> {
  const args = ['launch', profileName]
  if (extraArgs.length > 0) {
    args.push('--')
    args.push(...extraArgs)
  }
  return await runRead('profile', args)
}

export interface PathHealthEntry {
  entry: string
  status: 'healthy' | 'dead' | 'duplicate' | 'duplicate+dead'
  isProtected: boolean
  isDead: boolean
  isDuplicate: boolean
  fullPath: string
}

export interface PathHealthResult {
  scope: string
  dryRun: boolean
  totalEntries: number
  healthyCount: number
  duplicateCount: number
  deadCount: number
  wouldFix: boolean
  results: PathHealthEntry[]
}

/**
 * Detects PATH entries that are duplicates OR point to a directory that does not exist
 * (dead path). Protected entries are NEVER marked as duplicates (defense-in-depth: HashSet
 * isolation keeps protected entries out of the duplicate comparison set).
 *
 * By default (no flags), this is a pure read - returns the health report.
 * Pass `fix: true` to remove non-protected duplicates and dead entries in one write.
 * Pass `dryRun: true` to see what --fix would do without mutating the registry PATH.
 *
 * CLI: `path health [--scope user|system] [--fix] [--dry-run]`
 */
export async function pathHealth(scope: 'user' | 'system' = 'user', fix: boolean = false, dryRun: boolean = false): Promise<PathHealthResult> {
  const args = ['health', '--scope', scope]
  if (fix) args.push('--fix')
  if (dryRun) args.push('--dry-run')
  const fn = fix ? runWrite : runRead
  const output = await fn('path', args)
  return JSON.parse(output) as PathHealthResult
}


// --- v0.7 DPAPI-encrypted secret variable API (launch profiles) ---

/**
 * Adds a DPAPI-encrypted secret variable to a launch profile. The plaintext value is
 * passed to the CLI, the CLI encrypts it with CryptProtectData (CurrentUser scope) and
 * stores the base64 ciphertext in profiles.json. Plaintext lives only transiently in
 * CLI process memory.
 * CLI: `profile add-secret <profile> <name> <value>`
 */
export async function profileAddSecret(profileName: string, varName: string, varValue: string): Promise<string> {
  return await runWrite('profile', ['add-secret', profileName, varName, varValue])
}

/**
 * Edits (rename + re-encrypt) an existing secret variable in a launch profile.
 * CLI: `profile edit-secret <profile> <old-name> <new-name> <new-value>`
 */
export async function profileEditSecret(profileName: string, oldName: string, newName: string, newValue: string): Promise<string> {
  return await runWrite('profile', ['edit-secret', profileName, oldName, newName, newValue])
}

/**
 * Removes a secret variable from a profile (both the variable entry AND the SecretVariables membership).
 * CLI: `profile remove-secret <profile> <name>`
 */
export async function profileRemoveSecret(profileName: string, varName: string): Promise<string> {
  return await runWrite('profile', ['remove-secret', profileName, varName])
}

/**
 * Reveals one secret's plaintext to stdout. DPAPI CurrentUser scope means this only succeeds
 * when invoked by the same user account that encrypted it. Use sparingly; prefer `profileLaunch`
 * which decrypts into the child process env block in-process.
 * CLI: `profile reveal-secret <profile> <name>`
 */
export async function profileRevealSecret(profileName: string, varName: string): Promise<string> {
  return await runRead('profile', ['reveal-secret', profileName, varName])
}

// --- v0.8 Secret Provider Management ---

/**
 * Lists available secret providers and the active selection.
 * CLI: `profile secret-provider list`
 */
export async function secretProviderList(): Promise<string> {
  return await runRead('profile', ['secret-provider', 'list'])
}

/**
 * Sets the active secret provider.
 * CLI: `profile secret-provider set <name>`
 */
export async function secretProviderSet(providerName: string): Promise<string> {
  return await runWrite('profile', ['secret-provider', 'set', providerName])
}
