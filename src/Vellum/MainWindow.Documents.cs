using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Vellum.Controls;
using Vellum.Models;
using Vellum.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using IOPath = System.IO.Path;

namespace Vellum;

/// <summary>Tabs, opening and saving files, and keeping the session between launches.</summary>
public sealed partial class MainWindow
{
    private sealed record TabParts(EditorView Editor, TextBlock Title, Ellipse Dot, FontIcon Kind);

    private readonly SemaphoreSlim _dialogLock = new(1, 1);
    private DispatcherQueueTimer? _sessionTimer;
    private bool _restoring;

    private void SetupDocuments(SessionState? saved)
    {
        _sessionTimer = DispatcherQueue.CreateTimer();
        _sessionTimer.Interval = TimeSpan.FromSeconds(1);
        _sessionTimer.IsRepeating = false;
        _sessionTimer.Tick += (_, _) => SaveSessionNow();

        _autoSaveTimer = DispatcherQueue.CreateTimer();
        _autoSaveTimer.Interval = TimeSpan.FromSeconds(1.5);
        _autoSaveTimer.IsRepeating = false;
        _autoSaveTimer.Tick += (_, _) => AutoSaveFiles();

        // Ctrl+, opens Settings (the comma key has no name in XAML)
        var settingsKey = new KeyboardAccelerator { Key = (Windows.System.VirtualKey)188, Modifiers = Windows.System.VirtualKeyModifiers.Control };
        settingsKey.Invoked += Settings_Invoked;
        Root.KeyboardAccelerators.Add(settingsKey);

        Root.AddHandler(UIElement.DragOverEvent, new DragEventHandler(Root_DragOver), true);
        Root.AddHandler(UIElement.DropEvent, new DragEventHandler(Root_Drop), true);

        SetupNotes();
        RestoreSession(saved);
        if (!AllTabs.Any())
        {
            AddTab(NewEditor());
        }
    }

    // ---------- Tabs ----------

    /// <summary>Document tabs only (not the Settings tab).</summary>
    private IEnumerable<TabViewItem> AllTabs => Tabs.TabItems.OfType<TabViewItem>().Where(t => t.Tag is TabParts);

    private static TabParts Parts(TabViewItem tab) => (TabParts)tab.Tag;

    private EditorView? SelectedEditor => (Tabs.SelectedItem as TabViewItem)?.Tag is TabParts p ? p.Editor : null;

    private TabViewItem AddTab(EditorView editor, bool select = true)
    {
        var title = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var dot = new Ellipse
        {
            Width = 7,
            Height = 7,
            VerticalAlignment = VerticalAlignment.Center,
            Fill = (Brush)Application.Current.Resources["VellumAccentBrush"],
            Visibility = Visibility.Collapsed,
        };
        // File-type icon; while there are unsaved changes the accent dot takes its place
        var kind = new FontIcon { FontSize = 13, VerticalAlignment = VerticalAlignment.Center };
        var mark = new Grid { Width = 14, VerticalAlignment = VerticalAlignment.Center };
        mark.Children.Add(kind);
        dot.HorizontalAlignment = HorizontalAlignment.Center;
        mark.Children.Add(dot);
        var header = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        header.Children.Add(mark);
        header.Children.Add(title);

        var tab = new TabViewItem { Header = header, Content = editor, Tag = new TabParts(editor, title, dot, kind) };
        editor.DirtyChanged += (_, _) => RefreshTab(tab);
        editor.OpenFileRequested += (_, path) => OpenFiles([path]);
        editor.HelpRequested += (_, _) => Help.Show();
        editor.TabNumberRequested += (_, n) => SelectTabByNumber(n);
        editor.WindowCommandRequested += (_, command) =>
        {
            if (command == "library") ToggleLibrary();
            else if (command == "mini") ToggleMini();
            else ShowHistory(editor);
        };
        tab.ContextFlyout = BuildTabMenu(tab);
        editor.ContentChanged += (_, _) =>
        {
            if (editor.Document.IsNote) NoteEdited(tab, editor);
            ScheduleSessionSave();
            if (SettingsService.Current.AutoSaveFiles && editor.Document.FilePath is not null)
            {
                _autoSaveTimer?.Stop();
                _autoSaveTimer?.Start();
            }
        };
        RefreshTab(tab);

        Tabs.TabItems.Add(tab);
        if (select)
        {
            Tabs.SelectedItem = tab;
            editor.FocusEditor();
        }
        return tab;
    }

