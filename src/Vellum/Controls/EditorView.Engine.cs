using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using Vellum.Services;

namespace Vellum.Controls;

/// <summary>
/// The text itself: reading and writing it, the selection, undo / redo, and plain-text paste.
/// Line breaks are '\r' everywhere, like the rich text box uses.
/// </summary>
public sealed partial class EditorView
{
    private string _text = "";
    private bool _contentChanging;
    private readonly UndoHistory _history = new();

    private RichEditTextDocument Doc => Editor.Document;

    public string Text => _text;

    /// <summary>Ctrl+1..9 pressed inside the text (the window picks the tab).</summary>
    public event EventHandler<int>? TabNumberRequested;

    /// <summary>Ctrl+Shift+L (notes library) or Ctrl+Shift+H (version history) pressed inside the text.</summary>
    public event EventHandler<string>? WindowCommandRequested;

    private void SetupEngine(string text)
    {
        Doc.UndoLimit = 0; // see UndoHistory
        Editor.SelectionFlyout = null;
        Editor.ContextFlyout = BuildContextMenu();
        Editor.PreviewKeyDown += Editor_PreviewKeyDown;
        LoadText(text);
    }

    /// <summary>The rich text box always ends with one extra paragraph mark; it is not part of the text.</summary>
    private string ReadText()
    {
        Doc.GetText(TextGetOptions.None, out string t);
        return t.EndsWith('\r') ? t[..^1] : t;
    }

    private void LoadText(string text)
    {
        bool locked = Editor.IsReadOnly;
        Editor.IsReadOnly = false;
        Doc.SetText(TextSetOptions.None, text);
        Editor.IsReadOnly = locked;
        _text = ReadText();
        _history.Clear();
        Doc.Selection.SetRange(0, 0);
    }

    /// <summary>Picks up a change to the text; returns false if nothing changed.</summary>
    private bool SyncText(bool record, bool group)
    {
        string now = ReadText();
        if (HasControlChars(now))
        {
            // Shortcuts like Ctrl+/ also send an invisible control character; plain text never wants it
            for (int i = now.Length - 1; i >= 0; i--)
            {
                if (IsControl(now[i])) Doc.GetRange(i, i + 1).Text = "";
            }
            now = ReadText();
        }
        if (string.Equals(now, _text, StringComparison.Ordinal)) return false;
        if (record) _history.Record(_text, now, group);
        string before = _text;
        _text = now;
        OnTextChanged();
        OnEdit(before, now, typed: record && group && !_autoEditing);
        return true;
    }

    private static bool IsControl(char c) => c < ' ' && c is not '\t' and not '\r' and not '\n';

    private static bool HasControlChars(string text)
    {
        foreach (char c in text)
        {
            if (IsControl(c)) return true;
        }
        return false;
    }

    /// <summary>Replaces part of the text as one undo step and leaves the caret after it.</summary>
    private void ReplaceRange(int start, int length, string replacement)
    {
        if (BlockedByReadOnly()) return;
        replacement = replacement.Replace("\r\n", "\r").Replace('\n', '\r');
        Doc.GetRange(start, start + length).Text = replacement;
        _history.Seal();
        SyncText(record: true, group: false);
        _history.Seal();
        Select(start + replacement.Length, 0);
    }

    /// <summary>Puts in a whole new text (e.g. an older version) as one step that Ctrl+Z can undo.</summary>
    public void ReplaceAllText(string text)
    {
        int caret = SelStart;
        ReplaceRange(0, _text.Length, text);
        Select(Math.Min(caret, _text.Length), 0);
        RevealCaret();
    }

    // ---------- Selection ----------

    private int SelStart => Doc.Selection.StartPosition;

    private int SelLength => Doc.Selection.EndPosition - Doc.Selection.StartPosition;

    private string SelectedText
    {
        get
        {
            int start = Math.Min(SelStart, _text.Length);
            int end = Math.Min(Doc.Selection.EndPosition, _text.Length);
            return _text[start..end];
        }
    }

    private void Select(int start, int length)
    {
        start = Math.Clamp(start, 0, _text.Length);
        int end = Math.Clamp(start + length, start, _text.Length);
        Doc.Selection.SetRange(start, end);
    }

    // ---------- Events ----------

    private void Editor_TextChanging(RichEditBox sender, RichEditBoxTextChangingEventArgs args)
    {
        // Coloring also raises TextChanged; only real edits matter here
        if (args.IsContentChanging) _contentChanging = true;
    }

    private void Editor_TextChanged(object sender, RoutedEventArgs e)
    {
        if (!_contentChanging) return;
        _contentChanging = false;
        SyncText(record: true, group: true);
    }

    private void Editor_Paste(object sender, TextControlPasteEventArgs e)
    {
        // Paste text only, never formatting or pictures
        e.Handled = true;
        _ = PastePlainAsync();
    }

