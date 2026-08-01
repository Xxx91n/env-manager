#!/usr/bin/env node
// Env Manager - Cross-platform build orchestrator
// Produces release/{portable,cli-only,msi} with per-arch ZIP archives.
// Works on Windows, Linux, and macOS. No hardcoded paths.
//
// Usage:
//   node scripts/build.mjs                    # host arch (auto-detect)
//   node scripts/build.mjs --arch x64         # target x64
//   node scripts/build.mjs --arch x86         # target x86
//   node scripts/build.mjs --arch arm64       # target arm64
//   node scripts/build.mjs --skip-cli
//   node scripts/build.mjs --skip-gui
//   node scripts/build.mjs --skip-msi
//   node scripts/build.mjs --append       # Don't clean release/ (for multi-arch builds)

import { execSync, spawnSync } from 'node:child_process'
import {
  existsSync, rmSync, mkdirSync, readdirSync, copyFileSync,
  createWriteStream, readFileSync
} from 'node:fs'
import { resolve, join, dirname, basename } from 'node:path'
import { fileURLToPath } from 'node:url'
import { arch } from 'node:os'
import archiver from 'archiver'

const __dirname = dirname(fileURLToPath(import.meta.url))
const projectRoot = resolve(__dirname, '..')

// --- arg parsing ---
const argv = process.argv.slice(2)
const getFlag = (name) => argv.includes(name)
const getOpt = (name) => {
  const i = argv.indexOf(name)
  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : null
}
const skipCli = getFlag('--skip-cli')
const skipGui = getFlag('--skip-gui')
const skipMsi = getFlag('--skip-msi')

// --- arch detection ---
function detectHostArch() {
  const a = arch()
  if (a === 'x64') return 'x64'
  if (a === 'arm64') return 'arm64'
  if (a === 'ia32') return 'x86'
  return 'x64'
}
let targetArch = getOpt('--arch') || detectHostArch()
const validArchs = ['x64', 'x86', 'arm64']
if (!validArchs.includes(targetArch)) {
  console.error('[build] Invalid arch: ' + targetArch + '. Valid: ' + validArchs.join(', '))
  process.exit(1)
}
console.log('[build] Target arch: ' + targetArch)

// --- RID / triple mapping ---
const ridMap = { x64: 'win-x64', x86: 'win-x86', arm64: 'win-arm64' }
const tripleMap = {
  x64: 'x86_64-pc-windows-msvc',
  x86: 'i686-pc-windows-msvc',
  arm64: 'aarch64-pc-windows-msvc',
}
const wixArchMap = { x64: 'x64', x86: 'x86', arm64: 'arm64' }
const rid = ridMap[targetArch]
const triple = tripleMap[targetArch]
const wixArch = wixArchMap[targetArch]

// --- version ---
const pkg = JSON.parse(readFileSync(join(projectRoot, 'frontend', 'package.json'), 'utf8'))
const version = pkg.version

// --- paths (all auto-discovered, no hardcoding) ---
const releaseDir = join(projectRoot, 'release')
const portableDir = join(releaseDir, 'portable')
const msiDir = join(releaseDir, 'msi')
const cliOnlyDir = join(releaseDir, 'cli-only')

// --- helpers ---
function run(cmd, args, opts = {}) {
  console.log('[build] > ' + cmd + ' ' + args.join(' '))
  // Only use shell for npm/npx on Windows (they need cmd.exe resolution);
  // dotnet and other tools work without shell, avoiding deprecation warnings.
  const needsShell = (cmd === 'npm' || cmd === 'npx') && process.platform === 'win32'
  const result = spawnSync(cmd, args, { stdio: 'inherit', shell: needsShell, ...opts })
  if (result.status !== 0) throw new Error(cmd + ' failed with exit ' + result.status)
  return result
}

function findCliOutput() {
  const releaseBase = join(projectRoot, 'bin', 'Release')
  if (!existsSync(releaseBase)) return null
  for (const dir of readdirSync(releaseBase)) {
    const candidate = join(releaseBase, dir)
    try {
      if (existsSync(join(candidate, 'env-manager-cli.dll'))) return candidate
    } catch {}
  }
  return null
}

