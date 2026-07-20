using System;
using System.Collections.Generic;
using System.Text;

namespace EnvManager;

/// <summary>
/// Lenient command-line tokenizer that recovers from the classic Windows
/// "trailing backslash + quote" tokenizer hazard.
///
/// Background: the default <see cref="Environment.GetCommandLineArgs"/>
/// (and Main(string[] args)) follow CommandLineToArgvW rules. When a value
/// ends with an odd number of backslashes immediately before a closing quote,
/// the quote is treated as a literal escaped quote rather than the terminator.
/// As a result, the rest of the command line (including --scope, --overwrite,
/// etc.) gets merged into the same token.
///
/// Example victim:  path add "C:\Program Files\PowerShell\7\" --scope user
///   default args:  { path, add, 'C:\Program Files\PowerShell\7" --scope user' }
///   expected args: { path, add, 'C:\Program Files\PowerShell\7\', --scope, user }
///
/// This tokenizer re-scans <see cref="Environment.CommandLine"/> with the
/// following lenient rule: a quote is ALWAYS a terminator (never a literal
/// when preceded by backslashes). A backslash is literal unless it
/// immediately precedes a closing quote at which point the quote still acts
/// as terminator but the backslashes remain literal in the current token.
/// This matches user intent for PATH values that end with a directory separator.
///
/// The tokenizer also supports a "--" separator: everything after the first
/// standalone "--" token is treated as positional, never as a flag.
/// </summary>
static partial class LenientArgs
{
    /// <summary>
    /// Re-tokenize <see cref="Environment.CommandLine"/> leniently.
    /// Returns all tokens after the program path (i.e., matching the shape
    /// of Main(string[] args)) but with the trailing-backslash recovery.
    /// </summary>
    public static string[] Tokenize()
    {
        string raw = Environment.CommandLine;
        if (string.IsNullOrEmpty(raw)) return Array.Empty<string>();

        var tokens = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;
        bool afterSeparator = false;
        bool hasContent = false;  // has current token accumulated any content?

        int i = SkipProgramPath(raw);

        while (i < raw.Length)
        {
            char c = raw[i];

            // White space outside quotes ends the current token.
            if (!inQuotes && c == ' ')
            {
                if (hasContent || isAfterSeparatorBoundaryStart())
                {
                    // flush current token boundary
                    FlushToken();
                    // skip run of whitespace
                    while (i < raw.Length && raw[i] == ' ') i++;
                    continue;
                }
                // leading whitespace between tokens: just skip
                while (i < raw.Length && raw[i] == ' ') i++;
                continue;
            }

            // Quote: toggles inQuotes (always terminator-like, never literal).
            if (c == '"')
            {
                inQuotes = !inQuotes;
                hasContent = true; // empty-quoted-string still counts as a token
                i++;
                continue;
            }

            // "--" separator: only recognized at a token boundary (current is
            // empty and not in quotes), and only if followed by whitespace or
            // end-of-line. After it, all remaining tokens are positional.
            if (!inQuotes && !afterSeparator && c == '-' &&
                raw.Length > i + 1 && raw[i + 1] == '-' &&
                !hasContent)
            {
                int j = i + 2;
                if (j >= raw.Length || raw[j] == ' ')
                {
                    afterSeparator = true;
                    hasContent = false;
                    i = j;
                    // skip whitespace after --
                    while (i < raw.Length && raw[i] == ' ') i++;
                    continue;
                }
            }

            // Backslash run handling. Outside quotes backslashes are literal.
            // Inside quotes, a backslash run before a closing quote keeps
            // the backslashes literal under our lenient rule.
            if (c == '\\')
            {
                int runStart = i;
                while (i < raw.Length && raw[i] == '\\') i++;
                int runLen = i - runStart;
                // Whether or not the next char is a quote, emit run literally.
                // The quote (if present) will be handled by the '"' branch on
                // the next iteration as a terminator.
                for (int k = 0; k < runLen; k++) current.Append('\\');
                hasContent = true;
                continue;
            }

            // Regular character
            current.Append(c);
            hasContent = true;
            i++;
        }

        // Flush trailing token
        FlushToken();

        return tokens.ToArray();

        // Local helpers
        bool isAfterSeparatorBoundaryStart()
        {
            // After a "--" separator, an empty-content token boundary should
            // NOT flush a phantom empty token. We suppress it here.
            return !afterSeparator;
        }

        void FlushToken()
        {
            // Under "--" separator, we keep counting positions even if empty,
            // but we only push if hasContent.
            if (hasContent)
            {
                tokens.Add(current.ToString());
                current.Clear();
                hasContent = false;
            }
        }
    }

