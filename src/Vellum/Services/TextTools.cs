using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Vellum.Services;

public enum CaseKind
{
    Upper,
    Lower,
    Title,
    Sentence,
    Invert,
    Random,
}

/// <summary>Text transformations from the "Lines" and "Case" menus. Lines are separated by '\r'.</summary>
public static class TextTools
{
    public static string[] SortLines(string[] lines, bool descending)
    {
        var comparer = StringComparer.Create(CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        var sorted = lines.Order(comparer).ToArray();
        if (descending) Array.Reverse(sorted);
        return sorted;
    }

    public static string[] RemoveDuplicates(string[] lines) => lines.Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>Sorts by the number each line starts with (lines without one go last).</summary>
    public static string[] SortNumeric(string[] lines, bool descending)
    {
        static double Key(string l)
        {
            Match m = LeadingNumber.Match(l);
            return m.Success && double.TryParse(m.Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out double v)
                ? v
                : double.NaN;
        }
        var numbered = lines.Where(l => !double.IsNaN(Key(l)));
        var sorted = (descending ? numbered.OrderByDescending(Key) : numbered.OrderBy(Key)).ToList();
        sorted.AddRange(lines.Where(l => double.IsNaN(Key(l))));
        return sorted.ToArray();
    }

    private static readonly Regex LeadingNumber = new(@"^\s*[-+]?\d+([.,]\d+)?", RegexOptions.Compiled);

    public static string[] Shuffle(string[] lines)
    {
        string[] copy = (string[])lines.Clone();
        System.Random.Shared.Shuffle(copy);
        return copy;
    }

    public static string[] RemoveConsecutiveDuplicates(string[] lines) =>
        lines.Where((l, i) => i == 0 || !string.Equals(l, lines[i - 1], StringComparison.Ordinal)).ToArray();

    public static string[] TrimLeading(string[] lines) => lines.Select(l => l.TrimStart(' ', '\t')).ToArray();

    public static string[] TrimBoth(string[] lines) => lines.Select(l => l.Trim(' ', '\t')).ToArray();

    /// <summary>Every tab becomes spaces up to the next tab stop.</summary>
    public static string[] TabsToSpaces(string[] lines, int width) => lines.Select(l =>
    {
        if (!l.Contains('\t')) return l;
        var sb = new StringBuilder(l.Length + 8);
        foreach (char c in l)
        {
            if (c == '\t') sb.Append(' ', width - sb.Length % width);
            else sb.Append(c);
        }
        return sb.ToString();
    }).ToArray();

    /// <summary>Leading spaces become tabs (the indent only, text inside the line is left alone).</summary>
    public static string[] SpacesToTabs(string[] lines, int width) => lines.Select(l =>
    {
        int i = 0, column = 0;
        while (i < l.Length && (l[i] == ' ' || l[i] == '\t'))
        {
            column = l[i] == '\t' ? (column / width + 1) * width : column + 1;
            i++;
        }
        if (i == 0) return l;
        return new string('\t', column / width) + new string(' ', column % width) + l[i..];
    }).ToArray();

    public static string[] RemoveEmpty(string[] lines) => lines.Where(l => l.Trim().Length > 0).ToArray();

    public static string[] TrimTrailing(string[] lines) => lines.Select(l => l.TrimEnd(' ', '\t')).ToArray();

    public static string[] Reverse(string[] lines) => lines.Reverse().ToArray();

    public static string Join(string[] lines) => string.Join(" ", lines.Select((l, i) => i == 0 ? l.TrimEnd() : l.Trim()).Where(l => l.Length > 0));

    public static string ChangeCase(string text, CaseKind kind)
    {
        TextInfo info = CultureInfo.CurrentCulture.TextInfo;
        switch (kind)
        {
            case CaseKind.Upper:
                return text.ToUpper(CultureInfo.CurrentCulture);
            case CaseKind.Lower:
                return text.ToLower(CultureInfo.CurrentCulture);
            case CaseKind.Title:
                return info.ToTitleCase(text.ToLower(CultureInfo.CurrentCulture));
            case CaseKind.Random:
            {
                var sb = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    sb.Append(System.Random.Shared.Next(2) == 0 ? char.ToLower(c, CultureInfo.CurrentCulture) : char.ToUpper(c, CultureInfo.CurrentCulture));
                }
                return sb.ToString();
            }
            case CaseKind.Invert:
            {
                var sb = new StringBuilder(text.Length);
                foreach (char c in text)
                {
                    sb.Append(char.IsUpper(c) ? char.ToLower(c, CultureInfo.CurrentCulture) : char.ToUpper(c, CultureInfo.CurrentCulture));
                }
                return sb.ToString();
            }
            default:
            {
                // Sentence case: first letter after . ! ? or a line start goes up, the rest down
                var sb = new StringBuilder(text.Length);
                bool start = true;
                foreach (char c in text)
                {
                    if (char.IsLetter(c))
                    {
                        sb.Append(start ? char.ToUpper(c, CultureInfo.CurrentCulture) : char.ToLower(c, CultureInfo.CurrentCulture));
                        start = false;
                    }
                    else
                    {
                        sb.Append(c);
                        if (c is '.' or '!' or '?' or '\r') start = true;
                    }
                }
                return sb.ToString();
            }
        }
    }

