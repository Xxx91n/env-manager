using Microsoft.Win32;

namespace EnvManager;

/// <summary>
/// A single environment value as persisted: the raw (never-expanded) string plus its registry kind.
/// Introduced with the IEnvironmentScope engine seam (architecture-recovery issue 01, expand phase).
/// </summary>
internal sealed record EnvValueSnapshot(string Value, RegistryValueKind Kind);

/// <summary>Outcome of a successful seam toggle.</summary>
internal enum ToggleOutcome
{
    Disabled,
    Restored
}

/// <summary>Terminal state of a seam write; lets the command layer reproduce SetVariable's exact stderr branches.</summary>
internal enum WriteOutcome
{
    Verified,
    RolledBack,
    RollbackFailed,
    ScopeUnavailable
}

/// <summary>
/// Result of a seam toggle. On failure, Error carries the exact stderr message the original
/// toggle mechanics emitted, so later command-layer extraction can reproduce CLI output verbatim.
/// </summary>
internal sealed record ToggleResult(bool Success, bool IsDisabled, string? Error)
{
    public static ToggleResult Ok(ToggleOutcome outcome) =>
        new(Success: true, IsDisabled: outcome == ToggleOutcome.Disabled, Error: null);

    public static ToggleResult Fail(string error) =>
        new(Success: false, IsDisabled: false, Error: error);
}

/// <summary>
/// Single engine seam for environment variable persistence (expand phase: add-only).
/// Production implementation: <see cref="RegistryScope"/> (registry + WM_SETTINGCHANGE P/Invoke).
/// Test implementation: <see cref="InMemoryScope"/> (dictionary-backed, user/system isolated).
/// Existing call sites are deliberately NOT rewired in this ticket (expand phase).
///
/// Contract notes for later extraction tickets:
/// - scope uses the CLI vocabulary: "system" selects the system store, anything else selects user
///   (mirrors GetScopeTarget).
/// - WriteValue owns kind policy ('%' -> ExpandString), write-verification, automatic rollback, and
///   the broadcast on the verified path, exactly where SetVariable emits them today; the WriteOutcome
///   result lets the command layer reproduce both stderr failure branches.
/// - DeleteValue owns toggle-backup and _PowerToys_ backup cleanup and the trailing broadcast,
///   exactly as DeleteVariable does today; it returns false only when the scope key cannot be opened.
/// - Toggle owns the full disable/restore mechanics including the no-destructive-recovery guarantees;
///   error strings are returned, not printed.
/// - ListVariables may propagate UnauthorizedAccessException on the system scope; the command layer
///   keeps that catch (mirrors ListEnvironment).
/// - The profile batch delete mechanics (DeleteVariableWithoutNotify: raw delete without backup
///   cleanup or broadcast) are not yet on the seam; extraction tickets add them when rewiring
///   profile paths.
/// </summary>
internal interface IEnvironmentScope
{
    /// <summary>Enumerates one scope, projecting _EnvManager_disabled backups as disabled entries (moved AppendEnvironmentItems).</summary>
    IReadOnlyList<EnvVariable> ListVariables(string scope);

    /// <summary>Reads the raw value (DoNotExpand) plus kind; null when absent (moved GetVariableValue mechanics).</summary>
    EnvValueSnapshot? ReadValue(string name, string scope);

    /// <summary>Case-insensitive existence probe (the GetValueNames().Any primitive toggle uses).</summary>
    bool Exists(string name, string scope);

    /// <summary>Full write mechanics: kind policy, write-verify, rollback, broadcast-on-verified (moved SetVariable mechanics).</summary>
    WriteOutcome WriteValue(string name, string? value, string scope);

    /// <summary>Blind write preserving the existing kind (moved SetVariableWithoutNotify mechanics; no verify, no broadcast).</summary>
    void WriteValuePreservingKind(string name, string value, string scope);

    /// <summary>Delete mechanics incl. toggle-backup and _PowerToys_ cleanup, broadcast at end (moved DeleteVariable mechanics).</summary>
    bool DeleteValue(string name, string scope);

    /// <summary>Full toggle mechanics with no destructive recovery (moved RunToggle core; errors returned, not printed).</summary>
    ToggleResult Toggle(string name, string scope);

    /// <summary>Change-broadcast signal: WM_SETTINGCHANGE HWND_BROADCAST in production; counted in the in-memory double.</summary>
    void BroadcastSettingChange();
}
