using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Vellum.Models;
using Vellum.Services;
using Windows.System;

namespace Vellum.Controls;

/// <summary>
/// The notes library: every note with search, tags, pins and the trash.
/// The window decides what "open", "history" or "export" mean; this panel only asks.
/// </summary>
public sealed partial class LibraryPanel : UserControl
{
    private string? _tag;
    private bool _trash;

    public LibraryPanel()
    {
        InitializeComponent();
        NotesLibrary.Changed += (_, _) => { if (IsLoaded) Refresh(); };
    }

    /// <summary>Ids of notes open in tabs right now (marked with a dot).</summary>
    public Func<IReadOnlySet<Guid>> OpenNotes { get; set; } = () => new HashSet<Guid>();

    public event EventHandler<Guid>? OpenRequested;
    public event EventHandler? NewRequested;
    public event EventHandler<Guid>? HistoryRequested;
    public event EventHandler<Guid>? ExportRequested;
    /// <summary>Move to the trash; the window closes the note's tab first.</summary>
    public event EventHandler<Guid>? TrashRequested;
    public event EventHandler? CloseRequested;

    /// <summary>Called each time the panel is shown.</summary>
    public void Prepare()
    {
        _trash = false;
        TrashToggle.IsChecked = false;
        SearchBox.Text = "";
        Refresh();
        SearchBox.Focus(FocusState.Programmatic);
    }

    public void Refresh()
    {
        // The chosen tag may be gone after an edit
        List<(string Tag, int Count)> tags = NotesLibrary.AllTags();
        if (_tag is not null && !tags.Any(t => string.Equals(t.Tag, _tag, StringComparison.OrdinalIgnoreCase))) _tag = null;
        BuildTags(tags);

        IReadOnlySet<Guid> open = OpenNotes();
        List<NoteInfo> notes = NotesLibrary.Search(SearchBox.Text, _trash ? null : _tag, _trash);
        NoteList.ItemsSource = notes.Select(n => new NoteListItem
        {
            Id = n.Id,
            Title = n.Title.Length > 0 ? n.Title : Strings.Get("NoteUntitled"),
            Snippet = n.Locked ? Strings.Get("NoteLockedSnippet") : n.Snippet,
            Meta = Strings.Format("LibraryMeta", When(_trash ? n.Deleted ?? n.Modified : n.Modified), n.Words),
            Tags = string.Join("  ", n.Tags.Take(4).Select(t => "#" + t)),
            Pinned = n.Pinned,
            IsOpen = open.Contains(n.Id),
            Locked = n.Locked,
        }).ToList();

        int trashCount = NotesLibrary.Trash.Count();
        TrashText.Text = trashCount > 0 ? Strings.Format("LibraryTrashCount", trashCount) : Strings.Get("LibraryTrash");
        EmptyTrashButton.Visibility = _trash && trashCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        HeaderText.Text = Strings.Get(_trash ? "LibraryTrash" : "LibraryTitleText");
        NewButton.Visibility = _trash ? Visibility.Collapsed : Visibility.Visible;
        TagScroller.Visibility = !_trash && tags.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        bool empty = notes.Count == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        EmptyIcon.Glyph = _trash ? "" : SearchBox.Text.Length > 0 ? "" : "";
        EmptyText.Text = Strings.Get(
            _trash ? "LibraryEmptyTrashText" :
            SearchBox.Text.Length > 0 || _tag is not null ? "LibraryNothingFound" :
            "LibraryEmptyText");
    }

    private void BuildTags(List<(string Tag, int Count)> tags)
    {
        TagStrip.Children.Clear();
        if (tags.Count == 0) return;
        TagStrip.Children.Add(TagButton(Strings.Get("LibraryAllTags"), null));
        foreach (var (tag, count) in tags)
        {
            TagStrip.Children.Add(TagButton($"#{tag}  {count}", tag));
        }
    }

    private ToggleButton TagButton(string text, string? tag)
    {
        var button = new ToggleButton
        {
            Content = text,
            Padding = new Thickness(10, 3, 10, 4),
            FontSize = 12,
            CornerRadius = new CornerRadius(12),
            IsChecked = string.Equals(_tag, tag, StringComparison.OrdinalIgnoreCase),
        };
        button.Click += (_, _) =>
        {
            _tag = tag;
            Refresh();
        };
        return button;
    }

