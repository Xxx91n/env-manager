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
            AtomicWriteJson(AuditFilePath, history);
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
        var profiles = LoadProfiles();
        string cmd = entry.Command;

        if (cmd == "profile create")
        {
            // Undoing create => delete the profile if it still exists
            var p = FindProfile(profiles, entry.Name);
            if (p != null)
            {
                if (p.IsEnabled) UnapplyProfile(p);
                profiles.Remove(p);
                SaveProfiles(profiles);
            }
            return true;
        }

        if (cmd == "profile delete")
        {
            // Undoing delete => restore the captured profile from OldValue
            if (string.IsNullOrEmpty(entry.OldValue)) return true;
            var restored = JsonSerializer.Deserialize<ProfileData>(entry.OldValue, JsonOpts);
            if (restored != null && FindProfile(profiles, restored.Name) == null)
            {
                restored.Inherits ??= new();
                restored.PathEntries ??= new();
                restored.Variables ??= new();
                profiles.Add(restored);
                SaveProfiles(profiles);
            }
            return true;
        }

        if (cmd == "profile rename")
        {
            // OldValue is the original name, NewValue is the new name
            var p = FindProfile(profiles, entry.NewValue ?? "");
            if (p != null && entry.OldValue != null)
            {
                if (p.IsEnabled) UnapplyProfile(p);
                p.Name = entry.OldValue;
                SaveProfiles(profiles);
            }
            return true;
        }

        if (cmd == "profile add-var")
        {
            // NewValue is the added variable JSON, OldValue is null
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

        if (cmd == "profile remove-var")
        {
            // OldValue is the removed variable JSON, NewValue is null
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

        if (cmd == "profile edit-var")
        {
            // OldValue is the pre-edit variable JSON, NewValue is the post-edit variable JSON
            var p = FindProfile(profiles, entry.Name);
            if (p != null)
            {
                if (!string.IsNullOrEmpty(entry.OldValue))
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
            }
            return true;
        }

        // For non-undoable profile commands (apply/unapply/set-inherits/add-path/remove-path),
        // return true to signal "handled: no-op" rather than crashing history undo
        if (cmd.StartsWith("profile "))
        {
            DebugLog("Profile audit undo is a no-op for command: " + cmd);
            return true;
        }
        return false;
    }
}
