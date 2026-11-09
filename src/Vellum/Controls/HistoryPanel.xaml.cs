using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Vellum.Models;
using Vellum.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Vellum.Controls;

/// <summary>
/// Version history of one note or file: pick a version, look at it or at what changed since, bring it back.
/// </summary>
public sealed partial class HistoryPanel : UserControl
{
    // Long texts: only this many lines are shown in the preview
    private const int MaxLines = 20000;

    private Func<string> _current = () => "";
    private Action<string>? _restore;
    private string _versionText = "";

    public HistoryPanel()
    {
        InitializeComponent();
        ChangesSwitch.IsOn = true; // set here: in XAML it fires before the lists exist
        SizeChanged += (_, e) => ListColumn.Width = new GridLength(e.NewSize.Width < 620 ? 170 : 230);
    }

    public event EventHandler? CloseRequested;

    /// <param name="name">Note title or file name, under the panel title.</param>
    /// <param name="current">The text as it is now, to compare with.</param>
    /// <param name="restore">Puts an older text back into the editor.</param>
    /// <summary>Puts keyboard focus inside the panel: the version list, or the close button if there are none.</summary>
    public void FocusFirst()
    {
        if (VersionList.Items.Count > 0 && VersionList.Focus(FocusState.Programmatic)) return;
        if (FocusManager.FindFirstFocusableElement(this) is Control first) first.Focus(FocusState.Programmatic);
    }

    public void Show(string name, string key, Func<string> current, Action<string> restore)
    {
        NameText.Text = name;
        _current = current;
        _restore = restore;

        string now = current();
        List<HistoryVersion> versions = HistoryService.List(key);
        VersionList.ItemsSource = versions.Select(v => new VersionItem
        {
            Version = v,
            When = Describe(v.Time),
            Detail = Strings.Format("HistorySize", Size(v.Size)),
        }).ToList();

        bool any = versions.Count > 0;
        EmptyText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
        LineList.ItemsSource = null;
        SummaryText.Text = "";
        CopyButton.IsEnabled = RestoreButton.IsEnabled = false;
        ChangesSwitch.IsEnabled = any;
        if (any)
        {
            // Start on the newest version that differs from the current text
            int start = versions.FindIndex(v => v.ReadText() != now);
            VersionList.SelectedIndex = start < 0 ? 0 : start;
            VersionList.Focus(FocusState.Programmatic);
        }
    }

    private static string Describe(DateTime utc)
    {
        DateTime local = utc.ToLocalTime();
        CultureInfo culture = CultureInfo.CurrentCulture;
        string time = local.ToString("t", culture);
        if (local.Date == DateTime.Today) return Strings.Format("HistoryToday", time);
        if (local.Date == DateTime.Today.AddDays(-1)) return Strings.Format("HistoryYesterday", time);
        return local.ToString("d", culture) + ", " + time;
    }

    private static string Size(long bytes) => bytes < 1024
        ? Strings.Format("SizeBytes", bytes)
        : Strings.Format("SizeKb", (bytes / 1024.0).ToString("0.#", CultureInfo.CurrentCulture));

    private void VersionList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowSelected();

    private void ChangesSwitch_Toggled(object sender, RoutedEventArgs e) => ShowSelected();

    private void ShowSelected()
    {
        if (VersionList.SelectedItem is not VersionItem item)
        {
            CopyButton.IsEnabled = RestoreButton.IsEnabled = false;
            return;
        }

        _versionText = item.Version.ReadText();
        string now = _current();
        CopyButton.IsEnabled = true;
        RestoreButton.IsEnabled = _versionText != now;

        List<DiffLine> lines = ChangesSwitch.IsOn
            ? LineDiff.Compare(_versionText, now)
            : _versionText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n').Select(l => new DiffLine(DiffKind.Same, l)).ToList();

        int added = lines.Count(l => l.Kind == DiffKind.Added);
        int removed = lines.Count(l => l.Kind == DiffKind.Removed);
        SummaryText.Text = !ChangesSwitch.IsOn ? Strings.Format("HistoryLines", lines.Count)
            : added == 0 && removed == 0 ? Strings.Get("HistorySame")
            : Strings.Format("HistoryChanges", added, removed);

        LineList.ItemsSource = lines.Take(MaxLines).Select(l => new DiffRow(l.Kind, l.Text)).ToList();
        if (ChangesSwitch.IsOn && lines.FindIndex(l => l.Kind != DiffKind.Same) is int first and > 2)
        {
            // Scroll to the first change
            LineList.UpdateLayout();
            if (first - 2 < LineList.Items.Count) LineList.ScrollIntoView(LineList.Items[first - 2], ScrollIntoViewAlignment.Leading);
        }
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        var package = new DataPackage();
        package.SetText(_versionText.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n"));
        Clipboard.SetContent(package);
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        _restore?.Invoke(_versionText);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);
}
