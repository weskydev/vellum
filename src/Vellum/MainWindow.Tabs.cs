using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vellum.Controls;
using Vellum.Services;
using Windows.ApplicationModel.DataTransfer;
using IOPath = System.IO.Path;

namespace Vellum;

/// <summary>Tab menu (close others / to the right / saved), recent files, save all, always on top.</summary>
public sealed partial class MainWindow
{
    private const int MaxRecent = 15;

    // ---------- Tab menu ----------

    private MenuFlyout BuildTabMenu(TabViewItem tab)
    {
        var menu = new MenuFlyout();
        MenuFlyoutItem Item(string key, string glyph, Action action)
        {
            var item = new MenuFlyoutItem { Text = Strings.Get(key), Icon = new FontIcon { Glyph = glyph } };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
            return item;
        }

        Item("TabClose", "", () => _ = CloseTabAsync(tab));
        MenuFlyoutItem others = Item("TabCloseOthers", "", () => _ = CloseTabsAsync(AllTabs.Where(t => t != tab)));
        MenuFlyoutItem right = Item("TabCloseRight", "", () => _ = CloseTabsAsync(AllTabs.SkipWhile(t => t != tab).Skip(1)));
        Item("TabCloseSaved", "", () => _ = CloseTabsAsync(AllTabs.Where(t => !Parts(t).Editor.Document.IsDirty)));
        Item("TabCloseAll", "", () => _ = CloseTabsAsync(AllTabs));
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem copyPath = Item("TabCopyPath", "", () => CopyText(Parts(tab).Editor.Document.FilePath));
        MenuFlyoutItem copyName = Item("TabCopyName", "", () => CopyText(Parts(tab).Editor.Document.FileName));
        MenuFlyoutItem folder = Item("TabOpenFolder", "", () => RevealInExplorer(Parts(tab).Editor.Document.FilePath));
        MenuFlyoutItem reload = Item("TabReload", "", () => _ = Parts(tab).Editor.ReloadAsync());

        MenuFlyoutItem rename = Item("TabRename", "", () => _ = RenameAsync(tab));
        MenuFlyoutItem openWith = Item("TabOpenWith", "", () => OpenWithOtherApp(Parts(tab).Editor.Document.FilePath));
        menu.Items.Add(new MenuFlyoutSeparator());
        MenuFlyoutItem pin = Item("NotePin", "", () => PinNote(Parts(tab).Editor.Document.Id, !IsPinned(tab)));
        MenuFlyoutItem history = Item("NoteHistory", "", () => ShowHistory(Parts(tab).Editor));
        MenuFlyoutItem export = Item("NoteExport", "", () => _ = ExportNoteAsync(Parts(tab).Editor.Document.NoteTitle, Parts(tab).Editor.Text));
        MenuFlyoutItem exportPdf = Item("ExportPdf", "", () => _ = ExportAsync(Parts(tab).Editor, pdf: true));
        MenuFlyoutItem exportHtml = Item("ExportHtml", "", () => _ = ExportAsync(Parts(tab).Editor, pdf: false));
        MenuFlyoutItem lockNote = Item("LockNote", "", () => _ = LockNoteAsync(tab));
        MenuFlyoutItem unlockNote = Item("LockRemove", "", () => _ = UnlockNoteAsync(tab));
        MenuFlyoutItem delete = Item("NoteDelete", "", () => TrashNote(Parts(tab).Editor.Document.Id));
        MenuFlyoutSeparator noteLine = new();
        menu.Items.Add(noteLine);
        Item("TabSaveCopy", "", () => _ = SaveCopyAsync(Parts(tab).Editor));
        Item("TabFileInfo", "", () => _ = ShowFileInfoAsync(Parts(tab).Editor));

        menu.Opening += (_, _) =>
        {
            bool hasFile = Parts(tab).Editor.Document.FilePath is not null;
            copyPath.IsEnabled = folder.IsEnabled = reload.IsEnabled = rename.IsEnabled = openWith.IsEnabled = hasFile;
            copyName.IsEnabled = true;

            // Note actions only for notes; the history for notes and for files
            var doc = Parts(tab).Editor.Document;
            bool note = doc.IsNote && NotesLibrary.Contains(doc.Id);
            pin.Text = Strings.Get(IsPinned(tab) ? "NoteUnpin" : "NotePin");
            pin.Visibility = export.Visibility = delete.Visibility = note ? Visibility.Visible : Visibility.Collapsed;
            lockNote.Visibility = note && doc.Lock is null && SettingsService.Current.NoteLocks ? Visibility.Visible : Visibility.Collapsed;
            unlockNote.Visibility = note && doc.Lock is not null ? Visibility.Visible : Visibility.Collapsed;
            exportPdf.Visibility = exportHtml.Visibility = SettingsService.Current.Export ? Visibility.Visible : Visibility.Collapsed;
            history.Visibility = doc.Lock is null && HistoryService.IsOn && (note || (hasFile && SettingsService.Current.FileHistory)) ? Visibility.Visible : Visibility.Collapsed;
            noteLine.Visibility = note || history.Visibility == Visibility.Visible ? Visibility.Visible : Visibility.Collapsed;
            others.IsEnabled = AllTabs.Count() > 1;
            right.IsEnabled = AllTabs.SkipWhile(t => t != tab).Skip(1).Any();
        };
        return menu;
    }

