// sops-stub: PROTOTYPE helper for the p3 boundary probes. Not production, never merged.
//
// Stands in for the sops binary so V2/V3 can control two things real sops cannot be asked
// to do on demand: hold still long enough to be orphaned by a service stop, and count how
// many times a single rotate starts a helper process.
//
// Contract it implements (from src/Secrets/Providers/SopsProvider.cs):
//   --version                  -> print a version line, exit 0
//   -e --output <enc> <plain>  -> read {"value":"..."} from <plain>, write a fake
//                                 "ciphertext" file to <enc>
//   -d --output <plain> <enc>  -> write {"value":"..."} to <plain>, THEN hold
//   anything else              -> exit 1
//
// The stub is NOT a cipher: it copies the value through. That is deliberate - the marker
// string in the plaintext lets the probe prove that a residue file really is plaintext.
//
// Behaviour knobs come from a control file (default C:\proto\stub.json) rather than the
// environment, because a helper started by a LocalSystem service child cannot be handed
// per-cell environment without touching machine-wide state:
//   { "sleepMs": 20000, "log": "C:\\proto\\stub.log" }
//
// An explicit named entry point, not top-level statements: global code in this file would
// silently out-rank Program.Main if the file were ever globbed into the CLI project again.

using System.Text.Json;

internal static class SopsStub
{
    private static int Main(string[] argv)
    {
        string controlPath = Environment.GetEnvironmentVariable("PROTO_SOPS_STUB_CONTROL") ?? @"C:\proto\stub.json";
        int sleepMs = 0;
        string logPath = @"C:\proto\stub.log";

        if (File.Exists(controlPath))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(controlPath));
                if (doc.RootElement.TryGetProperty("sleepMs", out var s)) sleepMs = s.GetInt32();
                if (doc.RootElement.TryGetProperty("log", out var l)) logPath = l.GetString() ?? logPath;
            }
            catch { /* a broken control file means an instant stub, which is the safe default */ }
        }

        if (argv.Length == 1 && argv[0] == "--version")
        {
            Log(logPath, "version", $"temp={Path.GetTempPath()} cwd={Environment.CurrentDirectory}");
            Console.WriteLine("sops 3.99.0-proto-stub (env-manager p3 boundary probes; not a cipher)");
            return 0;
        }

        if (argv.Length == 4 && (argv[0] == "-e" || argv[0] == "-d") && argv[1] == "--output")
        {
            string output = argv[2];
            string input = argv[3];
            string value = "PROTO-STUB-NO-VALUE";

            // Pull the value out of whichever side carries it, so a round trip through the
            // CLI produces the same plaintext on both ends.
            try
            {
                if (File.Exists(input))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(input));
                    if (doc.RootElement.TryGetProperty("value", out var v)) value = v.GetString() ?? value;
                }
            }
            catch { /* the fake ciphertext may not be JSON; keep the default */ }

            string payload = argv[0] == "-e"
                ? "{\"sops_stub\":\"fake-ciphertext\",\"value\":\"" + Esc(value) + "\"}"
                : "{\"value\":\"" + Esc(value) + "\"}";

            // Written and closed before the sleep, so V2 measures a residue file that is
            // really on disk rather than a write that had not landed yet.
            File.WriteAllText(output, payload, new System.Text.UTF8Encoding(false));
            Log(logPath, argv[0], $"tempdir={Path.GetDirectoryName(output)} output={output} sleepMs={sleepMs}");

            if (sleepMs > 0) Thread.Sleep(sleepMs);
            Log(logPath, argv[0] + ":exit", "");
            return 0;
        }

        Log(logPath, "unknown", "argv=" + string.Join(" ", argv));
        Console.Error.WriteLine("sops-stub: unrecognized arguments");
        return 1;
    }

    private static void Log(string logPath, string mode, string detail)
    {
        try
        {
            string? dir = Path.GetDirectoryName(logPath);
            if (dir != null) Directory.CreateDirectory(dir);
            File.AppendAllText(logPath,
                $"{DateTime.UtcNow:O}\tpid={Environment.ProcessId}\tmode={mode}\t{detail}\n");
        }
        catch { /* logging must never change what the probe measures */ }
    }

    private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
