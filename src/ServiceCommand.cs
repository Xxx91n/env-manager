using Microsoft.Win32;
using System.Linq;
using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Service command domain (architecture-recovery issue 05): the `service` subcommand IPC gateway and its JSON escaping helper, moved verbatim from Program.cs. Behavior unchanged.
/// </summary>
partial class Program
{
    static int RunServiceCommand(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("Usage: env-manager service <status|health|refresh <id>|rotate <id>|reload|shutdown>");
            return 1;
        }

        string subcommand = args[1].ToLowerInvariant();
        // v0.9.8 A4: request_id propagated from Rust run_cli via env var, or generated here.
        string reqId = Environment.GetEnvironmentVariable("ENVMANAGER_REQUEST_ID") ?? "";
        if (string.IsNullOrEmpty(reqId)) reqId = DateTime.Now.ToString("HHmmss") + (new System.Random().Next(0, 9999)).ToString("D4");
        string pipeName = @"\\.\pipe\EnvManager.Background";
        if (args.Any(a => a == "--service-pipe"))
            pipeName = @"\\.\pipe\EnvManager.Service";

        ServiceIpcRequest request;
        try
        {
            request = ServiceIpcRequest.FromSubcommand(subcommand, reqId, args.Length >= 3 ? args[2] : null);
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine($"{{\"error\":\"unknown subcommand: {EscapeJsonLocal(subcommand)}\"}}");
            return 1;
        }
        if ((subcommand == "refresh" || subcommand == "rotate") && args.Length < 3)
        {
            Console.Error.WriteLine($"{{\"error\":\"{subcommand} requires mountId\"}}");
            return 1;
        }
        string requestJson = ServiceIpcRequest.Serialize(request);

        try
        {
            string pipePath = pipeName.Replace(@"\\.\pipe\", "");
                        // v0.9.7: fast-probe vs reliable-write connect strategy
            // Read probes (status/ping/health): 1 attempt, 2s timeout — fail fast, don't block GUI
            // Write ops (refresh/rotate/reload/shutdown): 3x retry, 5s timeout — reliable delivery
            bool isProbe = subcommand is "status" or "ping" or "health";
            int maxAttempts = isProbe ? 1 : 3;
            int connectTimeout = isProbe ? 2000 : 5000;
            using var client = new System.IO.Pipes.NamedPipeClientStream(
                ".", pipePath,
                System.IO.Pipes.PipeDirection.InOut,
                System.IO.Pipes.PipeOptions.None);
            bool connected = false;
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try { client.Connect(connectTimeout); connected = true; break; }
                catch (System.TimeoutException) when (attempt < maxAttempts - 1)
                {
                    System.Threading.Thread.Sleep(1000 << attempt);
                }
            }
            if (!connected)
            {
                // v0.9.8 A3: structured service state enum - not_running vs unresponsive.
                // Check if the named pipe exists at all (pipe server not started = not_running).
                // If pipe path exists but connect timed out = unresponsive (service stuck).
                bool pipeExists = System.IO.File.Exists($@"\\.\pipe\{pipePath}");
                string state = isProbe && pipeExists ? "unresponsive" : "not_running";
                // not_running is expected常态 = stdout JSON, stderr debug; unresponsive = stdout JSON, stderr warn.
                Console.Out.WriteLine(ServiceIpcResponse.SerializeDegraded(state, $"service {state} at {pipeName}"));
                if (state == "unresponsive")
                    Console.Error.WriteLine($"Warning: service unresponsive at {pipeName} (pipe exists but IPC timeout). Process may be deadlocked.");
                return 1;
            }

            // No `using var` — Dispose on pipe-close races with the service closing
            // its end. Manage lifetime manually, swallow all IOExceptions from cleanup
            // so the CLI exit code reflects the response, not the teardown noise.
            var writer = new System.IO.StreamWriter(client, leaveOpen: true) { AutoFlush = true };
            writer.WriteLine(requestJson);

            var reader = new System.IO.StreamReader(client, leaveOpen: true);
            string response = reader.ReadLine() ?? "";
            Console.WriteLine(response);

            try { writer.Dispose(); } catch { }
            try { reader.Dispose(); } catch { }
            try { client.Dispose(); } catch { }

            // Parse through the typed contract instead of substring matching, so a
            // schema change on either side surfaces here as a parse/field mismatch.
            var parsed = ServiceIpcResponse.Deserialize(response);
            return parsed is { Ok: true } ? 0 : 1;
        }
        catch (System.TimeoutException)
        {
            Console.Error.WriteLine($"Error: service not responding at {pipeName}. Is env-manager-service running?");
            return 1;
        }
        catch (System.IO.IOException ex)
        {
            Console.Error.WriteLine($"Error: failed to connect to service: {ScrubExceptionMessage(ex.Message)}");
            return 1;
        }
    }

    static string EscapeJsonLocal(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new System.Text.StringBuilder();
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }
}
