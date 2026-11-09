using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Vellum.Services;

namespace Vellum.Controls;

/// <summary>Editing tools (Notepad++ style): line operations, case, comments, encoders, web search.</summary>
public enum EditTool
{
    DuplicateLine,
    DeleteLine,
    MoveLineUp,
    MoveLineDown,
    SortAscending,
    SortDescending,
    RemoveDuplicateLines,
    RemoveEmptyLines,
    TrimTrailingSpaces,
    JoinLines,
    ReverseLines,
    UpperCase,
    LowerCase,
    TitleCase,
    SentenceCase,
    InvertCase,
    ToggleComment,
    InsertDateTime,
    Base64Encode,
    Base64Decode,
    UrlEncode,
    UrlDecode,
    SearchWeb,
    InsertLineAbove,
    InsertLineBelow,
    SortNumericAscending,
    SortNumericDescending,
    ShuffleLines,
    RemoveConsecutiveDuplicates,
    TrimLeadingSpaces,
    TrimBothSpaces,
    TabsToSpaces,
    SpacesToTabs,
    RandomCase,
    FormatJson,
    MinifyJson,
    HashMd5,
    HashSha1,
    HashSha256,
    HashSha512,
    ToggleBookmark,
    NextBookmark,
    PreviousBookmark,
    ClearBookmarks,
    MatchingBracket,
    ToggleReadOnly,
}

public sealed partial class EditorView
{
    /// <summary>Menu text key, icon and shortcut for each tool; the same list feeds the palette and menus.</summary>
    public static readonly (EditTool Tool, string Key, string Glyph, string Keys)[] ToolInfo =
    [
        (EditTool.DuplicateLine, "ToolDuplicateLine", "", "Ctrl+D"),
        (EditTool.DeleteLine, "ToolDeleteLine", "", "Ctrl+Shift+K"),
        (EditTool.MoveLineUp, "ToolMoveLineUp", "", "Alt+↑"),
        (EditTool.MoveLineDown, "ToolMoveLineDown", "", "Alt+↓"),
        (EditTool.SortAscending, "ToolSortAsc", "", ""),
        (EditTool.SortDescending, "ToolSortDesc", "", ""),
        (EditTool.RemoveDuplicateLines, "ToolRemoveDuplicates", "", ""),
        (EditTool.RemoveEmptyLines, "ToolRemoveEmpty", "", ""),
        (EditTool.TrimTrailingSpaces, "ToolTrimTrailing", "", ""),
        (EditTool.JoinLines, "ToolJoinLines", "", "Ctrl+J"),
        (EditTool.ReverseLines, "ToolReverseLines", "", ""),
        (EditTool.UpperCase, "ToolUpper", "", "Ctrl+Shift+U"),
        (EditTool.LowerCase, "ToolLower", "", "Ctrl+U"),
        (EditTool.TitleCase, "ToolTitle", "", ""),
        (EditTool.SentenceCase, "ToolSentence", "", ""),
        (EditTool.InvertCase, "ToolInvert", "", ""),
        (EditTool.ToggleComment, "ToolComment", "", "Ctrl+/"),
        (EditTool.InsertDateTime, "ToolDateTime", "", "F5"),
        (EditTool.Base64Encode, "ToolBase64Encode", "", ""),
        (EditTool.Base64Decode, "ToolBase64Decode", "", ""),
        (EditTool.UrlEncode, "ToolUrlEncode", "", ""),
        (EditTool.UrlDecode, "ToolUrlDecode", "", ""),
        (EditTool.SearchWeb, "ToolSearchWeb", "\uE721", "Ctrl+E"),
        (EditTool.InsertLineAbove, "ToolLineAbove", "\uE74A", "Ctrl+Shift+Enter"),
        (EditTool.InsertLineBelow, "ToolLineBelow", "\uE74B", "Ctrl+Enter"),
        (EditTool.SortNumericAscending, "ToolSortNumAsc", "\uE8CB", ""),
        (EditTool.SortNumericDescending, "ToolSortNumDesc", "\uE8CB", ""),
        (EditTool.ShuffleLines, "ToolShuffle", "\uE8B1", ""),
        (EditTool.RemoveConsecutiveDuplicates, "ToolRemoveConsecutive", "\uE74D", ""),
        (EditTool.TrimLeadingSpaces, "ToolTrimLeading", "\uE8E4", ""),
        (EditTool.TrimBothSpaces, "ToolTrimBoth", "\uE8E3", ""),
        (EditTool.TabsToSpaces, "ToolTabsToSpaces", "\uE8AB", ""),
        (EditTool.SpacesToTabs, "ToolSpacesToTabs", "\uE8AB", ""),
        (EditTool.RandomCase, "ToolRandomCase", "\uE8D2", ""),
        (EditTool.FormatJson, "ToolFormatJson", "\uE943", ""),
        (EditTool.MinifyJson, "ToolMinifyJson", "\uE943", ""),
        (EditTool.HashMd5, "ToolHashMd5", "\uE928", ""),
        (EditTool.HashSha1, "ToolHashSha1", "\uE928", ""),
        (EditTool.HashSha256, "ToolHashSha256", "\uE928", ""),
        (EditTool.HashSha512, "ToolHashSha512", "\uE928", ""),
        (EditTool.ToggleBookmark, "ToolBookmark", "\uE8A4", "Ctrl+F2"),
        (EditTool.NextBookmark, "ToolBookmarkNext", "\uE8A4", "F2"),
        (EditTool.PreviousBookmark, "ToolBookmarkPrev", "\uE8A4", "Shift+F2"),
        (EditTool.ClearBookmarks, "ToolBookmarkClear", "\uE8A4", "Ctrl+Shift+F2"),
        (EditTool.MatchingBracket, "ToolMatchingBracket", "\uE943", "Ctrl+B"),
        (EditTool.ToggleReadOnly, "ToolReadOnly", "\uE72E", ""),
    ];

