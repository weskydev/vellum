using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Vellum.Services.Syntax;
using Windows.UI;
using Block = Markdig.Syntax.Block;
using MdInline = Markdig.Syntax.Inlines.Inline;
using XamlInline = Microsoft.UI.Xaml.Documents.Inline;

namespace Vellum.Controls;

/// <summary>
/// Builds the Markdown preview from native controls, so it sits on the same glass as the text
/// (a web view can't be see-through in WinUI). Raw HTML is shown as plain text.
/// </summary>
internal sealed class MarkdownPreviewBuilder(SyntaxPalette palette, double fontSize, string fontFamily, string? folder, Action<string> openLink)
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .DisableHtml()
        .Build();

    private static readonly double[] HeadingScale = [1.85, 1.45, 1.22, 1.08, 1, 1];

    private readonly FontFamily _font = new(fontFamily);
    private readonly FontFamily _mono = new("Cascadia Mono, Consolas");
    private readonly FontFamily _icons = new("Segoe Fluent Icons, Segoe MDL2 Assets");
    private readonly SolidColorBrush _text = new(palette.Text);
    private readonly SolidColorBrush _accent = new(palette.Accent);
    private readonly SolidColorBrush _dim = new(palette.Dim);
    private readonly SolidColorBrush _soft = new(palette.IsDark ? Color.FromArgb(0x10, 255, 255, 255) : Color.FromArgb(0x0C, 0, 0, 0));
    private readonly SolidColorBrush _line = new(palette.IsDark ? Color.FromArgb(0x1E, 255, 255, 255) : Color.FromArgb(0x18, 0, 0, 0));

    /// <summary>Parsing is safe to do off the UI thread.</summary>
    public static MarkdownDocument Parse(string text) => Markdown.Parse(text.Replace('\r', '\n'), Pipeline);

    public void Fill(Panel target, MarkdownDocument document)
    {
        target.Children.Clear();
        foreach (Block block in document)
        {
            if (BuildBlock(block) is { } element)
            {
                target.Children.Add(element);
            }
        }
    }

    // ---------- Blocks ----------

    private FrameworkElement? BuildBlock(Block block)
    {
        switch (block)
        {
            case HeadingBlock h:
            {
                RichTextBlock text = Rich(h.Inline, fontSize * HeadingScale[Math.Clamp(h.Level, 1, 6) - 1], FontWeights.SemiBold);
                text.Margin = new Thickness(0, h.Level <= 2 ? 22 : 16, 0, 8);
                if (h.Level > 2) return text;
                text.Margin = new Thickness(0, 0, 0, 6);
                return new Border
                {
                    Child = text,
                    BorderBrush = _line,
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Margin = new Thickness(0, 22, 0, 12),
                };
            }
            case ParagraphBlock p:
                return WithMargin(Rich(p.Inline, fontSize, FontWeights.Normal), 0, 0, 0, 12);
            case ListBlock list:
                return BuildList(list);
            case QuoteBlock quote:
            {
                var panel = new StackPanel();
                foreach (Block child in quote)
                {
                    if (BuildBlock(child) is { } e) panel.Children.Add(e);
                }
                ColorTexts(panel, _dim);
                return new Border
                {
                    Child = panel,
                    BorderBrush = _accent,
                    BorderThickness = new Thickness(3, 0, 0, 0),
                    Padding = new Thickness(14, 2, 0, 0),
                    Margin = new Thickness(0, 0, 0, 12),
                };
            }
            case CodeBlock code:
                return BuildCode(code);
            case ThematicBreakBlock:
                return new Border { Height = 1, Background = _line, Margin = new Thickness(0, 14, 0, 22) };
            case Table table:
                return BuildTable(table);
            case LinkReferenceDefinitionGroup:
                return null;
            case LeafBlock leaf when leaf.Inline is not null:
                return WithMargin(Rich(leaf.Inline, fontSize, FontWeights.Normal), 0, 0, 0, 12);
            case ContainerBlock container:
            {
                var panel = new StackPanel();
                foreach (Block child in container)
                {
                    if (BuildBlock(child) is { } e) panel.Children.Add(e);
                }
                return panel;
            }
            default:
                return null;
        }
    }

    private FrameworkElement BuildList(ListBlock list)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
        int number = int.TryParse(list.OrderedStart, out int start) ? start : 1;
        foreach (Block item in list)
        {
            if (item is not ListItemBlock li) continue;

            bool? task = (li.FirstOrDefault() as ParagraphBlock)?.Inline?.FirstChild is TaskList t ? t.Checked : null;
            var marker = new TextBlock
            {
                Foreground = _accent,
                FontSize = fontSize,
                FontFamily = _font,
                MinWidth = 22,
                Margin = new Thickness(4, 0, 6, 0),
                Text = list.IsOrdered ? $"{number++}." : "•",
            };
            if (task is { } done)
            {
                marker.FontFamily = _icons;
                marker.Text = done ? "" : "";
                marker.FontSize = fontSize * 0.95;
                marker.Margin = new Thickness(4, 4, 8, 0);
            }

            var content = new StackPanel();
            foreach (Block child in li)
            {
                if (BuildBlock(child) is { } e)
                {
                    // Lists are tight: less room between items
                    if (e.Margin.Bottom >= 12) e.Margin = new Thickness(e.Margin.Left, e.Margin.Top, e.Margin.Right, 4);
                    content.Children.Add(e);
                }
            }
            if (task is true) ColorTexts(content, _dim);

            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.Children.Add(marker);
            Grid.SetColumn(content, 1);
            row.Children.Add(content);
            panel.Children.Add(row);
        }
        return panel;
    }

    private FrameworkElement BuildCode(CodeBlock block)
    {
        string code = block.Lines.ToString().Replace("\r\n", "\n").TrimEnd('\n');
        SyntaxLanguage? language = SyntaxLanguage.ForFence((block as FencedCodeBlock)?.Info ?? "");

        var paragraph = new Paragraph();
        if (language is null || code.Length > 100_000)
        {
            paragraph.Inlines.Add(new Run { Text = code });
        }
        else
        {
            List<Token> tokens = language.Tokenize(code.Replace('\n', '\r'));
            int pos = 0;
            foreach (Token t in tokens.Where(t => t.Kind < TokenKind.Heading1).OrderBy(t => t.Start))
            {
                if (t.Start < pos || t.End > code.Length) continue;
                if (t.Start > pos) paragraph.Inlines.Add(new Run { Text = code[pos..t.Start] });
                var run = new Run { Text = code.Substring(t.Start, t.Length) };
                if (palette.ColorOf(t.Kind) is { } c) run.Foreground = new SolidColorBrush(c);
                if (t.Kind == TokenKind.Comment) run.FontStyle = Windows.UI.Text.FontStyle.Italic;
                paragraph.Inlines.Add(run);
                pos = t.End;
            }
            if (pos < code.Length) paragraph.Inlines.Add(new Run { Text = code[pos..] });
        }

        var text = new RichTextBlock
        {
            FontFamily = _mono,
            FontSize = fontSize * 0.88,
            Foreground = _text,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.NoWrap,
            LineHeight = fontSize * 0.88 * 1.55,
        };
        text.Blocks.Add(paragraph);

        return new Border
        {
            Background = _soft,
            BorderBrush = _line,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(16, 12, 16, 12),
            Margin = new Thickness(0, 2, 0, 14),
            Child = new ScrollViewer
            {
                Content = text,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollMode = ScrollMode.Auto,
                VerticalScrollMode = ScrollMode.Disabled,
            },
        };
    }

    private FrameworkElement BuildTable(Table table)
    {
        var grid = new Grid { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 2, 0, 14) };
        int columns = table.OfType<TableRow>().Select(r => r.Count).DefaultIfEmpty(0).Max();
        for (int c = 0; c < columns; c++)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        int rowIndex = 0;
        foreach (TableRow row in table.OfType<TableRow>())
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int col = 0;
            foreach (Block cellBlock in row)
            {
                if (cellBlock is not TableCell cell) continue;
                // ":--", ":-:" and "--:" under the header align the column
                TextAlignment align = (col < table.ColumnDefinitions.Count ? table.ColumnDefinitions[col].Alignment : null) switch
                {
                    TableColumnAlign.Center => TextAlignment.Center,
                    TableColumnAlign.Right => TextAlignment.Right,
                    _ => TextAlignment.Left,
                };
                var content = new StackPanel();
                foreach (Block child in cell)
                {
                    if (BuildBlock(child) is { } e)
                    {
                        e.Margin = new Thickness(0);
                        if (e is RichTextBlock r)
                        {
                            if (row.IsHeader) r.FontWeight = FontWeights.SemiBold;
                            r.TextAlignment = align;
                        }
                        content.Children.Add(e);
                    }
                }
                var border = new Border
                {
                    Child = content,
                    BorderBrush = _line,
                    BorderThickness = new Thickness(col == 0 ? 1 : 0, rowIndex == 0 ? 1 : 0, 1, 1),
                    Padding = new Thickness(12, 6, 12, 6),
                    Background = row.IsHeader ? _soft : null,
                };
                Grid.SetRow(border, rowIndex);
                Grid.SetColumn(border, col);
                grid.Children.Add(border);
                col++;
            }
            rowIndex++;
        }
        return new ScrollViewer
        {
            Content = grid,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalScrollMode = ScrollMode.Auto,
            VerticalScrollMode = ScrollMode.Disabled,
        };
    }

    // ---------- Inline text ----------

    private RichTextBlock Rich(ContainerInline? inlines, double size, Windows.UI.Text.FontWeight weight)
    {
        var text = new RichTextBlock
        {
            FontFamily = _font,
            FontSize = size,
            FontWeight = weight,
            Foreground = _text,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            LineHeight = size * 1.6,
        };
        var paragraph = new Paragraph();
        if (inlines is not null) AddInlines(paragraph.Inlines, inlines);
        text.Blocks.Add(paragraph);
        return text;
    }

    private void AddInlines(InlineCollection target, ContainerInline container)
    {
        for (MdInline? inline = container.FirstChild; inline is not null; inline = inline.NextSibling)
        {
            if (BuildInline(inline) is { } x) target.Add(x);
        }
    }

    private XamlInline? BuildInline(MdInline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                return new Run { Text = literal.Content.ToString() };
            case TaskList:
                return null; // drawn as the list marker
            case CodeInline code:
                return new Run
                {
                    Text = code.Content,
                    FontFamily = _mono,
                    FontSize = fontSize * 0.9,
                    Foreground = new SolidColorBrush(palette.Key),
                };
            case EmphasisInline emphasis:
            {
                var span = new Span();
                if (emphasis.DelimiterChar is '~')
                {
                    span.TextDecorations = Windows.UI.Text.TextDecorations.Strikethrough;
                    span.Foreground = _dim;
                }
                else if (emphasis.DelimiterCount >= 2)
                {
                    span.FontWeight = FontWeights.SemiBold;
                }
                else
                {
                    span.FontStyle = Windows.UI.Text.FontStyle.Italic;
                }
                AddInlines(span.Inlines, emphasis);
                return span;
            }
            case LinkInline { IsImage: true } image:
                return BuildImage(image);
            case LinkInline link:
            {
                var hyperlink = new Hyperlink { Foreground = _accent, UnderlineStyle = UnderlineStyle.None };
                string url = link.GetDynamicUrl?.Invoke() ?? link.Url ?? "";
                hyperlink.Click += (_, _) => openLink(url);
                AddInlines(hyperlink.Inlines, link);
                if (hyperlink.Inlines.Count == 0) hyperlink.Inlines.Add(new Run { Text = url });
                ToolTipService.SetToolTip(hyperlink, url);
                return hyperlink;
            }
            case AutolinkInline auto:
            {
                var hyperlink = new Hyperlink { Foreground = _accent, UnderlineStyle = UnderlineStyle.None };
                string url = auto.IsEmail ? "mailto:" + auto.Url : auto.Url;
                hyperlink.Click += (_, _) => openLink(url);
                hyperlink.Inlines.Add(new Run { Text = auto.Url });
                return hyperlink;
            }
            case LineBreakInline br:
                return br.IsHard ? new LineBreak() : new Run { Text = " " };
            case HtmlEntityInline entity:
                return new Run { Text = entity.Transcoded.ToString() };
            case ContainerInline container:
            {
                var span = new Span();
                AddInlines(span.Inlines, container);
                return span;
            }
            case LeafInline leaf:
                return new Run { Text = leaf.ToString() ?? "" };
            default:
                return null;
        }
    }

    private XamlInline BuildImage(LinkInline image)
    {
        string url = image.Url ?? "";
        var picture = new Image { Stretch = Stretch.Uniform, MaxHeight = 520, HorizontalAlignment = HorizontalAlignment.Left };
        _ = LoadImageAsync(picture, url);
        var frame = new Border { Child = picture, CornerRadius = new CornerRadius(8), Margin = new Thickness(0, 4, 0, 4) };
        return new InlineUIContainer { Child = frame };
    }

    private async Task LoadImageAsync(Image target, string url)
    {
        try
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https")
            {
                target.Source = new BitmapImage(uri);
                return;
            }
            if (folder is null) return;
            string path = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(url)));
            if (!File.Exists(path)) return;
            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(path);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            target.Source = bitmap;
        }
        catch (Exception)
        {
            // a broken picture just stays empty
        }
    }

    // ---------- Helpers ----------

    private static T WithMargin<T>(T element, double l, double t, double r, double b) where T : FrameworkElement
    {
        element.Margin = new Thickness(l, t, r, b);
        return element;
    }

    private static void ColorTexts(DependencyObject root, Brush brush)
    {
        if (root is RichTextBlock rich) rich.Foreground = brush;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            ColorTexts(VisualTreeHelper.GetChild(root, i), brush);
        }
        if (root is Panel panel)
        {
            foreach (UIElement child in panel.Children) ColorTexts(child, brush);
        }
        else if (root is Border { Child: { } child })
        {
            ColorTexts(child, brush);
        }
    }
}
