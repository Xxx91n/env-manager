import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))
const wxs = readFileSync(join(__dirname2, '..', 'scripts', 'installer.wxs'), 'utf8')

// ADR-0010 / grill-plan-msi-residual-guid-v0928: string gates that must hold
// for the v0.9.28 MSI hygiene pass (RemoveFile wildcard residue wipe, explicit
// GUIDs on the three binaries, unconditional RemoveFolder on INSTALLDIR).
describe('installer.wxs v0.9.28 MSI residue / GUID hygiene invariants (ADR-0010)', () => {
  it('every INSTALLDIR file component has RemoveFile Name="*.*" On="uninstall"', () => {
    const fileComponents = [
      'GuiExecutable', 'CliExecutable', 'CliLibrary', 'CliDeps',
      'CliRuntime', 'AgentGuide', 'ServiceExecutable', 'WebViewLoader',
    ]
    for (const id of fileComponents) {
      const re = new RegExp(
        '<Component Id="' + id + '"[\\s\\S]*?<RemoveFile[^>]*Name="\\*\\.\\*"[^>]*On="uninstall"[\\s\\S]*?</Component>'
      )
      expect(wxs, id + ' missing RemoveFile wildcard').toMatch(re)
    }
  })

  it('exactly three components carry explicit pinned GUIDs (the three binaries)', () => {
    const explicitGuids = wxs.match(/<Component Id="[A-Za-z]+" Guid="[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}"/g) || []
    expect(explicitGuids.length).toBe(3)
    // And they must be on the binaries, not on shortcuts/registry/data components.
    expect(wxs).toMatch(/<Component Id="GuiExecutable" Guid="[0-9a-f-]{36}"/)
    expect(wxs).toMatch(/<Component Id="CliExecutable" Guid="[0-9a-f-]{36}"/)
    expect(wxs).toMatch(/<Component Id="ServiceExecutable" Guid="[0-9a-f-]{36}"/)
    // The shortcut/registry/data components must remain wildcard per Decision 2.
    expect(wxs).toMatch(/<Component Id="StartMenuShortcut" Guid="\*"/)
    expect(wxs).toMatch(/<Component Id="EnvManagerDataComponent" Guid="\*"/)
    expect(wxs).toMatch(/<Component Id="StopOldServiceOnUpgrade" Guid="\*"/)
  })

  it('pinned GUIDs are stable across rebuilds (immutable literal in wxs)', () => {
    // Hard-rule: once pinned, these GUIDs must not be regenerated. Lock the literals.
    expect(wxs).toMatch(/<Component Id="GuiExecutable" Guid="5138848f-6498-4896-805f-26a0004665c7"/)
    expect(wxs).toMatch(/<Component Id="CliExecutable" Guid="a690f1b0-3987-450e-a4c0-4d8f7f478f1f"/)
    expect(wxs).toMatch(/<Component Id="ServiceExecutable" Guid="e61585c6-4c5b-437c-be39-082f8b64bade"/)
  })

  it('INSTALLDIR RemoveFolder lives on a component rooted at INSTALLDIR (not EnvManagerDataComponent)', () => {
    // WiX RemoveFolder element: the effective Directory defaults to the parent
    // component's directory. Placing RemoveInstallDir inside EnvManagerDataComponent
    // (rooted at ProgramData\EnvManager) silently produces NO FolderRemove op for
    // INSTALLDIR on uninstall -- observed as residual INSTALLDIR in v0.9.30 smoke
    // test. ADR-0012 amendment: the RemoveFolder must live on the UninstallShortcut
    // component, which is rooted at INSTALLDIR via <DirectoryRef Id="INSTALLDIR">.
    const uninstallShortcutBody = wxs.match(/<Component Id="UninstallShortcut"[\s\S]*?<\/Component>/)
    expect(uninstallShortcutBody).not.toBeNull()
    expect(uninstallShortcutBody![0]).toMatch(/<RemoveFolder Id="RemoveInstallDir"[^>]*On="uninstall"[^>]*\/>/)
    // And it must NOT carry an explicit Directory= attribute (default = INSTALLDIR via the component)
    expect(uninstallShortcutBody![0]).toMatch(/<RemoveFolder Id="RemoveInstallDir"(?![^>]*\bDirectory=)[^>]*\/>/)
    // StopOldServiceOnUpgrade (the conditional component) must NOT own RemoveInstallDir.
    const stopOldBody = wxs.match(/<Component Id="StopOldServiceOnUpgrade"[\s\S]*?<\/Component>/)
    expect(stopOldBody).not.toBeNull()
    expect(stopOldBody![0]).not.toMatch(/<RemoveFolder Id="RemoveInstallDir"/)
    // EnvManagerDataComponent also must NOT own RemoveInstallDir anymore.
    const dataDirBody = wxs.match(/<Component Id="EnvManagerDataComponent"[\s\S]*?<\/Component>/)
    expect(dataDirBody).not.toBeNull()
    expect(dataDirBody![0]).not.toMatch(/<RemoveFolder Id="RemoveInstallDir"/)
  })

  it('no util:RemoveFolderEx anywhere (CVE-2024-29188 junction traversal)', () => {
    expect(wxs).not.toMatch(/util:RemoveFolderEx/)
  })

  it('version bumped to 0.9.28 or later in csproj and package.json', () => {
    const csproj = readFileSync(join(__dirname2, '..', '..', 'env-manager.csproj'), 'utf8')
    const pkg = JSON.parse(readFileSync(join(__dirname2, '..', 'package.json'), 'utf8'))
    // Allow >= 0.9.28 so later patch versions don't re-trip this assertion.
    const csMatch = csproj.match(/<Version>(\d+)\.(\d+)\.(\d+)<\/Version>/)
    expect(csMatch).not.toBeNull()
    const [mj, mn, pt] = [Number(csMatch![1]), Number(csMatch![2]), Number(csMatch![3])]
    const versionOk = mj > 0 || (mj === 0 && mn > 9) || (mj === 0 && mn === 9 && pt >= 28)
    expect(versionOk).toBe(true)
    expect(pkg.version).toBe(`${mj}.${mn}.${pt}`)
  })
})
