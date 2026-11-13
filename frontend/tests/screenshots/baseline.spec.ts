import { test, expect, type Page } from '@playwright/test'
import { readFileSync } from 'node:fs'
import { createRequire } from 'node:module'
import { mockDataset, type MockSettings } from './mock-data'

// Screenshot baseline for the Tailwind migration: 8 screens x slate light/dark
// (16) plus the main table across all 6 theme styles x light/dark (12) = 28.
// Local-only by design (same as tests/e2e); run via playwright.screenshots.config.ts.
// Baselines are machine- and browser-specific: re-shoot on the same machine with
// the Chromium build recorded in README.md.

const require = createRequire(import.meta.url)
// The package's own mockIPC implementation, injected before any page script runs.
const mocksSource = readFileSync(require.resolve('@tauri-apps/api/mocks'), 'utf8')

type Theme = 'light' | 'dark'

async function injectMocks(page: Page, settings: MockSettings): Promise<void> {
  await page.addInitScript(
    ({ mocksSource, dataset, settings }) => {
      const exports: Record<string, any> = {}
      new Function('exports', mocksSource)(exports)
      exports.mockWindows('main')

      const unmocked: string[] = []
      ;(window as any).__UNMOCKED_IPC__ = unmocked
      const ok = (data: unknown) => ({
        success: true,
        data: typeof data === 'string' ? data : JSON.stringify(data),
        error: null,
      })
      const cli = (command: string, args: string[] | undefined) => {
        const a = args ?? []
        if (command === 'list') return ok(dataset.variables)
        if (command === 'profile' && a[0] === 'list') return ok(dataset.profiles)
        if (command === 'profile' && a[0] === 'secret-provider' && a[1] === 'list') return ok(dataset.providerList)
        if (command === 'path' && a[0] === 'list') return ok(a[2] === 'system' ? dataset.pathSystem : dataset.pathUser)
        if (command === 'history' && a[0] === 'list') return ok(dataset.history)
        if (command === 'protection' && a[0] === 'list') return ok(dataset.protection)
        if (command === 'service' && a[0] === 'status') return ok(dataset.serviceStatus)
        if (command === 'service' && a[0] === 'health') return ok(dataset.serviceHealth)
        if (command === 'service' && a[0] === 'ping') return ok(dataset.servicePing)
        if (command === 'audit' && a[0] === 'list') return ok(dataset.audit)
        if (command === 'agents') return ok('')
        return null
      }
      exports.mockIPC((cmd: string, args: any) => {
        switch (cmd) {
          case 'run_cli': {
            const res = cli(args.command, args.args)
            if (res === null) {
              unmocked.push(`run_cli ${args.command} ${JSON.stringify(args.args ?? [])}`)
              return { success: false, data: null, error: 'unmocked command' }
            }
            return res
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
            unmocked.push(cmd)
            return null
        }
      })

      // Kill the 0.15s color transitions before first paint; screenshot-time
      // animation disabling alone would leave hover/theme fades mid-flight.
      const style = document.createElement('style')
      style.textContent = '*, *::before, *::after { transition: none !important; scroll-behavior: auto !important; }'
      document.documentElement.appendChild(style)
    },
    { mocksSource, dataset: mockDataset, settings },
  )
}

async function boot(page: Page, style: string, theme: Theme): Promise<void> {
  await injectMocks(page, {
    darkMode: theme === 'dark' ? 'true' : 'false',
    themeStyle: style,
    fontScale: '1',
    locale: 'en',
    autoLightweight: 'true',
    lightweightTimeout: '10',
  })
  await page.goto('/')
  await expect(page.locator('html')).toHaveAttribute('data-theme', theme)
  await expect(page.locator('html')).toHaveAttribute('data-theme-style', style)
}

async function settle(page: Page): Promise<void> {
  const unmocked = await page.evaluate(() => (window as any).__UNMOCKED_IPC__ as string[])
  expect(unmocked, `unmocked IPC calls: ${unmocked.join(', ')}`).toEqual([])
  // Park the pointer off any hoverable control so hover styles stay out of the shot.
  await page.mouse.move(0, 0)
  await page.evaluate(() => document.fonts.ready.then(() => true))
}

const screens: Array<{ id: string; open: (page: Page) => Promise<void> }> = [
  {
    id: 'variables',
    open: async (page) => {
      await expect(page.getByText('JAVA_HOME', { exact: true }).first()).toBeVisible()
    },
  },
  {
    id: 'settings',
    open: async (page) => {
      await page.getByRole('button', { name: 'Settings' }).click()
      await expect(page.locator('#settings-lang')).toBeVisible()
      // onMount awaits isCliInPath before anything else, so the settled toggle
      // means the dialog's async data has landed. The mock CLI dir is not in
      // the mocked user PATH, so the toggle reads false.
      await expect(page.getByRole('switch', { name: 'Add CLI to PATH' })).toHaveAttribute('aria-checked', 'false')
    },
  },
  {
    id: 'path-editor',
    open: async (page) => {
      await page.getByRole('tab', { name: 'Path Editor' }).click()
      await expect(page.getByText('C:\\Tools\\bin').first()).toBeVisible()
    },
  },
  {
    id: 'profiles',
    open: async (page) => {
      await page.getByRole('tab', { name: 'Profiles' }).click()
      await expect(page.getByText('dev-tools', { exact: true }).first()).toBeVisible()
    },
  },
  {
    id: 'protection',
    open: async (page) => {
      await page.getByRole('tab', { name: 'Protection' }).click()
      await expect(page.getByText('JAVA_HOME', { exact: true }).first()).toBeVisible()
    },
  },
  {
    id: 'history-import',
    open: async (page) => {
      await page.getByRole('tab', { name: 'History & Import' }).click()
      await expect(page.getByText('NODE_OPTIONS', { exact: true }).first()).toBeVisible()
    },
  },
  {
    id: 'service-manager',
    open: async (page) => {
      await page.getByRole('tab', { name: 'Service Manager' }).click()
      await expect(page.getByText('Running', { exact: true }).first()).toBeVisible()
    },
  },
  {
    id: 'audit-ledger',
    open: async (page) => {
      await page.getByRole('tab', { name: 'Audit Ledger' }).click()
      await expect(page.getByText('No audit records found')).toBeVisible()
    },
  },
]

const themeStyles = ['slate', 'blue', 'violet', 'rose', 'cyan', 'amber']

for (const theme of ['light', 'dark'] as const) {
  for (const screen of screens) {
    test(`${screen.id} slate ${theme}`, async ({ page }) => {
      await boot(page, 'slate', theme)
      await screen.open(page)
      await settle(page)
      await expect(page).toHaveScreenshot(`${screen.id}-slate-${theme}.png`)
    })
  }
}

for (const style of themeStyles) {
  for (const theme of ['light', 'dark'] as const) {
    test(`main table ${style} ${theme}`, async ({ page }) => {
      await boot(page, style, theme)
      await expect(page.getByText('JAVA_HOME', { exact: true }).first()).toBeVisible()
      await settle(page)
      await expect(page).toHaveScreenshot(`main-table-${style}-${theme}.png`)
    })
  }
}
