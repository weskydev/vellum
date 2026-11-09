using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using Windows.UI.ViewManagement;

namespace Vellum.Services;

/// <summary>The ready-made accent colors and how an accent is applied to the whole app.</summary>
public static class AccentPalette
{
    public const string DefaultKey = "sand";
    public const string SystemKey = "system";

    public static readonly IReadOnlyList<(string Key, Color Color)> Presets =
    [
        // Sand (parchment, the Vellum color) first, then around the color wheel, neutrals last
        ("sand", Rgb(0xC8A27C)),
        ("violet", Rgb(0x7C6CFF)),
        ("lavender", Rgb(0xA78BFA)),
        ("indigo", Rgb(0x6366F1)),
        ("ocean", Rgb(0x3B9EFF)),
        ("sky", Rgb(0x38BDF8)),
        ("teal", Rgb(0x14B8A6)),
        ("mint", Rgb(0x4FD1C5)),
        ("sage", Rgb(0x84A98C)),
        ("lime", Rgb(0x84CC16)),
        ("amber", Rgb(0xF5A524)),
        ("coral", Rgb(0xFF7A59)),
        ("crimson", Rgb(0xE5484D)),
        ("rose", Rgb(0xF472B6)),
        ("berry", Rgb(0xD946EF)),
        ("slate", Rgb(0x8B9BB4)),
        ("graphite", Rgb(0xA1A1AA)),
    ];

    private static Color Rgb(int rgb) => Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    public static Color Resolve(string key)
    {
        if (key == SystemKey)
        {
            return new UISettings().GetColorValue(UIColorType.Accent);
        }
        foreach (var (k, color) in Presets)
        {
            if (k == key) return color;
        }
        return Presets[0].Color;
    }

    /// <summary>
    /// Replaces the accent color resources used by every control.
    /// Controls pick the new shades up the next time the theme is re-applied.
    /// </summary>
    public static void Apply(Color accent)
    {
        var res = Application.Current.Resources;
        res["SystemAccentColor"] = accent;
        res["SystemAccentColorLight1"] = Mix(accent, Colors.White, 0.22);
        res["SystemAccentColorLight2"] = Mix(accent, Colors.White, 0.42);
        res["SystemAccentColorLight3"] = Mix(accent, Colors.White, 0.62);
        res["SystemAccentColorDark1"] = Mix(accent, Colors.Black, 0.14);
        res["SystemAccentColorDark2"] = Mix(accent, Colors.Black, 0.30);
        res["SystemAccentColorDark3"] = Mix(accent, Colors.Black, 0.46);

        if (res["VellumAccentBrush"] is SolidColorBrush brush)
        {
            brush.Color = accent;
        }
    }

    public static Color Mix(Color a, Color b, double amount) => Color.FromArgb(
        255,
        (byte)(a.R + (b.R - a.R) * amount),
        (byte)(a.G + (b.G - a.G) * amount),
        (byte)(a.B + (b.B - a.B) * amount));

    private static class Colors
    {
        public static readonly Color White = Color.FromArgb(255, 255, 255, 255);
        public static readonly Color Black = Color.FromArgb(255, 0, 0, 0);
    }
}
