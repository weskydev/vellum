using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Vellum.Services;

namespace Vellum;

/// <summary>
/// The mini window: small, on top of other windows, for a note next to whatever else is open (Ctrl+Shift+M).
/// It remembers its own place, separately from the normal window.
/// </summary>
public sealed partial class MainWindow
{
    private bool _mini;
    private WindowBounds? _miniBounds;
    private bool _topmostBeforeMini;

    private void SetupMini(SessionState? saved)
    {
        MiniButton.Content = Glyph("");
        _miniBounds = saved?.MiniBounds;
        // Also when the focus is not in the text (the editor handles it there itself)
        AddShortcut(Windows.System.VirtualKey.M, Windows.System.VirtualKeyModifiers.Control | Windows.System.VirtualKeyModifiers.Shift, ToggleMini);
        if (saved?.Mini == true && SettingsService.Current.MiniWindow) SetMini(true);
    }

    private void ToggleMini()
    {
        if (!_mini && !SettingsService.Current.MiniWindow)
        {
            SelectedEditor?.Notify(Strings.Get("MiniOff"));
            return;
        }
        SetMini(!_mini);
    }

    private void Mini_Click(object sender, RoutedEventArgs e) => ToggleMini();

    private void SetMini(bool on)
    {
        if (on == _mini || AppWindow.Presenter is not OverlappedPresenter presenter) return;
        if (on)
        {
            SetZen(false);
            if (presenter.State != OverlappedPresenterState.Restored) presenter.Restore();
            RememberNormalBounds();
            _topmostBeforeMini = presenter.IsAlwaysOnTop;
            _mini = true;
            presenter.IsAlwaysOnTop = true;
            presenter.IsMaximizable = false;
            AppWindow.MoveAndResize(MiniRect());
        }
        else
        {
            RememberMiniBounds();
            _mini = false;
            presenter.IsAlwaysOnTop = _topmostBeforeMini;
            presenter.IsMaximizable = true;
            if (_normalBounds.Width > 200 && _normalBounds.Height > 150)
            {
                AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(_normalBounds.X, _normalBounds.Y, _normalBounds.Width, _normalBounds.Height));
            }
        }
        MiniButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
        MaximizeButton.Visibility = MinimizeButton.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        // Little room for tabs: no "+" button (Ctrl+T still works)
        Tabs.IsAddTabButtonVisible = !on;
        string name = Strings.Get(on ? "CaptionMiniName" : "CaptionMaximizeName");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MiniButton, name);
        UpdateDragRegion();
        ScheduleSessionSave();
        FocusSelectedEditor();
    }

    /// <summary>Last place of the mini window if it is still on a screen; otherwise the bottom-right corner.</summary>
    private Windows.Graphics.RectInt32 MiniRect()
    {
        if (_miniBounds is { Width: > 150, Height: > 120 } b &&
            DisplayArea.GetFromRect(new Windows.Graphics.RectInt32(b.X, b.Y, b.Width, b.Height), DisplayAreaFallback.None) is not null)
        {
            return new Windows.Graphics.RectInt32(b.X, b.Y, b.Width, b.Height);
        }
        double scale = Native.GetDpiForWindow(Hwnd) / 96.0;
        if (scale <= 0) scale = 1;
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
        int width = Math.Min(area.Width, (int)(380 * scale));
        int height = Math.Min(area.Height, (int)(460 * scale));
        int margin = (int)(16 * scale);
        return new Windows.Graphics.RectInt32(area.X + area.Width - width - margin, area.Y + area.Height - height - margin, width, height);
    }

    private void RememberMiniBounds()
    {
        if (!_mini || AppWindow.Presenter is not OverlappedPresenter { State: OverlappedPresenterState.Restored }) return;
        _miniBounds = new WindowBounds
        {
            X = AppWindow.Position.X,
            Y = AppWindow.Position.Y,
            Width = AppWindow.Size.Width,
            Height = AppWindow.Size.Height,
        };
    }
}