    public bool CanComment => TextTools.CommentFor(Language.Id) is not (null, null, null);

    public void Apply(EditTool tool)
    {
        try
        {
            switch (tool)
            {
                case EditTool.DuplicateLine: DuplicateLine(); break;
                case EditTool.DeleteLine: DeleteLines(); break;
                case EditTool.MoveLineUp: MoveLines(up: true); break;
                case EditTool.MoveLineDown: MoveLines(up: false); break;
                case EditTool.SortAscending: TransformLines(l => TextTools.SortLines(l, descending: false), wholeIfEmpty: true); break;
                case EditTool.SortDescending: TransformLines(l => TextTools.SortLines(l, descending: true), wholeIfEmpty: true); break;
                case EditTool.RemoveDuplicateLines: TransformLines(TextTools.RemoveDuplicates, wholeIfEmpty: true); break;
                case EditTool.RemoveEmptyLines: TransformLines(TextTools.RemoveEmpty, wholeIfEmpty: true); break;
                case EditTool.TrimTrailingSpaces: TransformLines(TextTools.TrimTrailing, wholeIfEmpty: true); break;
                case EditTool.ReverseLines: TransformLines(TextTools.Reverse, wholeIfEmpty: true); break;
                case EditTool.JoinLines: JoinLines(); break;
                case EditTool.UpperCase: ChangeCase(CaseKind.Upper); break;
                case EditTool.LowerCase: ChangeCase(CaseKind.Lower); break;
                case EditTool.TitleCase: ChangeCase(CaseKind.Title); break;
                case EditTool.SentenceCase: ChangeCase(CaseKind.Sentence); break;
                case EditTool.InvertCase: ChangeCase(CaseKind.Invert); break;
                case EditTool.ToggleComment: ToggleComment(); break;
                case EditTool.InsertDateTime: InsertDateTime(); break;
                case EditTool.Base64Encode: ConvertSelection(TextTools.ToBase64); break;
                case EditTool.Base64Decode: ConvertSelection(TextTools.FromBase64); break;
                case EditTool.UrlEncode: ConvertSelection(TextTools.ToUrl); break;
                case EditTool.UrlDecode: ConvertSelection(TextTools.FromUrl); break;
                case EditTool.SearchWeb: SearchWeb(); break;
                case EditTool.InsertLineAbove: InsertLine(above: true); break;
                case EditTool.InsertLineBelow: InsertLine(above: false); break;
                case EditTool.SortNumericAscending: TransformLines(l => TextTools.SortNumeric(l, descending: false), wholeIfEmpty: true); break;
                case EditTool.SortNumericDescending: TransformLines(l => TextTools.SortNumeric(l, descending: true), wholeIfEmpty: true); break;
                case EditTool.ShuffleLines: TransformLines(TextTools.Shuffle, wholeIfEmpty: true); break;
                case EditTool.RemoveConsecutiveDuplicates: TransformLines(TextTools.RemoveConsecutiveDuplicates, wholeIfEmpty: true); break;
                case EditTool.TrimLeadingSpaces: TransformLines(TextTools.TrimLeading, wholeIfEmpty: true); break;
                case EditTool.TrimBothSpaces: TransformLines(TextTools.TrimBoth, wholeIfEmpty: true); break;
                case EditTool.TabsToSpaces: TransformLines(l => TextTools.TabsToSpaces(l, TabWidth), wholeIfEmpty: true); break;
                case EditTool.SpacesToTabs: TransformLines(l => TextTools.SpacesToTabs(l, TabWidth), wholeIfEmpty: true); break;
                case EditTool.RandomCase: ChangeCase(CaseKind.Random); break;
                case EditTool.FormatJson: ReformatJson(indented: true); break;
                case EditTool.MinifyJson: ReformatJson(indented: false); break;
                case EditTool.HashMd5: CopyHash("MD5"); break;
                case EditTool.HashSha1: CopyHash("SHA-1"); break;
                case EditTool.HashSha256: CopyHash("SHA-256"); break;
                case EditTool.HashSha512: CopyHash("SHA-512"); break;
                case EditTool.ToggleBookmark: ToggleBookmark(); break;
                case EditTool.NextBookmark: GoToBookmark(next: true); break;
                case EditTool.PreviousBookmark: GoToBookmark(next: false); break;
                case EditTool.ClearBookmarks: ClearBookmarks(); break;
                case EditTool.MatchingBracket: GoToMatchingBracket(); break;
                case EditTool.ToggleReadOnly: IsReadOnly = !IsReadOnly; break;
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Tool " + tool, ex);
        }
        FocusEditor();
    }

    // ---------- Ranges ----------

    private int SelEnd => Math.Min(Doc.Selection.EndPosition, _text.Length);

    private int LineStartAt(int index) => index <= 0 ? 0 : _text.LastIndexOf('\r', Math.Min(index, _text.Length) - 1) + 1;

    private int LineEndAt(int index)
    {
        int end = _text.IndexOf('\r', Math.Min(index, _text.Length));
        return end < 0 ? _text.Length : end;
    }

    /// <summary>Whole lines touched by the selection, without the final line break.</summary>
    private (int Start, int End) SelectedLines()
    {
        int s = Math.Min(SelStart, _text.Length), e = SelEnd;
        // A selection that ends at the very start of a line does not include that line
        if (e > s && _text[e - 1] == '\r') e--;
        return (LineStartAt(s), LineEndAt(e));
    }

    /// <summary>The selection, or the word at the caret when nothing is selected.</summary>
    private (int Start, int End) SelectionOrWord()
    {
        int s = Math.Min(SelStart, _text.Length), e = SelEnd;
        if (e > s) return (s, e);
        static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';
        while (s > 0 && IsWordChar(_text[s - 1])) s--;
        while (e < _text.Length && IsWordChar(_text[e])) e++;
        return (s, e);
    }

    private void ReplaceAndSelect(int start, int end, string replacement, bool selectResult)
    {
        int caret = SelStart;
        ReplaceRange(start, end - start, replacement);
        if (selectResult) Select(start, replacement.Length);
        else Select(Math.Min(caret, _text.Length), 0);
    }

    // ---------- Line tools ----------

    private void TransformLines(Func<string[], string[]> change, bool wholeIfEmpty)
    {
        bool whole = wholeIfEmpty && SelLength == 0;
        var (start, end) = whole ? (0, _text.Length) : SelectedLines();
        string block = _text[start..end];
        string result = string.Join('\r', change(block.Split('\r')));
        if (result == block)
        {
            Flash(Strings.Get("ToolNothingChanged"));
            return;
        }
        ReplaceAndSelect(start, end, result, selectResult: !whole);
    }

    private void DuplicateLine()
    {
        if (SelLength > 0)
        {
            int s = SelStart, e = SelEnd;
            string copy = _text[s..e];
            ReplaceRange(e, 0, copy);
            Select(e, copy.Length);
            return;
        }
        int caret = SelStart;
        var (start, end) = SelectedLines();
        string line = _text[start..end];
        ReplaceRange(end, 0, "\r" + line);
        Select(caret + line.Length + 1, 0);
        ScrollCaretIntoView();
    }

    private void DeleteLines()
    {
        var (start, end) = SelectedLines();
        bool last = end >= _text.Length;
        if (!last) end++;              // take the line break after it
        else if (start > 0) start--;   // last line: take the break before it
        ReplaceRange(start, end - start, "");
        Select(last ? LineStartAt(start) : start, 0);
    }

    private void MoveLines(bool up)
    {
        var (start, end) = SelectedLines();
        int selFrom = SelStart - start, selLength = SelLength;
        string block = _text[start..end];
        if (up)
        {
            if (start == 0) return;
            int prevStart = LineStartAt(start - 1);
            string prev = _text[prevStart..(start - 1)];
            ReplaceRange(prevStart, end - prevStart, block + "\r" + prev);
            Select(prevStart + selFrom, selLength);
        }
        else
        {
            if (end >= _text.Length) return;
            int nextEnd = LineEndAt(end + 1);
            string next = _text[(end + 1)..nextEnd];
            ReplaceRange(start, nextEnd - start, next + "\r" + block);
            Select(start + next.Length + 1 + selFrom, selLength);
        }
        ScrollCaretIntoView();
    }

    private void JoinLines()
    {
        var (start, end) = SelectedLines();
        if (SelLength == 0 || !_text[start..end].Contains('\r'))
        {
            // Nothing selected: join this line with the next one
            if (end >= _text.Length) return;
            end = LineEndAt(end + 1);
        }
        string result = TextTools.Join(_text[start..end].Split('\r'));
        ReplaceAndSelect(start, end, result, selectResult: false);
        Select(start + result.Length, 0);
    }

    // ---------- Case, comments, encoders ----------

    private void ChangeCase(CaseKind kind)
    {
        var (s, e) = SelectionOrWord();
        if (e <= s) return;
        bool hadSelection = SelLength > 0;
        string result = TextTools.ChangeCase(_text[s..e], kind);
        if (result == _text[s..e]) return;
        int caret = SelStart;
        ReplaceRange(s, e - s, result);
        if (hadSelection) Select(s, result.Length);
        else Select(caret, 0);
    }

    private void ToggleComment()
    {
        var (line, open, close) = TextTools.CommentFor(Language.Id);
        if (line is not null)
        {
            TransformLines(l => TextTools.ToggleLineComment(l, line), wholeIfEmpty: false);
        }
        else if (open is not null && close is not null)
        {
            var (start, end) = SelLength > 0 ? (SelStart, SelEnd) : SelectedLines();
            string result = TextTools.ToggleBlockComment(_text[start..end], open, close);
            ReplaceAndSelect(start, end, result, selectResult: SelLength > 0);
        }
        else
        {
            Flash(Strings.Get("ToolNoComments"));
        }
    }

    private void InsertDateTime()
    {
        DateTime now = DateTime.Now;
        string stamp = $"{now.ToString("t", CultureInfo.CurrentCulture)} {now.ToString("d", CultureInfo.CurrentCulture)}";
        ReplaceRange(SelStart, SelLength, stamp);
    }

    private void ConvertSelection(Func<string, string?> convert)
    {
        if (SelLength == 0)
        {
            Flash(Strings.Get("ToolSelectFirst"));
            return;
        }
        int s = SelStart, e = SelEnd;
        // Line breaks are converted the way they are saved in the file
        string newline = Document.LineEnding switch { Models.LineEnding.LF => "\n", Models.LineEnding.CR => "\r", _ => "\r\n" };
        string? result = convert(_text[s..e].Replace("\r", newline));
        if (result is null)
        {
            Flash(Strings.Get("ToolCannotDecode"));
            return;
        }
        result = TextFileService.ToEditorText(result);
        ReplaceRange(s, e - s, result);
        Select(s, result.Length);
    }

    private async void SearchWeb()
    {
        var (s, e) = SelectionOrWord();
        string query = e > s ? _text[s..e].Trim() : "";
        if (query.Length == 0)
        {
            Flash(Strings.Get("ToolSelectFirst"));
            return;
        }
        if (query.Length > 500) query = query[..500];
        await Windows.System.Launcher.LaunchUriAsync(TextTools.SearchUri(SettingsService.Current.SearchEngine, query));
    }

    private int TabWidth => SettingsService.Current.TabSize > 0 ? SettingsService.Current.TabSize : 4;

    /// <summary>Ctrl+Enter / Ctrl+Shift+Enter: a new line below / above, keeping the indent, wherever the caret is.</summary>
    private void InsertLine(bool above)
    {
        var (start, end) = SelectedLines();
        string line = _text[start..end];
        string indent = line[..(line.Length - line.TrimStart(' ', '\t').Length)];
        if (above)
        {
            ReplaceRange(start, 0, indent + "\r");
            Select(start + indent.Length, 0);
        }
        else
        {
            ReplaceRange(end, 0, "\r" + indent);
        }
        ScrollCaretIntoView();
    }

    /// <summary>Formats or minifies the selection (or the whole text) as JSON.</summary>
    private void ReformatJson(bool indented)
    {
        bool whole = SelLength == 0;
        int s = whole ? 0 : SelStart, e = whole ? _text.Length : SelEnd;
        string? result = TextTools.FormatJson(_text[s..e], indented, IndentUnit);
        if (result is null)
        {
            Flash(Strings.Get("ToolNotJson"));
            return;
        }
        if (result == _text[s..e]) return;
        ReplaceAndSelect(s, e, result, selectResult: !whole);
    }

    /// <summary>Copies the hash of the selection (or the whole text, as saved: UTF-8 with the file's line breaks).</summary>
    private void CopyHash(string algorithm)
    {
        string source = SelLength > 0 ? _text[SelStart..SelEnd] : _text;
        string newline = Document.LineEnding switch { Models.LineEnding.LF => "\n", Models.LineEnding.CR => "\r", _ => "\r\n" };
        string hash = TextTools.Hash(source.Replace("\r", newline), algorithm);
        var package = new Windows.ApplicationModel.DataTransfer.DataPackage();
        package.SetText(hash);
        Windows.ApplicationModel.DataTransfer.Clipboard.SetContent(package);
        Flash(Strings.Format("ToolHashCopied", algorithm));
    }

    // ---------- Tab and Enter ----------

    private string IndentUnit => SettingsService.Current.TabSize > 0 ? new string(' ', SettingsService.Current.TabSize) : "\t";

    /// <summary>Tab inserts the chosen indent; with several lines selected, Tab / Shift+Tab shift them.</summary>
    private void HandleTab(bool shift)
    {
        SyncText(record: true, group: true);
        bool multiLine = SelLength > 0 && _text[SelStart..SelEnd].Contains('\r');
        if (!multiLine && !shift)
        {
            ReplaceRange(SelStart, SelLength, IndentUnit);
            return;
        }

        string unit = IndentUnit;
        int width = unit == "\t" ? 4 : unit.Length;
        TransformLines(lines => lines.Select(l =>
        {
            if (!shift) return l.Length == 0 ? l : unit + l;
            if (l.StartsWith('\t')) return l[1..];
            int spaces = 0;
            while (spaces < width && spaces < l.Length && l[spaces] == ' ') spaces++;
            return l[spaces..];
        }).ToArray(), wholeIfEmpty: false);
    }

    private static readonly Regex ListMarker = new(@"^(\s*)([-*+]|(\d+)([.)]))(\s+)(\[[ xX]\]\s+)?", RegexOptions.Compiled);

    /// <summary>New line that keeps the indent; Markdown list items continue (an empty item ends the list).</summary>
    private void HandleEnter()
    {
        // Fast typing can get here before the last keys were picked up
        SyncText(record: true, group: true);
        int caret = Math.Min(SelStart, _text.Length);
        int lineStart = Math.Min(LineStartAt(caret), caret);
        string before = _text[lineStart..caret];
        string indent = before[..(before.Length - before.TrimStart(' ', '\t').Length)];
        string insert = "\r" + indent;

        if (Language.IsMarkdown && ListMarker.Match(before) is { Success: true } m)
        {
            if (before.Length == m.Length && SelLength == 0 && LineEndAt(caret) == caret)
            {
                // Enter on an empty item: drop the marker and end the list
                ReplaceRange(lineStart, caret - lineStart, "");
                return;
            }
            string marker = m.Groups[3].Success
                ? (int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture) + m.Groups[4].Value
                : m.Groups[2].Value;
            string box = m.Groups[6].Success ? "[ ] " : "";
            insert = "\r" + m.Groups[1].Value + marker + m.Groups[5].Value + box;
        }

        ReplaceRange(SelStart, SelLength, insert);
        ScrollCaretIntoView();
    }

    private void ScrollCaretIntoView()
    {
        DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            try
            {
                Doc.Selection.ScrollIntoView(PointOptions.None);
            }
            catch (Exception)
            {
                // not loaded yet
            }
        });
    }

    // ---------- Short messages in the status line ----------

    private DispatcherQueueTimer? _flashTimer;

    /// <summary>Shows a short note in place of the word count for a few seconds.</summary>
    private void Flash(string message)
    {
        CountText.Text = message;
        _flashTimer ??= DispatcherQueue.CreateTimer();
        _flashTimer.Interval = TimeSpan.FromSeconds(3);
        _flashTimer.IsRepeating = false;
        _flashTimer.Tick -= FlashDone;
        _flashTimer.Tick += FlashDone;
        _flashTimer.Stop();
        _flashTimer.Start();
    }

    private void FlashDone(DispatcherQueueTimer sender, object args) => UpdateStatus();
}
