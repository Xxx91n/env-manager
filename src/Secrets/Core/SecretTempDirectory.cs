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

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.AccessControl;
using System.Security.Principal;

namespace EnvManager.Secrets.Core;

/// <summary>
/// Creates the temp directory a provider needs for file-in/file-out plaintext handoff and
/// restricts it: inheritance off, and only the current identity (SYSTEM for the service,
/// the invoking user for the CLI), SYSTEM and BUILTIN\Administrators granted. The grant is
/// inheritable, so files created inside inherit the same three ACEs instead of an empty
/// DACL.
/// </summary>
/// <remarks>
/// The directory exists with its inherited ACL for the two statements it takes to write
/// the DACL. Nothing the provider does observes that window - the provider helper (sops)
/// is started afterwards - and closing it entirely would mean creating the directory with
/// a security descriptor in the same call, which the managed API surface this project
/// already uses (GetAccessControl / SetAccessControl on a FileSystemInfo) does not offer.
/// </remarks>
internal static class SecretTempDirectory
{
    // Prefix shape the plaintext-lifecycle table documents (docs/architecture.md):
    // %TEMP%\env-manager-<provider>-<guid>.
    private const string Prefix = "env-manager-";

    /// <summary>
    /// Creates <c>%TEMP%\env-manager-&lt;provider&gt;-&lt;guid&gt;</c> and applies the
    /// owner-only DACL. Never throws because of the ACL: a failed restriction leaves the
    /// directory with the inherited ACL it would have had before this helper existed, and
    /// the failure is recorded on the debug channel (same best-effort contract as
    /// <c>Program.SetFileAclRestricted</c> for the audit file).
    /// </summary>
    internal static DirectoryInfo Create(string provider)
    {
        string path = Path.Combine(Path.GetTempPath(), Prefix + provider + "-" + Guid.NewGuid().ToString("N"));
        DirectoryInfo directory = Directory.CreateDirectory(path);
        RestrictToOwner(directory);
        return directory;
    }

    private static void RestrictToOwner(DirectoryInfo directory)
    {
        try
        {
            DirectorySecurity security = directory.GetAccessControl();
            // Inheritance off, inherited ACEs not copied: the DACL ends up holding only
            // the rules added below, whatever %TEMP% granted.
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

            foreach (IdentityReference identity in AllowedIdentities())
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    identity,
                    FileSystemRights.FullControl,
                    // Inheritable on purpose: secret.json / secret.dec.json are created
                    // inside this directory, and a non-inheritable ACE would leave them
                    // with an empty DACL - which denies everyone, sops included.
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            directory.SetAccessControl(security);
        }
        catch (Exception ex)
        {
            Program.DebugLog($"ACL restriction failed for {directory.FullName}: {ex.Message}");
        }
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