    /// <summary>"14:05" today, "Yesterday", "3 Nov", "3 Nov 2025".</summary>
    private static string When(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        DateTime today = DateTime.Today;
        CultureInfo culture = CultureInfo.CurrentCulture;
        if (local.Date == today) return local.ToString("t", culture);
        if (local.Date == today.AddDays(-1)) return Strings.Get("LibraryYesterday");
        string dayMonth = culture.DateTimeFormat.MonthDayPattern;
        return local.Year == today.Year ? local.ToString(dayMonth, culture) : local.ToString("d", culture);
    }

    // ---------- Actions ----------

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void SearchBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Enter when NoteList.Items.Count > 0:
                Open((NoteListItem)NoteList.Items[0]);
                e.Handled = true;
                break;
            case VirtualKey.Down when NoteList.Items.Count > 0:
                NoteList.SelectedIndex = 0;
                (NoteList.ContainerFromIndex(0) as ListViewItem)?.Focus(FocusState.Keyboard);
                e.Handled = true;
                break;
        }
    }

    private void NoteList_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (NoteList.SelectedItem is not NoteListItem item) return;
        switch (e.Key)
        {
            case VirtualKey.Enter:
                Open(item);
                e.Handled = true;
                break;
            case VirtualKey.Delete:
                if (_trash) NotesLibrary.DeleteForever(item.Id);
                else TrashRequested?.Invoke(this, item.Id);
                e.Handled = true;
                break;
        }
    }

    private void NoteList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is NoteListItem item) Open(item);
    }

    private void Open(NoteListItem item)
    {
        if (_trash)
        {
            NotesLibrary.Restore(item.Id); // a note from the trash comes back when opened
        }
        OpenRequested?.Invoke(this, item.Id);
    }

    private void Item_ContextRequested(UIElement sender, ContextRequestedEventArgs args)
    {
        if ((sender as FrameworkElement)?.DataContext is not NoteListItem item) return;
        args.Handled = true;

        var menu = new MenuFlyout();
        void Add(string key, string glyph, Action action)
        {
            var mi = new MenuFlyoutItem { Text = Strings.Get(key), Icon = new FontIcon { Glyph = glyph } };
            mi.Click += (_, _) => action();
            menu.Items.Add(mi);
        }

        if (_trash)
        {
            Add("LibraryRestore", "", () => NotesLibrary.Restore(item.Id));
            Add("LibraryDeleteForever", "", () => NotesLibrary.DeleteForever(item.Id));
        }
        else
        {
            Add("LibraryOpen", "", () => Open(item));
            Add(item.Pinned ? "NoteUnpin" : "NotePin", item.Pinned ? "" : "", () => NotesLibrary.SetPinned(item.Id, !item.Pinned));
            if (!item.Locked) // a locked note has no history, and is exported once opened
            {
                Add("NoteHistory", "", () => HistoryRequested?.Invoke(this, item.Id));
                Add("NoteExport", "", () => ExportRequested?.Invoke(this, item.Id));
            }
            menu.Items.Add(new MenuFlyoutSeparator());
            Add("NoteDelete", "", () => TrashRequested?.Invoke(this, item.Id));
        }

        if (args.TryGetPosition(sender, out var point))
        {
            menu.ShowAt(sender, point);
        }
        else
        {
            menu.ShowAt((FrameworkElement)sender);
        }
    }

    private void New_Click(object sender, RoutedEventArgs e) => NewRequested?.Invoke(this, EventArgs.Empty);

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    private void TrashToggle_Click(object sender, RoutedEventArgs e)
    {
        _trash = TrashToggle.IsChecked == true;
        Refresh();
    }

    private void EmptyTrash_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 12, MaxWidth = 260 };
        panel.Children.Add(new TextBlock { Text = Strings.Get("LibraryEmptyTrashConfirm"), TextWrapping = TextWrapping.Wrap });
        var yes = new Button { Content = Strings.Get("LibraryEmptyTrashYes"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        yes.Click += (_, _) =>
        {
            flyout.Hide();
            NotesLibrary.EmptyTrash();
        };
        panel.Children.Add(yes);
        flyout.Content = panel;
        flyout.ShowAt(EmptyTrashButton);
    }
}
