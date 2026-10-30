// SurvivalKitTempFileAclTests.cs - the survival kit's plaintext temp file, read back from a real path
// License: Apache-2.0
//
// Why this exists: the kit used to be written straight into
// %TEMP%\envmanager-kit-<pid>.json with no DACL of its own, so its privacy was whatever
// %TEMP% inherits - C:\Windows\Temp grants BUILTIN\Users read. #182 moves the file into a
// directory that is born owner-only (SecretTempDirectory, the #178 primitive); this pins
// that on a real temp path instead of trusting the call. The assertions are structural on
// purpose: expected SIDs are derived from the current identity and the well-known SID
// types, never from SID text, so the test passes on a localized Windows as well as an
// English one.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using EnvManager;
using Xunit;

namespace EnvManager.Engine.Tests;

public sealed class SurvivalKitTempFileAclTests
{
    [Fact]
    public void Create_KitFileInheritsAnOwnerOnlyDaclInsteadOfTempInheritance()
    {
        (DirectoryInfo directory, string filePath) = SurvivalKitTempFile.Create();
        try
        {
            Assert.True(Directory.Exists(directory.FullName));
            Assert.StartsWith(Path.Combine(Path.GetTempPath(), "env-manager-kit-"), directory.FullName);
            // The documented sink shape survives - README and the architecture plaintext
            // table both address the file by this name; only its parent changed.
            Assert.Equal("envmanager-kit-" + Environment.ProcessId + ".json", Path.GetFileName(filePath));

            // The production path is WriteAtomicUtf8: it writes <path>.tmp.<pid> beside the
            // kit and renames it into place. That scratch name holds the same plaintext for
            // a moment, and SurvivalKitTempFile claims the directory covers it - so the
            // assertions below run against a file that arrived exactly that way, not against
            // one written by a shortcut the kit never uses. Known limit: WriteAtomicUtf8 is
            // a private member of Program, so this reproduces its scratch-name shape instead
            // of calling it - if that shape ever changes, the scratch-name half of this test
            // goes stale while staying green, and has to be updated with it.
            string scratchPath = filePath + ".tmp." + Environment.ProcessId;
            File.WriteAllText(scratchPath, "{\"mountId\":\"probe\"}");
            File.Move(scratchPath, filePath, overwrite: true);

            // Inheritance off on the directory: %TEMP%'s ACEs are not copied in, so the
            // file cannot pick up the BUILTIN\Users read C:\Windows\Temp grants.
            DirectorySecurity directorySecurity = directory.GetAccessControl();
            Assert.True(directorySecurity.AreAccessRulesProtected,
                "the kit directory DACL must be protected, otherwise %TEMP%'s inherited ACEs come back");

            List<FileSystemAccessRule> directoryRules = ReadRules(directorySecurity);
            Assert.Equal(ExpectedSids(), ActualSids(directoryRules));
            Assert.All(directoryRules, rule =>
            {
                Assert.False(rule.IsInherited);
                Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
                Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
                // Inheritable, so the kit file and the scratch name carry the same ACEs
                // instead of an empty DACL that would deny the writer too.
                Assert.True(rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit));
                Assert.True(rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit));
            });

            // The file's DACL is that same set, and every ACE on it is inherited from the
            // protected directory: an unprotected parent is the only way a file in %TEMP%
            // can carry %TEMP%'s grants, so this is the half that actually closes the leak.
            // The rights are asserted too - a DACL with the right identities but only Read
            // would satisfy the SID comparison while breaking the kit's own write.
            List<FileSystemAccessRule> fileRules = ReadRules(new FileInfo(filePath).GetAccessControl());
            Assert.Equal(ExpectedSids(), ActualSids(fileRules));
            Assert.All(fileRules, rule =>
            {
                Assert.True(rule.IsInherited);
                Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
                // Read *and* write, asserted by flag rather than by equality with
                // FullControl: an inherited ACE carries whatever bit pattern the directory
                // granted, and what must not happen is a kit file the writer can only read.
                Assert.True(rule.FileSystemRights.HasFlag(FileSystemRights.Read));
                Assert.True(rule.FileSystemRights.HasFlag(FileSystemRights.Write));
            });
        }
        finally
        {
            Delete(directory.FullName);
        }
    }

    [Fact]
    public void Create_OwnerStillReadsBackThePlaintextItWrote()
    {
        // Functional half of the guarantee: the tightened DACL must not lock out the
        // process that assembles and re-reads the kit.
        (DirectoryInfo directory, string filePath) = SurvivalKitTempFile.Create();
        try
        {
            File.WriteAllText(filePath, "{\"mountId\":\"probe\"}");
            Assert.Equal("{\"mountId\":\"probe\"}", File.ReadAllText(filePath));
        }
        finally
        {
            Delete(directory.FullName);
        }
    }

    private static List<FileSystemAccessRule> ReadRules(FileSystemSecurity security)
    {
        return security.GetAccessRules(true, true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();
    }

    private static List<string> ActualSids(List<FileSystemAccessRule> rules)
    {
        return rules
            .Select(rule => ((SecurityIdentifier)rule.IdentityReference).Value)
            .OrderBy(sid => sid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // Current identity + SYSTEM + Administrators, deduplicated: under LOCAL SYSTEM the
    // first two are one account, so the directory carries two ACEs there and three for an
    // ordinary user - the same set SecretTempDirectoryAclTests pins for the sops sink.
    private static List<string> ExpectedSids()
    {
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (WindowsIdentity current = WindowsIdentity.GetCurrent())
        {
            if (current.User != null)
                expected.Add(current.User.Value);
        }
        expected.Add(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null).Value);
        expected.Add(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null).Value);

        return expected
            .OrderBy(sid => sid, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void Delete(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch (IOException)
        {
            // a failed cleanup must not turn a DACL assertion green into a red run
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
