using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Vellum.Services;
using Windows.Foundation;
using Windows.UI;

namespace Vellum.Controls;

/// <summary>
/// Marks drawn around the text: bookmarks in the margin, every copy of the selected word,
/// the matching bracket, plus auto-closing brackets and read-only mode.
/// </summary>
public sealed partial class EditorView
{
    private const string Openers = "([{";
    private const string Closers = ")]}";

    private readonly List<int> _bookmarks = [];      // line indices, sorted
    private readonly List<Rectangle> _markPool = []; // word / bracket marks under the text
    private readonly List<Rectangle> _bookmarkPool = [];
    private int _pendingCloser = -1;                 // where an auto-inserted closer sits
    private bool _readOnly;
    private bool _autoEditing;                       // true while auto-close changes the text itself

    // ---------- Read-only ----------

    public bool IsReadOnly
    {
        get => _readOnly;
        set
        {
            if (_readOnly == value) return;
            _readOnly = value;
            Editor.IsReadOnly = value;
            ReadOnlyButton.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            DirtyChanged?.Invoke(this, EventArgs.Empty); // the tab shows a lock
            Flash(Strings.Get(value ? "ReadOnlyOn" : "ReadOnlyOff"));
        }
    }

    /// <summary>True (and a note in the status line) when the text must not change.</summary>
    private bool BlockedByReadOnly()
    {
        if (!_readOnly) return false;
        Flash(Strings.Get("ReadOnlyNote"));
        return true;
    }

    private void ReadOnlyButton_Click(object sender, RoutedEventArgs e) => IsReadOnly = false;

    // ---------- Following edits ----------

    /// <summary>Called for every change of the text: keeps bookmarks on their lines and closes brackets.</summary>
    private void OnEdit(string before, string after, bool typed)
    {
        if (_bookmarks.Count == 0 && _pendingCloser < 0 && !typed) return;

        // The changed part: same beginning and end, different middle
        int prefix = 0, max = Math.Min(before.Length, after.Length);
        while (prefix < max && before[prefix] == after[prefix]) prefix++;
        int suffix = 0;
        while (suffix < max - prefix && before[^(suffix + 1)] == after[^(suffix + 1)]) suffix++;
        string removed = before[prefix..(before.Length - suffix)];
        string inserted = after[prefix..(after.Length - suffix)];

        // "]" typed in front of "]" looks like it went after it; the caret tells where it really went
        if (typed && removed.Length == 0 && inserted.Length == 1)
        {
            int real = SelStart - 1;
            if (real >= 0 && real < prefix && after.AsSpan(real, prefix - real + 1).IndexOfAnyExcept(inserted[0]) < 0)
            {
                prefix = real;
            }
        }

        ShiftBookmarks(before, prefix, removed, inserted);

        if (_pendingCloser >= 0)
        {
            if (_pendingCloser >= prefix + removed.Length) _pendingCloser += inserted.Length - removed.Length;
            else if (_pendingCloser >= prefix) _pendingCloser = -1;
        }

        if (typed && removed.Length == 0 && inserted.Length == 1) AfterTyped(after, prefix, inserted[0]);
    }

    private void ShiftBookmarks(string before, int at, string removed, string inserted)
    {
        if (_bookmarks.Count == 0) return;
        int removedBreaks = removed.Count(c => c == '\r');
        int delta = inserted.Count(c => c == '\r') - removedBreaks;
        if (delta == 0) return;

        int line = 0;
        for (int i = 0; i < at && i < before.Length; i++)
        {
            if (before[i] == '\r') line++;
        }
        for (int i = 0; i < _bookmarks.Count; i++)
        {
            int b = _bookmarks[i];
            if (b > line + removedBreaks) _bookmarks[i] = b + delta;
            else if (b > line) _bookmarks[i] = line; // its line was removed: keep it on the joined line
        }
        var unique = _bookmarks.Distinct().Order().ToList();
        _bookmarks.Clear();
        _bookmarks.AddRange(unique);
    }

