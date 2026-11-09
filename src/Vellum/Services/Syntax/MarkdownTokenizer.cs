using System.Text.RegularExpressions;

namespace Vellum.Services.Syntax;

/// <summary>
/// Markdown, styled right in the editor: big bold headings, bold/italic text, code in a
/// monospace font, and the markup characters (#, **, `, [](…)) dimmed so the text reads cleanly.
/// </summary>
public static partial class MarkdownTokenizer
{
    [GeneratedRegex(@"^[ ]{0,3}(`{3,}|~{3,})[ \t]*([^\s`]*)")]
    private static partial Regex FenceOpen();

    [GeneratedRegex(@"^[ ]{0,3}(#{1,6})(?:[ \t]+|$)")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^[ ]{0,3}((?:>[ \t]?)+)")]
    private static partial Regex Quote();

    [GeneratedRegex(@"^[ \t]*(?:[-*+]|\d{1,9}[.)])[ \t]+(?:\[[ xX]\][ \t]+)?")]
    private static partial Regex ListItem();

    [GeneratedRegex(@"^[ ]{0,3}([-*_])(?:[ \t]*\1){2,}[ \t]*$")]
    private static partial Regex Rule();

    [GeneratedRegex(@"^[ \t]*\|")]
    private static partial Regex TableRow();

    // Inline, in order of priority
    [GeneratedRegex(
        @"(?<code>(?<tick>`+)(?!`)[\s\S]*?(?<!`)\k<tick>(?!`))" +
        @"|(?<image>!?\[(?<ltext>[^\]\r]*)\]\((?<lurl>[^)\r]*)\))" +
        @"|(?<auto><https?://[^>\s]+>|\bhttps?://[^\s<>()]+[^\s<>().,;:!?""'])" +
        @"|(?<bi>(?<bim>\*\*\*|___)(?=\S)[^\r]*?(?<=\S)\k<bim>)" +
        @"|(?<bold>(?<bm>\*\*|__)(?=\S)[^\r]*?(?<=\S)\k<bm>)" +
        @"|(?<italic>(?<!\w)(?<im>\*|_)(?=[^\s*_])[^\r*_]*?(?<=[^\s*_])\k<im>(?!\w))" +
        @"|(?<strike>~~(?=\S)[^\r]*?(?<=\S)~~)" +
        @"|(?<escape>\\[\\`*_{}\[\]()#+\-.!|~>])")]
    private static partial Regex Inline();

    public static List<Token> Tokenize(string text)
    {
        var tokens = new List<Token>();
        int pos = 0;
        string? fence = null;
        int fenceBodyStart = 0;
        SyntaxLanguage? fenceLanguage = null;

        while (pos <= text.Length)
        {
            int end = text.IndexOf('\r', pos);
            if (end < 0) end = text.Length;
            string line = text[pos..end];

            if (fence is not null)
            {
                if (line.TrimStart().StartsWith(fence, StringComparison.Ordinal) && line.Trim().Trim(fence[0]).Length == 0)
                {
                    AddCodeBlock(tokens, text, fenceBodyStart, pos, fenceLanguage);
                    tokens.Add(new Token(pos, line.Length, TokenKind.Marker));
                    fence = null;
                }
            }
            else if (FenceOpen().Match(line) is { Success: true } f)
            {
                fence = f.Groups[1].Value;
                fenceLanguage = SyntaxLanguage.ForFence(f.Groups[2].Value);
                tokens.Add(new Token(pos, line.Length, TokenKind.Marker));
                if (f.Groups[2].Length > 0)
                {
                    tokens.Add(new Token(pos + f.Groups[2].Index, f.Groups[2].Length, TokenKind.Meta));
                }
                fenceBodyStart = Math.Min(end + 1, text.Length);
            }
            else
            {
                TokenizeLine(tokens, line, pos);
            }

            if (end >= text.Length) break;
            pos = end + 1;
        }

        if (fence is not null)
        {
            AddCodeBlock(tokens, text, fenceBodyStart, text.Length, fenceLanguage);
        }
        return tokens;
    }

    private static void AddCodeBlock(List<Token> tokens, string text, int start, int end, SyntaxLanguage? language)
    {
        if (end <= start) return;
        tokens.Add(new Token(start, end - start, TokenKind.CodeBlock));
        if (language is { IsCode: true } or { Id: "log" })
        {
            foreach (Token t in language.Tokenize(text[start..end]))
            {
                tokens.Add(t with { Start = t.Start + start });
            }
        }
    }

    private static void TokenizeLine(List<Token> tokens, string line, int offset)
    {
        if (line.Length == 0) return;

        if (Rule().IsMatch(line))
        {
            tokens.Add(new Token(offset, line.Length, TokenKind.Marker));
            return;
        }

        int contentStart = 0;
        if (Heading().Match(line) is { Success: true } h)
        {
            TokenKind kind = (TokenKind)((int)TokenKind.Heading1 + h.Groups[1].Length - 1);
            tokens.Add(new Token(offset, line.Length, kind));
            tokens.Add(new Token(offset, h.Length, TokenKind.Marker));
            // Closing hashes: "## Title ##"
            int trailing = line.Length;
            while (trailing > h.Length && line[trailing - 1] is '#' or ' ') trailing--;
            if (trailing < line.Length && line.TrimEnd().EndsWith('#'))
            {
                tokens.Add(new Token(offset + trailing, line.Length - trailing, TokenKind.Marker));
            }
            contentStart = h.Length;
        }
        else if (Quote().Match(line) is { Success: true } q)
        {
            tokens.Add(new Token(offset, line.Length, TokenKind.Quote));
            tokens.Add(new Token(offset, q.Length, TokenKind.ListMarker));
            contentStart = q.Length;
        }
        else if (ListItem().Match(line) is { Success: true } li)
        {
            tokens.Add(new Token(offset, li.Length, TokenKind.ListMarker));
            contentStart = li.Length;
        }
        else if (TableRow().IsMatch(line))
        {
            for (int i = 0; i < line.Length; i++)
            {
                if (line[i] == '|') tokens.Add(new Token(offset + i, 1, TokenKind.Marker));
            }
            if (line.Replace("|", "").Trim().All(c => c is '-' or ':' or ' '))
            {
                tokens.Add(new Token(offset, line.Length, TokenKind.Marker));
                return;
            }
        }

        foreach (Match m in Inline().Matches(line, contentStart))
        {
            int at = offset + m.Index;
            if (m.Groups["code"].Success)
            {
                int tick = m.Groups["tick"].Length;
                tokens.Add(new Token(at, m.Length, TokenKind.CodeInline));
                tokens.Add(new Token(at, tick, TokenKind.Marker));
                tokens.Add(new Token(at + m.Length - tick, tick, TokenKind.Marker));
            }
            else if (m.Groups["image"].Success)
            {
                Group text = m.Groups["ltext"];
                tokens.Add(new Token(at, m.Length, TokenKind.Marker));
                tokens.Add(new Token(offset + text.Index, text.Length, TokenKind.Link));
            }
            else if (m.Groups["auto"].Success)
            {
                tokens.Add(new Token(at, m.Length, TokenKind.Link));
            }
            else if (m.Groups["bi"].Success)
            {
                AddEmphasis(tokens, at, m.Length, 3, TokenKind.BoldItalic);
            }
            else if (m.Groups["bold"].Success)
            {
                AddEmphasis(tokens, at, m.Length, 2, TokenKind.Bold);
            }
            else if (m.Groups["italic"].Success)
            {
                AddEmphasis(tokens, at, m.Length, 1, TokenKind.Italic);
            }
            else if (m.Groups["strike"].Success)
            {
                AddEmphasis(tokens, at, m.Length, 2, TokenKind.Strike);
            }
            else if (m.Groups["escape"].Success)
            {
                tokens.Add(new Token(at, 1, TokenKind.Marker));
            }
        }
    }

    private static void AddEmphasis(List<Token> tokens, int at, int length, int marker, TokenKind kind)
    {
        tokens.Add(new Token(at, length, kind));
        tokens.Add(new Token(at, marker, TokenKind.Marker));
        tokens.Add(new Token(at + length - marker, marker, TokenKind.Marker));
    }
}
