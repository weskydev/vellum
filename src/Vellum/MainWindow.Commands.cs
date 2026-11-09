using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Vellum.Controls;
using Vellum.Models;
using Vellum.Services;
using Windows.System;

namespace Vellum;

/// <summary>Keyboard shortcuts and the command palette.</summary>
public sealed partial class MainWindow
{
    private void SetupCommands()
    {
        const VirtualKeyModifiers ctrl = VirtualKeyModifiers.Control;
        const VirtualKeyModifiers shift = VirtualKeyModifiers.Shift;

        AddShortcut(VirtualKey.P, ctrl | shift, () => TogglePalette(""));
        AddShortcut(VirtualKey.G, ctrl, () => TogglePalette(":"));
        AddShortcut(VirtualKey.F, ctrl, () => SelectedEditor?.ShowFind(replace: false));
        AddShortcut(VirtualKey.H, ctrl, () => SelectedEditor?.ShowFind(replace: true));
        AddShortcut(VirtualKey.F3, VirtualKeyModifiers.None, () => SelectedEditor?.FindNext(forward: true));
        AddShortcut(VirtualKey.F3, shift, () => SelectedEditor?.FindNext(forward: false));

        AddShortcut(VirtualKey.V, ctrl | shift, () => SelectedEditor?.TogglePreview());
        AddShortcut(VirtualKey.S, ctrl | VirtualKeyModifiers.Menu, () => _ = SaveAllAsync());
        AddShortcut(VirtualKey.F11, VirtualKeyModifiers.None, ToggleZen);
        AddShortcut(VirtualKey.T, ctrl | shift, ReopenClosedTab);

        // Zoom: the "=" / "-" keys next to Backspace and the ones on the number pad
        AddShortcut((VirtualKey)187, ctrl, ZoomService.ZoomIn);
        AddShortcut(VirtualKey.Add, ctrl, ZoomService.ZoomIn);
        AddShortcut((VirtualKey)189, ctrl, ZoomService.ZoomOut);
        AddShortcut(VirtualKey.Subtract, ctrl, ZoomService.ZoomOut);
        AddShortcut(VirtualKey.Number0, ctrl, ZoomService.Reset);
        AddShortcut(VirtualKey.NumberPad0, ctrl, ZoomService.Reset);

        // Ctrl+1..8 picks a tab, Ctrl+9 the last one
        for (int i = 1; i <= 9; i++)
        {
            int n = i;
            AddShortcut(VirtualKey.Number0 + n, ctrl, () => SelectTabByNumber(n));
        }

        Palette.GoToLine = line => SelectedEditor?.GoToLine(line);
        Palette.Closed += (_, _) =>
        {
            if (SelectedEditor is { IsFindOpen: false } editor) editor.FocusEditor();
        };
        Help.Closed += (_, _) => FocusSelectedEditor();
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, Action action)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (_, args) =>
        {
            args.Handled = true;
            action();
        };
        Root.KeyboardAccelerators.Add(accelerator);
    }

    private void SelectTabByNumber(int n)
    {
        int count = Tabs.TabItems.Count;
        if (count == 0) return;
        int index = n == 9 ? count - 1 : n - 1;
        if (index < count) Tabs.SelectedIndex = index;
    }

    private void TogglePalette(string initialText)
    {
        if (Palette.IsOpen)
        {
            Palette.Hide();
            return;
        }
        if (initialText == ":" && SelectedEditor is null) return;
        Palette.Show(BuildCommands(), initialText);
    }

    private void Palette_Click(object sender, RoutedEventArgs e) => TogglePalette("");

    private void Find_Click(object sender, RoutedEventArgs e) => SelectedEditor?.ShowFind(replace: false);

    private void Replace_Click(object sender, RoutedEventArgs e) => SelectedEditor?.ShowFind(replace: true);

    /// <summary>Everything the palette can do right now.</summary>
    private List<PaletteCommand> BuildCommands()
    {
        var list = new List<PaletteCommand>();
        void Add(string key, string glyph, string shortcut, Action run) => list.Add(new PaletteCommand(Strings.Get(key), glyph, shortcut, run));

        EditorView? editor = SelectedEditor;

        Add("CmdNewTab", "", "Ctrl+T", () => AddTab(NewEditor()));
        if (LibraryOn)
        {
            Add("CmdLibrary", "", "Ctrl+Shift+L", ToggleLibrary);
        }
        if (editor is not null && HistoryService.IsOn && editor.Document.HistoryKey is not null && editor.Document.Lock is null)
        {
            Add("CmdHistory", "", "Ctrl+Shift+H", () => ShowHistory(editor));
        }
        if (editor?.Document is { IsNote: true } noteDoc && NotesLibrary.Contains(noteDoc.Id))
        {
            bool pinned = NotesLibrary.Find(noteDoc.Id)?.Pinned == true;
            Add(pinned ? "NoteUnpin" : "NotePin", "", "", () => PinNote(noteDoc.Id, !pinned));
            Add("NoteExport", "", "", () => _ = ExportNoteAsync(noteDoc.NoteTitle, editor.Text));
            if (Tabs.SelectedItem is TabViewItem lockTab)
            {
                if (noteDoc.Lock is not null) Add("LockRemove", "", "", () => _ = UnlockNoteAsync(lockTab));
                else if (SettingsService.Current.NoteLocks) Add("LockNote", "", "", () => _ = LockNoteAsync(lockTab));
            }
            Add("NoteDelete", "", "", () => TrashNote(noteDoc.Id));
        }
        Add("CmdOpen", "", "Ctrl+O", () => _ = OpenWithPickerAsync());
        if (editor is not null)
        {
            Add("CmdSave", "", "Ctrl+S", () => _ = SaveAsync(editor, saveAs: false));
            Add("CmdSaveAs", "", "Ctrl+Shift+S", () => _ = SaveAsync(editor, saveAs: true));
            Add("CmdFind", "", "Ctrl+F", () => editor.ShowFind(replace: false));
            Add("CmdReplace", "", "Ctrl+H", () => editor.ShowFind(replace: true));
            Add("CmdGoToLine", "", "Ctrl+G", () => DispatcherQueue.TryEnqueue(() => Palette.Show(BuildCommands(), ":")));
        }
        if (Tabs.SelectedItem is TabViewItem current)
        {
            Add("CmdCloseTab", "", "Ctrl+W", () => _ = CloseTabAsync(current));
            Add("TabCloseOthers", "", "", () => _ = CloseTabsAsync(AllTabs.Where(t => t != current)));
            Add("TabCloseSaved", "", "", () => _ = CloseTabsAsync(AllTabs.Where(t => !Parts(t).Editor.Document.IsDirty)));
        }
        Add("CmdSettings", "", "Ctrl+,", OpenSettings);
        Add("CmdSaveAll", "", "Ctrl+Alt+S", () => _ = SaveAllAsync());
        if (CanReopenClosed) Add("CmdReopenClosed", "", "Ctrl+Shift+T", ReopenClosedTab);
        if (editor is not null)
        {
            Add("TabSaveCopy", "", "", () => _ = SaveCopyAsync(editor));
            Add("TabFileInfo", "", "", () => _ = ShowFileInfoAsync(editor));
        }
        if (editor?.Document.FilePath is { } withPath && Tabs.SelectedItem is TabViewItem renameTab)
        {
            Add("TabOpenWith", "", "", () => OpenWithOtherApp(withPath));
            Add("TabRename", "", "", () => _ = RenameAsync(renameTab));
        }
        Add("TabCloseAll","", "", () => _ = CloseTabsAsync(AllTabs));
        if (editor?.Document.FilePath is { } filePath)
        {
            Add("TabCopyPath", "", "", () => CopyText(filePath));
            Add("TabOpenFolder", "", "", () => RevealInExplorer(filePath));
            Add("TabReload", "", "", () => _ = editor.ReloadAsync());
        }
        foreach (string recent in SettingsService.Current.RecentFiles.Where(File.Exists))
        {
            string r = recent;
            list.Add(new PaletteCommand(Strings.Format("CmdOpenRecent", System.IO.Path.GetFileName(r)), "", "", () => OpenFiles([r])));
        }

        AppSettings s = SettingsService.Current;
        Add(s.WordWrap ? "CmdWrapOff" : "CmdWrapOn", "", "", () => SettingsService.Update(x => x.WordWrap = !x.WordWrap));
        Add(s.ShowStatusBar ? "CmdStatusOff" : "CmdStatusOn", "", "", () => SettingsService.Update(x => x.ShowStatusBar = !x.ShowStatusBar));
        if (editor is { CanPreview: true })
        {
            Add("CmdPreview", "", "Ctrl+Shift+V", editor.TogglePreview);
        }
        Add(WritingModes.Zen ? "CmdZenOff" : "CmdZenOn", "", "F11", ToggleZen);
        Add("CmdTopmost", "", "", ToggleTopmost);
        if (SettingsService.Current.MiniWindow || _mini) Add("CmdMini", "", "Ctrl+Shift+M", ToggleMini);
        if (SettingsService.Current.Export && SelectedEditor is { } exported)
        {
            Add("CmdExportPdf", "", "", () => _ = ExportAsync(exported, pdf: true));
            Add("CmdExportHtml", "", "", () => _ = ExportAsync(exported, pdf: false));
        }
        Add("CmdMarkdownHelp", "", "", Help.Show);
        Add(s.LineNumbers ? "CmdLineNumbersOff" : "CmdLineNumbersOn", "", "", () => SettingsService.Update(x => x.LineNumbers = !x.LineNumbers));
        Add(s.HighlightCurrentLine ? "CmdLineBandOff" : "CmdLineBandOn", "", "", () => SettingsService.Update(x => x.HighlightCurrentLine = !x.HighlightCurrentLine));
        if (editor is not null)
        {
            foreach (var (tool, key, glyph, keys) in EditorView.ToolInfo)
            {
                if (tool == EditTool.ToggleComment && !editor.CanComment) continue;
                EditTool t = tool;
                list.Add(new PaletteCommand(Strings.Get(key), glyph, keys, () => editor.Apply(t)));
            }
        }
        Add(s.FocusMode ? "CmdFocusOff" : "CmdFocusOn", "", "", () => SettingsService.Update(x => x.FocusMode = !x.FocusMode));
        Add(s.TypewriterMode ? "CmdTypewriterOff" : "CmdTypewriterOn", "", "", () => SettingsService.Update(x => x.TypewriterMode = !x.TypewriterMode));
        Add(s.ReadableWidth ? "CmdReadableOff" : "CmdReadableOn", "", "", () => SettingsService.Update(x => x.ReadableWidth = !x.ReadableWidth));
        Add(s.MarkdownStyling ? "CmdMarkdownOff" : "CmdMarkdownOn", "", "", () => SettingsService.Update(x => x.MarkdownStyling = !x.MarkdownStyling));
        Add(s.SyntaxHighlighting ? "CmdSyntaxOff" : "CmdSyntaxOn", "", "", () => SettingsService.Update(x => x.SyntaxHighlighting = !x.SyntaxHighlighting));
        if (editor is not null)
        {
            foreach (Services.Syntax.SyntaxLanguage language in Services.Syntax.SyntaxLanguage.All)
            {
                Services.Syntax.SyntaxLanguage l = language;
                list.Add(new PaletteCommand(Strings.Format("CmdSyntax", l.DisplayName), "", "", () => editor.SetLanguage(l)));
            }
        }
        Add("CmdZoomIn", "", "Ctrl+=", ZoomService.ZoomIn);
        Add("CmdZoomOut", "", "Ctrl+-", ZoomService.ZoomOut);
        Add("CmdZoomReset", "", "Ctrl+0", ZoomService.Reset);

        Add("CmdThemeSystem", "", "", () => SettingsService.Update(x => x.Theme = AppTheme.System));
        Add("CmdThemeLight", "", "", () => SettingsService.Update(x => x.Theme = AppTheme.Light));
        Add("CmdThemeDark", "", "", () => SettingsService.Update(x => x.Theme = AppTheme.Dark));

        foreach (var (key, _) in AccentPalette.Presets)
        {
            string k = key;
            list.Add(new PaletteCommand(Strings.Format("CmdAccent", Strings.Get("Accent_" + k)), "", "", () => SettingsService.Update(x => x.Accent = k)));
        }
        list.Add(new PaletteCommand(Strings.Format("CmdAccent", Strings.Get("Accent_system")), "", "", () => SettingsService.Update(x => x.Accent = AccentPalette.SystemKey)));

        Add("CmdBackdropAcrylic", "", "", () => SettingsService.Update(x => x.Backdrop = BackdropKind.Acrylic));
        Add("CmdBackdropMica", "", "", () => SettingsService.Update(x => x.Backdrop = BackdropKind.Mica));
        Add("CmdBackdropSolid", "", "", () => SettingsService.Update(x => x.Backdrop = BackdropKind.Solid));

        // Jump to any open tab by its name
        int number = 0;
        foreach (TabViewItem tab in Tabs.TabItems.OfType<TabViewItem>())
        {
            number++;
            string name = tab.Tag is TabParts p ? p.Editor.Document.FileName : Strings.Get("SettingsTab");
            string shortcut = number <= 8 ? $"Ctrl+{number}" : "";
            TabViewItem target = tab;
            list.Add(new PaletteCommand(Strings.Format("CmdSwitchTab", name), "", shortcut, () => Tabs.SelectedItem = target));
        }

        return list;
    }
}
