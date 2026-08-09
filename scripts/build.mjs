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
//   node scripts/build.mjs --skip-service     # skip Rust service build

import { execSync, spawnSync } from 'node:child_process'
import {
  existsSync, rmSync, mkdirSync, readdirSync, copyFileSync,
  createWriteStream, readFileSync
} from 'node:fs'
import { resolve, join, dirname, basename } from 'node:path'
import { fileURLToPath } from 'node:url'
import { arch, tmpdir } from 'node:os'
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
const skipService = getFlag('--skip-service')

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

// --- RID / triple mapping ---
const ridMap = { x64: 'win-x64', x86: 'win-x86', arm64: 'win-arm64' }
const tripleMap = {
  x64: 'x86_64-pc-windows-msvc',
  x86: 'i686-pc-windows-msvc',
  arm64: 'aarch64-pc-windows-msvc',
}
const wixArchMap = { x64: 'x64', x86: 'x86', arm64: 'arm64' }

// --- version (v0.9.6: single source from csproj, auto-sync to package.json) ---
const csprojPath = join(projectRoot, 'env-manager.csproj')
const csprojRaw = readFileSync(csprojPath, 'utf8')
const versionMatch = csprojRaw.match(/<Version>([^<]+)<\/Version>/)
if (!versionMatch) throw new Error('Could not find <Version> in env-manager.csproj')
const version = versionMatch[1].trim()
// Auto-sync to frontend/package.json
const pkgPath = join(projectRoot, 'frontend', 'package.json')
const pkg = JSON.parse(readFileSync(pkgPath, 'utf8'))
if (pkg.version !== version) {
  pkg.version = version
  writeFileSync(pkgPath, JSON.stringify(pkg, null, 2) + '\n', 'utf8')
  console.log('[build] synced frontend/package.json version to csproj: v' + version)
}
const pkgVer = pkg.version

// --- paths (all auto-discovered, no hardcoding) ---
const releaseDir = join(projectRoot, 'release')
const portableDir = join(releaseDir, 'portable')
const msiDir = join(releaseDir, 'msi')
const cliOnlyDir = join(releaseDir, 'cli-only')

// --- helpers ---
function run(cmd, args, opts = {}) {
  console.log('[build] > ' + cmd + ' ' + args.join(' '))
  const needsShell = (cmd === 'npm' || cmd === 'npx') && process.platform === 'win32'
  const result = spawnSync(cmd, args, { stdio: 'inherit', shell: needsShell, ...opts })
  if (result.status !== 0) throw new Error(cmd + ' failed with exit ' + result.status)
  return result
}

function findCliOutput(rid) {
  // With dotnet publish -r <rid>, output goes to bin/Release/<tfm>/<rid>/
  const releaseBase = join(projectRoot, 'bin', 'Release')
  if (!existsSync(releaseBase)) return null
  const preferred = 'net10.0-windows'
  // RID-specific publish output
  const ridDir = join(releaseBase, preferred, rid)
  if (existsSync(join(ridDir, 'env-manager-cli.dll'))) return ridDir
  // Fallback: non-RID build (host arch)
  const preferredDir = join(releaseBase, preferred)
  if (existsSync(join(preferredDir, 'env-manager-cli.dll'))) return preferredDir
  for (const dir of readdirSync(releaseBase)) {
    if (dir === preferred) continue
    const candidate = join(releaseBase, dir)
    try {
      if (existsSync(join(candidate, 'env-manager-cli.dll'))) return candidate
    } catch {}
  }
  return null
}

