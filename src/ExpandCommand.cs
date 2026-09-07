using System.Text.Json;
using System.Text.RegularExpressions;

namespace EnvManager;

/// <summary>
/// expand command domain (architecture-recovery issue 06): members moved verbatim
/// from EnvFeatures.cs. Behavior unchanged.
/// </summary>
internal static class ExpandCommand
{
    static readonly Regex ExpandPattern = new("%([^%]+)%", RegexOptions.Compiled);

    internal static int Run(string value)
    {
        string expanded = value;
        for (int depth = 0; depth < 8; depth++)
        {
            string next = ExpandPattern.Replace(expanded, match =>
            {
                string name = match.Groups[1].Value;
                return Program.GetVariableValue(name, "user") ?? Program.GetVariableValue(name, "system") ??
                    Environment.GetEnvironmentVariable(name) ?? match.Value;
            });
            if (next == expanded) break;
            expanded = next;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { value, expanded }, Program.JsonOpts));
        return 0;
    }
}
