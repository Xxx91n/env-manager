// Prebuild script: build the C# CLI and copy it to src-tauri/bin/ for bundling.
// Runs before "vite build" during "tauri build".
import { execSync } from 'child_process'
import { mkdirSync, copyFileSync, readdirSync, existsSync, rmSync } from 'fs'
import { resolve, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(__dirname, '..', '..')
const cliProject = projectRoot
const cliOutputDir = resolve(cliProject, 'bin', 'Release', 'net10.0')
const cliExe = resolve(cliOutputDir, 'env-manager-cli.exe')
const binDir = resolve(__dirname, '..', 'src-tauri', 'bin')
const binTarget = resolve(binDir, 'env-manager-cli.exe')

console.log('[prebuild] Building C# CLI...')

try {
  execSync('dotnet build -c Release', { cwd: cliProject, stdio: 'inherit' })
} catch {
  console.error('[prebuild] dotnet build failed')
  process.exit(1)
}

if (!existsSync(cliExe)) {
  console.error(`[prebuild] CLI exe not found at ${cliExe}`)
  process.exit(1)
}

// Clean and recreate bin directory
if (existsSync(binDir)) {
  rmSync(binDir, { recursive: true, force: true })
}
mkdirSync(binDir, { recursive: true })

// Copy CLI exe and its dependency DLLs (Spectre.Console.dll, etc.)
const filesToCopy = readdirSync(cliOutputDir).filter(f =>
  f.endsWith('.dll') || f.endsWith('.exe') || f.endsWith('.json')
)

for (const file of filesToCopy) {
  copyFileSync(resolve(cliOutputDir, file), resolve(binDir, file))
}

console.log(`[prebuild] CLI copied to ${binTarget} (${filesToCopy.length} files)`)
