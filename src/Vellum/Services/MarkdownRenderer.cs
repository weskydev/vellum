using System.Net;
using System.Text;
using Markdig;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Vellum.Services.Syntax;

namespace Vellum.Services;

/// <summary>Turns Markdown into the HTML shown in the preview. Raw HTML in the text is shown as text, never run.</summary>
public static class MarkdownRenderer
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    public static string ToHtml(string markdown)
    {
        string text = markdown.Replace('\r', '\n');
        var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer);
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.ReplaceOrAdd<CodeBlockRenderer>(new HighlightedCodeBlockRenderer());
        renderer.Render(Markdown.Parse(text, Pipeline));
        writer.Flush();
        return writer.ToString();
    }

    /// <summary>The page around the rendered Markdown, styled to match Vellum's glass.</summary>
    public static string Page(SyntaxPalette p, double fontSize, string fontFamily, bool hasBase)
    {
        string accent = SyntaxPalette.Css(p.Accent);
        string text = SyntaxPalette.Css(p.Text);
        string dim = SyntaxPalette.Css(p.Dim);
        string soft = p.IsDark ? "rgba(255,255,255,.06)" : "rgba(0,0,0,.045)";
        string line = p.IsDark ? "rgba(255,255,255,.10)" : "rgba(0,0,0,.09)";
        var sb = new StringBuilder();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        if (hasBase) sb.Append("<base href=\"https://vellum.document/\">");
        sb.Append("<style>");
        sb.Append($$"""
            html, body { background: transparent; margin: 0; }
            body { color: {{text}}; font: {{fontSize}}px/1.65 '{{fontFamily}}', 'Segoe UI Variable Text', 'Segoe UI', sans-serif; padding: 22px 32px 60px; overflow-wrap: anywhere; }
            #c { max-width: 820px; }
            h1, h2, h3, h4, h5, h6 { line-height: 1.25; margin: 1.4em 0 .5em; font-weight: 650; }
            h1 { font-size: 1.85em; } h2 { font-size: 1.45em; } h3 { font-size: 1.22em; } h4 { font-size: 1.08em; }
            h1, h2 { padding-bottom: .25em; border-bottom: 1px solid {{line}}; }
            #c > :first-child { margin-top: 0; }
            p, ul, ol, blockquote, pre, table { margin: 0 0 1em; }
            a { color: {{accent}}; text-decoration: none; } a:hover { text-decoration: underline; }
            code { font-family: 'Cascadia Mono', Consolas, monospace; font-size: .9em; background: {{soft}}; padding: .12em .38em; border-radius: 5px; }
            pre { background: {{soft}}; border: 1px solid {{line}}; border-radius: 10px; padding: 14px 16px; overflow-x: auto; }
            pre code { background: none; padding: 0; font-size: .88em; line-height: 1.55; }
            blockquote { margin-left: 0; padding: .2em 1em; border-left: 3px solid {{accent}}; color: {{dim}}; }
            hr { border: 0; border-top: 1px solid {{line}}; margin: 1.6em 0; }
            table { border-collapse: collapse; } th, td { border: 1px solid {{line}}; padding: 6px 12px; } th { background: {{soft}}; }
            img { max-width: 100%; border-radius: 8px; }
            li { margin: .2em 0; } li > input[type=checkbox] { accent-color: {{accent}}; margin-right: .4em; }
            ul.contains-task-list { list-style: none; padding-left: 1.2em; }
            ::selection { background: {{accent}}55; }
            ::-webkit-scrollbar { width: 10px; height: 10px; } ::-webkit-scrollbar-thumb { background: {{line}}; border-radius: 5px; }
            """);
        foreach (TokenKind kind in Enum.GetValues<TokenKind>())
        {
            if (p.ColorOf(kind) is { } c)
            {
                sb.Append($".t{(int)kind}{{color:{SyntaxPalette.Css(c)}}}");
            }
        }
        sb.Append($".t{(int)TokenKind.Comment}{{font-style:italic}}");
        sb.Append("</style></head><body><div id=\"c\"></div><script>");
        sb.Append("""
            const c = document.getElementById('c');
            window.chrome.webview.addEventListener('message', e => {
              const m = e.data;
              if (m.html !== undefined) c.innerHTML = m.html;
              if (m.scroll !== undefined) {
                const max = document.documentElement.scrollHeight - innerHeight;
                window.scrollTo(0, max * m.scroll);
              }
            });
            document.addEventListener('click', e => {
              const a = e.target.closest('a');
              if (!a) return;
              e.preventDefault();
              const href = a.getAttribute('href') || '';
              if (href.startsWith('#')) {
                const id = decodeURIComponent(href.slice(1));
                const el = document.getElementById(id);
                if (el) el.scrollIntoView({ behavior: 'smooth' });
              } else {
                window.chrome.webview.postMessage(href);
              }
            });
            window.chrome.webview.postMessage('ready:');
            """);
        sb.Append("</script></body></html>");
        return sb.ToString();
    }

    /// <summary>Code as HTML, colored with the same tokens as the editor (classes t0, t1, ...).</summary>
    internal static string Highlight(string code, SyntaxLanguage? language)
    {
        if (language is null || language.IsPlain || code.Length > 200_000) return WebUtility.HtmlEncode(code);
        var sb = new StringBuilder(code.Length * 2);
        // Tokenizers expect '\r' line breaks; same length, so positions match
        List<Token> tokens = language.Tokenize(code.Replace('\n', '\r'));
        int pos = 0;
        foreach (Token t in tokens.Where(t => t.Kind < TokenKind.Heading1).OrderBy(t => t.Start))
        {
            if (t.Start < pos) continue; // overlapping token
            sb.Append(WebUtility.HtmlEncode(code[pos..t.Start]));
            sb.Append($"<span class=\"t{(int)t.Kind}\">");
            sb.Append(WebUtility.HtmlEncode(code.Substring(t.Start, t.Length)));
            sb.Append("</span>");
            pos = t.End;
        }
        sb.Append(WebUtility.HtmlEncode(code[pos..]));
        return sb.ToString();
    }

    /// <summary>Code blocks with the same highlighting as the editor.</summary>
    private sealed class HighlightedCodeBlockRenderer : HtmlObjectRenderer<CodeBlock>
    {
        protected override void Write(HtmlRenderer renderer, CodeBlock block)
        {
            string info = (block as FencedCodeBlock)?.Info ?? "";
            string code = block.Lines.ToString().Replace("\r\n", "\n");
            SyntaxLanguage? language = SyntaxLanguage.ForFence(info);

            renderer.Write("<pre><code>");
            renderer.Write(Highlight(code, language));
            renderer.Write("</code></pre>");
        }
    }
}
