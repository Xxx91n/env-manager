import { execSync, spawn } from 'node:child_process'
import { exit } from 'node:process'

// --- arg parsing ---
const argv = process.argv.slice(2)
const getOpt = (name) => {
  const i = argv.indexOf(name)
  return i >= 0 && i + 1 < argv.length ? argv[i + 1] : null
}
const targetArch = getOpt('--arch')

// Triple mapping for Rust target
const tripleMap = {
  x64: 'x86_64-pc-windows-msvc',
  x86: 'i686-pc-windows-msvc',
  arm64: 'aarch64-pc-windows-msvc',
}

let triple = ''
try {
  const output = execSync('rustc -vV', { encoding: 'utf-8' })
  triple = output.match(/host:\s*(\S+)/)?.[1] ?? ''
} catch {
  // Tauri reports a clear error when rustc is unavailable.
}

// Override triple if --arch is specified
if (targetArch && tripleMap[targetArch]) {
  triple = tripleMap[targetArch]
}

const args = ['tauri', 'build', '--no-bundle']
if (triple) args.push('--target', triple)
// Pass through remaining args (excluding --arch and its value)
const passthroughArgs = argv.filter((a, i) => a !== '--arch' && (argv[i - 1] !== '--arch'))
args.push(...passthroughArgs)

const tauriCli = new URL('../node_modules/@tauri-apps/cli/tauri.js', import.meta.url)
const child = spawn(process.execPath, [tauriCli.pathname.slice(1), ...args.slice(1)], { stdio: 'inherit', shell: false })
child.on('error', (error) => {
  console.error('[tauri-build] Failed to start: ' + error.message)
  exit(1)
})
child.on('exit', (code) => exit(code ?? 1))
