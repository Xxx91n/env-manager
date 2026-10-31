// SecretTempDirectory.cs - provider temp directory created with an owner-only DACL
// License: Apache-2.0
//
// Why this exists: SopsProvider writes plaintext (secret.json / secret.dec.json) into
// %TEMP%\env-manager-sops-<guid>\. Until now the directory inherited its DACL from
// %TEMP%, so the plaintext was only as private as that inherited ACL. On the service
// identity's default temp path (C:\Windows\SystemTemp: SYSTEM + Administrators, no Users
// ACE) that happened to be tight; on C:\Windows\Temp the inheritance carries
// BUILTIN\Users:(I)(RX) plus (OI)(CI)(IO)(GR,GE), so any local user could read the file.
// Nothing in the code expressed that dependency and no test pinned it.
//
// Placement: Secrets.Core, not Program's NativeMethods. The file-only
// SetFileAclRestricted is a private member of the CLI's Program class and takes a
// FileInfo; the secrets adapters are a bounded context that consumes Secrets.Core /
// Secrets.Manager, so the directory counterpart belongs on the secrets surface rather
// than behind the CLI command surface.
//
// Seam (#184): Create has a second overload that takes the DACL-bearing create call and
// the degradation channel as parameters. It exists because the failure branch - the half
// of this boundary that decides whether a rejected DACL costs the rotation or only the
// ACL - was reachable by nothing but a reviewer's attention; the parameterless overload
// stays the production path and keeps the call sequence #178 landed.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EnvManager.Secrets.Core;

/// <summary>
/// Creates the temp directory a provider needs for file-in/file-out plaintext handoff and
/// restricts it in the same call that creates it: inheritance off, and only the current
/// identity (SYSTEM for the service, the invoking user for the CLI), SYSTEM and
/// BUILTIN\Administrators granted. The grant is inheritable, so files created inside
/// inherit the same three ACEs instead of an empty DACL.
/// </summary>
/// <remarks>
/// <c>Directory.CreateDirectory(string, DirectorySecurity)</c> does not exist on this
/// target framework, so the security descriptor goes in through
/// <c>FileSystemAclExtensions.CreateDirectory(DirectorySecurity, string)</c> - the
/// overload passes it as <c>SECURITY_ATTRIBUTES</c>, which means the directory never
/// exists with the inherited ACL, not even for one statement.
/// </remarks>
internal static class SecretTempDirectory
{
    // Prefix shape the plaintext-lifecycle table documents (docs/architecture.md):
    // %TEMP%\env-manager-<provider>-<guid>.
    private const string Prefix = "env-manager-";

    /// <summary>
    /// Creates <c>%TEMP%\env-manager-&lt;provider&gt;-&lt;guid&gt;</c> with the owner-only
    /// DACL already applied. A DACL that cannot be applied is best-effort: the directory is
    /// then created with the inherited <c>%TEMP%</c> ACL it would have had before this
    /// boundary existed, and the degradation is recorded on the same debug channel
    /// <c>SetFileAclRestricted</c> uses for the audit file. Only a directory that cannot be
    /// created at all reaches the caller as an exception.
    /// </summary>
    internal static DirectoryInfo Create(string provider)
        => Create(provider, CreateWithDacl, RecordDegradation);