function findGuiExe() {
  const targetBase = join(projectRoot, 'frontend', 'src-tauri', 'target')
  if (!existsSync(targetBase)) return null
  const triplePath = join(targetBase, triple, 'release', 'env-manager.exe')
  if (existsSync(triplePath)) return triplePath
  const hostDefault = join(targetBase, 'release', 'env-manager.exe')
  if (existsSync(hostDefault)) return hostDefault
  for (const dir of readdirSync(targetBase)) {
    const candidate = join(targetBase, dir, 'release', 'env-manager.exe')
    if (existsSync(candidate)) return candidate
  }
  return null
}

function copyDir(src, dst) {
  mkdirSync(dst, { recursive: true })
  for (const entry of readdirSync(src, { withFileTypes: true })) {
    const s = join(src, entry.name)
    const d = join(dst, entry.name)
    if (entry.isDirectory()) copyDir(s, d)
    else copyFileSync(s, d)
  }
}

function makeZip(sourceDir, zipPath) {
  // Remove existing ZIP to prevent recursive packaging (a residual .zip in the dir would be re-archived)
  if (existsSync(zipPath)) rmSync(zipPath, { force: true })
  return new Promise((resolveP, reject) => {
    const output = createWriteStream(zipPath)
    const archive = archiver('zip', { zlib: { level: 6 } })
    output.on('close', () => {
      console.log('[build] ZIP created: ' + zipPath + ' (' + archive.pointer() + ' bytes)')
      resolveP()
    })
    archive.on('error', reject)
    archive.pipe(output)
    // Exclude .zip files from packaging to prevent recursive nesting
    const entries = readdirSync(sourceDir, { withFileTypes: true })
    for (const entry of entries) {
      if (entry.name.endsWith('.zip')) continue
      const fullPath = join(sourceDir, entry.name)
      if (entry.isDirectory()) archive.directory(fullPath, entry.name)
      else archive.file(fullPath, { name: entry.name })
    }
    archive.finalize()
  })
}

// --- clean ---
// Only clean the release dirs if this is a fresh build (no --append flag).
// With --append, other archs' output in the same dirs are preserved.
const appendMode = getFlag('--append')
if (!appendMode) {
  if (existsSync(releaseDir)) rmSync(releaseDir, { recursive: true, force: true })
}
mkdirSync(portableDir, { recursive: true })
mkdirSync(msiDir, { recursive: true })
mkdirSync(cliOnlyDir, { recursive: true })

// --- Step 1: Build C# CLI ---
if (!skipCli) {
  console.log('[build] Step 1: Build C# CLI')
  // Framework-dependent build: no RID needed (CLI runs on any arch with .NET 10 runtime)
run('dotnet', ['build', '-c', 'Release'], { cwd: projectRoot })
}

const cliDir = findCliOutput()
if (!cliDir) throw new Error('CLI output directory not found under bin/Release')
console.log('[build] CLI output: ' + cliDir)

// --- Step 2: Build Tauri GUI ---
if (!skipGui) {
  console.log('[build] Step 2: Build Tauri GUI')
  run('npm', ['run', 'build'], { cwd: join(projectRoot, 'frontend') })
  run('npx', ['tauri', 'build', '--no-bundle', '--target', triple], { cwd: join(projectRoot, 'frontend') })
}

const guiExe = findGuiExe()
if (!guiExe) throw new Error('GUI exe not found under frontend/src-tauri/target for ' + triple)
console.log('[build] GUI exe: ' + guiExe)

// --- Step 3: Assemble portable package ---
console.log('[build] Step 3: Assemble portable package')
copyFileSync(guiExe, join(portableDir, 'env-manager.exe'))
for (const f of readdirSync(cliDir)) {
  if (f.endsWith('.exe') || f.endsWith('.dll') || f.endsWith('.json')) {
    copyFileSync(join(cliDir, f), join(portableDir, f))
  }
}
const agentsMd = join(projectRoot, 'AGENTS.cli.md')
if (existsSync(agentsMd)) copyFileSync(agentsMd, join(portableDir, 'AGENTS.cli.md'))
const guiDir2 = dirname(guiExe)
const webviewLoader = join(guiDir2, 'WebView2Loader.dll')
if (existsSync(webviewLoader)) copyFileSync(webviewLoader, join(portableDir, 'WebView2Loader.dll'))

