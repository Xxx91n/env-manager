import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))
const wxs = readFileSync(join(__dirname2, '..', 'scripts', 'installer.wxs'), 'utf8')

// ADR-0012 / grill-plan-msi-uninstall-shortcut-logs: uninstall shortcut in INSTALLDIR
// + legacy INSTALLDIR\logs residue cleanup.
describe('installer.wxs v0.9.30 MSI uninstall + logs externalization', () => {
  it('declares UninstallShortcut component targeting msiexec /x [ProductCode]', () => {
    expect(wxs).toMatch(/<Component Id="UninstallShortcut"/)
    expect(wxs).toMatch(/<Shortcut Id="UninstallShortcutLnk"[^/]*Target="\[System64Folder\]msiexec\.exe"/)
    expect(wxs).toMatch(/Arguments="\/x \[ProductCode\]"/)
  })

  it('uninstall shortcut carries RegistryValue KeyPath for ICE compliance', () => {
    // Non-advertised shortcuts require a KeyPath RegistryValue in the same component.
    const compMatch = wxs.match(/<Component Id="UninstallShortcut"[\s\S]*?<\/Component>/)
    expect(compMatch).not.toBeNull()
    const body = compMatch![0]
    expect(body).toMatch(/<RegistryValue[^/]*Key="Software\\EnvManager"[^/]*Name="UninstallShortcut"[^/]*KeyPath="yes"/)
  })

  it('declares LegacyLogsCleanup component for [INSTALLDIR]\\logs residue', () => {
    expect(wxs).toMatch(/<Component Id="LegacyLogsCleanup"/)
    const compMatch = wxs.match(/<Component Id="LegacyLogsCleanup"[\s\S]*?<\/Component>/)
    const body = compMatch![0]
    expect(body).toMatch(/<RemoveFile[^/]*Name="\*\.log\*"[^/]*On="uninstall"/)
    expect(body).toMatch(/<RemoveFolder[^/]*On="uninstall"/)
  })

  it('LogsDir directory is nested under INSTALLDIR (both arch branches)', () => {
    const matches = wxs.match(/<Directory Id="LogsDir" Name="logs" \/>/g)
    expect(matches).not.toBeNull()
    expect(matches!.length).toBeGreaterThanOrEqual(2)
  })

  it('Feature references both UninstallShortcut and LegacyLogsCleanup', () => {
    expect(wxs).toContain('<ComponentRef Id="UninstallShortcut" />')
    expect(wxs).toContain('<ComponentRef Id="LegacyLogsCleanup" />')
  })

  it('uninstall shortcut named "Uninstall Env Manager"', () => {
    expect(wxs).toContain('Name="Uninstall Env Manager"')
  })

  it('RemoveInstallDir lives on a component rooted at INSTALLDIR (regression v0.9.30)', () => {
    // Smoke-test evidence: v0.9.30 initial wiring put RemoveInstallDir inside
    // EnvManagerDataComponent (rooted at ProgramData\EnvManager). The RemoveFolder's
    // effective Directory defaulted to that component's directory, NOT INSTALLDIR,
    // so uninstall left an empty INSTALLDIR behind. Fix: move to UninstallShortcut
    // component (rooted at INSTALLDIR via <DirectoryRef Id="INSTALLDIR">) and let
    // RemoveFolder default its Directory to that component's directory.
    const uninstallShortcutBody = wxs.match(/<Component Id="UninstallShortcut"[\s\S]*?<\/Component>/)
    expect(uninstallShortcutBody).not.toBeNull()
    expect(uninstallShortcutBody![0]).toMatch(/<RemoveFolder Id="RemoveInstallDir"/)

    const dataComponentBody = wxs.match(/<Component Id="EnvManagerDataComponent"[\s\S]*?<\/Component>/)
    expect(dataComponentBody).not.toBeNull()
    expect(dataComponentBody![0]).not.toMatch(/<RemoveFolder Id="RemoveInstallDir"/)
  })
})
