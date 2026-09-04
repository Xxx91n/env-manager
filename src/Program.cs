using Microsoft.Win32;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace EnvManager;

partial class Program
{
    static int Main(string[] args)
    {
        // v0.9.13 Phase 2D/4A: Disable WER crash dialogs + SEM_NOGPFAULTERRORBOX (best-effort)
        DisableCrashDialogs();

        // Route seam debug output (RegistryScope write/toggle failure logs) into the CLI
        // --debug channel; before the seam migration these lines were DebugLog calls inside
        // SetVariable/Toggle. No output change without --debug.
        RegistryScope.DebugSink = DebugLog;

        // v0.7.1 fix: recover from the classic Windows "trailing backslash + quote"
        // tokenizer hazard, where values like "C:\Program Files\PowerShell\7\"
        // merge with following --scope/--overwrite flags. Main(args) follows
        // CommandLineToArgvW, treating an odd backslash count before a quote as
        // an escaped literal quote. We re-tokenize Environment.CommandLine with
        // a lenient rule (quote is always a terminator, backslashes are literal)
        // so trailing-backslash PATH values survive.
        if (LenientArgs.WasArgsCorruptedByTrailingBackslashQuote(args))
        {
            string[] recovered = LenientArgs.Tokenize();
            if (recovered.Length == 0)
            {
                Console.Error.WriteLine("Error: Command-line recovery failed");
                return 1;
            }

            // The runtime argv is already known to be corrupted. Prefer the
            // deterministic recovery even when it has fewer elements: the
            // malformed runtime split can introduce spurious empty fragments.
            args = recovered;
        }

        if (args.Length == 0)
        {
            ShowHelp();
            return 0;
        }

        // Check for --debug flag anywhere in args
        var argList = args.ToList();
        if (argList.Remove("--debug") || argList.Remove("-d"))
        {
            DebugMode = true;
        }
        args = argList.ToArray();

        DebugLog($"Command: {args.FirstOrDefault() ?? "none"}; argumentCount={Math.Max(0, args.Length - 1)}");

        string command = args[0];
        if (!ValidCommands.Contains(command))
        {
            Console.Error.WriteLine($"Unknown command: {command}");
            ShowHelp();
            return 1;
        }

        Mutex? mutationLock = null;
        Dictionary<string, string?>? beforeSnapshot = null;
        try
        {
            mutationLock = AcquireMutationLock(args);
            if (mutationLock != null) beforeSnapshot = CaptureEnvironmentSnapshot();

            int exitCode = command.ToLowerInvariant() switch
            {
                "list" => ListEnvironment(),
                "get" => args.Length < 2 ? ArgError("Usage: env-manager get <name>") : GetVariable(args[1]),
                "set" => args.Length < 3 ? ArgError("Usage: env-manager set <name> <value> [--scope user|system] [--overwrite]") : RunSet(args),
                "rename" => args.Length < 3 ? ArgError("Usage: env-manager rename <old> <new> [--scope user|system] [--overwrite]") : RunRename(args),
                "change-scope" => args.Length < 3 ? ArgError("Usage: env-manager change-scope <name> <new-scope> [--scope user|system] [--overwrite]") : RunChangeScope(args),
                "delete" => args.Length < 2 ? ArgError("Usage: env-manager delete <name> [--scope user|system]") : RunDelete(args),
                "toggle" => args.Length < 2 ? ArgError("Usage: env-manager toggle <name> [--scope user|system]") : RunToggle(args),
                "backup" => RunBackup(args),
                "restore" => args.Length < 2 ? ArgError("Usage: env-manager restore <file> [--scope user|system]") : RunRestore(args),
                "diff" => args.Length < 3 ? ArgError("Usage: env-manager diff <old> <new>") : DiffBackups(args[1], args[2]),
                "merge" => args.Length < 5 || args[3] != "--output" ? ArgError("Usage: env-manager merge <old> <new> --output <file>") : MergeBackups(args[1], args[2], args[4]),
                "validate" => args.Length < 2 ? ArgError("Usage: env-manager validate <file>") : ValidateBackup(args[1]),
                "profile" => RunProfileCommand(args),
                "path" => RunPathCommand(args),
                "agents" => RunAgents(args),
                "history" => RunHistoryCommand(args),
                "bulk" => RunBulkCommand(args),
                "expand" => args.Length < 2 ? ArgError("Usage: env-manager expand <value>") : RunExpand(args[1]),
                "protection" => RunProtectionCommand(args),
                "update" => RunUpdate(args),
                "service" => RunServiceCommand(args),
                "audit" => RunAuditCommand(args),
                "export-state" => RunExportState(args),
                "import-state" => RunImportState(args),
                "help" => ShowHelp(),
                _ => 1
            };

            if (exitCode == 0 && beforeSnapshot != null)
            {
                try
                {
                    RecordSnapshotDiff(args[0] + (args.Length > 1 && args[0] is "profile" or "path" or "history" or "bulk" ? " " + args[1] : ""), beforeSnapshot, CaptureEnvironmentSnapshot());
                }
                catch (Exception auditError)
                {
                    DebugLog("Audit recording failed: " + auditError.GetType().Name);
                }
            }
            return exitCode;
        }
        catch (UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Error: Access denied (requires elevation)");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ScrubExceptionMessage(ex.Message)}");
            return 1;
        }
        finally
        {
            if (mutationLock != null)
            {
                mutationLock.ReleaseMutex();
                mutationLock.Dispose();
            }
        }
    }

}
