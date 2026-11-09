using System.Text.RegularExpressions;

namespace Vellum.Services;

public readonly record struct SearchOptions(bool MatchCase, bool WholeWord, bool UseRegex);

/// <summary>
/// Find and replace over editor text.
/// The editor uses '\r' for line breaks; searching runs over a copy with '\n' (same length),
/// so ^ and $ work per line and positions match the editor exactly.
/// </summary>
public static class TextSearch
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(1);

    /// <summary>Builds the pattern, or returns null when the regular expression is invalid.</summary>
    public static Regex? Build(string query, SearchOptions options)
    {
        if (string.IsNullOrEmpty(query)) return null;

        string pattern = options.UseRegex ? query : Regex.Escape(query);
        if (options.WholeWord)
        {
            pattern = $@"\b(?:{pattern})\b";
        }

        var flags = RegexOptions.Multiline | RegexOptions.CultureInvariant;
        if (!options.MatchCase) flags |= RegexOptions.IgnoreCase;

        try
        {
            return new Regex(pattern, flags, Timeout);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static string Prepare(string editorText) => editorText.Replace('\r', '\n');

    /// <summary>Next match after <paramref name="start"/> (or before it when going back), wrapping around the end.</summary>
    public static Match? Find(string text, Regex regex, int start, bool forward)
    {
        start = Math.Clamp(start, 0, text.Length);
        try
        {
            if (forward)
            {
                Match m = regex.Match(text, start);
                if (m.Success && m.Length == 0 && m.Index == start)
                {
                    m = start < text.Length ? regex.Match(text, start + 1) : Match.Empty;
                }
                if (!m.Success)
                {
                    m = regex.Match(text, 0); // wrap to the top
                }
                return m.Success ? m : null;
            }

            Match? before = null, last = null;
            for (Match m = regex.Match(text); m.Success; m = m.NextMatch())
            {
                if (m.Index < start) before = m;
                last = m;
            }
            return before ?? last; // wrap to the bottom
        }
        catch (RegexMatchTimeoutException)
        {
            return null;
        }
    }

    /// <summary>Total number of matches and the 1-based number of the match at <paramref name="current"/> (0 if none).</summary>
    public static (int Total, int Current) Count(string text, Regex regex, int current, int limit = 9999)
    {
        int total = 0, index = 0;
        try
        {
            for (Match m = regex.Match(text); m.Success && total < limit; m = m.NextMatch())
            {
                total++;
                if (m.Index == current) index = total;
                if (m.Length == 0 && m.Index >= text.Length) break;
            }
        }
        catch (RegexMatchTimeoutException)
        {
        }
        return (total, index);
    }

    /// <summary>The replacement for one match ($1 etc. work in regex mode).</summary>
    public static string Expand(Match match, string replacement, SearchOptions options) =>
        (options.UseRegex ? match.Result(replacement) : replacement).Replace('\n', '\r');

    public static (string Text, int Count) ReplaceAll(string text, Regex regex, string replacement, SearchOptions options)
    {
        int count = 0;
        string result = regex.Replace(text, m =>
        {
            count++;
            return Expand(m, replacement, options);
        });
        return (result.Replace('\n', '\r'), count);
    }
}
