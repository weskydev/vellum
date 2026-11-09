using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Vellum.Controls;
using Vellum.Models;
using Vellum.Services;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace Vellum;

/// <summary>
/// Notes library and version history.
/// A note saves itself a moment after you stop typing; closing its tab keeps it in the library.
/// </summary>
public sealed partial class MainWindow
{
    private DispatcherQueueTimer? _noteTimer;

    private void SetupNotes()
    {
        _noteTimer = DispatcherQueue.CreateTimer();
        _noteTimer.Interval = TimeSpan.FromSeconds(0.8);
        _noteTimer.IsRepeating = false;
        _noteTimer.Tick += (_, _) => SaveNotesNow();

        Library.OpenNotes = () => AllTabs.Select(t => Parts(t).Editor.Document).Where(d => d.IsNote).Select(d => d.Id).ToHashSet();
        Library.OpenRequested += (_, id) =>
        {
            CloseLibrary();
            OpenNote(id);
        };
        Library.NewRequested += (_, _) =>
        {
            CloseLibrary();
            AddTab(NewEditor(note: true));
        };
        Library.HistoryRequested += (_, id) =>
        {
            CloseLibrary();
            if (OpenNote(id) is { } editor) ShowHistory(editor);
        };
        Library.ExportRequested += async (_, id) =>
        {
            if (NotesLibrary.LoadText(id) is { } text) await ExportNoteAsync(NotesLibrary.Find(id)?.Title ?? "", text);
        };
        Library.TrashRequested += (_, id) => TrashNote(id);
        Library.CloseRequested += (_, _) => CloseLibrary();

        History.CloseRequested += (_, _) => CloseHistory();

        AddShortcut(Windows.System.VirtualKey.L, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, ToggleLibrary);
        AddShortcut(Windows.System.VirtualKey.H, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, ShowHistoryForSelected);
    }

    private static bool LibraryOn => SettingsService.Current.NotesLibrary;

    /// <summary>A new, empty tab: a note in the library, or a plain untitled text when the library is off.</summary>
    private static EditorView NewEditor(bool note = false)
    {
        var document = new TextDocument();
        if (note || LibraryOn)
        {
            document.IsNote = true;
            document.SyntaxOverride = "markdown"; // notes get headings, lists and the preview
        }
        return new EditorView(document);
    }

    /// <summary>Opens a note from the library (or switches to its tab). Returns its editor.</summary>
    private EditorView? OpenNote(Guid id)
    {
        if (AllTabs.FirstOrDefault(t => Parts(t).Editor.Document.IsNote && Parts(t).Editor.Document.Id == id) is { } open)
        {
            Tabs.SelectedItem = open;
            return Parts(open).Editor;
        }
        if (NotesLibrary.IsLocked(id))
        {
            _ = UnlockAndOpenAsync(id); // asks for the password first
            return null;
        }
        if (NotesLibrary.LoadText(id) is not { } text) return null;

        // Reuse an empty new tab instead of leaving it behind
        TabViewItem? pristine = Tabs.SelectedItem is TabViewItem { Tag: TabParts sp } sel && sp.Editor.IsPristine ? sel : null;

        var document = new TextDocument { Id = id, IsNote = true, SyntaxOverride = "markdown", NoteTitle = NotesLibrary.TitleOf(text) };
        var editor = new EditorView(document, text, text);
        AddTab(editor);
        if (pristine is not null) Tabs.TabItems.Remove(pristine);
        return editor;
    }

    /// <summary>Called on every edit of a note: the title follows the first line, the text is saved shortly after.</summary>
    private void NoteEdited(TabViewItem tab, EditorView editor)
    {
        string title = NotesLibrary.TitleOf(editor.Text);
        if (title != editor.Document.NoteTitle)
        {
            editor.Document.NoteTitle = title;
            RefreshTab(tab);
        }
        _noteTimer?.Stop();
        _noteTimer?.Start();
    }

    /// <summary>Writes every changed note to the library, with an automatic version now and then.</summary>
    private void SaveNotesNow()
    {
        _noteTimer?.Stop();
        foreach (TabViewItem tab in AllTabs.ToList())
        {
            EditorView editor = Parts(tab).Editor;
            if (!editor.Document.IsNote || !editor.Document.IsDirty) continue;
            SaveNote(editor, auto: true);
            RefreshTab(tab);
        }
    }

    private static void SaveNote(EditorView editor, bool auto)
    {
        TextDocument doc = editor.Document;
        string text = editor.Text;
        if (text.Trim().Length == 0 && !NotesLibrary.Contains(doc.Id))
        {
            editor.MarkSaved(); // an empty new note is not worth keeping
            return;
        }
        if (doc.Lock is { } key)
        {
            NotesLibrary.SaveLocked(doc.Id, text, key); // encrypted, and no versions are kept of it
        }
        else
        {
            NotesLibrary.Save(doc.Id, text);
            HistoryService.Snapshot(HistoryService.NoteKey(doc.Id), text, auto);
        }
        editor.MarkSaved();
    }

    /// <summary>Before a note's tab closes: save it and keep a version; an emptied note goes to the trash.</summary>
    private static void CloseNote(EditorView editor)
    {
        SaveNote(editor, auto: false);
        if (editor.Text.Trim().Length == 0 && NotesLibrary.Contains(editor.Document.Id))
        {
            NotesLibrary.MoveToTrash(editor.Document.Id);
        }
    }

