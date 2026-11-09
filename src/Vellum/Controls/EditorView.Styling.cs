using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Vellum.Services.Syntax;
using Windows.Foundation;

namespace Vellum.Controls;

/// <summary>
/// How the text looks: Markdown and code highlighting, focus mode, typewriter scrolling,
/// line spacing and the reading column. Only the visible part is painted, so big files stay fast.
/// </summary>
public sealed partial class EditorView
{
    private const int MaxHighlightLength = 300_000;
    private const int PaintMargin = 4000;
    private static readonly float[] HeadingScale = [1.75f, 1.45f, 1.25f, 1.1f, 1f, 1f];

    private DispatcherQueueTimer? _restyleTimer;
    private ScrollViewer? _scroller;
    private SyntaxPalette _palette = SyntaxPalette.For(dark: true);
    private List<Token> _tokens = [];
    private int _tokensVersion = -1;
    private string _tokensLanguage = "";

    private void SetupStyling()
    {
        _restyleTimer = DispatcherQueue.CreateTimer();
        _restyleTimer.IsRepeating = false;
        _restyleTimer.Tick += (_, _) => Restyle();

        Editor.Loaded += (_, _) =>
        {
            EnsureScroller();
            UpdatePadding();
            RestyleAll();
        };
        Editor.SizeChanged += (_, _) =>
        {
            UpdatePadding();
            ScheduleGutter();
        };

        // In the dark theme the text box resets text colors when its look changes
        // (focus, mouse over), so paint again right after
        Editor.GotFocus += (_, _) => RepaintSoon();
        Editor.LostFocus += (_, _) => RepaintSoon();
        Editor.PointerEntered += (_, _) => RepaintSoon();
        Editor.PointerExited += (_, _) => RepaintSoon();
    }

