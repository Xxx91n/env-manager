import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))
const wxs = readFileSync(join(__dirname2, '..', 'scripts', 'installer.wxs'), 'utf8')

// ADR-0011 / grill-plan-msi-ui-v0929: WixUI_InstallDir UI + ARPPRODUCTICON + desktop shortcut gates.
describe('installer.wxs MSI UI (v0.9.29)', () => {
  it('declares WIXUI_INSTALLDIR bound to INSTALLDIR (browse-dialog data flow)', () => {
    expect(wxs).toMatch(/<Property Id="WIXUI_INSTALLDIR" Value="INSTALLDIR"/)
  })
  it('references WixUI_InstallDir built-in dialog set', () => {
    expect(wxs).toContain('<UIRef Id="WixUI_InstallDir" />')
    expect(wxs).toContain('<UIRef Id="WixUI_ErrorProgressText" />')
  })
  it('sets ARPPRODUCTICON for Add/Remove Programs icon', () => {
    expect(wxs).toContain('<Icon Id="ProductIcon" SourceFile="$(var.IconPath)" />')
    expect(wxs).toContain('<Property Id="ARPPRODUCTICON" Value="ProductIcon" />')
  })
  it('declares secure public INSTALLDESKTOPSHORTCUT property defaulting to 1', () => {
    expect(wxs).toMatch(/<Property Id="INSTALLDESKTOPSHORTCUT" Value="1" Secure="yes"/)
  })
  it('gates DesktopShortcut component on INSTALLDESKTOPSHORTCUT condition', () => {
    expect(wxs).toContain('<Component Id="DesktopShortcut"')
    expect(wxs).toContain('<Condition>INSTALLDESKTOPSHORTCUT = "1"</Condition>')
    expect(wxs).toContain('<Shortcut Id="DesktopShortcut"')
  })
  it('includes DesktopShortcut in MainFeature', () => {
    expect(wxs).toContain('<ComponentRef Id="DesktopShortcut" />')
  })
  it('does not fork the WixUI dialog set (uses built-in WixUI_InstallDir only)', () => {
    expect(wxs).not.toMatch(/<UI Id=/)
    expect(wxs).not.toContain('WixUILicenseRtf')
  })
})
