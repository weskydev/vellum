using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vellum.Controls;
using Vellum.Models;
using Vellum.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using IOPath = System.IO.Path;

namespace Vellum;

/// <summary>
/// File tools: reopen a closed tab, file info, save a copy, rename, open with another app,
/// and noticing when a file was changed by another program.
/// </summary>
public sealed partial class MainWindow
{
    private const int MaxClosed = 20;

    /// <summary>Closed tabs, newest last: the file, a note from the library, or the text of a tab that was never saved.</summary>
    private readonly List<(string? Path, string? Text, Guid? Note)> _closedTabs = [];

    private bool _checkingDisk;

    // ---------- Reopen closed tab ----------

    private void RememberClosed(EditorView editor)
    {
        string? path = editor.Document.FilePath;
        if (path is null && editor.Text.Length == 0) return;
        _closedTabs.Add(
            editor.Document.IsNote ? (null, null, editor.Document.Id) :
            path is not null ? (path, null, null) : (null, editor.Text, null));
        if (_closedTabs.Count > MaxClosed) _closedTabs.RemoveAt(0);
    }

    private bool CanReopenClosed => _closedTabs.Count > 0;

    private void ReopenClosedTab()
    {
        while (_closedTabs.Count > 0)
        {
            var (path, text, note) = _closedTabs[^1];
            _closedTabs.RemoveAt(_closedTabs.Count - 1);
            if (note is { } id)
            {
                if (!NotesLibrary.Contains(id)) continue; // deleted since
                OpenNote(id);
                return;
            }
            if (path is not null)
            {
                if (!File.Exists(path)) continue;
                OpenFiles([path]);
                return;
            }
            AddTab(new EditorView(new TextDocument { IsDirty = true }, text ?? "", ""));
            return;
        }
    }

    // ---------- File info ----------

    private async Task ShowFileInfoAsync(EditorView editor)
    {
        TextDocument doc = editor.Document;
        string text = editor.Text;
        int lines = 1, words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (c == '\r') lines++;
            bool letter = !char.IsWhiteSpace(c);
            if (letter && !inWord) words++;
            inWord = letter;
        }

        var rows = new List<(string Key, string Value)>();
        if (doc.FilePath is { } path && File.Exists(path))
        {
            var info = new FileInfo(path);
            rows.Add(("InfoPath", path));
            rows.Add(("InfoSize", FormatSize(info.Length)));
            rows.Add(("InfoCreated", info.CreationTime.ToString("g")));
            rows.Add(("InfoModified", info.LastWriteTime.ToString("g")));
        }
        rows.Add(("InfoEncoding", TextFileService.Describe(doc.Encoding)));
        rows.Add(("InfoLineEnding", TextFileService.Describe(doc.LineEnding)));
        rows.Add(("InfoLines", lines.ToString("N0")));
        rows.Add(("InfoWords", words.ToString("N0")));
        rows.Add(("InfoChars", (text.Length - (lines - 1)).ToString("N0")));
        rows.Add(("InfoLanguage", editor.Language.DisplayName));

