import { test, expect } from '@playwright/test'

test.describe('Env Manager GUI', () => {
  test.beforeEach(async ({ page }) => {
    await page.goto('http://localhost:5173')
  })

  test('should display app title', async ({ page }) => {
    const title = page.locator('h1')
    await expect(title).toContainText('Env Manager')
  })

  test('should have settings button', async ({ page }) => {
    const settingsBtn = page.locator('[aria-label="Settings"], [aria-label="设置"]')
    await expect(settingsBtn).toBeVisible()
  })

  test('should open settings dialog and show language selector', async ({ page }) => {
    const settingsBtn = page.locator('[aria-label="Settings"], [aria-label="设置"]')
    await settingsBtn.click()
    const langSelect = page.locator('#settings-lang')
    await expect(langSelect).toBeVisible()
  })

  test('should list environment variables or show empty state', async ({ page }) => {
    // Wait for either the table or the empty state
    await page.waitForTimeout(2000)
    const table = page.locator('table')
    const emptyState = page.locator('text=No variables found').or(page.locator('text=没有找到'))
    // At least one should be present
    await expect(table.or(emptyState).first()).toBeVisible({ timeout: 10000 })
  })
})
