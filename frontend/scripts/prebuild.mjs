// Prebuild script: build the C# CLI and copy it to src-tauri/bin/ for bundling.
// Runs before "vite build" during "tauri build".
import { execSync } from 'child_process'
import { mkdirSync, copyFileSync, readdirSync, existsSync, rmSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(__dirname, '..', '..')
const cliProject = projectRoot
const releaseBase = resolve(cliProject, 'bin', 'Release')
const binDir = resolve(__dirname, '..', 'src-tauri', 'bin')

console.log('[prebuild] Building C# CLI...')

try {
  execSync('dotnet build -c Release', { cwd: cliProject, stdio: 'inherit' })
} catch {
  console.error('[prebuild] dotnet build failed')
  process.exit(1)
}

// Auto-detect the TFM output directory (net10.0, net10.0-windows, etc.)
let cliOutputDir = ''
for (const dir of readdirSync(releaseBase)) {
  const candidate = resolve(releaseBase, dir)
  if (existsSync(resolve(candidate, 'env-manager-cli.dll'))) {
    cliOutputDir = candidate
    break
  }
}
if (!cliOutputDir) {
  console.error('[prebuild] Could not find CLI output directory under ' + releaseBase)
  process.exit(1)
}

const cliExe = resolve(cliOutputDir, 'env-manager-cli.exe')
if (!existsSync(cliExe)) {
  console.error(`[prebuild] CLI exe not found at ${cliExe}`)
  process.exit(1)
}

// Clean and recreate bin directory
if (existsSync(binDir)) {
  rmSync(binDir, { recursive: true, force: true })
}
mkdirSync(binDir, { recursive: true })

// Copy CLI exe and its dependency DLLs
const filesToCopy = readdirSync(cliOutputDir).filter(f =>
  f.endsWith('.dll') || f.endsWith('.exe') || f.endsWith('.json')
)

for (const file of filesToCopy) {
  copyFileSync(resolve(cliOutputDir, file), resolve(binDir, file))
}

console.log(`[prebuild] CLI copied to ${binDir} (${filesToCopy.length} files from ${cliOutputDir})`)