// --- Step 3b: Assemble CLI-only package ---
console.log('[build] Step 3b: Assemble CLI-only package')
for (const f of readdirSync(cliDir)) {
  if (f.endsWith('.exe') || f.endsWith('.dll') || f.endsWith('.json')) {
    copyFileSync(join(cliDir, f), join(cliOnlyDir, f))
  }
}
if (existsSync(agentsMd)) copyFileSync(agentsMd, join(cliOnlyDir, 'AGENTS.cli.md'))

// --- Step 4: Build MSI installer ---
if (!skipMsi && process.platform === 'win32') {
  console.log('[build] Step 4: Build MSI installer')
  const wixRoot = process.env.WIX || join(process.env.LOCALAPPDATA || (process.env.HOME || '.'), 'tauri', 'WixTools314')
  const candle = join(wixRoot, 'candle.exe')
  const light = join(wixRoot, 'light.exe')
  if (existsSync(candle) && existsSync(light)) {
    const wixSource = join(__dirname, 'installer.wxs')
    const os = require('node:os')
    const wixObject = join(os.tmpdir(), 'env-manager-' + Date.now() + '.wixobj')
    const msiPath = join(msiDir, 'Env Manager_' + version + '_' + targetArch + '.msi')
    const win64Val = targetArch === 'x86' ? 'no' : 'yes'
    try {
      const candleResult = spawnSync(candle, ['-nologo', '-arch', wixArch, '-dVersion=' + version, '-dSourceDir=' + portableDir, '-dWin64=' + win64Val, '-out', wixObject, wixSource], { stdio: 'inherit' })
      if (candleResult.status !== 0) throw new Error('WiX candle failed (exit ' + candleResult.status + ')')
      const lightResult = spawnSync(light, ['-nologo', '-spdb', '-out', msiPath, wixObject], { stdio: 'inherit' })
      if (lightResult.status !== 0) throw new Error('WiX light failed (exit ' + lightResult.status + ')')
      if (!existsSync(msiPath)) throw new Error('WiX light failed - MSI not created')
      console.log('[build] MSI: ' + basename(msiPath))
    } finally {
      try { rmSync(wixObject, { force: true }) } catch {}
      try { rmSync(wixObject.replace(/\.wixobj$/, '.wixpdb'), { force: true }) } catch {}
    }
  } else {
    console.log('[build] WiX tools not found, skipping MSI. Set WIX env var or install WiX 3.14.')
  }
} else if (!skipMsi) {
  console.log('[build] Step 4: MSI build skipped (not on Windows)')
}

// --- Step 5: Create ZIP archives (inside each arch-specific subdirectory) ---
// Layout: release/{portable,cli-only}/*.zip  (not release/*.zip)
console.log('[build] Step 5: Create ZIP archives')
const portableZip = join(portableDir, 'Env-Manager_portable_' + version + '_' + targetArch + '.zip')
const cliOnlyZip = join(cliOnlyDir, 'Env-Manager_cli-only_' + version + '_' + targetArch + '.zip')
await makeZip(portableDir, portableZip)
await makeZip(cliOnlyDir, cliOnlyZip)

// --- Summary ---
console.log('')
console.log('[build] Done. Output:')
console.log('  Portable (dir):', portableDir)
for (const f of readdirSync(portableDir)) console.log('    ', f)
console.log('  Portable (zip):', portableZip)
console.log('    (inside release/portable/, not release/ root)')
console.log('  CLI-only (dir):', cliOnlyDir)
for (const f of readdirSync(cliOnlyDir)) console.log('    ', f)
console.log('  CLI-only (zip):', cliOnlyZip)
if (existsSync(msiDir) && readdirSync(msiDir).some(f => f.endsWith('.msi'))) {
  console.log('  MSI:', msiDir)
  for (const f of readdirSync(msiDir)) if (f.endsWith('.msi')) console.log('    ', f)
}