    private void RepaintSoon() => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, Restyle);

    private void ScheduleRestyle(bool soon = false)
    {
        if (_restyleTimer is null) return;
        _restyleTimer.Interval = TimeSpan.FromMilliseconds(soon ? 15 : 90);
        _restyleTimer.Stop();
        _restyleTimer.Start();
    }

    private bool HighlightMarkdown => Language.IsMarkdown && SettingsService.Current.MarkdownStyling;

    private bool HighlightCode => !Language.IsMarkdown && !Language.IsPlain && SettingsService.Current.SyntaxHighlighting;

    private bool FocusActive => SettingsService.Current.FocusMode;

    private bool IsDark => ActualTheme == ElementTheme.Dark
        || ActualTheme == ElementTheme.Default && Application.Current.RequestedTheme == ApplicationTheme.Dark;

    /// <summary>Resets the look of the whole text (font, size, color, spacing), then paints what is visible.</summary>
    private void RestyleAll()
    {
        _palette = SyntaxPalette.For(IsDark);
        Doc.BatchDisplayUpdates();
        try
        {
            ITextRange all = Doc.GetRange(0, _text.Length + 1);
            ApplyBase(all.CharacterFormat);
            all.ParagraphFormat.SetLineSpacing(LineSpacingRule.Multiple, (float)Math.Clamp(SettingsService.Current.LineSpacing, 1, 3));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Restyle all", ex);
        }
        finally
        {
            Doc.ApplyDisplayUpdates();
        }
        Restyle();
    }

    private void ApplyBase(ITextCharacterFormat f)
    {
        f.Name = FirstFont(EditorFont);
        f.Size = (float)EditorFontSize;
        f.Bold = FormatEffect.Off;
        f.Italic = FormatEffect.Off;
        f.Strikethrough = FormatEffect.Off;
        f.Underline = UnderlineType.None;
        f.ForegroundColor = _palette.Text;
    }

    private static string FirstFont(string fonts) => fonts.Split(',')[0].Trim();

    /// <summary>Paints the visible part of the text.</summary>
    private void Restyle()
    {
        bool markdown = HighlightMarkdown, code = HighlightCode, focus = FocusActive;
        if (!markdown && !code && !focus) return;

        List<Token> tokens = (markdown || code) && _text.Length <= MaxHighlightLength ? Tokens() : [];
        var (from, to) = VisibleSpan();

        Doc.BatchDisplayUpdates();
        try
        {
            ApplyBase(Doc.GetRange(from, to).CharacterFormat);

            float size = (float)EditorFontSize;
            foreach (Token t in tokens)
            {
                if (t.End <= from || t.Start >= to) continue;
                ITextCharacterFormat f = Doc.GetRange(Math.Max(t.Start, from), Math.Min(t.End, to)).CharacterFormat;
                Paint(f, t.Kind, size);
            }

            if (focus)
            {
                // Everything but the paragraph with the caret fades back
                int caret = Math.Min(SelStart, _text.Length);
                int start = caret == 0 ? 0 : _text.LastIndexOf('\r', caret - 1) + 1;
                int end = _text.IndexOf('\r', caret);
                if (end < 0) end = _text.Length;
                if (start > from) Doc.GetRange(from, start).CharacterFormat.ForegroundColor = _palette.Faded;
                if (end < to) Doc.GetRange(end, to).CharacterFormat.ForegroundColor = _palette.Faded;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Restyle", ex);
        }
        finally
        {
            Doc.ApplyDisplayUpdates();
        }
    }

    private void Paint(ITextCharacterFormat f, TokenKind kind, float size)
    {
        switch (kind)
        {
            case >= TokenKind.Heading1 and <= TokenKind.Heading6:
                f.Bold = FormatEffect.On;
                f.Size = size * HeadingScale[kind - TokenKind.Heading1];
                return;
            case TokenKind.Bold:
                f.Bold = FormatEffect.On;
                return;
            case TokenKind.Italic:
                f.Italic = FormatEffect.On;
                return;
            case TokenKind.BoldItalic:
                f.Bold = FormatEffect.On;
                f.Italic = FormatEffect.On;
                return;
            case TokenKind.Strike:
                f.Strikethrough = FormatEffect.On;
                break;
            case TokenKind.CodeInline:
            case TokenKind.CodeBlock:
                f.Name = "Cascadia Mono";
                f.Size = size * 0.92f;
                break;
            case TokenKind.Comment:
            case TokenKind.Quote:
                f.Italic = FormatEffect.On;
                break;
        }
        if (_palette.ColorOf(kind) is { } color)
        {
            f.ForegroundColor = color;
        }
    }

    private List<Token> Tokens()
    {
        SyntaxLanguage language = Language;
        if (_tokensVersion != ContentVersion || _tokensLanguage != language.Id)
        {
            _tokens = language.Tokenize(_text);
            _tokensVersion = ContentVersion;
            _tokensLanguage = language.Id;
        }
        return _tokens;
    }

    /// <summary>Character range on screen, with a margin so short scrolls are already painted.</summary>
    private (int From, int To) VisibleSpan()
    {
        int length = _text.Length;
        try
        {
            if (Editor.ActualHeight > 0)
            {
                var (top, bottom) = VisibleChars();
                if (bottom >= top)
                {
                    int from = Math.Max(0, top - PaintMargin);
                    from = from == 0 ? 0 : _text.LastIndexOf('\r', Math.Min(from, length - 1)) + 1;
                    int to = Math.Min(length, bottom + PaintMargin);
                    return (from, to);
                }
            }
        }
        catch (Exception)
        {
            // fall through: paint everything
        }
        return (0, Math.Min(length, 2 * PaintMargin + 20000));
    }

    // ---------- Layout ----------

    /// <summary>Side margins for the reading column, and room above/below the text for typewriter mode.</summary>
    private void UpdatePadding()
    {
        AppSettings s = SettingsService.Current;
        double width = Editor.ActualWidth;
        double side = 32;
        if ((s.ReadableWidth || WritingModes.Zen) && width > 0)
        {
            side = Math.Max(32, (width - 760) / 2);
        }
        double top = WritingModes.Zen ? 48 : 22, bottom = 22;
        double left = Math.Max(side, GutterWidth + 4);
        var padding = new Thickness(left, top, side, bottom);
        if (Editor.Padding != padding) Editor.Padding = padding;

        // Note: extra blank room for typewriter mode is not possible here: the text box
        // only draws text inside its padding, so the line is centred whenever scrolling allows
        StatusBar.Padding = new Thickness(Math.Max(32, Math.Min(left, side)), 2, 16, 4);
        ScheduleGutter();
    }

    // ---------- Scrolling ----------

    private bool _scrollerHooked;

    /// <summary>Finds the text box's scroller and listens to it once, whoever asks first.</summary>
    private ScrollViewer? EnsureScroller()
    {
        _scroller ??= FindChild<ScrollViewer>(Editor);
        if (_scroller is not null && !_scrollerHooked)
        {
            _scrollerHooked = true;
            _scroller.ViewChanged += (_, _) =>
            {
                ScheduleRestyle(soon: true);
                SyncPreviewScroll();
                ScheduleGutter();
            };
        }
        return _scroller;
    }

    private Rect? CaretRect(int index)
    {
        try
        {
            Doc.GetRange(index, index).GetRect(PointOptions.ClientCoordinates, out Rect rect, out _);
            return new Rect(rect.X, rect.Y - ScrollY, rect.Width, rect.Height); // on screen
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Scrolls so the character is comfortably visible (about a third from the top if it was off screen).</summary>
    private void RevealIndex(int index)
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            EnsureScroller();
            if (_scroller is null || CaretRect(index) is not { } r) return;
            double view = _scroller.ViewportHeight;
            if (r.Top < 10 || r.Bottom > view - 10)
            {
                _scroller.ChangeView(null, Math.Max(0, _scroller.VerticalOffset + r.Top - view / 3), null, true);
            }
        });
    }

    private void RevealCaret() => RevealIndex(SelStart);

    /// <summary>Typewriter mode: the line being written stays in the middle of the window.</summary>
    private void CenterCaret()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            EnsureScroller();
            if (_scroller is null || CaretRect(SelStart) is not { } r) return;
            double target = _scroller.VerticalOffset + r.Top + r.Height / 2 - _scroller.ViewportHeight / 2;
            if (Math.Abs(target - _scroller.VerticalOffset) > 2)
            {
                _scroller.ChangeView(null, Math.Max(0, target), null, false);
            }
        });
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T found) return found;
            if (FindChild<T>(child) is { } deeper) return deeper;
        }
        return null;
    }
}