function findGuiExe(triple) {
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

function findServiceExe(triple) {
  const serviceTarget = join(projectRoot, 'service', 'target')
  if (!existsSync(serviceTarget)) return null
  // RID-specific build output
  if (triple) {
    const triplePath = join(serviceTarget, triple, 'release', 'env-manager-service.exe')
    if (existsSync(triplePath)) return triplePath
  }
  // Fallback: host arch
  const releasePath = join(serviceTarget, 'release', 'env-manager-service.exe')
  if (existsSync(releasePath)) return releasePath
  for (const dir of readdirSync(serviceTarget)) {
    const candidate = join(serviceTarget, dir, 'release', 'env-manager-service.exe')
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

// --- Per-arch build function ---
async function buildArch(targetArch) {
  const rid = ridMap[targetArch]
  const triple = tripleMap[targetArch]
  const wixArch = wixArchMap[targetArch]
  console.log('\n[build] === Building arch: ' + targetArch + ' (RID: ' + rid + ', triple: ' + triple + ') ===')

  // Per-arch temp staging dirs (cleaned after ZIP creation)
  const archPortableDir = join(releaseDir, '_staging', 'portable-' + targetArch)
  const archCliOnlyDir = join(releaseDir, '_staging', 'cli-only-' + targetArch)
  // Remove stale staging
  for (const d of [archPortableDir, archCliOnlyDir]) {
    if (existsSync(d)) rmSync(d, { recursive: true, force: true })
    mkdirSync(d, { recursive: true })
  }

  // --- Step 1: Build C# CLI with RID ---
  if (!skipCli) {
    console.log('[build] Step 1: Build C# CLI (RID: ' + rid + ')')
    // Framework-dependent publish with RID: produces arch-specific apphost exe
    // but does NOT bundle the .NET runtime (target machine needs .NET 10 runtime)
    run('dotnet', ['publish', '-c', 'Release', '-r', rid, '--no-self-contained', '-p:PublishSingleFile=true'], { cwd: projectRoot })
  }

  const cliDir = findCliOutput(rid)
  if (!cliDir) throw new Error('CLI output directory not found for RID ' + rid)
  console.log('[build] CLI output: ' + cliDir)

  // Version verification (skip if cross-arch: can't run x86/arm64 exe on x64 host)
  const hostArch = detectHostArch()
  if (targetArch === hostArch) {
    const cliExe = join(cliDir, 'env-manager-cli.exe')
    const probe = spawnSync(cliExe, [], { encoding: 'utf8', timeout: 10000, cwd: cliDir })
    if (probe.status !== 0) {
      throw new Error('CLI probe failed (exit ' + probe.status + '): ' + probe.stderr)
    }
    const m = probe.stdout.match(/v(\d+\.\d+\.\d+)/)
    const cliVer = m ? m[1] : 'unknown'
    if (cliVer !== version) {
      throw new Error('CLI version mismatch: expected v' + version + ' got v' + cliVer + '.')
    }
    console.log('[build] CLI version verified: v' + cliVer)
  } else {
    console.log('[build] CLI version probe skipped (cross-arch: target=' + targetArch + ' host=' + hostArch + ')')
  }

  // --- Step 2: Build Tauri GUI ---
  if (!skipGui) {
    console.log('[build] Step 2: Build Tauri GUI (triple: ' + triple + ')')
    run('npm', ['run', 'build'], { cwd: join(projectRoot, 'frontend') })
    run('npx', ['tauri', 'build', '--no-bundle', '--target', triple], { cwd: join(projectRoot, 'frontend') })
  }

  const guiExe = findGuiExe(triple)
  if (!guiExe) throw new Error('GUI exe not found for ' + triple)
  console.log('[build] GUI exe: ' + guiExe)

  // --- Step 2b: Build Rust service binary ---
  let serviceExe = null
  if (!skipService) {
    console.log('[build] Step 2b: Build env-manager-service (Rust, triple: ' + triple + ')')
    try {
      run('cargo', ['build', '--release', '--target', triple, '--manifest-path', join(projectRoot, 'service', 'Cargo.toml')], { cwd: projectRoot })
      serviceExe = findServiceExe(triple)
    } catch (e) {
      console.log('[build] Warning: cargo build --target ' + triple + ' failed: ' + e.message)
      // Fallback: host-arch build
      console.log('[build] Falling back to host-arch service build')
      run('cargo', ['build', '--release', '--manifest-path', join(projectRoot, 'service', 'Cargo.toml')], { cwd: projectRoot })
      serviceExe = findServiceExe(null)
    }
    if (serviceExe) {
      console.log('[build] Service exe: ' + serviceExe)
    } else {
      console.log('[build] Warning: env-manager-service.exe not found.')
    }
  }

  // --- Step 3: Assemble portable package (staging dir) ---
  console.log('[build] Step 3: Assemble portable package -> ' + archPortableDir)
  copyFileSync(guiExe, join(archPortableDir, 'env-manager.exe'))
  for (const f of readdirSync(cliDir)) {
    if (f.endsWith('.exe') || f.endsWith('.dll') || f.endsWith('.json')) {
      copyFileSync(join(cliDir, f), join(archPortableDir, f))
    }
  }
  if (serviceExe) copyFileSync(serviceExe, join(archPortableDir, 'env-manager-service.exe'))
  const agentsMd = join(projectRoot, 'AGENTS.cli.md')
  if (existsSync(agentsMd)) copyFileSync(agentsMd, join(archPortableDir, 'AGENTS.cli.md'))
  const guiDir2 = dirname(guiExe)
  const webviewLoader = join(guiDir2, 'WebView2Loader.dll')
  if (existsSync(webviewLoader)) copyFileSync(webviewLoader, join(archPortableDir, 'WebView2Loader.dll'))
  const dotnetCheck = join(projectRoot, 'scripts', 'check-dotnet-runtime.ps1')
  if (existsSync(dotnetCheck)) copyFileSync(dotnetCheck, join(archPortableDir, 'check-dotnet-runtime.ps1'))

  // --- Step 3b: Assemble CLI-only package (staging dir) ---
  console.log('[build] Step 3b: Assemble CLI-only package -> ' + archCliOnlyDir)
  for (const f of readdirSync(cliDir)) {
    if (f.endsWith('.exe') || f.endsWith('.dll') || f.endsWith('.json')) {
      copyFileSync(join(cliDir, f), join(archCliOnlyDir, f))
    }
  }
  if (serviceExe) copyFileSync(serviceExe, join(archCliOnlyDir, 'env-manager-service.exe'))
  if (existsSync(agentsMd)) copyFileSync(agentsMd, join(archCliOnlyDir, 'AGENTS.cli.md'))
  if (existsSync(dotnetCheck)) copyFileSync(dotnetCheck, join(archCliOnlyDir, 'check-dotnet-runtime.ps1'))

  // --- Step 4: Build MSI installer ---
  if (!skipMsi && process.platform === 'win32') {
    console.log('[build] Step 4: Build MSI installer (' + targetArch + ')')
    const wixRoot = process.env.WIX || join(process.env.LOCALAPPDATA || (process.env.HOME || '.'), 'tauri', 'WixTools314')
    const candle = join(wixRoot, 'candle.exe')
    const light = join(wixRoot, 'light.exe')
    if (existsSync(candle) && existsSync(light)) {
      const wixSource = join(projectRoot, 'frontend', 'scripts', 'installer.wxs')
      const wixObject = join(tmpdir(), 'env-manager-' + targetArch + '-' + Date.now() + '.wixobj')
      const msiPath = join(msiDir, 'Env Manager_' + version + '_' + targetArch + '.msi')
      const win64Val = targetArch === 'x86' ? 'no' : 'yes'
      try {
        const webviewLoaderPath = join(archPortableDir, 'WebView2Loader.dll')
        const webviewPresent = existsSync(webviewLoaderPath) ? '1' : '0'
        const candleResult = spawnSync(candle, ['-nologo', '-arch', wixArch, '-dVersion=' + version, '-dSourceDir=' + archPortableDir, '-dWin64=' + win64Val, '-dWebViewLoaderPresent=' + webviewPresent, '-out', wixObject, wixSource], { stdio: 'inherit' })
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

  // --- Step 5: Create ZIP archives ---
  console.log('[build] Step 5: Create ZIP archives (' + targetArch + ')')
  const portableZip = join(portableDir, 'Env-Manager_portable_' + version + '_' + targetArch + '.zip')
  const cliOnlyZip = join(cliOnlyDir, 'Env-Manager_cli-only_' + version + '_' + targetArch + '.zip')
  await makeZip(archPortableDir, portableZip)
  await makeZip(archCliOnlyDir, cliOnlyZip)

  // --- Clean staging dirs ---
  for (const d of [archPortableDir, archCliOnlyDir]) {
    if (existsSync(d)) rmSync(d, { recursive: true, force: true })
  }
}

// --- Main ---
// Always clean release/ at start (no append mode — each build is authoritative)
if (existsSync(releaseDir)) rmSync(releaseDir, { recursive: true, force: true })
mkdirSync(portableDir, { recursive: true })
mkdirSync(msiDir, { recursive: true })
mkdirSync(cliOnlyDir, { recursive: true })

// Build single arch (no loop — CI matrix handles multi-arch)
console.log('[build] Building arch: ' + targetArch)
await buildArch(targetArch)

// --- Clean staging ---
const stagingDir = join(releaseDir, '_staging')
if (existsSync(stagingDir)) rmSync(stagingDir, { recursive: true, force: true })

// --- Summary ---
console.log('')
console.log('[build] Done. Output layout:')
console.log('  release/')
console.log('  ' + cliOnlyDir + '/')
for (const f of readdirSync(cliOnlyDir)) console.log('    ' + f)
console.log('  ' + msiDir + '/')
for (const f of readdirSync(msiDir)) console.log('    ' + f)
console.log('  ' + portableDir + '/')
for (const f of readdirSync(portableDir)) console.log('    ' + f)
