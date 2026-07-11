import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/svelte'
import App from './App.svelte'

describe('App.svelte', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('renders application title', async () => {
    render(App)
    const title = await screen.findByText(/Env Manager/i)
    expect(title).toBeTruthy()
  })

  it('has settings button', () => {
    render(App)
    const settingsBtn = screen.queryByLabelText(/settings/i)
    expect(settingsBtn).toBeTruthy()
  })
})
