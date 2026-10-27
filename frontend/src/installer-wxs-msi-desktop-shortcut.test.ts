import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))
const wxs = readFileSync(join(__dirname2, '..', 'scripts', 'installer.wxs'), 'utf8')
const loc = readFileSync(join(__dirname2, '..', 'scripts', 'EnvManager_en-US.wxl'), 'utf8')
const stockLoc = readFileSync(join(__dirname2, '..', 'scripts', 'WixUI_en-US.wxl'), 'utf8')
const buildScript = readFileSync(join(__dirname2, '..', '..', 'scripts', 'build.mjs'), 'utf8')

// MSI Optional Settings page: a desktop-shortcut check box wired to the existing
// public INSTALLDESKTOPSHORTCUT property without forking the WixUI dialog set.
describe('installer.wxs desktop shortcut options page', () => {
  it('links a project UI fragment next to the built-in WixUI set', () => {
    expect(wxs).toContain('<UIRef Id="EnvManagerShortcutUI" />')
    expect(wxs).toMatch(/<UI Id="EnvManagerShortcutUI">/)
  })

  it('renders a check box bound to INSTALLDESKTOPSHORTCUT on the options page', () => {
    expect(wxs).toMatch(/<Dialog Id="DesktopShortcutDlg"/)
    const controlMatch = wxs.match(/<Control Id="DesktopShortcutCheckBox"[\s\S]*?\/>/)
    expect(controlMatch).not.toBeNull()
    const control = controlMatch![0]
    expect(control).toContain('Type="CheckBox"')
    expect(control).toContain('Property="INSTALLDESKTOPSHORTCUT"')
    // Without CheckBoxValue the checked state would reuse the property's old value.
    expect(control).toContain('CheckBoxValue="1"')
  })

  it('opens after the built-in install-dir transition so its NewDialog wins', () => {
    // WixUI_InstallDir publishes InstallDirDlg -> VerifyReadyDlg at Order 4 and the
    // last NewDialog in ascending order is the one displayed.
    const entry = wxs.match(/<Publish Dialog="InstallDirDlg" Control="Next" Event="NewDialog" Value="DesktopShortcutDlg" Order="(\d+)">/)
    expect(entry).not.toBeNull()
    expect(Number(entry![1])).toBeGreaterThan(4)
  })

  it('normalizes a command-line "0" before the check box is created', () => {
    const clear = wxs.match(/<Publish Dialog="InstallDirDlg" Control="Next" Property="INSTALLDESKTOPSHORTCUT" Value="\[WIXUI_EMPTY\]" Order="(\d+)">INSTALLDESKTOPSHORTCUT = "0"<\/Publish>/)
    expect(clear).not.toBeNull()
    const entry = wxs.match(/<Publish Dialog="InstallDirDlg" Control="Next" Event="NewDialog" Value="DesktopShortcutDlg" Order="(\d+)">/)
    expect(entry).not.toBeNull()
    // An undefined property formats to an empty string, clearing the value; MSI
    // would otherwise draw any non-empty property as checked.
    expect(Number(clear![1])).toBeLessThan(Number(entry![1]))
  })

  it('returns to the install-dir page and continues to VerifyReadyDlg', () => {
    expect(wxs).toMatch(/<Publish Dialog="DesktopShortcutDlg" Control="Back" Event="NewDialog" Value="InstallDirDlg"/)
    expect(wxs).toMatch(/<Publish Dialog="DesktopShortcutDlg" Control="Next" Event="NewDialog" Value="VerifyReadyDlg"/)
  })

  it('routes VerifyReadyDlg Back through the options page on first install', () => {
    // The stock VerifyReadyDlg Back transitions to InstallDirDlg at Order 1.
    const back = wxs.match(/<Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="DesktopShortcutDlg" Order="(\d+)">NOT Installed<\/Publish>/)
    expect(back).not.toBeNull()
    expect(Number(back![1])).toBeGreaterThan(1)
  })

  it('keeps the default install behavior gated by the same property', () => {
    expect(wxs).toMatch(/<Property Id="INSTALLDESKTOPSHORTCUT" Value="1" Secure="yes"/)
    expect(wxs).toContain('<Condition>INSTALLDESKTOPSHORTCUT = "1"</Condition>')
  })

  it('defines every localization id the options page uses', () => {
    const dialog = wxs.match(/<Dialog Id="DesktopShortcutDlg"[\s\S]*?<\/Dialog>/)
    expect(dialog).not.toBeNull()
    const ids = [...dialog![0].matchAll(/!\(loc\.([A-Za-z0-9_]+)\)/g)].map((m) => m[1])
    expect(ids).toContain('DesktopShortcutDlgCheckBox')
    for (const id of ids) {
      // Stock WixUI ids stay in the vendored catalog; project strings live in ours.
      expect(loc.includes(`Id="${id}"`) || stockLoc.includes(`Id="${id}"`)).toBe(true)
    }
    for (const id of ids.filter((candidate) => candidate.startsWith('DesktopShortcutDlg'))) {
      expect(loc).toContain(`Id="${id}"`)
    }
  })

  it('passes the project localization file to light.exe', () => {
    expect(buildScript).toContain('EnvManager_en-US.wxl')
  })
})
