// SurvivalKitTempFile.cs - the survival kit's plaintext temp file, born under an owner-only directory
// License: Apache-2.0
//
// Why this exists: the kit used to be written straight into
// %TEMP%\envmanager-kit-<pid>.json with no DACL of its own, so its privacy was whatever
// %TEMP% inherits - C:\Windows\SystemTemp happens to be SYSTEM + Administrators only, but
// C:\Windows\Temp carries BUILTIN\Users:(I)(RX), and on a host whose TEMP is that path the
// assembled kit was readable by any local user. Nothing in the code expressed that
// dependency and no test pinned it. This is the third %TEMP% plaintext sink found in the
// full-tree sweep, and it closes the last one: a sweep of src/ finds exactly one
// Path.GetTempPath() call site in production code - SecretTempDirectory.Create - and every
// temp sink now goes through it.
//
// Placement: a type of its own rather than two more members on partial class Program.
// Program carries a method budget pinned by ProgramMethodBudgetTests, and the audit
// domain is on the ramp-2+ list of domains to migrate off it; the kit's temp file is a
// single-purpose concern with no need for the command surface.

using System;
using System.IO;
// Secrets.Core is one of the two surfaces the Secrets context publishes to the rest of the
// engine (AGENTS.md: cross-context code consumes Secrets.Core and Secrets.Manager), so the
// audit context reaching for SecretTempDirectory here is the sanctioned direction -
// Providers would not be. The dependency is one call, and it is the only reason this file
// needs a Secrets import at all.
using EnvManager.Secrets.Core;

namespace EnvManager;

/// <summary>
/// Creates and removes the survival kit's plaintext temp file:
/// <c>%TEMP%\env-manager-kit-&lt;guid&gt;\envmanager-kit-&lt;pid&gt;.json</c>. The directory carries
/// the owner-only DACL from the call that creates it, and the file inherits it, so the kit's
/// plaintext is never as public as <c>%TEMP%</c> happens to be.
/// </summary>
internal static class SurvivalKitTempFile
{
    /// <summary>
    /// Creates the temp directory and returns it together with the kit file's path inside it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Why the directory form (the two-candidate decision): the file-level alternative was a
    /// twin of <c>SetFileAclRestricted</c>, but that helper is a private member of the CLI's
    /// <c>Program</c>, takes a <c>FileInfo</c>, and retunes the ACL *after* the file exists -
    /// a window this boundary exists to close. The directory form (a) reuses
    /// <c>SecretTempDirectory.Create</c>, the create-with-descriptor path #178 already ships
    /// and CI already proves, so no second API surface has to be trusted; (b) covers the
    /// <c>.tmp.&lt;pid&gt;</c> scratch name <c>WriteAtomicUtf8</c> writes beside the kit, which a
    /// file-only DACL would have to be re-applied to; and (c) grants an inheritable ACE, so
    /// the file gets the same three identities instead of an empty DACL.
    /// </para>
    /// <para>
    /// The file keeps its documented name, but its path gains a parent: the plaintext-lifecycle
    /// tables address the sink as <c>envmanager-kit-*.json</c>, and those rows now spell out the
    /// directory it lives in, because a glob against <c>%TEMP%</c> itself no longer finds it.
    /// </para>
    /// <para>
    /// Failure semantics are #178's, unchanged: a DACL that cannot be applied is recorded on
    /// the debug channel through <c>ScrubExceptionMessage</c>, and the directory falls back to
    /// the inherited <c>%TEMP%</c> ACL it would have had before this boundary existed. The
    /// export is never refused because a DACL could not be tightened. Only a directory that
    /// cannot be created at all throws - and then there is nowhere to put the plaintext, so
    /// the export cannot proceed either way.
    /// </para>
    /// </remarks>
    internal static (DirectoryInfo Directory, string FilePath) Create()
    {
        DirectoryInfo directory = SecretTempDirectory.Create("kit");
        return (directory, Path.Combine(directory.FullName, "envmanager-kit-" + Environment.ProcessId + ".json"));
    }

    /// <summary>
    /// Best-effort removal of the directory <see cref="Create"/> made. The caller deletes the
    /// plaintext file inside first, so the usual residue of a failed delete is an empty
    /// directory rather than a secret - but both deletes are best-effort, and a failure of
    /// both leaves the plaintext on disk, exactly as the sops rows of the plaintext
    /// lifecycle table can. Failures are recorded rather than swallowed, because silent
    /// accumulation in <c>%TEMP%</c> is its own problem.
    /// </summary>
    internal static void DeleteDirectory(DirectoryInfo directory)
    {
        try
        {
            if (directory.Exists) directory.Delete(recursive: true);
        }
        catch (Exception ex)
        {
            Program.DebugLog(Program.ScrubExceptionMessage($"survival-kit temp cleanup failed for {directory.FullName}: {ex.Message}"));
        }
    }
}
