// SecretTempDirectoryAclTests.cs - the sops temp directory's DACL, read back from a real directory
// License: Apache-2.0
//
// Why this exists: SopsProvider used to create %TEMP%\env-manager-sops-<guid> with
// Directory.CreateDirectory and let the inherited ACL decide who could read the plaintext
// written inside it. The inherited ACL is not a boundary - C:\Windows\Temp grants
// BUILTIN\Users read - so the guarantee has to be asserted on the directory itself.
// The assertions are structural on purpose: expected SIDs are derived from the current
// identity and the well-known SID types, never from SID text, so the test passes on a
// localized Windows as well as an English one.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using EnvManager.Secrets.Core;
using Xunit;

namespace EnvManager.Engine.Tests;

public sealed class SecretTempDirectoryAclTests
{
    [Fact]
    public void Create_LeavesInheritanceOffAndOnlyTheThreeExpectedAces()
    {
        DirectoryInfo directory = SecretTempDirectory.Create("sops");
        try
        {
            Assert.StartsWith(Path.Combine(Path.GetTempPath(), "env-manager-sops-"), directory.FullName);
            Assert.True(Directory.Exists(directory.FullName));

            DirectorySecurity security = directory.GetAccessControl();
            Assert.True(security.AreAccessRulesProtected,
                "the directory DACL must be protected, otherwise %TEMP%'s inherited ACEs come back");

            List<FileSystemAccessRule> rules = ReadRules(security);
            Assert.Equal(ExpectedSids(), ActualSids(rules));

            foreach (FileSystemAccessRule rule in rules)
            {
                Assert.False(rule.IsInherited);
                Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
                Assert.Equal(FileSystemRights.FullControl, rule.FileSystemRights);
                // Inheritable, so the plaintext files created inside carry the same ACL
                // instead of an empty DACL.
                Assert.True(rule.InheritanceFlags.HasFlag(InheritanceFlags.ContainerInherit));
                Assert.True(rule.InheritanceFlags.HasFlag(InheritanceFlags.ObjectInherit));
            }
        }
        finally
        {
            Delete(directory.FullName);
        }
    }

    [Fact]
    public void Create_FilesWrittenInsideInheritTheRestrictedAclAndStayReadableToTheOwner()
    {
        // Functional half of the guarantee: the tightened DACL must not lock out the
        // process that writes and reads the plaintext - the same identity sops runs as.
        DirectoryInfo directory = SecretTempDirectory.Create("sops");
        try
        {
            string plainFile = Path.Combine(directory.FullName, "secret.json");
            File.WriteAllText(plainFile, "{\"value\":\"probe\"}");
            Assert.Equal("{\"value\":\"probe\"}", File.ReadAllText(plainFile));

            List<FileSystemAccessRule> rules = ReadRules(new FileInfo(plainFile).GetAccessControl());
            Assert.Equal(ExpectedSids(), ActualSids(rules));
            // Inherited from the directory, not re-granted on the file: this is what keeps
            // the plaintext covered by the directory's DACL rather than by its own.
            Assert.All(rules, rule => Assert.True(rule.IsInherited));
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
    // first two are one account, so the directory carries two ACEs there and three for
    // an ordinary user - the "three or four" the ticket allows for.
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
