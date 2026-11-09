using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace Vellum;

/// <summary>Vellum's own minimize / maximize / close buttons on soft rounded hover pills instead of the Windows ones.</summary>
public sealed partial class MainWindow
{
    // Thin caption glyphs from Segoe Fluent Icons
    private const string MinimizeGlyph = "";
    private const string MaximizeGlyph = "";
    private const string RestoreGlyph = "";
    private const string CloseGlyph = "";

    private bool? _shownMaximized;

    private void SetupCaption()
    {
        HideSystemCaption();
        MinimizeButton.Content = Glyph(MinimizeGlyph);
        CloseButton.Content = Glyph(CloseGlyph);
        UpdateMaximizeIcon();

        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPresenterChange || args.DidSizeChange) UpdateMaximizeIcon();
        };
        // Dim the buttons while another window is active, like Windows does
        Activated += (_, args) =>
            CaptionButtons.Opacity = args.WindowActivationState == WindowActivationState.Deactivated ? 0.45 : 1;
    }

    /// <summary>Removes the Windows caption buttons; the window keeps its border and can still be resized.</summary>
    private void HideSystemCaption()
    {
        try
        {
            if (AppWindow.Presenter is OverlappedPresenter p)
            {
                p.SetBorderAndTitleBar(true, false);
            }
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Caption: hide system buttons", ex);
        }
    }

    private void UpdateMaximizeIcon()
    {
        bool maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized };
        if (_shownMaximized == maximized) return;
        _shownMaximized = maximized;
        MaximizeButton.Content = Glyph(maximized ? RestoreGlyph : MaximizeGlyph);
        string name = Services.Strings.Get(maximized ? "CaptionRestoreName" : "CaptionMaximizeName");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(MaximizeButton, name);
        ToolTipService.SetToolTip(MaximizeButton, name);
    }

    private static FontIcon Glyph(string glyph) => new()
    {
        Glyph = glyph,
        FontSize = 10,
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
    };

    private void Minimize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is OverlappedPresenter p) p.Minimize();
    }

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        if (AppWindow.Presenter is not OverlappedPresenter p) return;
        if (p.State == OverlappedPresenterState.Maximized) p.Restore();
        else p.Maximize();
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        if (CloseHidesToTray) HideToTray();
        else Exit_Click(sender, e);
    }
}
