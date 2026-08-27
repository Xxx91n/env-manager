import { describe, it, expect } from 'vitest'
import { readFileSync } from 'fs'
import { join, dirname } from 'path'
import { fileURLToPath } from 'url'

const __dirname2 = typeof __dirname !== 'undefined' ? __dirname : dirname(fileURLToPath(import.meta.url))
const wxs = readFileSync(join(__dirname2, '..', 'scripts', 'installer.wxs'), 'utf8')

// ADR-0009 / grill-plan-msi-wix-v3-fix-v0927: string-gate invariants that must
// hold for the WiX v3 installer to not re-create the v0.9.26 silent-install hang.
describe('installer.wxs v0.9.27 MSI hang-fix invariants (ADR-0009)', () => {
  it('MajorUpgrade uses Schedule="afterInstallExecute" (remove old product BEFORE new-service start)', () => {
    expect(wxs).toMatch(/<MajorUpgrade\s+Schedule="afterInstallExecute"/)
  })

  it('uses WiX UtilExtension namespace for declarative service failure actions', () => {
    expect(wxs).toMatch(/xmlns:util="http:\/\/schemas\.microsoft\.com\/wix\/UtilExtension"/)
    expect(wxs).toMatch(/<util:ServiceConfig[\s\S]*FirstFailureActionType="restart"/)
  })

  it('no sc.exe deferred custom actions remain (replaced by util:ServiceConfig)', () => {
    expect(wxs).not.toMatch(/SetServiceDelayedAutoStart/)
    expect(wxs).not.toMatch(/SetServiceFailureActions/)
    expect(wxs).not.toMatch(/ExeCommand="[^"]*sc\.exe/)
  })

  it('ServiceControl does not block install on service start (Start=install forbidden)', () => {
    // Any <ServiceControl ... Start="install" ...> re-creates the 30s SCM handshake hang.
    expect(wxs).not.toMatch(/<ServiceControl[^>]*Start="install"/)
  })

  it('ServiceControl is Wait="no" to keep msiexec silent install non-blocking', () => {
    expect(wxs).toMatch(/<ServiceControl[^>]*Wait="no"/)
  })

  it('WIX_UPGRADE_DETECTED conditional StopOldServiceOnUpgrade component present', () => {
    expect(wxs).toMatch(/<Component\s+Id="StopOldServiceOnUpgrade"/)
    expect(wxs).toMatch(/<Condition>\s*WIX_UPGRADE_DETECTED\s*<\/Condition>/)
    expect(wxs).toMatch(/<ComponentRef\s+Id="StopOldServiceOnUpgrade"\s*\/>/)
  })

  it('util:ServiceConfig nested inside ServiceInstall (failure actions bound to the installed service)', () => {
    expect(wxs).toMatch(/<ServiceInstall[\s\S]*?<util:ServiceConfig[\s\S]*?\/[\s\S]*?<\/ServiceInstall>/)
  })
})
