import { test, expect, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { mockDataset, type MockSettings } from './mock-data'

// High-contrast spot check required by ticket #315 (spec #310): the v4
// `focus:outline-hidden` rename differs from v3's `focus:outline-none` only
// under `forced-colors: active` (Windows High Contrast). Normal mode is covered
// by the 28-shot baseline; this test verifies the focus indicator is actually
// PAINTED under forced colors on one representative control (the Variables
// scope select, one of the 20 renamed spots).
//
// getComputedStyle cannot be used as the oracle here: under forced-colors
// emulation Chromium reports outline-style 'none' even when it paints the
// author outline, so the assertion compares focused vs unfocused pixels of the
// same region inside the browser instead. The cascade prerequisite was fixed in
// app.css (input:focus rule moved into @layer base — see the comment there);
// without that fix the unlayered rule buried the utility's forced-colors
// branch and this test fails with a 0-pixel diff.
// Evidence screenshot lands in tests/screenshots/evidence/.

const require = createRequire(import.meta.url)
const mocksSource = readFileSync(require.resolve('@tauri-apps/api/mocks'), 'utf8')

async function injectMocks(page: Page, settings: MockSettings): Promise<void> {
  // Same injection as baseline.spec.ts; copied (not imported) so the baseline
  // suite's code stays byte-identical for future re-shoots.
  await page.addInitScript(
    ({ mocksSource, dataset, settings }) => {
      const exports: Record<string, any> = {}
      new Function('exports', mocksSource)(exports)
      exports.mockWindows('main')

      const ok = (data: unknown) => ({
        success: true,
        data: typeof data === 'string' ? data : JSON.stringify(data),
        error: null,
      })
      const cli = (command: string, args: string[] | undefined) => {
        const a = args ?? []
        if (command === 'list') return ok(dataset.variables)
        if (command === 'profile' && a[0] === 'list') return ok(dataset.profiles)
        if (command === 'path' && a[0] === 'list') return ok(a[2] === 'system' ? dataset.pathSystem : dataset.pathUser)
        if (command === 'history' && a[0] === 'list') return ok(dataset.history)
        if (command === 'protection' && a[0] === 'list') return ok(dataset.protection)
        if (command === 'service' && a[0] === 'status') return ok(dataset.serviceStatus)
        if (command === 'service' && a[0] === 'health') return ok(dataset.serviceHealth)
        if (command === 'service' && a[0] === 'ping') return ok(dataset.servicePing)
        if (command === 'audit' && a[0] === 'list') return ok(dataset.audit)
        return null
      }
      exports.mockIPC((cmd: string, args: any) => {
        switch (cmd) {
          case 'run_cli': {
            const res = cli(args.command, args.args)
            return res === null ? { success: false, data: null, error: 'unmocked command' } : res
          }
          case 'read_gui_setting':
            return settings[args.key] ?? null
          case 'app_version':
            return dataset.version
          case 'cli_diagnostics':
            return dataset.diagnostics
          case 'check_update_plugin':
          case 'check_for_updates':
            return { latestVersion: '', releaseUrl: '', isUpdateAvailable: false }
          case 'get_lightweight_config':
            return { enabled: true, timeoutMinutes: 10 }
          case 'read_var_notes':
            return dataset.notes
          case 'write_gui_setting':
          case 'write_var_notes':
          case 'frontend_log':
          case 'update_tray_locale':
            return null
          default:
            return null
        }
      })

      // Kill the 0.15s color transitions before first paint.
      const style = document.createElement('style')
      style.textContent = '*, *::before, *::after { transition: none !important; scroll-behavior: auto !important; }'
      document.documentElement.appendChild(style)
    },
    { mocksSource, dataset: mockDataset, settings },
  )
}

// Channel deltas within this margin count as antialiasing noise, not paint.
const PIXEL_NOISE_MARGIN = 8
// A 2px offset outline around the ~150x30 select paints well past this floor.
// The pre-fix regression measured 220 border-only pixels vs 712 with the
// outline, so the floor sits between the two (CodeRabbit caught that 100 would
// still pass on the regression): reverting the app.css fix must fail this test.
const MIN_FOCUS_INDICATOR_PIXELS = 400
// Padding around the control's bounding box so the 2px offset outline (4px
// outside the border box) stays inside every clipped screenshot.
const CLIP_PADDING = 12

async function shot(page: Page, clip: { x: number; y: number; width: number; height: number }): Promise<Buffer> {
  return Buffer.from(await page.screenshot({ clip }))
}

// Count pixels whose color differs by more than the AA-noise margin, decoded
// inside the page so no external PNG decoder is needed.
async function pixelDiff(page: Page, a: Buffer, b: Buffer): Promise<number> {
  return page.evaluate(
    ([aUrl, bUrl, noiseMargin]) =>
      new Promise<number>((resolve, reject) => {
        const load = (url: string) =>
          new Promise<HTMLImageElement>((res, rej) => {
            const img = new Image()
            img.onload = () => res(img)
            img.onerror = () => rej(new Error('image decode failed'))
            img.src = url
          })
        Promise.all([load(aUrl), load(bUrl)])
          .then(([ia, ib]) => {
            const w = Math.min(ia.naturalWidth, ib.naturalWidth)
            const h = Math.min(ia.naturalHeight, ib.naturalHeight)
            const c1 = document.createElement('canvas')
            c1.width = w
            c1.height = h
            const c2 = document.createElement('canvas')
            c2.width = w
            c2.height = h
            const x1 = c1.getContext('2d')!
            const x2 = c2.getContext('2d')!
            x1.drawImage(ia, 0, 0)
            x2.drawImage(ib, 0, 0)
            const d1 = x1.getImageData(0, 0, w, h).data
            const d2 = x2.getImageData(0, 0, w, h).data
            let diff = 0
            for (let i = 0; i < d1.length; i += 4) {
              if (
                Math.abs(d1[i] - d2[i]) > noiseMargin ||
                Math.abs(d1[i + 1] - d2[i + 1]) > noiseMargin ||
                Math.abs(d1[i + 2] - d2[i + 2]) > noiseMargin
              )
                diff++
            }
            resolve(diff)
          })
          .catch(reject)
      }),
    [
      `data:image/png;base64,${a.toString('base64')}`,
      `data:image/png;base64,${b.toString('base64')}`,
      PIXEL_NOISE_MARGIN,
    ],
  )
}

test('forced-colors focus indicator is painted on the Variables scope select (slate light)', async ({ page }) => {
  await injectMocks(page, {
    darkMode: 'false',
    themeStyle: 'slate',
    fontScale: '1',
    locale: 'en',
    autoLightweight: 'true',
    lightweightTimeout: '10',
  })
  await page.emulateMedia({ forcedColors: 'active' })
  await page.goto('/')
  await expect(page.locator('html')).toHaveAttribute('data-theme', 'light')
  await expect(page.locator('html')).toHaveAttribute('data-theme-style', 'slate')
  await expect(page.getByText('JAVA_HOME', { exact: true }).first()).toBeVisible()

  const select = page.locator('select').first()
  await expect(select).toBeVisible()
  const box = await select.boundingBox()
  const clip = {
    x: box!.x - CLIP_PADDING,
    y: box!.y - CLIP_PADDING,
    width: box!.width + CLIP_PADDING * 2,
    height: box!.height + CLIP_PADDING * 2,
  }

  await select.focus()
  const focused = await shot(page, clip)
  await page.screenshot({
    path: 'tests/screenshots/evidence/forced-colors-focus-select.png',
    clip,
  })
  await select.blur()
  const unfocused = await shot(page, clip)

  const diff = await pixelDiff(page, focused, unfocused)
  console.log(`forced-colors focus diff pixels: ${diff}`)
  expect(
    diff,
    'focus indicator must be painted under forced colors; 0 means the forced-colors branch of focus:outline-hidden lost the cascade (see app.css @layer base comment)',
  ).toBeGreaterThan(MIN_FOCUS_INDICATOR_PIXELS)
})
