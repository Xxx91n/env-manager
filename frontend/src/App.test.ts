import { describe, it, expect, beforeEach } from 'vitest'
import { render, screen } from '@testing-library/svelte'
import App from './App.svelte'

describe('App.svelte', () => {
  beforeEach(() => {
    localStorage.clear()
  })

  it('renders application title', async () => {
    render(App)
    const title = await screen.findByText(/Env Manager|环境变量管理器/)
    expect(title).toBeTruthy()
  })

  it('has language switcher buttons', () => {
    render(App)
    const enButton = screen.queryByText('EN')
    const zhButton = screen.queryByText('ZH')
    expect(enButton || zhButton).toBeTruthy()
  })
})
