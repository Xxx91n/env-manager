/**
 * Vitest global setup.
 *
 * Provides a minimal mock for @tauri-apps/api/core so that components
 * importing `invoke` can render in jsdom without a running Tauri backend.
 * Also mocks svelte-i18n to avoid intl-messageformat ESM resolution issues.
 */
import { vi, beforeEach } from 'vitest'

// Mock Tauri invoke - returns a default empty success response.
vi.mock('@tauri-apps/api/core', () => ({
  invoke: vi.fn().mockResolvedValue({
    success: true,
    data: '[]',
    error: null,
  }),
}))

// Mock svelte-i18n to avoid intl-messageformat ESM resolution issues under vitest.
const mockLocaleStore = { subscribe: vi.fn((cb) => { cb('en'); return () => {} }), set: vi.fn() }
vi.mock('svelte-i18n', () => ({
  register: vi.fn(),
  init: vi.fn(),
  getLocaleFromNavigator: vi.fn(() => null),
  addMessages: vi.fn(),
  locale: mockLocaleStore,
  _: { subscribe: vi.fn((cb) => { cb({}); return () => {} }) },
  t: { subscribe: vi.fn((cb) => { cb((key: string) => key); return () => {} }) },
}))

// Reset localStorage and all mock call counts before each test.
beforeEach(() => {
  if (typeof localStorage !== 'undefined') {
    localStorage.clear()
  }
  vi.clearAllMocks()
})
