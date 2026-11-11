// AwsJson.cs - the JSON string escaping both AWS request bodies need
// License: Apache-2.0

// The bodies are built by hand so the exact bytes that get signed are the bytes that
// travel; a JSON serializer would decide key order and escaping for us, and the
// signature covers both.

using System.Globalization;
using System.Text;

namespace EnvManager.Secrets.Providers;

internal static class AwsJson
{
    internal static string Escape(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            switch (c)
            {
                case '"': sb.Append("\\\""); break;
                case '\\': sb.Append("\\\\"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    // Any other C0 control character is illegal raw inside a JSON
                    // string, and Secrets Manager rejects the request rather than the
                    // value. The \u form is valid JSON for all of them.
                    if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    else sb.Append(c);
                    break;
            }
        }
        return sb.ToString();
    }
}