    private void AfterTyped(string text, int at, char c)
    {
        if (!SettingsService.Current.AutoCloseBrackets || Language.IsPlain) return;
        char next = at + 1 < text.Length ? text[at + 1] : '\0';

        // Typing the closer that was added for you just steps over it
        if (at + 1 == _pendingCloser && next == c && (Closers.Contains(c) || c == '"'))
        {
            _pendingCloser = -1;
            Later(() =>
            {
                Doc.GetRange(at + 1, at + 2).Text = "";
                SyncText(record: true, group: true);
                Select(at + 1, 0);
            });
            return;
        }

        bool roomAfter = next is '\0' or ' ' or '\t' or '\r' or ',' or ';' || Closers.Contains(next);
        if (!roomAfter) return;

        char closer;
        int open = Openers.IndexOf(c);
        if (open >= 0)
        {
            closer = Closers[open];
        }
        else if (c == '"' && !Language.IsMarkdown)
        {
            char prev = at > 0 ? text[at - 1] : '\0';
            if (char.IsLetterOrDigit(prev) || prev == '"') return;
            closer = '"';
        }
        else
        {
            return;
        }

        Later(() =>
        {
            Doc.GetRange(at + 1, at + 1).Text = closer.ToString();
            SyncText(record: true, group: true);
            Select(at + 1, 0);
            _pendingCloser = at + 1;
        });
    }