    private void RefreshTab(TabViewItem tab)
    {
        var (editor, title, dot, kind) = Parts(tab);
        bool selected = ReferenceEquals(tab, Tabs.SelectedItem);
        // Notes save themselves, so they never show the "unsaved" dot
        bool dirty = editor.Document.IsDirty && !editor.Document.IsNote;
        title.Text = editor.Document.FileName;
        title.FontWeight = selected ? Microsoft.UI.Text.FontWeights.SemiBold : Microsoft.UI.Text.FontWeights.Normal;
        dot.Visibility = dirty ? Visibility.Visible : Visibility.Collapsed;
        kind.Visibility = dirty ? Visibility.Collapsed : Visibility.Visible;
        var language = editor.Language;
        kind.Glyph = editor.Document.Lock is not null ? "" : editor.Document.IsNote ? (NotesLibrary.Find(editor.Document.Id)?.Pinned == true ? "" : "") :
            editor.IsReadOnly ? "" : language.IsMarkdown ?"" : language.IsPlain ? "" : "";
        kind.Foreground = selected
            ? (Brush)Application.Current.Resources["VellumAccentBrush"]
            : ThemeBrushes.Get<Brush>(Root, "StatusTextBrush");
        ToolTipService.SetToolTip(tab, editor.Document.FilePath ?? editor.Document.FileName);
        if (editor.Document.IsNote && LibraryOpen) Library.Refresh();
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(tab, editor.Document.FileName);
        if (ReferenceEquals(tab, Tabs.SelectedItem))
        {
            UpdateWindowTitle();
        }
    }

    private void UpdateWindowTitle()
    {
        EditorView? editor = SelectedEditor;
        AppWindow.Title = editor is null
            ? (ReferenceEquals(Tabs.SelectedItem, _settingsTab) ? Strings.Get("SettingsTab") + " — Vellum" : "Vellum")
            : $"{(editor.Document.IsDirty ? "• " : "")}{editor.Document.FileName} — Vellum";
    }

    private void FocusSelectedEditor() => SelectedEditor?.FocusEditor();

    /// <summary>Closes a tab, asking to save first if it has unsaved changes. Returns false if cancelled.</summary>
    private async Task<bool> CloseTabAsync(TabViewItem tab, bool bulk = false)
    {
        if (ReferenceEquals(tab, _settingsTab))
        {
            Tabs.TabItems.Remove(tab);
            _settingsTab = null;
            if (!AllTabs.Any()) AddTab(NewEditor());
            return true;
        }

        EditorView editor = Parts(tab).Editor;
        bool hasContent = editor.Document.FilePath is not null || editor.Text.Length > 0;
        if (editor.Document.IsNote)
        {
            CloseNote(editor); // already in the library: nothing to ask
        }
        else if (editor.Document.IsDirty && hasContent)
        {
            Tabs.SelectedItem = tab;
            switch (await AskToSaveAsync(editor))
            {
                case ContentDialogResult.Primary:
                    if (!await SaveAsync(editor, saveAs: false)) return false;
                    break;
                case ContentDialogResult.None:
                    return false;
            }
        }

        RememberClosed(editor);
        Tabs.TabItems.Remove(tab);
        if (!AllTabs.Any())
        {
            if (!bulk && SettingsService.Current.ExitWithLastTab && _settingsTab is null)
            {
                // Closing the last tab closes Vellum (setting); nothing unsaved is left at this point
                if (CloseHidesToTray)
                {
                    AddTab(NewEditor());
                    HideToTray();
                    return true;
                }
                SaveSessionNow();
                _exiting = true;
                Close();
                return true;
            }
            AddTab(NewEditor());
        }
        ScheduleSessionSave();
        return true;
    }

    private void Tabs_AddTabButtonClick(TabView sender, object args) => AddTab(NewEditor());