        var grid = new Grid { ColumnSpacing = 20, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < rows.Count; i++)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var key = new TextBlock
            {
                Text = Strings.Get(rows[i].Key),
                Foreground = ThemeBrushes.Get<Microsoft.UI.Xaml.Media.Brush>(Root, "StatusTextBrush"),
            };
            var value = new TextBlock { Text = rows[i].Value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
            Grid.SetRow(key, i);
            Grid.SetRow(value, i);
            Grid.SetColumn(value, 1);
            grid.Children.Add(key);
            grid.Children.Add(value);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            RequestedTheme = Root.ActualTheme,
            Title = doc.FileName,
            Content = grid,
            CloseButtonText = Strings.Get("ButtonOk"),
            DefaultButton = ContentDialogButton.Close,
        };
        await dialog.ShowAsync();
    }

    private static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => Strings.Format("SizeBytes", bytes),
        < 1024 * 1024 => Strings.Format("SizeKb", (bytes / 1024.0).ToString("0.#")),
        _ => Strings.Format("SizeMb", (bytes / 1024.0 / 1024.0).ToString("0.##")),
    };

    // ---------- Save a copy / rename / open with ----------

    /// <summary>Writes the text to another file; the tab stays on its own file.</summary>
    private async Task SaveCopyAsync(EditorView editor)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        string name = editor.Document.FilePath is { } p ? IOPath.GetFileNameWithoutExtension(p) + " — " + Strings.Get("CopySuffix") : editor.Document.FileName;
        string ext = editor.Document.FilePath is { } q && IOPath.GetExtension(q) is { Length: > 0 } e ? e : ".txt";
        picker.SuggestedFileName = name;
        picker.FileTypeChoices.Add(Strings.Get("FileTypeText"), [ext]);
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            TextFileService.Save(file.Path, editor.Text, editor.Document.Encoding, editor.Document.LineEnding);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Strings.Format("ErrorSaveTitle", file.Name), ex.Message);
        }
    }

    private async Task RenameAsync(TabViewItem tab)
    {
        EditorView editor = Parts(tab).Editor;
        if (editor.Document.FilePath is not { } path || !File.Exists(path)) return;

        var box = new TextBox { Text = IOPath.GetFileName(path), IsSpellCheckEnabled = false };
        box.Loaded += (_, _) =>
        {
            box.Focus(FocusState.Programmatic);
            int dot = box.Text.LastIndexOf('.');
            box.Select(0, dot > 0 ? dot : box.Text.Length);
        };
        var dialog = new ContentDialog
        {
            XamlRoot = Root.XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            RequestedTheme = Root.ActualTheme,
            Title = Strings.Get("RenameTitle"),
            Content = box,
            PrimaryButtonText = Strings.Get("RenameConfirm"),
            CloseButtonText = Strings.Get("ButtonCancel"),
            DefaultButton = ContentDialogButton.Primary,
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        string newName = box.Text.Trim();
        if (newName.Length == 0 || newName == IOPath.GetFileName(path)) return;
        string target = IOPath.Combine(IOPath.GetDirectoryName(path)!, newName);
        try
        {
            if (newName.IndexOfAny(IOPath.GetInvalidFileNameChars()) >= 0) throw new IOException(Strings.Get("RenameInvalid"));
            if (File.Exists(target)) throw new IOException(Strings.Get("RenameExists"));
            File.Move(path, target);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Strings.Format("RenameError", newName), ex.Message);
            return;
        }
        editor.Document.FilePath = target;
        editor.Document.StampDisk();
        editor.UpdateFileInfo();
        RememberRecent(target);
        RefreshTab(tab);
        ScheduleSessionSave();
    }

    private static async void OpenWithOtherApp(string? path)
    {
        if (path is null || !File.Exists(path)) return;
        try
        {
            StorageFile file = await StorageFile.GetFileFromPathAsync(path);
            await Windows.System.Launcher.LaunchFileAsync(file, new Windows.System.LauncherOptions { DisplayApplicationPicker = true });
        }
        catch (Exception ex)
        {
            CrashLog.Write("Open with", ex);
        }
    }

    // ---------- Changes made by other programs ----------

    /// <summary>
    /// When Vellum comes back to the front, files changed elsewhere are reloaded
    /// (or, if they have unsaved edits here, the user is asked).
    /// </summary>
    private async Task CheckDiskChangesAsync()
    {
        if (_checkingDisk || !SettingsService.Current.WatchFiles) return;
        _checkingDisk = true;
        try
        {
            foreach (TabViewItem tab in AllTabs.ToList())
            {
                EditorView editor = Parts(tab).Editor;
                TextDocument doc = editor.Document;
                if (doc.FilePath is not { } path || doc.DiskTime is not { } known || !File.Exists(path)) continue;
                DateTime now = File.GetLastWriteTimeUtc(path);
                if (now == known) continue;
                doc.DiskTime = now; // ask once per change

                if (!doc.IsDirty)
                {
                    await editor.ReloadAsync(confirmed: true);
                    editor.Notify(Strings.Get("ReloadedExternal"));
                    continue;
                }

                Tabs.SelectedItem = tab;
                var dialog = new ContentDialog
                {
                    XamlRoot = Root.XamlRoot,
                    Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
                    RequestedTheme = Root.ActualTheme,
                    Title = Strings.Format("ExternalTitle", doc.FileName),
                    Content = Strings.Get("ExternalText"),
                    PrimaryButtonText = Strings.Get("ExternalReload"),
                    CloseButtonText = Strings.Get("ExternalKeep"),
                    DefaultButton = ContentDialogButton.Close,
                };
                if (await dialog.ShowAsync() == ContentDialogResult.Primary)
                {
                    await editor.ReloadAsync(confirmed: true);
                }
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Disk check", ex);
        }
        finally
        {
            _checkingDisk = false;
        }
    }
}
