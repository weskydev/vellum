using System.Net;
using System.Text;
using Vellum.Services.Syntax;

namespace Vellum.Services;

/// <summary>
/// Turns a tab into a standalone, light HTML page: Markdown rendered with headings, lists and tables,
/// code highlighted like in the editor, plain text kept as it is. The same page is printed to PDF.
/// </summary>
public static class ExportService
{
    public static string BuildHtml(string title, string text, SyntaxLanguage language, string? baseFolder)
    {
        string body = text.Replace("\r\n", "\n").Replace('\r', '\n');
        SyntaxPalette p = SyntaxPalette.For(false);
        string accent = SyntaxPalette.Css(p.Accent);

        var sb = new StringBuilder();
        sb.Append("<!doctype html>\n<html><head><meta charset=\"utf-8\">\n");
        sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
        sb.Append("<meta name=\"generator\" content=\"Vellum\">\n");
        sb.Append($"<title>{WebUtility.HtmlEncode(title)}</title>\n");
        if (baseFolder is not null)
        {
            // Pictures next to the file keep working
            sb.Append($"<base href=\"{WebUtility.HtmlEncode(new Uri(baseFolder.TrimEnd('\\') + "\\").AbsoluteUri)}\">\n");
        }
        sb.Append("<style>\n");
        sb.Append($$"""
            :root { color-scheme: light; }
            html { background: #fff; }
            body { color: #1f1d1b; font: 15px/1.65 'Segoe UI Variable Text', 'Segoe UI', system-ui, sans-serif; max-width: 760px; margin: 0 auto; padding: 48px 32px 64px; overflow-wrap: anywhere; }
            h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.4em 0 .5em; font-weight: 650; break-after: avoid; }
            h1 { font-size: 1.9em; } h2 { font-size: 1.45em; } h3 { font-size: 1.22em; } h4 { font-size: 1.08em; }
            h1, h2 { padding-bottom: .25em; border-bottom: 1px solid #e6e2dc; }
            body > :first-child { margin-top: 0; }
            p, ul, ol, blockquote, pre, table { margin: 0 0 1em; }
            a { color: {{accent}}; }
            code { font-family: 'Cascadia Mono', Consolas, monospace; font-size: .9em; background: #f4f1ec; padding: .12em .38em; border-radius: 5px; }
            pre { background: #f7f5f1; border: 1px solid #e6e2dc; border-radius: 10px; padding: 14px 16px; overflow-x: auto; white-space: pre-wrap; break-inside: avoid; }
            pre code { background: none; padding: 0; font-size: .86em; line-height: 1.55; }
            pre.text { background: none; border: 0; padding: 0; font: inherit; white-space: pre-wrap; }
            blockquote { margin-left: 0; padding: .2em 1em; border-left: 3px solid {{accent}}; color: #6b655e; }
            hr { border: 0; border-top: 1px solid #e6e2dc; margin: 1.6em 0; }
            table { border-collapse: collapse; } th, td { border: 1px solid #e6e2dc; padding: 6px 12px; } th { background: #f7f5f1; }
            img { max-width: 100%; border-radius: 8px; }
            li { margin: .2em 0; } li > input[type=checkbox] { accent-color: {{accent}}; margin-right: .4em; }
            ul.contains-task-list { list-style: none; padding-left: 1.2em; }
            @media print { body { padding: 0; max-width: none; } a { text-decoration: none; } }

            """);
        foreach (TokenKind kind in Enum.GetValues<TokenKind>())
        {
            if (p.ColorOf(kind) is { } c) sb.Append($".t{(int)kind}{{color:{SyntaxPalette.Css(c)}}}");
        }
        sb.Append($".t{(int)TokenKind.Comment}{{font-style:italic}}\n");
        sb.Append("</style></head><body>\n");

        if (language.IsMarkdown)
        {
            sb.Append(MarkdownRenderer.ToHtml(body));
        }
        else if (language.IsPlain)
        {
            sb.Append("<pre class=\"text\">").Append(WebUtility.HtmlEncode(body)).Append("</pre>");
        }
        else
        {
            sb.Append("<pre><code>").Append(MarkdownRenderer.Highlight(body, language)).Append("</code></pre>");
        }
        sb.Append("\n</body></html>\n");
        return sb.ToString();
    }
}