    /// <summary>Runs right after the current key press has been handled.</summary>
    private void Later(Action action) => DispatcherQueue.TryEnqueue(DispatcherQueuePriority.High, () =>
    {
        _autoEditing = true;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Auto-close", ex);
        }
        finally
        {
            _autoEditing = false;
        }
    });

    // ---------- Bookmarks ----------

    public void ToggleBookmark()
    {
        int line = LineOf(Math.Min(SelStart, _text.Length));
        int i = _bookmarks.BinarySearch(line);
        if (i >= 0) _bookmarks.RemoveAt(i);
        else _bookmarks.Insert(~i, line);
        ScheduleGutter();
    }

    public void GoToBookmark(bool next)
    {
        if (_bookmarks.Count == 0)
        {
            Flash(Strings.Get("BookmarksNone"));
            return;
        }
        int line = LineOf(Math.Min(SelStart, _text.Length));
        int target = next
            ? _bookmarks.FirstOrDefault(b => b > line, _bookmarks[0])
            : _bookmarks.LastOrDefault(b => b < line, _bookmarks[^1]);
        int[] starts = LineStarts();
        Select(starts[Math.Min(target, starts.Length - 1)], 0);
        ScrollCaretIntoView();
    }

    public void ClearBookmarks()
    {
        _bookmarks.Clear();
        ScheduleGutter();
    }

    // ---------- Brackets ----------

    /// <summary>Position of the bracket next to the caret and of its partner, or null.</summary>
    private (int At, int Match)? BracketPair()
    {
        int caret = Math.Min(SelStart, _text.Length);
        foreach (int at in new[] { caret - 1, caret })
        {
            if (at < 0 || at >= _text.Length) continue;
            char c = _text[at];
            int open = Openers.IndexOf(c), close = Closers.IndexOf(c);
            if (open < 0 && close < 0) continue;
            int step = open >= 0 ? 1 : -1;
            char self = c, other = open >= 0 ? Closers[open] : Openers[close];
            int depth = 0;
            for (int i = at, n = 0; i >= 0 && i < _text.Length && n < 100_000; i += step, n++)
            {
                if (_text[i] == self) depth++;
                else if (_text[i] == other && --depth == 0) return (at, i);
            }
            return null;
        }
        return null;
    }

    public void GoToMatchingBracket()
    {
        if (BracketPair() is not { } pair)
        {
            Flash(Strings.Get("BracketNone"));
            return;
        }
        // Land on the same side of the partner as the caret was of the bracket
        bool after = SelStart > pair.At;
        Select(pair.Match + (after ? 1 : 0), 0);
        ScrollCaretIntoView();
    }

    // ---------- Drawing ----------

    private void UpdateMarks(int topChar, int bottomChar, Color accent)
    {
        int usedMarks = 0;
        bool smart = SettingsService.Current.SmartHighlight && !WritingModes.Zen;

        if (smart && SelectedWord() is { } word)
        {
            var fill = new SolidColorBrush(Color.FromArgb(IsDark ? (byte)52 : (byte)44, accent.R, accent.G, accent.B));
            int from = Math.Max(0, topChar - word.Length), to = Math.Min(_text.Length, bottomChar + word.Length);
            for (int i = _text.IndexOf(word, from, StringComparison.Ordinal);
                 i >= 0 && i + word.Length <= to && usedMarks < 300;
                 i = _text.IndexOf(word, i + word.Length, StringComparison.Ordinal))
            {
                if (!IsWholeWord(i, word.Length)) continue;
                if (DrawMark(usedMarks, i, i + word.Length, fill, null)) usedMarks++;
            }
        }

        if (smart && SelLength == 0 && !Language.IsPlain && BracketPair() is { } pair)
        {
            var stroke = new SolidColorBrush(Color.FromArgb(200, accent.R, accent.G, accent.B));
            var fill = new SolidColorBrush(Color.FromArgb(30, accent.R, accent.G, accent.B));
            if (DrawMark(usedMarks, pair.At, pair.At + 1, fill, stroke)) usedMarks++;
            if (DrawMark(usedMarks, pair.Match, pair.Match + 1, fill, stroke)) usedMarks++;
        }

        for (int i = usedMarks; i < _markPool.Count; i++) _markPool[i].Visibility = Visibility.Collapsed;

        // Bookmarks: a small accent pill at the very left of the line
        int usedBookmarks = 0;
        if (_bookmarks.Count > 0 && !WritingModes.Zen)
        {
            int[] starts = LineStarts();
            int firstLine = LineOf(topChar), lastLine = LineOf(Math.Min(bottomChar, _text.Length));
            var brush = new SolidColorBrush(accent);
            foreach (int line in _bookmarks)
            {
                if (line < firstLine || line > lastLine || line >= starts.Length) continue;
                Rect r = RectAt(starts[line]);
                Rectangle pill = Pooled(_bookmarkPool, Gutter, usedBookmarks++);
                pill.Fill = brush;
                pill.Stroke = null;
                pill.Width = 4;
                pill.Height = Math.Max(6, r.Height - 6);
                pill.RadiusX = pill.RadiusY = 2;
                Canvas.SetLeft(pill, 8);
                Canvas.SetTop(pill, r.Top + 3);
                pill.Visibility = Visibility.Visible;
            }
        }
        for (int i = usedBookmarks; i < _bookmarkPool.Count; i++) _bookmarkPool[i].Visibility = Visibility.Collapsed;
    }

    /// <summary>Draws a rounded box behind the characters; false when they are off screen or wrap.</summary>
    private bool DrawMark(int index, int start, int end, Brush fill, Brush? stroke)
    {
        Rect a = RectAt(start), b = RectAt(end);
        if (Math.Abs(a.Top - b.Top) > 1 || b.X <= a.X) return false;
        if (a.Bottom < 0 || a.Top > Editor.ActualHeight) return false;
        Rectangle mark = Pooled(_markPool, Underlay, index);
        mark.Fill = fill;
        mark.Stroke = stroke;
        mark.StrokeThickness = stroke is null ? 0 : 1;
        mark.RadiusX = mark.RadiusY = 3;
        mark.Width = b.X - a.X + 2;
        mark.Height = a.Height;
        Canvas.SetLeft(mark, a.X - 1);
        Canvas.SetTop(mark, a.Top);
        mark.Visibility = Visibility.Visible;
        return true;
    }

    private static Rectangle Pooled(List<Rectangle> pool, Canvas host, int i)
    {
        while (pool.Count <= i)
        {
            var r = new Rectangle { IsHitTestVisible = false };
            pool.Add(r);
            host.Children.Add(r);
        }
        return pool[i];
    }

    /// <summary>The selected text when it is one whole word (2–100 letters, digits or _).</summary>
    private string? SelectedWord()
    {
        int s = Math.Min(SelStart, _text.Length), e = SelEnd;
        int length = e - s;
        if (length is < 2 or > 100) return null;
        for (int i = s; i < e; i++)
        {
            if (!IsWordChar(_text[i])) return null;
        }
        return IsWholeWord(s, length) ? _text[s..e] : null;
    }

    private bool IsWholeWord(int start, int length) =>
        (start == 0 || !IsWordChar(_text[start - 1])) &&
        (start + length >= _text.Length || !IsWordChar(_text[start + length]));

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
}
