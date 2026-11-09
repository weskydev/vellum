using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Windows.System;

namespace Vellum;

/// <summary>Zen mode: full screen, tabs hidden, just the text.</summary>
public sealed partial class MainWindow
{
    private AppWindowPresenter? _presenterBeforeZen;

    private void SetupWriting()
    {
        // Esc leaves zen mode (unless something inside, like the find box, used it already)
        Root.KeyDown += (_, e) =>
        {
            if (e.Key != VirtualKey.Escape) return;
            // Panels first, even when focus stayed behind them
            if (HistoryOpen)
            {
                CloseHistory();
                e.Handled = true;
            }
            else if (LibraryOpen)
            {
                CloseLibrary();
                e.Handled = true;
            }
            else if (WritingModes.Zen)
            {
                SetZen(false);
                e.Handled = true;
            }
        };
    }

    private void ToggleZen() => SetZen(!WritingModes.Zen);

    private void SetZen(bool on)
    {
        if (on == WritingModes.Zen) return;

        // Set first: the window size during zen must not be remembered as the normal size
        if (on) WritingModes.SetZen(true);

        if (on)
        {
            _presenterBeforeZen = AppWindow.Presenter;
            AppWindow.SetPresenter(AppWindowPresenterKind.FullScreen);
        }
        else if (_presenterBeforeZen is not null)
        {
            AppWindow.SetPresenter(_presenterBeforeZen);
        }
        else
        {
            AppWindow.SetPresenter(AppWindowPresenterKind.Overlapped);
        }
        if (!on) HideSystemCaption();

        if (FindByName(Tabs, "TabContainerGrid") is FrameworkElement strip)
        {
            strip.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
        }
        if (!on) WritingModes.SetZen(false);
        FocusSelectedEditor();
    }

    private static DependencyObject? FindByName(DependencyObject parent, string name)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is FrameworkElement { Name: var n } && n == name) return child;
            if (FindByName(child, name) is { } deeper) return deeper;
        }
        return null;
    }
}
