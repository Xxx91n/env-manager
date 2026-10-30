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
//
// The two tests in the first section cover the success path only. The second half of the
// boundary - what happens when the DACL is rejected - is driven through
// SecretTempDirectory's seam overload in the section below, because nothing on a normal
// machine can make an owner-only DACL fail on demand.

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

    // ---- the failure branch: the DACL is rejected ----
    //
    // These drive SecretTempDirectory's seam overload instead of SopsProvider: the
    // provider's create point is SecretTempDirectory.Create(Name).FullName, so the
    // directory asserted on here is the one Encrypt/Decrypt would have received, and
    // reaching it through Encrypt/Decrypt would need the sops binary - a provider stub is
    // #181's, not this ticket's. Residual gap, stated rather than implied: nothing here
    // goes red if SopsProvider stops routing through SecretTempDirectory, so that one line
    // of the provider stays covered by review rather than by a test.

    [Fact]
    public void Create_DaclRejected_FallsBackToAUsableDirectoryUnderTheInheritedAcl()
    {
        string? requestedPath = null;
        var recorded = new List<string>();

        // Stands in for the create call that carries the DACL: it fails the way a rejected
        // security descriptor fails, before the directory exists. Everything after it is
        // the real production fallback.
        DirectoryInfo directory = SecretTempDirectory.Create(
            "sops",
            (path, _) =>
            {
                requestedPath = path;
                throw new UnauthorizedAccessException("the DACL was rejected");
            },
            recorded.Add);
        try
        {
            Assert.Equal(requestedPath!, directory.FullName);
            Assert.True(Directory.Exists(directory.FullName),
                "a rejected DACL must still hand the provider a directory that exists");

            // Usable, because the provider puts plaintext in it: the fallback has to take a
            // file and hand it back.
            string plainFile = Path.Combine(directory.FullName, "secret.json");
            File.WriteAllText(plainFile, "{\"value\":\"probe\"}");
            Assert.Equal("{\"value\":\"probe\"}", File.ReadAllText(plainFile));

            // And inherited, i.e. whatever %TEMP% grants: the fallback is the degradation
            // #178 documented, not a second attempt at the owner-only DACL.
            Assert.False(directory.GetAccessControl().AreAccessRulesProtected,
                "the fallback directory must not be protected - protection was what the failed call was for");
            Assert.Single(recorded);
        }
        finally
        {
            Delete(directory.FullName);
        }
    }

    [Fact]
    public void Create_DaclRejected_RecordsTheDegradationAfterTheScrubber()
    {
        string? requestedPath = null;
        var recorded = new List<string>();
        // A bearer token in the failure text: whatever the boundary leaves behind has to
        // come out the other side of Program.ScrubExceptionMessage. Eight characters on
        // purpose - that is how much the scrubber masks after a matched pattern, so a
        // token this short leaves nothing behind and the assertion below is not just
        // "the first half is gone".
        const string token = "s3cr3t-1";

        DirectoryInfo directory = SecretTempDirectory.Create(
            "sops",
            (path, _) =>
            {
                requestedPath = path;
                throw new UnauthorizedAccessException("Bearer " + token);
            },
            recorded.Add);
        try
        {
            Assert.Single(recorded);
            string line = recorded[0];
            Assert.Contains("ACL restriction failed", line);
            // The recorded line names the directory it could not restrict.
            Assert.Contains(requestedPath!, line);
            // Scrubbed: the mask replaced the token whole, so no part of it survives.
            Assert.Contains("Bearer <redacted>", line);
            Assert.DoesNotContain(token, line);
        }
        finally
        {
            Delete(directory.FullName);
        }
    }

    [Fact]
    public void Create_DirectoryCannotBeCreated_PropagatesTheFailure()
    {
        string? requestedPath = null;
        var recorded = new List<string>();

        // Occupy the generated path with a file so the fallback cannot create the directory
        // either: Directory.CreateDirectory reports "a file already has this name" as an
        // IOException. This is the one case the fallback must not absorb - no temp
        // directory means no sops.
        try
        {
            Assert.Throws<IOException>(() =>
            {
                SecretTempDirectory.Create(
                    "sops",
                    (path, _) =>
                    {
                        requestedPath = path;
                        File.WriteAllText(path, "occupies the name the directory needs");
                        throw new UnauthorizedAccessException("the DACL was rejected");
                    },
                    recorded.Add);
            });

            // Recorded before the failure escapes: the line documents the degradation, it
            // does not replace the exception.
            Assert.Single(recorded);
            Assert.False(Directory.Exists(requestedPath!));
        }
        finally
        {
            if (requestedPath != null && File.Exists(requestedPath)) File.Delete(requestedPath);
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