    // ---------- Hashes and JSON ----------

    public static string Hash(string text, string algorithm)
    {
        byte[] data = Encoding.UTF8.GetBytes(text);
        byte[] hash = algorithm switch
        {
            "MD5" => MD5.HashData(data),
            "SHA-1" => SHA1.HashData(data),
            "SHA-512" => SHA512.HashData(data),
            _ => SHA256.HashData(data),
        };
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>Re-indents JSON; null when the text is not valid JSON.</summary>
    public static string? FormatJson(string text, bool indented, string indent)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
            var options = new JsonWriterOptions
            {
                Indented = indented,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                IndentCharacter = indent == "\t" ? '\t' : ' ',
                IndentSize = indent == "\t" ? 1 : indent.Length,
                NewLine = "\n",
            };
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, options))
            {
                doc.WriteTo(writer);
            }
            return Encoding.UTF8.GetString(stream.ToArray()).Replace('\n', '\r');
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // ---------- Base64 / URL ----------

    public static string ToBase64(string text) => Convert.ToBase64String(Encoding.UTF8.GetBytes(text));

    public static string? FromBase64(string text)
    {
        try
        {
            string clean = new(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
            // URL-safe Base64 and missing padding are common too
            clean = clean.Replace('-', '+').Replace('_', '/');
            clean = clean.PadRight(clean.Length + (4 - clean.Length % 4) % 4, '=');
            byte[] bytes = Convert.FromBase64String(clean);
            return new UTF8Encoding(false, true).GetString(bytes);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static string ToUrl(string text) => Uri.EscapeDataString(text);

    public static string? FromUrl(string text)
    {
        try
        {
            return Uri.UnescapeDataString(text.Replace('+', ' '));
        }
        catch (Exception)
        {
            return null;
        }
    }

    // ---------- Comments ----------

    /// <summary>Line comment for a language (by <see cref="Syntax.SyntaxLanguage.Id"/>), or a block comment pair.</summary>
    public static (string? Line, string? Open, string? Close) CommentFor(string languageId) => languageId switch
    {
        "csharp" or "javascript" or "typescript" or "c" or "java" or "go" or "rust" or "swift" or "php" => ("//", null, null),
        "python" or "yaml" or "powershell" or "shell" => ("#", null, null),
        "sql" => ("--", null, null),
        "ini" => (";", null, null),
        "batch" => ("REM", null, null),
        "css" => (null, "/*", "*/"),
        "xml" or "markdown" => (null, "<!--", "-->"),
        _ => (null, null, null),
    };

    /// <summary>Comments the lines out, or back in if they all are comments already.</summary>
    public static string[] ToggleLineComment(string[] lines, string prefix)
    {
        bool IsBlank(string l) => l.Trim().Length == 0;
        bool allCommented = lines.Where(l => !IsBlank(l)).All(l => l.TrimStart().StartsWith(prefix, StringComparison.Ordinal));
        if (lines.All(IsBlank)) return lines;

        if (allCommented)
        {
            return lines.Select(l =>
            {
                if (IsBlank(l)) return l;
                int at = l.IndexOf(prefix, StringComparison.Ordinal);
                int end = at + prefix.Length;
                if (end < l.Length && l[end] == ' ') end++;
                return l[..at] + l[end..];
            }).ToArray();
        }

        int indent = lines.Where(l => !IsBlank(l)).Min(l => l.Length - l.TrimStart().Length);
        return lines.Select(l => IsBlank(l) ? l : l[..indent] + prefix + " " + l[indent..]).ToArray();
    }

    public static string ToggleBlockComment(string text, string open, string close)
    {
        string trimmed = text.Trim();
        if (trimmed.StartsWith(open, StringComparison.Ordinal) && trimmed.EndsWith(close, StringComparison.Ordinal) && trimmed.Length >= open.Length + close.Length)
        {
            string inner = trimmed[open.Length..^close.Length];
            if (inner.StartsWith(' ')) inner = inner[1..];
            if (inner.EndsWith(' ')) inner = inner[..^1];
            int lead = text.Length - text.TrimStart().Length;
            return text[..lead] + inner;
        }
        return $"{open} {text} {close}";
    }

    // ---------- Web search ----------

    public static Uri SearchUri(SearchEngine engine, string query)
    {
        string q = Uri.EscapeDataString(query.Trim());
        return new Uri(engine switch
        {
            SearchEngine.Bing => "https://www.bing.com/search?q=" + q,
            SearchEngine.DuckDuckGo => "https://duckduckgo.com/?q=" + q,
            SearchEngine.Yandex => "https://yandex.ru/search/?text=" + q,
            _ => "https://www.google.com/search?q=" + q,
        });
    }
}