    private async Task PastePlainAsync()
    {
        try
        {
            DataPackageView content = Clipboard.GetContent();
            if (!content.Contains(StandardDataFormats.Text)) return;
            string text = await content.GetTextAsync();
            ReplaceRange(SelStart, SelLength, text);
            RevealCaret();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Paste", ex);
        }
    }

    private void Editor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        bool ctrl = IsDown(VirtualKey.Control);
        bool shift = IsDown(VirtualKey.Shift);
        bool alt = IsDown(VirtualKey.Menu);

        if (e.Key == VirtualKey.F2 && !alt)
        {
            // Ctrl+F2 bookmark, F2 / Shift+F2 next / previous, Ctrl+Shift+F2 clear all
            if (ctrl && shift) ClearBookmarks();
            else if (ctrl) ToggleBookmark();
            else GoToBookmark(next: !shift);
            e.Handled = true;
            return;
        }

        if (!ctrl && !alt)
        {
            switch (e.Key)
            {
                case VirtualKey.Tab:
                    HandleTab(shift);
                    e.Handled = true;
                    break;
                case VirtualKey.Enter when shift || SettingsService.Current.AutoIndent:
                    // Shift+Enter would add a soft line break, which plain text does not have
                    HandleEnter();
                    e.Handled = true;
                    break;
                case VirtualKey.F5:
                    Apply(EditTool.InsertDateTime);
                    e.Handled = true;
                    break;
            }
            return;
        }

        if (alt && !ctrl)
        {
            if (e.Key is VirtualKey.Up or VirtualKey.Down)
            {
                Apply(e.Key == VirtualKey.Up ? EditTool.MoveLineUp : EditTool.MoveLineDown);
                e.Handled = true;
            }
            return;
        }

        // Ctrl+1..9 picks a tab; the text box would change line spacing on Ctrl+1 / 2 / 5
        if (!shift && e.Key is >= VirtualKey.Number1 and <= VirtualKey.Number9)
        {
            TabNumberRequested?.Invoke(this, e.Key - VirtualKey.Number0);
            e.Handled = true;
            return;
        }

