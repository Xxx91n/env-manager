import { describe, it, expect, beforeEach, vi } from 'vitest'
import { render, screen, waitFor } from '@testing-library/svelte'
import { invoke } from '@tauri-apps/api/core'
import App from './App.svelte'

// Cast the mocked invoke so we can configure its return value per test.
const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('App.svelte', () => {
  beforeEach(() => {
    localStorage.clear()
    vi.clearAllMocks()
  })

  it('renders h1 element for title', async () => {
    // Under the svelte-i18n mock, $t returns the key string, so we check
    // for the h1 element rather than translated text.
    mockInvoke.mockResolvedValue({
      success: true,
      data: JSON.stringify([
        { name: 'PATH', value: 'C:\\Windows', scope: 'user' },
      ]),
      error: null,
    })

    render(App)

    await waitFor(() => {
      const h1 = document.querySelector('h1')
      expect(h1).toBeTruthy()
      expect(h1?.textContent).toBeTruthy()
    })
  })

  it('has settings button with aria-label', async () => {
    mockInvoke.mockResolvedValue({
      success: true,
      data: '[]',
      error: null,
    })

    render(App)

    await waitFor(() => {
      const btn = document.querySelector('[aria-label]')
      expect(btn).toBeTruthy()
    })
  })

  it('renders navigation tabs', async () => {
    mockInvoke.mockResolvedValue({
      success: true,
      data: '[]',
      error: null,
    })

    render(App)

    // The nav should contain Variables, Profiles, and Path buttons.
    await waitFor(() => {
      const nav = document.querySelector('nav')
      expect(nav).toBeTruthy()
      const buttons = nav?.querySelectorAll('button')
      expect(buttons?.length).toBeGreaterThanOrEqual(3)
    })
  })
})
