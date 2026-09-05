using System.Text;
using EnvManager;
using SharpFuzz;

// Architecture-recovery issue 25: libFuzzer harness over the CLI's untrusted
// argument-parsing surface. The driver (libfuzzer-dotnet-windows.exe) feeds
// fuzz input into the span; we treat each input as the raw command line.
//
// Exception bisection discipline (spec Phase 4, ticket 25):
//   swallowed (expected, argument-shaped): FormatException, ArgumentException
//     (incl. ArgumentNullException/ArgumentOutOfRangeException), OverflowException
//   crash (real bug classes): NullReferenceException, IndexOutOfRangeException,
//     OutOfMemoryException, StackOverflowException, AccessViolationException and
//     anything else unexpected - escape the callback so libFuzzer records it.

// env-manager.csproj must list this assembly in InternalsVisibleTo for the
// internal LenientArgs type to be reachable here.

Fuzzer.LibFuzzer.Run(span =>
{
    string commandLine = Encoding.UTF8.GetString(span);

    // Surface 1: LenientArgs.Tokenize re-scans the whole command line
    // (quote toggling, backslash runs, program-path skipping).
    string[] tokens;
    try
    {
        tokens = LenientArgs.Tokenize(commandLine);
    }
    catch (FormatException) { return; }
    catch (ArgumentException) { return; }
    catch (OverflowException) { return; }

    // Surface 2: the dispatcher's untrusted-input predicates, which guard
    // lock acquisition and command routing before any registry access.
    try
    {
        _ = global::EnvManager.Program.IsWriteInvocationForFuzz(tokens);
        _ = LenientArgs.WasArgsCorruptedByTrailingBackslashQuote(tokens);
    }
    catch (FormatException) { }
    catch (ArgumentException) { }
    catch (OverflowException) { }
});
