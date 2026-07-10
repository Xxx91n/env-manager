import { test, expect } from '@playwright/test'

test.describe('Env Manager GUI', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:5173')
  })

  test('should display app title', async ({ page }) => {
    const title = page.locator('h1')
    await expect(title).toContainText('Env Manager')
  })

  test('should have language switcher', async ({ page }) => {
    const enButton = page.locator('button:has-text("EN")')
    const zhButton = page.locator('button:has-text("ZH")')
    await expect(enButton).toBeVisible()
    await expect(zhButton).toBeVisible()
  })

  test('should switch language to Chinese', async ({ page }) => {
    const zhButton = page.locator('button:has-text("ZH")')
    await zhButton.click()
    await page.waitForTimeout(500)
    const title = page.locator('h1')
    await expect(title).toContainText('环境变量管理器')
  })

  test('should list environment variables', async ({ page }) => {
    // Wait for variables to load
    await page.waitForSelector('table', { timeout: 5000 })
    const table = page.locator('table')
    await expect(table).toBeVisible()
  })
})