    // Seam overload: createWithDacl is the create call that carries the DACL, and
    // recordDegradation receives the line AFTER ScrubExceptionMessage has run, so a test
    // observes exactly what production would have recorded and cannot bypass
    // ScrubExceptionMessage.
    //
    // Why a parameter seam rather than the alternatives:
    //
    // * Not a settable static hook (the shape RegistryScope.DebugSink uses): that hook is
    //   wired once by Main for the whole process, while xunit runs test classes in
    //   parallel - two tests would race on one field. A parameter scopes the substitute
    //   to the single call under test.
    // * Not an interface plus a constructor dependency: this is a static helper reached
    //   from a private method on SopsProvider, and the CLI has no composition root to
    //   thread one through. Wiring it in would change the production call sequence this
    //   boundary exists to keep.
    // * The inherited-ACL fallback stays a real Directory.CreateDirectory call and is
    //   deliberately NOT part of the seam: what the ticket needs pinned is that the
    //   provider gets a usable directory back, and a stubbed fallback would let that
    //   assertion pass on a promise instead of on a directory that exists.
    internal static DirectoryInfo Create(
        string provider,
        Func<string, DirectorySecurity, DirectoryInfo> createWithDacl,
        Action<string> recordDegradation)
    {
        string path = Path.Combine(Path.GetTempPath(), Prefix + provider + "-" + Guid.NewGuid().ToString("N"));

        try
        {
            return createWithDacl(path, OwnerOnlyDacl());
        }
        catch (Exception ex)
        {
            // The create call carried the DACL, so a failure here means the DACL was
            // rejected. Fall back to the documented behaviour instead of failing the
            // rotation: create the directory without a security descriptor (inherited ACL)
            // and record what was lost. A failure of THIS call is a real creation failure
            // - no temp directory, no sops - and is left to propagate.
            recordDegradation(Program.ScrubExceptionMessage($"ACL restriction failed for {path}: {ex.Message}"));
            return Directory.CreateDirectory(path);
        }
    }

    // Production delegates, i.e. the two calls #178 landed: the security descriptor goes
    // in as SECURITY_ATTRIBUTES on the create call, and the degradation is recorded on the
    // --debug channel through the scrubber.
    //
    // Residual gap: the success-path tests pin that Create(provider) still passes
    // CreateWithDacl - a directory carrying %TEMP%'s inherited ACL fails them - but
    // nothing pins that it passes RecordDegradation. Swapping that one delegate for a
    // no-op would silence every degradation with the suite still green, so the binding is
    // covered by review rather than by a test.
    private static DirectoryInfo CreateWithDacl(string path, DirectorySecurity security)
        => security.CreateDirectory(path);

    private static void RecordDegradation(string message)
        => Program.DebugLog(message);

    private static DirectorySecurity OwnerOnlyDacl()
    {
        var security = new DirectorySecurity();
        // Rules first, protection second: adding a rule is what materialises a present
        // DACL, and only a present DACL can be marked protected. The other order risks
        // handing CreateDirectory a security descriptor with a null DACL, which grants
        // everyone full control.
        foreach (IdentityReference identity in AllowedIdentities())
        {
            security.AddAccessRule(new FileSystemAccessRule(
                identity,
                FileSystemRights.FullControl,
                // Inheritable on purpose: secret.json / secret.dec.json are created
                // inside this directory, and a non-inheritable ACE would leave them with
                // an empty DACL - which denies everyone, sops included.
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                PropagationFlags.None,
                AccessControlType.Allow));
        }
        // Inheritance off, inherited ACEs not copied: the DACL holds only the rules above,
        // whatever %TEMP% grants.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        return security;
    }

    /// <summary>
    /// Current identity plus SYSTEM plus Administrators, deduplicated by SID: under
    /// LOCAL SYSTEM the first two are the same account, and a duplicate ACE would be a
    /// second grant of rights already granted.
    /// </summary>
    private static List<IdentityReference> AllowedIdentities()
    {
        var identities = new List<IdentityReference>(3);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using (WindowsIdentity current = WindowsIdentity.GetCurrent())
        {
            SecurityIdentifier? user = current.User;
            if (user != null && seen.Add(user.Value))
                identities.Add(user);
        }

        AddWellKnown(identities, seen, WellKnownSidType.LocalSystemSid);
        AddWellKnown(identities, seen, WellKnownSidType.BuiltinAdministratorsSid);
        return identities;
    }

    private static void AddWellKnown(List<IdentityReference> identities, HashSet<string> seen, WellKnownSidType sidType)
    {
        var sid = new SecurityIdentifier(sidType, null);
        if (seen.Add(sid.Value))
            identities.Add(sid);
    }
}
