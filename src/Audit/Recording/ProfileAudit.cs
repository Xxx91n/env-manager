using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Audit records for profile-level mutations (create/delete/rename/add-var/
/// remove-var/edit-var/set-inherits/add-path/remove-path). These operations
/// mutate profiles.json rather than the registry, so they bypass the standard
/// snapshot diff in Main(). We record them explicitly here so users can see
/// profile changes in history and undo them.
///
/// Scope is stored as "profile" so the GUI and `history` command can
/// distinguish profile-level audit entries from registry-level ones.
/// OldValue/NewValue carry a compact JSON summary of the profile state so
/// undo can restore it without holding a full snapshot in memory.
/// </summary>
partial class Program
{
    static void RecordProfileAudit(string command, string profileName, string? oldSummary, string? newSummary)
    {
        try
        {
            var history = LoadAuditHistory();
            history.Add(new AuditEntry
            {
                Command = command,
                Name = profileName,
                Scope = "profile",
                OldValue = oldSummary,
                NewValue = newSummary
            });
            if (history.Count > MaxAuditEntries) history = history[^MaxAuditEntries..];
            // AES-GCM encrypt audit content at rest
            var auditJson = JsonSerializer.Serialize(history, JsonOpts);
            var encrypted = EncryptAuditContent(auditJson);
            WriteAtomicUtf8(AuditFilePath, encrypted);
            // Restrict NTFS ACL on audit file after write
            try { SetFileAclRestricted(AuditFilePath); } catch { } // best-effort
        }
        catch (Exception ex)
        {
            DebugLog("Profile audit recording failed: " + ex.GetType().Name);
        }
    }

    /// <summary>
    /// Serializes a single profile to the compact form used by audit entries.
    /// Only a single profile is captured, not the whole profiles.json, so an
    /// undo restores that one profile state without clobbering other profiles.
    /// </summary>
    static string? ProfileSummary(ProfileData? profile)
    {
        if (profile == null) return null;
        return JsonSerializer.Serialize(new
        {
            id = profile.Id,
            name = profile.Name,
            isEnabled = profile.IsEnabled,
            inherits = profile.Inherits,
            pathEntries = profile.PathEntries,
            variables = profile.Variables
        }, JsonOpts);
    }

    /// <summary>
    /// Undo a profile-level audit entry by restoring the captured profile state.
    /// Supports create (delete the profile), delete (re-create from OldValue),
    /// rename (restore old name), add-var/remove-var/edit-var (restore variable
    /// set). Returns true if handled, false if the entry is not a profile entry
    /// or the command is not undoable.
    /// </summary>
    static bool TryUndoProfileAudit(AuditEntry entry)
    {
        if (entry.Scope != "profile") return false;
        return ProfileUndoHelpers.TryUndo(entry);
    }

    /// <summary>
    /// Undo logic for profile-level audit entries. Extracted from
    /// TryUndoProfileAudit into a nested helper class to keep cognitive
    /// complexity under the repository gate. Behavior is identical to the
    /// original method: same allow-list, same error text, same return
    /// values, and the same save side effects (including the existing
    /// cases that call SaveProfiles even when the undo is a no-op).
    /// </summary>
    private static class ProfileUndoHelpers
    {
        private static readonly string[] UndoableCommands =
        {
            "profile create",
            "profile delete",
            "profile rename",
            "profile add-var",
            "profile remove-var",
            "profile edit-var",
        };

        /// <summary>
        /// Dispatches an undoable profile audit command to its handler and refuses
        /// unsupported commands loudly instead of silently reporting success.
        /// </summary>
        internal static bool TryUndo(AuditEntry entry)
        {
            string cmd = entry.Command;

            // Explicit allow-list of undoable profile subcommands. A future
            // profile subcommand that mutates profiles.json MUST be added here
            // (its own branch below) or audit undo will refuse with a clear
            // "cannot be undone" error -- never silently report success.
            // This catches the silent-success footgun flagged by the architect
            // and code-reviewer lanes (HIGH severity).
            if (!UndoableCommands.Contains(cmd))
            {
                Console.Error.WriteLine("Error: Profile command '" + cmd + "' has no undo path; this change cannot be undone");
                return false;
            }

            try
            {
                var profiles = LoadProfiles();
                switch (cmd)
                {
                    case "profile create": return UndoCreate(profiles, entry);
                    case "profile delete": return UndoDelete(profiles, entry);
                    case "profile rename": return UndoRename(profiles, entry);
                    case "profile add-var": return UndoAddVar(profiles, entry);
                    case "profile remove-var": return UndoRemoveVar(profiles, entry);
                    case "profile edit-var": return UndoEditVar(profiles, entry);
                    default:
                        // Defensive: unreachable because of the allow-list above,
                        // but keep a safety net that refuses rather than silently succeeding.
                        Console.Error.WriteLine("Error: Profile command " + cmd + " reached undo without a handler");
                        return false;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: Profile undo failed: " + ex.GetType().Name + " -- " + ex.Message);
                return false;
            }
        }

        /// <summary>Undoes a profile create by removing the profile if it still exists (idempotent).</summary>
        private static bool UndoCreate(List<ProfileData> profiles, AuditEntry entry)
        {
            // Undoing create => delete the profile if it still exists.
            // Idempotent: if the profile is already gone, report true (nothing to do).
            var existing = FindProfile(profiles, entry.Name);
            if (existing != null)
            {
                if (existing.IsEnabled) UnapplyProfile(existing);
                profiles.Remove(existing);
                SaveProfiles(profiles);
            }
            return true;
        }

        /// <summary>Undoes a profile delete by restoring the profile from OldValue, failing loud on name shadowing.</summary>
        private static bool UndoDelete(List<ProfileData> profiles, AuditEntry entry)
        {
            // Undoing delete => restore from OldValue. Use Id-based conflict
            // detection so a same-Named different-Id profile does NOT silently
            // swallow the restore, and a true idempotent repeat undo is a no-op.
            if (string.IsNullOrEmpty(entry.OldValue)) return true;
            var restored = JsonSerializer.Deserialize<ProfileData>(entry.OldValue, JsonOpts);
            if (restored == null) return true;

            var conflictById = profiles.FirstOrDefault(p => p.Id.Equals(restored.Id, StringComparison.OrdinalIgnoreCase));
            var conflictByName = FindProfile(profiles, restored.Name);

            if (conflictById != null)
            {
                // Same Id already present => idempotent undo, nothing to do.
                return true;
            }
            if (conflictByName != null)
            {
                // Different Id but same Name => restoring would shadow a live profile.
                // Fail loud rather than silently dropping the restore (HIGH finding).
                Console.Error.WriteLine("Error: A profile named " + restored.Name + " already exists; undo would shadow it. Rename or delete that profile first.");
                return false;
            }
            restored.EnsureCollections();
            profiles.Add(restored);
            SaveProfiles(profiles);
            return true;
        }

        /// <summary>Undoes a profile rename by restoring the original name; refuses stale renames.</summary>
        private static bool UndoRename(List<ProfileData> profiles, AuditEntry entry)
        {
            // OldValue is the original name, NewValue is the new name.
            // If the profile is no longer at NewValue (e.g. further renamed
            // since the audit), refuse rather than silently no-op -- this
            // matches the registry path's stale-value contract.
            var p = FindProfile(profiles, entry.NewValue ?? "");
            if (p == null)
            {
                Console.Error.WriteLine("Error: Profile " + entry.NewValue + " no longer exists; undo is stale. Use --force to override if the profile was further renamed.");
                return false;
            }
            if (entry.OldValue != null)
            {
                if (p.IsEnabled) UnapplyProfile(p);
                p.SetName(entry.OldValue);
                SaveProfiles(profiles);
            }
            return true;
        }

        /// <summary>Undoes a profile add-var by removing the added variable (idempotent).</summary>
        private static bool UndoAddVar(List<ProfileData> profiles, AuditEntry entry)
        {
            // NewValue is the added variable JSON. Removing it is idempotent.
            var p = FindProfile(profiles, entry.Name);
            if (p != null && !string.IsNullOrEmpty(entry.NewValue))
            {
                var added = JsonSerializer.Deserialize<ProfileVariable>(entry.NewValue, JsonOpts);
                if (added != null)
                    p.Variables.RemoveAll(v => v.Name.Equals(added.Name, StringComparison.OrdinalIgnoreCase));
                SaveProfiles(profiles);
            }
            return true;
        }

        /// <summary>Undoes a profile remove-var by re-adding the removed variable if absent (idempotent).</summary>
        private static bool UndoRemoveVar(List<ProfileData> profiles, AuditEntry entry)
        {
            // OldValue is the removed variable JSON. Re-add is idempotent (only add if not already present).
            var p = FindProfile(profiles, entry.Name);
            if (p != null && !string.IsNullOrEmpty(entry.OldValue))
            {
                var removed = JsonSerializer.Deserialize<ProfileVariable>(entry.OldValue, JsonOpts);
                if (removed != null && !p.Variables.Any(v => v.Name.Equals(removed.Name, StringComparison.OrdinalIgnoreCase)))
                    p.Variables.Add(removed);
                SaveProfiles(profiles);
            }
            return true;
        }

        /// <summary>Undoes a profile edit-var by restoring the pre-edit variable value.</summary>
        private static bool UndoEditVar(List<ProfileData> profiles, AuditEntry entry)
        {
            // OldValue is the pre-edit variable JSON, NewValue is the post-edit variable JSON.
            var p = FindProfile(profiles, entry.Name);
            if (p != null && !string.IsNullOrEmpty(entry.OldValue))
            {
                var pre = JsonSerializer.Deserialize<ProfileVariable>(entry.OldValue, JsonOpts);
                var post = string.IsNullOrEmpty(entry.NewValue) ? null : JsonSerializer.Deserialize<ProfileVariable>(entry.NewValue, JsonOpts);
                if (pre != null && post != null)
                {
                    p.Variables.RemoveAll(v => v.Name.Equals(post.Name, StringComparison.OrdinalIgnoreCase));
                    p.Variables.Add(pre);
                    SaveProfiles(profiles);
                }
            }
            return true;
        }
    }
}
