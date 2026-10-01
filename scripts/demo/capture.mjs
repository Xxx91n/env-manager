#!/usr/bin/env node
// Capture docs/assets/demo.gif: simulated terminal (terminal.html + script.json) ->
// Playwright recordVideo webm -> ffmpeg two-pass palette gif.
// No real CLI, registry, or machine data is involved - all output comes from script.json.
//
// Usage:   node scripts/demo/capture.mjs [--out <gif-path>]
// Env:     PW_SHELL overrides the pinned chromium headless-shell path;
//          DEMO_GIF_OUT overrides the output gif path.
import { execFileSync } from 'node:child_process'
import { mkdirSync, readFileSync, statSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { createRequire } from 'node:module'
import { pathToFileURL, fileURLToPath } from 'node:url'

const HERE = path.dirname(fileURLToPath(import.meta.url))
const ROOT = path.resolve(HERE, '..', '..')
const SCRIPT = JSON.parse(readFileSync(path.join(HERE, 'script.json'), 'utf8'))
const argOut = process.argv.indexOf('--out')
const OUT_GIF = path.resolve(
  argOut >= 0 && process.argv[argOut + 1]
    ? process.argv[argOut + 1]
    : (process.env.DEMO_GIF_OUT || path.join(ROOT, 'docs', 'assets', 'demo.gif')))
const TMP = path.join(tmpdir(), 'env-manager-demo-gif')
mkdirSync(TMP, { recursive: true })

const { chromium } = createRequire(pathToFileURL(path.join(ROOT, 'frontend', 'package.json')))('@playwright/test')
const SHELL = process.env.PW_SHELL || path.join(process.env.LOCALAPPDATA || '', 'ms-playwright/chromium_headless_shell-1243/chrome-headless-shell-win64/chrome-headless-shell.exe')

const W = SCRIPT.capture.width
const H = SCRIPT.capture.height
const FPS = SCRIPT.capture.fps

const browser = await chromium.launch({ executablePath: SHELL })
const ctx = await browser.newContext({ viewport: { width: W, height: H }, recordVideo: { dir: TMP, size: { width: W, height: H } } })
await ctx.addInitScript({ content: 'window.__DEMO_SCRIPT__ = ' + JSON.stringify(SCRIPT) })
const page = await ctx.newPage()
const video = page.video()
await page.goto(pathToFileURL(path.join(HERE, 'terminal.html')).href, { waitUntil: 'load' })
await page.waitForFunction(() => window.__DEMO_DONE__ === true, null, { timeout: 120000 })
await page.waitForTimeout(250)
await ctx.close()
const webm = path.join(TMP, 'demo.webm')
await video.saveAs(webm)
await browser.close()
console.log('webm ->', webm)

const palette = path.join(TMP, 'palette.png')
const vf = 'fps=' + FPS + ',scale=' + W + ':-1:flags=lanczos'
// Terminal content is flat-color: dither=none + mpdecimate frame dedupe keeps gif small.
const vfDedup = vf + ',mpdecimate=hi=64*12:lo=64*5:frac=0.05'
execFileSync('ffmpeg', ['-y', '-i', webm, '-vf', vf + ',palettegen=stats_mode=diff', palette], { stdio: ['ignore', 'ignore', 'inherit'] })
execFileSync('ffmpeg', ['-y', '-i', webm, '-i', palette, '-filter_complex', vfDedup + ' [x]; [x][1:v] paletteuse=dither=none:diff_mode=rectangle', OUT_GIF], { stdio: ['ignore', 'ignore', 'inherit'] })

const meta = JSON.parse(execFileSync('ffprobe', ['-v', 'error', '-show_entries', 'format=duration,size', '-of', 'json', OUT_GIF], { encoding: 'utf8' }))
const bytes = statSync(OUT_GIF).size
console.log('WROTE', OUT_GIF)
console.log('duration', meta.format.duration, 's | size', (bytes / 1048576).toFixed(2), 'MiB')
if (bytes > SCRIPT.capture.maxBytes) {
  console.error('WARN: gif exceeds maxBytes=' + SCRIPT.capture.maxBytes)
  process.exitCode = 2
}
