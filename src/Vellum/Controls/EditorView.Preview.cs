using Markdig.Syntax;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vellum.Services;
using Vellum.Services.Syntax;

namespace Vellum.Controls;

public enum PreviewMode
{
    Off,
    Split,
    Full,
}

/// <summary>Markdown preview next to the text (Ctrl+Shift+V): off → side by side → preview only.</summary>
public sealed partial class EditorView
{
    private PreviewMode _previewMode = PreviewMode.Off;
    private ScrollViewer? _previewScroller;
    private StackPanel? _previewContent;
    private DispatcherQueueTimer? _previewTimer;
    private int _renderedVersion = -1;

    public bool CanPreview => Language.IsMarkdown && SettingsService.Current.MarkdownPreview;

    public void TogglePreview()
    {
        if (!CanPreview) return;
        SetPreviewMode(_previewMode switch
        {
            PreviewMode.Off => PreviewMode.Split,
            PreviewMode.Split => PreviewMode.Full,
            _ => PreviewMode.Off,
        });
    }

    private void PreviewButton_Click(object sender, RoutedEventArgs e) => TogglePreview();

    /// <summary>The "?" next to the preview button: show the Markdown cheat sheet.</summary>
    public event EventHandler? HelpRequested;

    private void HelpButton_Click(object sender, RoutedEventArgs e) => HelpRequested?.Invoke(this, EventArgs.Empty);

    private void SetPreviewMode(PreviewMode mode)
    {
        _previewMode = mode;
        PreviewHost.Visibility = mode == PreviewMode.Off ? Visibility.Collapsed : Visibility.Visible;
        PreviewHost.BorderThickness = new Thickness(mode == PreviewMode.Split ? 1 : 0, 0, 0, 0);
        EditorColumn.Width = mode == PreviewMode.Full ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        PreviewColumn.Width = mode == PreviewMode.Off ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
        Editor.Visibility = mode == PreviewMode.Full ? Visibility.Collapsed : Visibility.Visible;
        UpdatePreviewButton();
        ScheduleGutter(); // hides the line numbers and the line band with the text, or brings them back

        if (mode == PreviewMode.Off)
        {
            FocusEditor();
            return;
        }
        EnsurePreview();
        RefreshPreviewPage();
    }

    private void UpdatePreviewButton()
    {
        PreviewText.Text = Strings.Get(_previewMode switch
        {
            PreviewMode.Split => "PreviewSplit",
            PreviewMode.Full => "PreviewFull",
            _ => "PreviewOff",
        });
        PreviewIcon.Glyph = _previewMode == PreviewMode.Off ? "" : "";
    }

    private void EnsurePreview()
    {
        if (_previewScroller is not null) return;

        _previewTimer = DispatcherQueue.CreateTimer();
        _previewTimer.Interval = TimeSpan.FromMilliseconds(250);
        _previewTimer.IsRepeating = false;
        _previewTimer.Tick += (_, _) => _ = RenderPreviewAsync();

        // Fills the pane up to a comfortable reading width, then stays centred
        _previewContent = new StackPanel { MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Stretch };
        _previewScroller = new ScrollViewer
        {
            Content = _previewContent,
            Padding = new Thickness(32, 22, 32, 60),
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        PreviewHost.Child = _previewScroller;
    }

    /// <summary>Builds the preview again (after a theme, accent or font change).</summary>
    private void RefreshPreviewPage()
    {
        _renderedVersion = -1;
        _ = RenderPreviewAsync();
    }

    private void SchedulePreview()
    {
        if (_previewMode == PreviewMode.Off || _previewTimer is null) return;
        _previewTimer.Stop();
        _previewTimer.Start();
    }

    private async Task RenderPreviewAsync()
    {
        if (_previewContent is null || _previewMode == PreviewMode.Off || _renderedVersion == ContentVersion) return;
        int version = ContentVersion;
        string text = _text;
        try
        {
            MarkdownDocument document = await Task.Run(() => MarkdownPreviewBuilder.Parse(text));
            if (version != ContentVersion && _renderedVersion != -1) return; // a newer render is on its way
            string? folder = Document.FilePath is { } path ? Path.GetDirectoryName(path) : null;
            var builder = new MarkdownPreviewBuilder(SyntaxPalette.For(IsDark), SettingsService.Current.FontSize * ZoomService.Factor,
                SettingsService.Current.FontFamily, folder, OpenLink);
            builder.Fill(_previewContent, document);
            _renderedVersion = version;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Markdown preview", ex);
        }
    }

    private void SyncPreviewScroll()
    {
        if (_previewMode != PreviewMode.Split || _scroller is null || _previewScroller is null) return;
        double max = _scroller.ScrollableHeight;
        double ratio = max > 0 ? _scroller.VerticalOffset / max : 0;
        _previewScroller.ChangeView(null, ratio * _previewScroller.ScrollableHeight, null, true);
    }

    /// <summary>Web links open in the browser; links to local files open in Vellum.</summary>
    private async void OpenLink(string url)
    {
        try
        {
            if (url.StartsWith('#')) return;
            if (Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            {
                if (uri.Scheme is "http" or "https" or "mailto")
                {
                    await Windows.System.Launcher.LaunchUriAsync(uri);
                }
                else if (uri.IsFile && File.Exists(uri.LocalPath))
                {
                    OpenFileRequested?.Invoke(this, uri.LocalPath);
                }
                return;
            }
            if (Document.FilePath is { } path && Path.GetDirectoryName(path) is { } folder)
            {
                string target = Path.GetFullPath(Path.Combine(folder, Uri.UnescapeDataString(url.Split('#', '?')[0])));
                if (File.Exists(target)) OpenFileRequested?.Invoke(this, target);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Open link", ex);
        }
    }
}
