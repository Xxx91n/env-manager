import { defineConfig, devices } from '@playwright/test'

// Local-only screenshot baseline (see tests/screenshots/baseline.spec.ts).
// Deliberately separate from playwright.config.ts: baselines are Chromium- and
// machine-specific, so this suite must never run under the Firefox project or
// in CI. Run from frontend/: npx playwright test --config playwright.screenshots.config.ts
export default defineConfig({
  testDir: './tests/screenshots',
  fullyParallel: true,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 2,
  reporter: 'list',
  timeout: 60_000,
  expect: {
    timeout: 10_000,
    toHaveScreenshot: {
      // Playwright's default perceptual threshold. #306 originally picked 0, but
      // this machine's Chromium jitters rounded-border AA on fractional rem
      // offsets (13px root) by 1 color unit between runs, so threshold 0 can't
      // produce a stable repeat run. The owner chose 0.2: real theme/layout
      // changes are far above it, only sub-perceptual raster noise passes.
      threshold: 0.2,
      animations: 'disabled',
    },
  },
  use: {
    baseURL: 'http://localhost:5173',
  },
  projects: [
    {
      name: 'chromium',
      use: {
        ...devices['Desktop Chrome'],
        viewport: { width: 1280, height: 800 },
        deviceScaleFactor: 1,
        locale: 'en-US',
        timezoneId: 'UTC',
        colorScheme: 'light',
      },
    },
  ],
  webServer: {
    command: 'npm run dev',
    url: 'http://localhost:5173',
    reuseExistingServer: !process.env.CI,
    timeout: 120_000,
  },
})
