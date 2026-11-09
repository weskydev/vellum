using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vellum.Controls;
using Vellum.Models;
using Vellum.Services;

namespace Vellum;

/// <summary>
/// Notes under a password. The text is encrypted on disk, never kept in the session or in the history,
/// and opened only after the password is typed. A forgotten password cannot be recovered.
/// </summary>
public sealed partial class MainWindow
{
    /// <summary>Asks for the password of a locked note and opens it.</summary>
    private async Task UnlockAndOpenAsync(Guid id)
    {
        if (NotesLibrary.LoadText(id) is not { } fileText) return;
        string title = NotesLibrary.Find(id)?.Title ?? "";

        (string Text, NoteKey Key)? opened = null;
        bool ok = await AskPasswordAsync(
            Strings.Format("LockOpenTitle", title.Length > 0 ? title : Strings.Get("NoteUntitled")),
            Strings.Get("LockOpenText"),
            twice: false,
            primary: Strings.Get("LockOpenButton"),
            check: async password =>
            {
                opened = await Task.Run(() => NoteLock.TryDecrypt(fileText, password));
                return opened is null ? Strings.Get("LockWrongPassword") : null;
            });
        if (!ok || opened is not { } note) return;

        // It may have been opened meanwhile (two clicks)
        if (AllTabs.FirstOrDefault(t => Parts(t).Editor.Document.IsNote && Parts(t).Editor.Document.Id == id) is { } open)
        {
            Tabs.SelectedItem = open;
            return;
        }

        TabViewItem? pristine = Tabs.SelectedItem is TabViewItem { Tag: TabParts sp } sel && sp.Editor.IsPristine ? sel : null;
        var document = new TextDocument
        {
            Id = id,
            IsNote = true,
            SyntaxOverride = "markdown",
            NoteTitle = NotesLibrary.TitleOf(note.Text),
            Lock = note.Key,
        };
        AddTab(new EditorView(document, note.Text, note.Text));
        if (pristine is not null) Tabs.TabItems.Remove(pristine);
    }

    /// <summary>Puts a note under a password. Its earlier versions are deleted, since they are not encrypted.</summary>
    private async Task LockNoteAsync(TabViewItem tab)
    {
        EditorView editor = Parts(tab).Editor;
        if (!editor.Document.IsNote || editor.Document.Lock is not null) return;

        NoteKey? key = null;
        bool ok = await AskPasswordAsync(
            Strings.Get("LockSetTitle"),
            Strings.Get("LockSetText"),
            twice: true,
            primary: Strings.Get("LockSetButton"),
            check: async password =>
            {
                key = await Task.Run(() => NoteLock.NewKey(password));
                return null;
            });
        if (!ok || key is null) return;

        editor.Document.Lock = key;
        SaveNote(editor, auto: false);
        HistoryService.Forget(HistoryService.NoteKey(editor.Document.Id));
        RefreshTab(tab);
        ScheduleSessionSave(); // drops the note's text from the session
        editor.Notify(Strings.Get("LockDone"));
    }

    /// <summary>Takes the password off a note, after asking for it once more.</summary>
    private async Task UnlockNoteAsync(TabViewItem tab)
    {
        EditorView editor = Parts(tab).Editor;
        if (editor.Document.Lock is not { } key) return;

        bool ok = await AskPasswordAsync(
            Strings.Get("LockRemoveTitle"),
            Strings.Get("LockRemoveText"),
            twice: false,
            primary: Strings.Get("LockRemoveButton"),
            check: async password =>
                await Task.Run(() => NoteLock.Matches(key, password)) ? null : Strings.Get("LockWrongPassword"));
        if (!ok) return;

        editor.Document.Lock = null;
        SaveNote(editor, auto: false);
        RefreshTab(tab);
        ScheduleSessionSave();
        editor.Notify(Strings.Get("LockRemoved"));
    }

    /// <summary>
    /// A password dialog. <paramref name="check"/> runs when the button is pressed and returns an error to show
    /// (the dialog stays open), or null when all is well. Returns true when it closed with the button.
    /// </summary>
    private async Task<bool> AskPasswordAsync(string title, string text, bool twice, string primary, Func<string, Task<string?>> check)
    {
        await _dialogLock.WaitAsync();
        try
        {
            var panel = new StackPanel { Spacing = 10, MinWidth = 320 };
            panel.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Opacity = 0.85 });
            var first = new PasswordBox { PlaceholderText = Strings.Get("LockPassword") };
            panel.Children.Add(first);
            PasswordBox? second = null;
            if (twice)
            {
                second = new PasswordBox { PlaceholderText = Strings.Get("LockPasswordAgain") };
                panel.Children.Add(second);
            }
            var error = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Microsoft.UI.Xaml.Media.Brush)Application.Current.Resources["SystemFillColorCriticalBrush"],
                Visibility = Visibility.Collapsed,
            };
            panel.Children.Add(error);

            ContentDialog dialog = CreateDialog();
            dialog.Title = title;
            dialog.Content = panel;
            dialog.PrimaryButtonText = primary;
            dialog.CloseButtonText = Strings.Get("ButtonCancel");
            dialog.DefaultButton = ContentDialogButton.Primary;
            dialog.IsPrimaryButtonEnabled = false;

            void Validate(object? s, RoutedEventArgs e)
            {
                error.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = first.Password.Length > 0 && (second is null || second.Password.Length > 0);
            }
            first.PasswordChanged += Validate;
            if (second is not null) second.PasswordChanged += Validate;

            dialog.PrimaryButtonClick += async (_, args) =>
            {
                ContentDialogButtonClickDeferral deferral = args.GetDeferral();
                try
                {
                    string? problem = second is not null && second.Password != first.Password
                        ? Strings.Get("LockMismatch")
                        : null;
                    if (problem is null)
                    {
                        dialog.IsPrimaryButtonEnabled = false;
                        problem = await check(first.Password);
                    }
                    if (problem is not null)
                    {
                        args.Cancel = true;
                        error.Text = problem;
                        error.Visibility = Visibility.Visible;
                        dialog.IsPrimaryButtonEnabled = true;
                        first.Focus(FocusState.Programmatic);
                        first.SelectAll();
                    }
                }
                finally
                {
                    deferral.Complete();
                }
            };
            dialog.Opened += (_, _) => first.Focus(FocusState.Programmatic);

            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        finally
        {
            _dialogLock.Release();
        }
    }
}
