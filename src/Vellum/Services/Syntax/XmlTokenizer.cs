using System.Text.RegularExpressions;

namespace Vellum.Services.Syntax;

/// <summary>XML, XAML and HTML: tags, attributes, values, comments, entities.</summary>
public static partial class XmlTokenizer
{
    [GeneratedRegex(@"<!--[\s\S]*?(?:-->|\z)|<!\[CDATA\[[\s\S]*?(?:\]\]>|\z)|<[?!][^>]*>?|</?[\w:.-]+(?:[^<>""']|""[^""]*""|'[^']*')*/?>?|&#?\w+;", RegexOptions.CultureInvariant)]
    private static partial Regex Pieces();

    [GeneratedRegex(@"(?<name>[\w:.@-]+)(?:\s*=\s*(?<value>""[^""]*""?|'[^']*'?|[^\s>]+))?", RegexOptions.CultureInvariant)]
    private static partial Regex Attributes();

    // <style> and <script> bodies get their own highlighting
    [GeneratedRegex(@"(?<=<script\b[^>]*>)[\s\S]*?(?=</script>|\z)|(?<=<style\b[^>]*>)[\s\S]*?(?=</style>|\z)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex EmbeddedCode();

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        foreach (Match m in Pieces().Matches(text))
        {
            string s = m.Value;
            if (s.StartsWith("<!--") || s.StartsWith("<![CDATA["))
            {
                tokens.Add(new Token(m.Index, m.Length, s.StartsWith("<!--") ? TokenKind.Comment : TokenKind.String));
            }
            else if (s.StartsWith("<?") || s.StartsWith("<!"))
            {
                tokens.Add(new Token(m.Index, m.Length, TokenKind.Meta));
            }
            else if (s.StartsWith('&'))
            {
                tokens.Add(new Token(m.Index, m.Length, TokenKind.Meta));
            }
            else
            {
                AddTag(tokens, s, m.Index);
            }
        }

        foreach (Match m in EmbeddedCode().Matches(text))
        {
            if (m.Length == 0) continue;
            bool isScript = text.LastIndexOf("<script", m.Index, StringComparison.OrdinalIgnoreCase) >
                            text.LastIndexOf("<style", m.Index, StringComparison.OrdinalIgnoreCase);
            RegexRules rules = isScript ? CodeRules.JavaScript : CodeRules.Css;
            tokens.RemoveAll(t => t.Start >= m.Index && t.End <= m.Index + m.Length);
            tokens.AddRange(rules.Tokenize(m.Value).Select(t => t with { Start = t.Start + m.Index }));
        }
        tokens.Sort((a, b) => a.Start.CompareTo(b.Start));
        return tokens;
    }

    private static void AddTag(List<Token> tokens, string tag, int offset)
    {
        // "<" or "</", then the name
        int nameStart = tag.StartsWith("</") ? 2 : 1;
        int nameEnd = nameStart;
        while (nameEnd < tag.Length && (char.IsLetterOrDigit(tag[nameEnd]) || tag[nameEnd] is ':' or '.' or '-' or '_'))
        {
            nameEnd++;
        }
        tokens.Add(new Token(offset, nameStart, TokenKind.Marker));
        tokens.Add(new Token(offset + nameStart, nameEnd - nameStart, TokenKind.Tag));

        int close = tag.EndsWith("/>") ? tag.Length - 2 : tag.EndsWith('>') ? tag.Length - 1 : tag.Length;
        if (close > nameEnd)
        {
            string body = tag[nameEnd..close];
            foreach (Match a in Attributes().Matches(body))
            {
                Group name = a.Groups["name"], value = a.Groups["value"];
                tokens.Add(new Token(offset + nameEnd + name.Index, name.Length, TokenKind.Attribute));
                if (value.Success)
                {
                    tokens.Add(new Token(offset + nameEnd + value.Index, value.Length, TokenKind.String));
                }
            }
        }
        if (close < tag.Length)
        {
            tokens.Add(new Token(offset + close, tag.Length - close, TokenKind.Marker));
        }
    }
}
