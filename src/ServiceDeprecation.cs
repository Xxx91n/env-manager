namespace EnvManager;

/// <summary>
/// Deprecation surface for the two service verbs that no longer perform work (#293).
/// Lives here rather than on <c>partial class Program</c> so the
/// <c>ProgramMethodBudgetTests</c> structural guard keeps its budget: command domains
/// belong in their own <c>internal static class</c>, not on the dispatcher.
///
/// The reason text is part of the IPC contract: the service answers the same string in
/// its <c>noop</c> response and <c>docs/schemas/ipc-samples.json</c> carries it verbatim,
/// so rewording it is a golden-file change. Removal is scheduled for v0.16.0.
/// </summary>
internal static class ServiceDeprecation
{
    /// <summary>
    /// Warning text for a deprecated service verb, or the empty string for a live one.
    /// Emitted on <c>stderr</c> only, so a script parsing <c>stdout</c> keeps receiving the
    /// structured response JSON unpolluted; the exit code stays 0.
    /// </summary>
    internal static string WarningFor(string subcommand) => subcommand switch
    {
        "refresh" => "Warning: 'service refresh' is deprecated and performs no operation (values are fetched at CLI launch time); it will be removed in v0.16.0.",
        "reload" => "Warning: 'service reload' is deprecated and performs no operation (the service reads from disk on every request); it will be removed in v0.16.0.",
        _ => ""
    };

    internal static void WriteWarningIfAny(string subcommand)
    {
        var warning = WarningFor(subcommand);
        if (warning.Length > 0)
            Console.Error.WriteLine(warning);
    }
}
