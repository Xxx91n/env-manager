import { execSync, spawn } from 'node:child_process'
import { exit } from 'node:process'

let triple = ''
try {
  const output = execSync('rustc -vV', { encoding: 'utf-8' })
  triple = output.match(/host:\s*(\S+)/)?.[1] ?? ''
} catch {
  // Tauri reports a clear error when rustc is unavailable.
}

const args = ['tauri', 'build', '--no-bundle']
if (triple) args.push('--target', triple)
args.push(...process.argv.slice(2))

const tauriCli = new URL('../node_modules/@tauri-apps/cli/tauri.js', import.meta.url)
const child = spawn(process.execPath, [tauriCli.pathname.slice(1), ...args.slice(1)], { stdio: 'inherit', shell: false })
child.on('error', (error) => {
  console.error(`[tauri-build] Failed to start ${executable}: ${error.message}`)
  exit(1)
})
child.on('exit', (code) => exit(code ?? 1))