    private void TrashNote(Guid id)
    {
        if (AllTabs.FirstOrDefault(t => Parts(t).Editor.Document.IsNote && Parts(t).Editor.Document.Id == id) is { } tab)
        {
            SaveNote(Parts(tab).Editor, auto: false);
            Tabs.TabItems.Remove(tab);
            if (!AllTabs.Any()) AddTab(NewEditor(), select: !LibraryOpen);
        }
        NotesLibrary.MoveToTrash(id);
        ScheduleSessionSave();
    }

    private async Task ExportNoteAsync(string title, string text)
    {
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        picker.FileTypeChoices.Add("Markdown", [".md"]);
        picker.FileTypeChoices.Add(Strings.Get("FileTypeText"), [".txt"]);
        picker.SuggestedFileName = title.Length > 0 ? SuggestName(title) : Strings.Get("NoteUntitled");
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;
        try
        {
            var settings = SettingsService.Current;
            TextFileService.Save(file.Path, text, TextFileService.FromCodePage(settings.NewFileCodePage, settings.NewFileBom), settings.NewFileLineEnding);
        }
        catch (Exception ex)
        {
            await ShowErrorAsync(Strings.Format("ErrorSaveTitle", file.Name), ex.Message);
        }
    }

    // ---------- Files: versions taken on save ----------

    private static bool FileHistoryOn => SettingsService.Current.VersionHistory && SettingsService.Current.FileHistory;

    /// <summary>Before a file is overwritten the first time, its text on disk becomes the first version.</summary>
    private static void KeepOriginal(EditorView editor, string path)
    {
        if (!FileHistoryOn || !File.Exists(path)) return;
        string key = HistoryService.FileKey(path);
        if (HistoryService.Latest(key) is not null) return;
        try
        {
            HistoryService.Snapshot(key, TextFileService.Load(path).Text.Replace("\r\n", "\r").Replace('\n', '\r'), label: path);
        }
        catch (Exception ex)
        {
            CrashLog.Write("History of " + path, ex);
        }
    }

    private static void KeepSaved(EditorView editor, string path, bool auto)
    {
        if (FileHistoryOn) HistoryService.Snapshot(HistoryService.FileKey(path), editor.Text, auto, label: path);
    }

    // ---------- Library panel ----------

    private bool LibraryOpen => LibraryLayer.Visibility == Visibility.Visible;

    private void ToggleLibrary()
    {
        if (LibraryOpen)
        {
            CloseLibrary();
            return;
        }
        if (!LibraryOn)
        {
            SelectedEditor?.Notify(Strings.Get("LibraryOff"));
            return;
        }
        SaveNotesNow(); // the list shows the latest titles
        LibraryHost.Width = Math.Max(300, Math.Min(400, Root.ActualWidth - 32));
        LibraryLayer.Visibility = Visibility.Visible;
        Library.Prepare();
        Motion.Enter(LibraryHost, offsetY: 0, fromOpacity: 0, offsetX: -40);
    }

    private void CloseLibrary()
    {
        if (!LibraryOpen) return;
        LibraryLayer.Visibility = Visibility.Collapsed;
        FocusSelectedEditor();
    }

    private void Library_Click(object sender, RoutedEventArgs e) => ToggleLibrary();

    private void LibraryBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseLibrary();

    private void LibraryLayer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseLibrary();
            e.Handled = true;
        }
    }

    // ---------- History panel ----------

    private bool HistoryOpen => HistoryLayer.Visibility == Visibility.Visible;

    private void ShowHistoryForSelected()
    {
        if (SelectedEditor is { } editor) ShowHistory(editor);
    }

    private void ShowHistory(EditorView editor)
    {
        if (HistoryOpen)
        {
            CloseHistory();
            return;
        }
        if (!HistoryService.IsOn)
        {
            editor.Notify(Strings.Get("HistoryOff"));
            return;
        }
        if (editor.Document.Lock is not null)
        {
            editor.Notify(Strings.Get("HistoryLocked"));
            return;
        }
        if (editor.Document.IsNote) SaveNotesNow();
        if (editor.Document.HistoryKey is not { } key || (editor.Document.FilePath is not null && !FileHistoryOn))
        {
            editor.Notify(Strings.Get(editor.Document.FilePath is null ? "HistoryUntitled" : "HistoryFilesOff"));
            return;
        }

        HistoryHost.Width = Math.Max(340, Math.Min(1040, Root.ActualWidth - 48));
        HistoryHost.Height = Math.Max(320, Math.Min(760, Root.ActualHeight - 48));
        HistoryLayer.Visibility = Visibility.Visible;
        History.Show(
            editor.Document.FilePath ?? editor.Document.FileName,
            key,
            () => editor.Text,
            text =>
            {
                // The text as it is now becomes a version too, so the restore can be undone later
                HistoryService.Snapshot(key, editor.Text, label: editor.Document.FilePath);
                editor.ReplaceAllText(text);
                editor.Notify(Strings.Get("HistoryRestored"));
            });
        Motion.Enter(HistoryHost, offsetY: 12);
        // Keys go to the panel, not to the text behind it (the list is ready only after layout)
        DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, History.FocusFirst);
    }

    private void CloseHistory()
    {
        if (!HistoryOpen) return;
        HistoryLayer.Visibility = Visibility.Collapsed;
        FocusSelectedEditor();
    }

    private void History_Click(object sender, RoutedEventArgs e) => ShowHistoryForSelected();

    private void HistoryBackdrop_Tapped(object sender, TappedRoutedEventArgs e) => CloseHistory();

    private void HistoryLayer_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CloseHistory();
            e.Handled = true;
        }
    }
}
