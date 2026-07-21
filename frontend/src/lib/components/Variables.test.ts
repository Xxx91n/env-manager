import { describe, expect, it, vi } from 'vitest'
import { render, screen } from '@testing-library/svelte'
import Variables from './Variables.svelte'
import { variables, isWriteInProgress } from '../stores'
import { invoke } from '@tauri-apps/api/core'

const mockInvoke = invoke as unknown as ReturnType<typeof vi.fn>

describe('Variables protection controls', () => {
  it('disables the toggle, edit, and delete controls for a protected variable', () => {
    variables.set([{ name: 'LOCKED_VAR', value: 'safe', scope: 'user', isProtected: true }])
    isWriteInProgress.set(false)
    render(Variables)
    expect((screen.getByRole('switch') as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('buttons.edit') as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('buttons.delete') as HTMLButtonElement).disabled).toBe(true)
  })

  it('keeps a disabled variable visible and re-enableable', () => {
    variables.set([{ name: 'DISABLED_VAR', value: 'safe', scope: 'user', isDisabled: true }])
    render(Variables)
    const toggle = screen.getByRole('switch') as HTMLButtonElement
    expect(toggle.disabled).toBe(false)
    expect(toggle.getAttribute('aria-checked')).toBe('false')
  })

  it('disables all mutation controls for a built-in protected variable', () => {
    variables.set([{ name: 'SYSTEM_LOCKED', value: 'safe', scope: 'system', isProtected: true, isBuiltinProtected: true }])
    render(Variables)
    expect((screen.getByRole('switch') as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('buttons.edit') as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByLabelText('buttons.delete') as HTMLButtonElement).disabled).toBe(true)
  })

  it('does not invoke the CLI when a protected toggle is clicked programmatically', async () => {
    variables.set([{ name: 'LOCKED_VAR', value: 'safe', scope: 'user', isProtected: true }])
    render(Variables)
    ;(screen.getByRole('switch') as HTMLButtonElement).click()
    await Promise.resolve()
    expect(mockInvoke).not.toHaveBeenCalled()
  })
})