    /// <summary>
    /// Detect whether the runtime-passed args were corrupted by the Windows
    /// "trailing backslash + quote" tokenizer hazard. The signature is an arg
    /// element that simultaneously contains a double quote character AND one of
    /// the flag names that the user intended to pass as a separate token
    /// (--scope, --overwrite, --index, --output, --debug, -d). When this is
    /// detected, re-tokenizing Environment.CommandLine can recover the
    /// intended split.
    /// </summary>
    public static bool WasArgsCorruptedByTrailingBackslashQuote(string[] args)
    {
        if (args == null || args.Length == 0) return false;

        // Lightweight scan: any element with both a literal '"' and one of the
        // suspicious flag markers (bordered by start|space so we don't hit
        // legitimate PATH values that happen to include these substrings).
        for (int i = 0; i < args.Length; i++)
        {
            string a = args[i];
            if (a == null || a.Length == 0) continue;
            if (a.IndexOf('"') < 0) continue;
            if (ContainsEmbeddedFlag(a)) return true;
        }
        return false;
    }

    private static bool ContainsEmbeddedFlag(string s)
    {
        // Detect embedded standalone flag tokens like ' --scope user',
        // ' --overwrite', ' --index N', ' --output file', ' --debug', ' -d'.
        // We require a leading space (or start of string) before the flag to
        // avoid matching legitimate path portions that happen to include these
        // substrings.
        foreach (var flag in new[]
        {
            " --scope ", " --overwrite", " --index ", " --output ",
            " --debug", " -d"
        })
        {
            if (s.Contains(flag)) return true;
        }
        // Also: trailing flag with no value (end of string, e.g. "value --debug")
        if (s.EndsWith(" --debug") || s.EndsWith(" -d") ||
            s.EndsWith(" --overwrite")) return true;
        return false;
    }

    /// <summary>
    /// Advance past the program-path token at the start of the command line.
    /// The program path may be quoted (possibly containing spaces) or unquoted
    /// (up to the first whitespace). Returns the index just past the path
    /// and any trailing whitespace.
    /// </summary>
    private static int SkipProgramPath(string raw)
    {
        int i = 0;
        while (i < raw.Length && raw[i] == ' ') i++;
        if (i >= raw.Length) return i;

        if (raw[i] == '"')
        {
            i++; // skip opening quote
            while (i < raw.Length && raw[i] != '"')
            {
                // Backslash-quote inside program path: per CommandLineToArgvW
                // odd run = escaped quote, even run = terminator. Keep it simple
                // here - skip until the matching closing quote.
                if (raw[i] == '\\')
                {
                    int runStart = i;
                    while (i < raw.Length && raw[i] == '\\') i++;
                    if (i < raw.Length && raw[i] == '"')
                    {
                        int run = i - runStart;
                        if ((run & 1) == 1)
                        {
                            // odd run: escaped quote, keep going
                            i++;
                            continue;
                        }
                        // even run: terminator
                        break;
                    }
                    continue;
                }
                i++;
            }
            if (i < raw.Length && raw[i] == '"') i++; // skip closing quote
        }
        else
        {
            while (i < raw.Length && raw[i] != ' ') i++;
        }

        while (i < raw.Length && raw[i] == ' ') i++;
        return i;
    }
}
