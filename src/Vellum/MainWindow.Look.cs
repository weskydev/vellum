using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.UI;

namespace Vellum;

/// <summary>Small touches of the window's look that follow the accent color: the glow on the active tab.</summary>
public sealed partial class MainWindow
{
    private readonly LinearGradientBrush _tabGlow = new() { StartPoint = new Point(0, 0), EndPoint = new Point(0, 1) };

    /// <summary>The active tab gets a thin accent line on top that fades into a soft glow.</summary>
    private void SetupLook()
    {
        for (int i = 0; i < 5; i++) _tabGlow.GradientStops.Add(new GradientStop());
        Tabs.Resources["TabViewItemHeaderBackgroundSelected"] = _tabGlow;
        Root.SizeChanged += (_, _) =>
        {
            if (SettingsOpen) SettingsPanel.Width = Math.Max(320, Math.Min(760, Root.ActualWidth - 48));
        };
    }

    private void UpdateTabGlow(Color accent, bool dark)
    {
        if (_tabGlow.GradientStops.Count < 5) return;
        Color sheet = dark ? Color.FromArgb(0x16, 255, 255, 255) : Color.FromArgb(0xA6, 255, 255, 255);
        Color glow = Color.FromArgb(dark ? (byte)0x38 : (byte)0x30, accent.R, accent.G, accent.B);
        Set(0, 0, accent);
        Set(1, 0.055, accent);
        Set(2, 0.07, glow);
        Set(3, 0.5, sheet);
        Set(4, 1, sheet);

        void Set(int i, double offset, Color color)
        {
            _tabGlow.GradientStops[i].Offset = offset;
            _tabGlow.GradientStops[i].Color = color;
        }
    }
}
