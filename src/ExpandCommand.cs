using System.Text.Json;
using System.Text.RegularExpressions;

namespace EnvManager;

/// <summary>
/// expand command domain (architecture-recovery issue 06): members moved verbatim
/// from EnvFeatures.cs. Behavior unchanged.
/// </summary>
partial class Program
{
    static readonly Regex ExpandPattern = new("%([^%]+)%", RegexOptions.Compiled);

    static int RunExpand(string value)
    {
        string expanded = value;
        for (int depth = 0; depth < 8; depth++)
        {
            string next = ExpandPattern.Replace(expanded, match =>
            {
                string name = match.Groups[1].Value;
                return GetVariableValue(name, "user") ?? GetVariableValue(name, "system") ??
                    Environment.GetEnvironmentVariable(name) ?? match.Value;
            });
            if (next == expanded) break;
            expanded = next;
        }
        Console.WriteLine(JsonSerializer.Serialize(new { value, expanded }, JsonOpts));
        return 0;
    }
}
