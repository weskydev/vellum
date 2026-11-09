using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Windows.Foundation;
using Windows.UI;

namespace Vellum.Controls;

/// <summary>Line numbers in the left margin and a soft band behind the current line.</summary>
public sealed partial class EditorView
{
    private const double GutterGap = 14;

    private int[] _lineStarts = [0];
    private int _lineStartsVersion = -1;
    private readonly List<TextBlock> _numberPool = [];
    private bool _gutterQueued;

    private bool ShowLineNumbers => SettingsService.Current.LineNumbers && !WritingModes.Zen;

    private bool ShowLineBand => SettingsService.Current.HighlightCurrentLine && !WritingModes.Zen;

    /// <summary>Room the numbers need on the left of the text (0 when they are off).</summary>
    private double GutterWidth
    {
        get
        {
            if (!ShowLineNumbers) return 0;
            int digits = Math.Max(3, LineStarts().Length.ToString().Length);
            return digits * NumberFontSize * 0.62 + GutterGap + 12;
        }
    }

    private double NumberFontSize => Math.Max(10, EditorFontSize * 0.82);

    private int[] LineStarts()
    {
        if (_lineStartsVersion == ContentVersion) return _lineStarts;
        var starts = new List<int>(_lineStarts.Length + 8) { 0 };
        string text = _text;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\r') starts.Add(i + 1);
        }
        _lineStarts = [.. starts];
        _lineStartsVersion = ContentVersion;
        return _lineStarts;
    }

    /// <summary>Index of the line that holds the character (0-based).</summary>
    private int LineOf(int index)
    {
        int[] starts = LineStarts();
        int i = Array.BinarySearch(starts, index);
        return i >= 0 ? i : ~i - 1;
    }

    private void ScheduleGutter()
    {
        if (_gutterQueued) return;
        _gutterQueued = true;
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            _gutterQueued = false;
            UpdateGutter();
        });
    }

    // The text box measures from the top of the whole text, not from what is on screen,
    // so every position is shifted by how far it is scrolled
    private double ScrollX => _scroller?.HorizontalOffset ?? 0;

    private double ScrollY => _scroller?.VerticalOffset ?? 0;

    /// <summary>Caret box of a character, on screen in the editor's own coordinates (the text box leaves out its padding).</summary>
    private Rect RectAt(int index)
    {
        Doc.GetRange(index, index).GetRect(PointOptions.ClientCoordinates, out Rect rect, out _);
        return new Rect(rect.X + Editor.Padding.Left - ScrollX, rect.Y + Editor.Padding.Top - ScrollY, rect.Width, rect.Height);
    }

    /// <summary>First and last character on screen.</summary>
    private (int Top, int Bottom) VisibleChars()
    {
        double x = ScrollX, y = ScrollY;
        int top = Doc.GetRangeFromPoint(new Point(x, y), PointOptions.ClientCoordinates).StartPosition;
        int bottom = Doc.GetRangeFromPoint(new Point(x + Editor.ActualWidth, y + Editor.ActualHeight), PointOptions.ClientCoordinates).EndPosition;
        return (top, bottom);
    }

    private void UpdateGutter()
    {
        // Preview only: nothing drawn for the hidden text may show over the page
        Visibility drawn = Editor.Visibility;
        if (Underlay.Visibility != drawn) Underlay.Visibility = drawn;
        if (Gutter.Visibility != drawn) Gutter.Visibility = drawn;
        UpdateNumbers();
        try
        {
            if (Editor.ActualHeight <= 0 || Editor.Visibility != Visibility.Visible) return;
            var (topChar, bottomChar) = VisibleChars();
            UpdateMarks(topChar, bottomChar, AccentPalette.Resolve(SettingsService.Current.Accent));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Marks", ex);
        }
    }

    private void UpdateNumbers()
    {
        bool numbers = ShowLineNumbers, band = ShowLineBand;
        if ((!numbers && !band) || Editor.ActualHeight <= 0 || Editor.Visibility != Visibility.Visible)
        {
            CurrentLineBand.Visibility = Visibility.Collapsed;
            GutterLine.Visibility = Visibility.Collapsed;
            foreach (TextBlock t in _numberPool) t.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            Color accent = AccentPalette.Resolve(SettingsService.Current.Accent);
            int caretLine = LineOf(Math.Min(SelStart, _text.Length));
            int[] starts = LineStarts();

            // The band behind the line with the caret (all of it, if it wraps)
            if (band)
            {
                int ls = starts[caretLine];
                int le = caretLine + 1 < starts.Length ? starts[caretLine + 1] - 1 : _text.Length;
                Rect top = RectAt(ls), bottom = RectAt(le);
                double y = top.Top, h = bottom.Bottom - top.Top;
                if (h > 0 && y + h > 0 && y < Editor.ActualHeight)
                {
                    // Soft fill with a faint outline, so the line glows a little
                    CurrentLineBand.Fill = new SolidColorBrush(Color.FromArgb(IsDark ? (byte)18 : (byte)22, accent.R, accent.G, accent.B));
                    CurrentLineBand.Stroke = new SolidColorBrush(Color.FromArgb(IsDark ? (byte)46 : (byte)52, accent.R, accent.G, accent.B));
                    CurrentLineBand.StrokeThickness = 1;
                    CurrentLineBand.Width = Math.Max(0, Editor.ActualWidth - 12);
                    CurrentLineBand.Height = h;
                    Canvas.SetLeft(CurrentLineBand, 6);
                    Canvas.SetTop(CurrentLineBand, y);
                    CurrentLineBand.Visibility = Visibility.Visible;
                }
                else
                {
                    CurrentLineBand.Visibility = Visibility.Collapsed;
                }
            }
            else
            {
                CurrentLineBand.Visibility = Visibility.Collapsed;
            }

            // Hairline between the numbers and the text
            if (numbers)
            {
                Canvas.SetLeft(GutterLine, Math.Round(Editor.Padding.Left - GutterGap / 2));
                Canvas.SetTop(GutterLine, 0);
                GutterLine.Height = Editor.ActualHeight;
                GutterLine.Visibility = Visibility.Visible;
            }
            else
            {
                GutterLine.Visibility = Visibility.Collapsed;
            }

            int used = 0;
            if (numbers)
            {
                var (topChar, bottomChar) = VisibleChars();
                double right = Editor.Padding.Left - GutterGap;
                double size = NumberFontSize;
                // Same as StatusTextBrush, but for this view's theme (app resources follow the system theme)
                var dim = new SolidColorBrush(IsDark ? Color.FromArgb(0x8F, 0xFF, 0xFF, 0xFF) : Color.FromArgb(0x8C, 0x1D, 0x1B, 0x29));
                var current = new SolidColorBrush(accent);

                for (int line = LineOf(topChar); line < starts.Length && starts[line] <= bottomChar && used < 400; line++)
                {
                    Rect r = RectAt(starts[line]);
                    if (r.Bottom < 0) continue;
                    if (r.Top > Editor.ActualHeight) break;

                    TextBlock t = NumberBlock(used++);
                    t.Text = (line + 1).ToString();
                    t.FontSize = size;
                    t.Foreground = line == caretLine ? current : dim;
                    t.Width = right;
                    // Centered on the first row of the line (r is the caret box at the line start)
                    Canvas.SetTop(t, r.Top + (r.Height - size * 1.33) / 2);
                    t.Visibility = Visibility.Visible;
                }
            }
            for (int i = used; i < _numberPool.Count; i++) _numberPool[i].Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Gutter", ex);
        }
    }

    private TextBlock NumberBlock(int i)
    {
        while (_numberPool.Count <= i)
        {
            var t = new TextBlock
            {
                TextAlignment = TextAlignment.Right,
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
            };
            Canvas.SetLeft(t, 0);
            _numberPool.Add(t);
            Gutter.Children.Add(t);
        }
        return _numberPool[i];
    }
}