        switch (e.Key)
        {
            case VirtualKey.Enter:
                Apply(shift ? EditTool.InsertLineAbove : EditTool.InsertLineBelow);
                e.Handled = true;
                break;
            case VirtualKey.B when !shift:
                GoToMatchingBracket();
                e.Handled = true;
                break;
            case VirtualKey.D when !shift:
                Apply(EditTool.DuplicateLine);
                e.Handled = true;
                break;
            case VirtualKey.K when shift:
                Apply(EditTool.DeleteLine);
                e.Handled = true;
                break;
            case VirtualKey.U:
                Apply(shift ? EditTool.UpperCase : EditTool.LowerCase);
                e.Handled = true;
                break;
            case (VirtualKey)191 when !shift: // Ctrl+/
                Apply(EditTool.ToggleComment);
                e.Handled = true;
                break;
            case VirtualKey.E when !shift:
                Apply(EditTool.SearchWeb);
                e.Handled = true;
                break;
            case VirtualKey.J when !shift:
                Apply(EditTool.JoinLines);
                e.Handled = true;
                break;
            case VirtualKey.L when shift:
                WindowCommandRequested?.Invoke(this, "library");
                e.Handled = true;
                break;
            case VirtualKey.M when shift:
                WindowCommandRequested?.Invoke(this, "mini");
                e.Handled = true;
                break;
            case VirtualKey.H when shift:
                WindowCommandRequested?.Invoke(this, "history");
                e.Handled = true;
                break;
            case VirtualKey.L:
            case VirtualKey.R:
                // The rich text box would change paragraph alignment or add bullets: not for plain text
                e.Handled = true;
                break;
            case VirtualKey.Z when !shift:
                Undo();
                e.Handled = true;
                break;
            case VirtualKey.Y:
            case VirtualKey.Z when shift:
                Redo();
                e.Handled = true;
                break;
            case VirtualKey.V when shift: // the text box would paste; here it is the preview
                TogglePreview();
                e.Handled = true;
                break;
            case (VirtualKey)189: // Ctrl+- : the text box would insert a soft hyphen
                ZoomService.ZoomOut();
                e.Handled = true;
                break;
        }
    }

    private static bool IsDown(VirtualKey key) =>
        Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);

    // ---------- Undo / redo ----------

    public void Undo()
    {
        if (BlockedByReadOnly() || _history.TakeUndo() is not { } edit) return;
        Doc.GetRange(edit.Start, edit.Start + edit.Inserted.Length).Text = edit.Removed;
        SyncText(record: false, group: false);
        Select(edit.Start + edit.Removed.Length, 0);
        RevealCaret();
    }

    public void Redo()
    {
        if (BlockedByReadOnly() || _history.TakeRedo() is not { } edit) return;
        Doc.GetRange(edit.Start, edit.Start + edit.Removed.Length).Text = edit.Inserted;
        SyncText(record: false, group: false);
        Select(edit.Start + edit.Inserted.Length, 0);
        RevealCaret();
    }

    // ---------- Context menu ----------

    private MenuFlyout BuildContextMenu()
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string key, string glyph, string keys, Action action)
        {
            var item = new MenuFlyoutItem { Text = Strings.Get(key), Icon = new FontIcon { Glyph = glyph }, KeyboardAcceleratorTextOverride = keys };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        MenuFlyoutItem undo = Item("CtxUndo", "", "Ctrl+Z", Undo);
        MenuFlyoutItem redo = Item("CtxRedo", "", "Ctrl+Y", Redo);
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem cut = Item("CtxCut", "", "Ctrl+X", () => { CopySelection(); ReplaceRange(SelStart, SelLength, ""); });
        MenuFlyoutItem copy = Item("CtxCopy", "", "Ctrl+C", CopySelection);
        Item("CtxPaste", "", "Ctrl+V", () => _ = PastePlainAsync());
        menu.Items.Add(new MenuFlyoutSeparator());
        Item("CtxSelectAll", "", "Ctrl+A", () => Select(0, _text.Length));

        menu.Items.Add(new MenuFlyoutSeparator());

        menu.Items.Add(ToolItem(EditTool.SearchWeb));
        menu.Items.Add(ToolMenu("CtxLines", "\uE8C4", EditTool.DuplicateLine, EditTool.DeleteLine, EditTool.MoveLineUp, EditTool.MoveLineDown,
            EditTool.InsertLineAbove, EditTool.InsertLineBelow, EditTool.JoinLines, null,
            EditTool.SortAscending, EditTool.SortDescending, EditTool.SortNumericAscending, EditTool.SortNumericDescending,
            EditTool.ReverseLines, EditTool.ShuffleLines, null,
            EditTool.RemoveDuplicateLines, EditTool.RemoveConsecutiveDuplicates, EditTool.RemoveEmptyLines));
        menu.Items.Add(ToolMenu("CtxSpaces", "\uE75D", EditTool.TrimTrailingSpaces, EditTool.TrimLeadingSpaces, EditTool.TrimBothSpaces, null,
            EditTool.TabsToSpaces, EditTool.SpacesToTabs));
        menu.Items.Add(ToolMenu("CtxCase", "\uE8D2", EditTool.UpperCase, EditTool.LowerCase, EditTool.TitleCase, EditTool.SentenceCase, EditTool.InvertCase, EditTool.RandomCase));
        menu.Items.Add(ToolMenu("CtxConvert", "\uE8AB", EditTool.Base64Encode, EditTool.Base64Decode, null, EditTool.UrlEncode, EditTool.UrlDecode, null,
            EditTool.FormatJson, EditTool.MinifyJson, null, EditTool.HashMd5, EditTool.HashSha1, EditTool.HashSha256, EditTool.HashSha512));
        menu.Items.Add(ToolMenu("CtxBookmarks", "\uE8A4", EditTool.ToggleBookmark, EditTool.NextBookmark, EditTool.PreviousBookmark, EditTool.ClearBookmarks));
        MenuFlyoutItem comment = ToolItem(EditTool.ToggleComment);
        menu.Items.Add(comment);
        menu.Items.Add(ToolItem(EditTool.InsertDateTime));
        var readOnly = new ToggleMenuFlyoutItem { Text = Strings.Get("ToolReadOnly"), Icon = new FontIcon { Glyph = "\uE72E" } };
        readOnly.Click += (_, _) => IsReadOnly = readOnly.IsChecked;
        menu.Items.Add(new MenuFlyoutSeparator());
        menu.Items.Add(readOnly);

        menu.Opening += (_, _) =>
        {
            undo.IsEnabled = _history.CanUndo;
            redo.IsEnabled = _history.CanRedo;
            cut.IsEnabled = copy.IsEnabled = SelLength > 0;
            comment.Visibility = CanComment ? Visibility.Visible : Visibility.Collapsed;
            readOnly.IsChecked = IsReadOnly;
        };
        return menu;
    }

    private MenuFlyoutItem ToolItem(EditTool tool)
    {
        var info = ToolInfo.First(t => t.Tool == tool);
        var item = new MenuFlyoutItem { Text = Strings.Get(info.Key), KeyboardAcceleratorTextOverride = info.Keys };
        item.Click += (_, _) => Apply(tool);
        return item;
    }

    /// <summary>A submenu of tools; null adds a separator.</summary>
    private MenuFlyoutSubItem ToolMenu(string key, string glyph, params EditTool?[] tools)
    {
        var sub = new MenuFlyoutSubItem { Text = Strings.Get(key), Icon = new FontIcon { Glyph = glyph } };
        foreach (EditTool? tool in tools)
        {
            sub.Items.Add(tool is { } t ? ToolItem(t) : new MenuFlyoutSeparator());
        }
        return sub;
    }

    private void CopySelection()
    {
        if (SelLength == 0) return;
        var package = new DataPackage();
        package.SetText(SelectedText.Replace("\r", "\r\n"));
        Clipboard.SetContent(package);
    }
}