    private async void Tabs_TabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args) => await CloseTabAsync(args.Tab);

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        foreach (TabViewItem t in AllTabs) RefreshTab(t);
        if (e.AddedItems.Count > 0 && e.RemovedItems.Count > 0 && (Tabs.SelectedItem as TabViewItem)?.Content is UIElement page)
        {
            Motion.Enter(page);
        }
        UpdateWindowTitle();
        FocusSelectedEditor();
        ScheduleSessionSave();
    }

    private void Tabs_TabItemsChanged(TabView sender, Windows.Foundation.Collections.IVectorChangedEventArgs args) => ScheduleSessionSave();

    // ---------- Opening files ----------

    /// <summary>Opens files in new tabs (or switches to them if they are already open).</summary>
    public async void OpenFiles(IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            string fullPath;
            try
            {
                fullPath = IOPath.GetFullPath(path);
            }
            catch (Exception)
            {
                continue;
            }

            TabViewItem? existing = AllTabs.FirstOrDefault(t =>
                string.Equals(Parts(t).Editor.Document.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
            if (existing is not null)
            {
                Tabs.SelectedItem = existing;
                continue;
            }

            LoadedText loaded;
            try
            {
                loaded = TextFileService.Load(fullPath);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(Strings.Format("ErrorOpenTitle", IOPath.GetFileName(fullPath)), ex.Message);
                continue;
            }

            // Reuse an empty "Untitled" tab instead of leaving it behind
            TabViewItem? pristine = Tabs.SelectedItem is TabViewItem { Tag: TabParts sp } sel && sp.Editor.IsPristine ? sel : null;

            var document = new TextDocument { FilePath = fullPath, Encoding = loaded.Encoding, LineEnding = loaded.LineEnding };
            AddTab(new EditorView(document, loaded.Text, loaded.Text));
            RememberRecent(fullPath);

            if (pristine is not null)
            {
                Tabs.TabItems.Remove(pristine);
            }
        }
    }

    private async Task OpenWithPickerAsync()
    {
        var picker = new FileOpenPicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeFilter.Add("*");
        picker.FileTypeFilter.Add(".txt");
        picker.FileTypeFilter.Add(".md");

        var files = await picker.PickMultipleFilesAsync();
        if (files is { Count: > 0 })
        {
            OpenFiles(files.Select(f => f.Path));
        }
    }

    private void Root_DragOver(object sender, DragEventArgs e)
    {
        if (e.DataView.Contains(StandardDataFormats.StorageItems))
        {
            e.AcceptedOperation = DataPackageOperation.Copy;
            e.Handled = true;
        }
    }

    private async void Root_Drop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.Handled = true;
        var items = await e.DataView.GetStorageItemsAsync();
        OpenFiles(items.OfType<StorageFile>().Select(f => f.Path).Where(p => !string.IsNullOrEmpty(p)));
    }

    // ---------- Saving files ----------

    private async Task<bool> SaveAsync(EditorView editor, bool saveAs)
    {
        if (editor.Document.IsNote)
        {
            // Ctrl+S on a note keeps a version; "Save as" writes it to a file and the note stays in the library
            if (saveAs)
            {
                await ExportNoteAsync(editor.Document.NoteTitle, editor.Text);
            }
            else
            {
                SaveNote(editor, auto: false);
                editor.Notify(Strings.Get(editor.IsPristine ? "NoteEmpty" : "NoteSaved"));
            }
            return true;
        }

        string? path = editor.Document.FilePath;
        if (saveAs || path is null)
        {
            path = await PickSavePathAsync(editor);
            if (path is null) return false;
        }

        try
        {
            KeepOriginal(editor, path);
            TextFileService.Save(path, editor.Text, editor.Document.Encoding, editor.Document.LineEnding);
            KeepSaved(editor, path, auto: false);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Strings.Format("ErrorSaveTitle", IOPath.GetFileName(path)), ex.Message);
            return false;
        }

        editor.Document.FilePath = path;
        editor.MarkSaved();
        RememberRecent(path);
        if (AllTabs.FirstOrDefault(t => ReferenceEquals(Parts(t).Editor, editor)) is { } tab)
        {
            RefreshTab(tab);
        }
        ScheduleSessionSave();
        return true;
    }

    private async Task<string?> PickSavePathAsync(EditorView editor)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);

        string? current = editor.Document.FilePath;
        string extension = current is null ? ".txt" : IOPath.GetExtension(current);
        if (!string.IsNullOrEmpty(extension) && extension is not ".txt" and not ".md")
        {
            picker.FileTypeChoices.Add(extension.TrimStart('.').ToUpperInvariant(), [extension]);
        }
        picker.FileTypeChoices.Add(Strings.Get("FileTypeText"), [".txt"]);
        picker.FileTypeChoices.Add("Markdown", [".md"]);
        picker.SuggestedFileName = current is not null
            ? IOPath.GetFileNameWithoutExtension(current)
            : SuggestName(editor.Text);

        StorageFile? file = await picker.PickSaveFileAsync();
        return file?.Path;
    }

    /// <summary>File name from the first line of the text, like "Shopping list".</summary>
    private static string SuggestName(string text)
    {
        string first = text.Split('\r', 2)[0].Trim();
        foreach (char c in IOPath.GetInvalidFileNameChars())
        {
            first = first.Replace(c.ToString(), "");
        }
        if (first.Length > 40) first = first[..40].Trim();
        return first.Length > 0 ? first : Strings.Get("UntitledTab");
    }

    // ---------- Dialogs ----------

    private ContentDialog CreateDialog() => new()
    {
        XamlRoot = Root.XamlRoot,
        Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
        RequestedTheme = Root.ActualTheme,
    };

    private async Task<ContentDialogResult> AskToSaveAsync(EditorView editor)
    {
        await _dialogLock.WaitAsync();
        try
        {
            ContentDialog dialog = CreateDialog();
            dialog.Title = Strings.Format("SaveChangesTitle", editor.Document.FileName);
            dialog.Content = Strings.Get("SaveChangesText");
            dialog.PrimaryButtonText = Strings.Get("ButtonSave");
            dialog.SecondaryButtonText = Strings.Get("ButtonDontSave");
            dialog.CloseButtonText = Strings.Get("ButtonCancel");
            dialog.DefaultButton = ContentDialogButton.Primary;
            return await dialog.ShowAsync();
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    private async Task ShowErrorAsync(string title, string message)
    {
        await _dialogLock.WaitAsync();
        try
        {
            ContentDialog dialog = CreateDialog();
            dialog.Title = title;
            dialog.Content = message;
            dialog.CloseButtonText = Strings.Get("ButtonOk");
            await dialog.ShowAsync();
        }
        finally
        {
            _dialogLock.Release();
        }
    }

    // ---------- Session ----------

    private void ScheduleSessionSave()
    {
        if (_restoring || _sessionTimer is null) return;
        _sessionTimer.Stop();
        _sessionTimer.Start();
    }

    private void SaveSessionNow()
    {
        if (_restoring) return;
        _sessionTimer?.Stop();
        SaveNotesNow();

        // With "restore session" off only the window position is remembered
        // Notes under a password are never written to the session: their text stays encrypted on disk
        List<TabParts> tabs = SettingsService.Current.RestoreSession ? AllTabs.Select(Parts).Where(p => p.Editor.Document.Lock is null).ToList() : [];
        int selectedIndex = (Tabs.SelectedItem as TabViewItem)?.Tag is TabParts selected
            ? tabs.Where(p => !p.Editor.IsPristine).ToList().IndexOf(selected)
            : 0;
        var state = new SessionState
        {
            SelectedIndex = Math.Max(0, selectedIndex),
            Window = CurrentBounds(),
            Mini = _mini,
            MiniBounds = _miniBounds,
            Tabs = tabs
                // An empty, never-saved tab is not worth remembering
                .Where(p => !p.Editor.IsPristine)
                .Select(p => new TabState
                {
                    Id = p.Editor.Document.Id,
                    FilePath = p.Editor.Document.FilePath,
                    IsDirty = p.Editor.Document.IsDirty,
                    IsNote = p.Editor.Document.IsNote,
                    CodePage = p.Editor.Document.Encoding.CodePage,
                    Bom = TextFileService.HasBom(p.Editor.Document.Encoding),
                    LineEnding = p.Editor.Document.LineEnding,
                    SelectionStart = p.Editor.SelectionStart,
                    Syntax = p.Editor.Document.SyntaxOverride,
                })
                .ToList(),
        };

        var unsaved = tabs
            .Where(p => p.Editor.Document.IsDirty && !p.Editor.IsPristine)
            .Select(p => (p.Editor.Document.Id, p.Editor.ContentVersion, (Func<string>)(() => p.Editor.Text)))
            .ToList();

        _session.Save(state, unsaved);
    }

    private void RestoreSession(SessionState? saved)
    {
        if (saved is null) return;

        _restoring = true;
        try
        {
            foreach (TabState t in saved.Tabs)
            {
                var document = new TextDocument
                {
                    Id = t.Id,
                    FilePath = t.FilePath,
                    LineEnding = t.LineEnding,
                    SyntaxOverride = t.Syntax,
                };
                try
                {
                    document.Encoding = TextFileService.FromCodePage(t.CodePage, t.Bom);
                }
                catch (Exception)
                {
                    // keep UTF-8
                }

                if (t.IsNote)
                {
                    // Notes come from the library; text cached in the session covers a crash before the note was saved
                    if (NotesLibrary.IsLocked(t.Id)) continue; // locked since; opened from the library with its password
                    bool inLibrary = NotesLibrary.Contains(t.Id);
                    string? noteText = inLibrary ? NotesLibrary.LoadText(t.Id) : null;
                    string? cached = t.IsDirty ? _session.LoadText(t.Id) : null;
                    if (cached is null && noteText is null) continue; // deleted from the library meanwhile
                    document.IsNote = true;
                    document.SyntaxOverride ??= "markdown";
                    var note = new EditorView(document, cached ?? noteText!, noteText ?? "");
                    document.NoteTitle = NotesLibrary.TitleOf(note.Text);
                    note.SelectionStart = t.SelectionStart;
                    AddTab(note, select: false);
                    continue;
                }

                LoadedText? onDisk = null;
                if (t.FilePath is not null && File.Exists(t.FilePath))
                {
                    try
                    {
                        onDisk = TextFileService.Load(t.FilePath);
                    }
                    catch (Exception)
                    {
                        // unreadable now; fall back to what we cached
                    }
                }

                EditorView editor;
                if (t.IsDirty)
                {
                    string text = _session.LoadText(t.Id) ?? onDisk?.Text ?? "";
                    string? baseline = t.FilePath is null ? "" : onDisk?.Text;
                    editor = new EditorView(document, text, baseline);
                }
                else if (onDisk is not null)
                {
                    // Saved file: always show its current content from disk
                    document.Encoding = onDisk.Encoding;
                    document.LineEnding = onDisk.LineEnding;
                    editor = new EditorView(document, onDisk.Text, onDisk.Text);
                }
                else
                {
                    continue; // the file is gone and there was nothing unsaved in it
                }

                if (t.IsDirty && !editor.Document.IsDirty) editor.MarkFormatChanged(); // only encoding / line breaks changed
                editor.SelectionStart = t.SelectionStart;
                AddTab(editor, select: false);
            }

            if (Tabs.TabItems.Count > 0)
            {
                Tabs.SelectedIndex = Math.Clamp(saved.SelectedIndex, 0, Tabs.TabItems.Count - 1);
            }
        }
        finally
        {
            _restoring = false;
        }
    }

    // ---------- Commands ----------

    private void NewTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        AddTab(NewEditor());
    }

    private async void Open_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        await OpenWithPickerAsync();
    }

    private async void Save_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SelectedEditor is { } editor) await SaveAsync(editor, saveAs: false);
    }

    private async void SaveAs_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (SelectedEditor is { } editor) await SaveAsync(editor, saveAs: true);
    }

    private async void CloseTab_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        if (Tabs.SelectedItem is TabViewItem tab) await CloseTabAsync(tab);
    }

    private void NewTab_Click(object sender, RoutedEventArgs e) => AddTab(NewEditor());

    private async void Open_Click(object sender, RoutedEventArgs e) => await OpenWithPickerAsync();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEditor is { } editor) await SaveAsync(editor, saveAs: false);
    }

    private async void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEditor is { } editor) await SaveAsync(editor, saveAs: true);
    }

    private async void CloseTab_Click(object sender, RoutedEventArgs e)
    {
        if (Tabs.SelectedItem is TabViewItem tab) await CloseTabAsync(tab);
    }

    private async void Exit_Click(object sender, RoutedEventArgs e)
    {
        if (NeedsCloseConfirmation() && !await ConfirmCloseAsync()) return;
        _closeConfirmed = true;
        _exiting = true;
        Close();
    }

    private void Settings_Click(object sender, RoutedEventArgs e) => OpenSettings();

    private void Settings_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        OpenSettings();
    }

    // ---------- Settings tab ----------

    private TabViewItem? _settingsTab;

    // Settings live in a side panel (like Notepads); _settingsTab stays null

    private SettingsView? _settingsView;

    private bool SettingsOpen => SettingsLayer.Visibility == Visibility.Visible;

    private void OpenSettings()
    {
        if (SettingsOpen) return; // the shortcut can arrive twice; Esc or the close button closes
        SetMini(false); // Settings need room
        if (_settingsView is null)
        {
            _settingsView = new SettingsView();
            _settingsView.RestartRequested += async (_, _) => await RestartAsync();
            _settingsView.CloseRequested += (_, _) => CloseSettings();
            SettingsPanel.Child = _settingsView;
        }
        SettingsPanel.Width = Math.Max(320, Math.Min(760, Root.ActualWidth - 48));
        SettingsLayer.Visibility = Visibility.Visible;
        _settingsView.ShowSection(0);
        Motion.Enter(SettingsPanel, offsetY: 0, fromOpacity: 0, offsetX: 40);
        _settingsView.Focus(FocusState.Programmatic);
    }

    private void CloseSettings()
    {
        if (!SettingsOpen) return;
        SettingsLayer.Visibility = Visibility.Collapsed;
        FocusSelectedEditor();
    }

    private void SettingsBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseSettings();

    private void SettingsLayer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseSettings();
            e.Handled = true;
        }
    }

    /// <summary>Restarts Vellum (to apply a new language), keeping all tabs.</summary>
    private async Task RestartAsync()
    {
        if (NeedsCloseConfirmation() && !await ConfirmCloseAsync()) return;
        SaveSessionNow();
        Microsoft.Windows.AppLifecycle.AppInstance.GetCurrent().UnregisterKey();
        Microsoft.Windows.AppLifecycle.AppInstance.Restart("");
    }

    // ---------- Closing ----------

    /// <summary>
    /// Unsaved text is normally kept in the session, so closing never loses anything.
    /// Only when "restore session" is off do we have to ask about unsaved changes.
    /// </summary>
    private bool NeedsCloseConfirmation() =>
        !SettingsService.Current.RestoreSession && UnsavedTabs().Any();

    private IEnumerable<TabViewItem> UnsavedTabs() =>
        AllTabs.Where(t => Parts(t).Editor.Document.IsDirty && !Parts(t).Editor.IsPristine && !Parts(t).Editor.Document.IsNote);

    private async Task<bool> ConfirmCloseAsync()
    {
        foreach (TabViewItem tab in UnsavedTabs().ToList())
        {
            if (!await CloseTabAsync(tab)) return false;
        }
        return true;
    }

    // ---------- Auto-save to disk ----------

    private DispatcherQueueTimer? _autoSaveTimer;

    private void AutoSaveFiles()
    {
        if (!SettingsService.Current.AutoSaveFiles) return;
        foreach (TabViewItem tab in AllTabs.ToList())
        {
            EditorView editor = Parts(tab).Editor;
            if (!editor.Document.IsDirty || editor.Document.FilePath is null) continue;
            try
            {
                KeepOriginal(editor, editor.Document.FilePath);
                TextFileService.Save(editor.Document.FilePath, editor.Text, editor.Document.Encoding, editor.Document.LineEnding);
                KeepSaved(editor, editor.Document.FilePath, auto: true);
                editor.MarkSaved();
                RefreshTab(tab);
            }
            catch (Exception ex)
            {
                // e.g. the file is read-only; it stays marked as unsaved
                CrashLog.Write("Auto-save " + editor.Document.FilePath, ex);
            }
        }
        ScheduleSessionSave();
    }
}