    /// <summary>Closes several tabs; stops at the first one the user decides to keep.</summary>
    private async Task CloseTabsAsync(IEnumerable<TabViewItem> tabs)
    {
        foreach (TabViewItem tab in tabs.ToList())
        {
            if (!await CloseTabAsync(tab, bulk: true)) return;
        }
    }

    private static void CopyText(string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }

    private static void RevealInExplorer(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Open folder", ex);
        }
    }

    // ---------- Recent files ----------

    private static void RememberRecent(string path)
    {
        List<string> list = SettingsService.Current.RecentFiles;
        if (list.Count > 0 && string.Equals(list[0], path, StringComparison.OrdinalIgnoreCase)) return;
        SettingsService.Update(s =>
        {
            s.RecentFiles.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            s.RecentFiles.Insert(0, path);
            if (s.RecentFiles.Count > MaxRecent) s.RecentFiles.RemoveRange(MaxRecent, s.RecentFiles.Count - MaxRecent);
        });
    }

    private void MainMenu_Opening(object? sender, object e)
    {
        RecentMenu.Items.Clear();
        List<string> recent = SettingsService.Current.RecentFiles.Where(File.Exists).ToList();
        foreach (string path in recent)
        {
            var item = new MenuFlyoutItem { Text = IOPath.GetFileName(path) };
            ToolTipService.SetToolTip(item, path);
            string p = path;
            item.Click += (_, _) => OpenFiles([p]);
            RecentMenu.Items.Add(item);
        }
        if (recent.Count > 0)
        {
            RecentMenu.Items.Add(new MenuFlyoutSeparator());
            var clear = new MenuFlyoutItem { Text = Strings.Get("MenuRecentClear") };
            clear.Click += (_, _) => SettingsService.Update(s => s.RecentFiles.Clear());
            RecentMenu.Items.Add(clear);
        }
        RecentMenu.IsEnabled = recent.Count > 0;
        TopmostItem.IsChecked = AppWindow.Presenter is OverlappedPresenter { IsAlwaysOnTop: true };
        MiniItem.IsChecked = _mini;
        MiniItem.Visibility = SettingsService.Current.MiniWindow || _mini ? Visibility.Visible : Visibility.Collapsed;
        ExportMenu.Visibility = SettingsService.Current.Export ? Visibility.Visible : Visibility.Collapsed;
    }

    // ---------- Save all, always on top, zen, help ----------

    private bool IsPinned(TabViewItem tab) => NotesLibrary.Find(Parts(tab).Editor.Document.Id)?.Pinned == true;

    private void PinNote(Guid id, bool pinned)
    {
        NotesLibrary.SetPinned(id, pinned);
        foreach (TabViewItem t in AllTabs) RefreshTab(t);
    }

    private async Task SaveAllAsync()
    {
        SaveNotesNow();
        foreach (TabViewItem tab in AllTabs.ToList())
        {
            EditorView editor = Parts(tab).Editor;
            if (!editor.Document.IsDirty || editor.IsPristine) continue;
            Tabs.SelectedItem = tab;
            if (!await SaveAsync(editor, saveAs: false)) return; // cancelled the file picker
        }
    }

    private async void SaveAll_Click(object sender, RoutedEventArgs e) => await SaveAllAsync();

    private async void CloseAll_Click(object sender, RoutedEventArgs e) => await CloseTabsAsync(AllTabs);

    private void ToggleTopmost()
    {
        if (AppWindow.Presenter is OverlappedPresenter p)
        {
            p.IsAlwaysOnTop = !p.IsAlwaysOnTop;
        }
    }

    private void Topmost_Click(object sender, RoutedEventArgs e) => ToggleTopmost();

    private void Zen_Click(object sender, RoutedEventArgs e) => ToggleZen();

    private void MarkdownHelp_Click(object sender, RoutedEventArgs e) => Help.Show();
}
