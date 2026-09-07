using Microsoft.Win32;
using System.Linq;
using System.Text.Json;

namespace EnvManager;

/// <summary>
/// Update command domain (architecture-recovery issue 05 + ticket 27): `update check` and version comparison, extracted from partial class Program to its own internal class so Program stops growing. VersionIsNewer stays nested in this class (no other domain reads it) - the closure of the helper on Run is the boundary the compiler enforces. Program.JsonOpts / Program.ArgError / Program.ScrubExceptionMessage still live on partial Program via CliRuntime; this class reaches them via the existing Program.JsonOpts call shape.
/// </summary>
internal static class UpdateCommand
{
    internal static int Run(string[] args)
    {
        string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "check";

        if (sub == "check")
        {
            // Query GitHub Releases API for latest version
            try
            {
                string url = "https://api.github.com/repos/Xxx91n/env-manager/releases/latest";
                using var client = new System.Net.Http.HttpClient();
                client.DefaultRequestHeaders.UserAgent.ParseAdd("env-manager-cli");
                client.Timeout = TimeSpan.FromSeconds(10);
                var response = client.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = System.Text.Json.JsonDocument.Parse(response);
                string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                tag = tag.TrimStart('v');
                string htmlUrl = doc.RootElement.GetProperty("html_url").GetString() ?? "";

                var cv = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string currentVersion = $"{cv.Major}.{cv.Minor}.{cv.Build}";
                bool isNewer = VersionIsNewer(tag, currentVersion);

                var result = new
                {
                    currentVersion = currentVersion,
                    latestVersion = tag,
                    isUpdateAvailable = isNewer,
                    releaseUrl = htmlUrl,
                };
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(result, Program.JsonOpts));
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error checking for updates: " + Program.ScrubExceptionMessage(ex.Message));
                return 1;
            }
        }

        return Program.ArgError("Usage: env-manager update check");
    }

    internal static bool VersionIsNewer(string remote, string local)
    {
        var parse = (string s) => s.Split('.')
            .Select(p => int.TryParse(p.Trim(), out int n) ? n : 0)
            .ToArray();
        var r = parse(remote);
        var l = parse(local);
        int max = Math.Max(r.Length, l.Length);
        for (int i = 0; i < max; i++)
        {
            int rv = i < r.Length ? r[i] : 0;
            int lv = i < l.Length ? l[i] : 0;
            if (rv > lv) return true;
            if (rv < lv) return false;
        }
        return false;
    }
}
