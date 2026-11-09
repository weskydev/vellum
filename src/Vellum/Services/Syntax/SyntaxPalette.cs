using Windows.UI;

namespace Vellum.Services.Syntax;

/// <summary>Colors for highlighted text, tuned for the glass sheet in each theme. Keywords use the accent.</summary>
public sealed class SyntaxPalette
{
    private SyntaxPalette(bool dark, Color accent)
    {
        IsDark = dark;
        Text = dark ? Rgb(0xEE, 0xED, 0xF3) : Rgb(0x26, 0x24, 0x31);
        Background = dark ? Rgb(0x23, 0x20, 0x2E) : Rgb(0xF4, 0xF3, 0xF8);
        Accent = dark ? AccentPalette.Mix(accent, Rgb(255, 255, 255), 0.25) : AccentPalette.Mix(accent, Rgb(0, 0, 0), 0.2);
        Dim = AccentPalette.Mix(Text, Background, dark ? 0.55 : 0.5);
        Faded = AccentPalette.Mix(Text, Background, dark ? 0.62 : 0.6);
        Quote = AccentPalette.Mix(Text, Background, 0.3);
        Comment = dark ? Rgb(0x86, 0x82, 0x9A) : Rgb(0x8A, 0x87, 0x99);
        String = dark ? Rgb(0x9F, 0xE0, 0xB0) : Rgb(0x1F, 0x8A, 0x4C);
        Number = dark ? Rgb(0xF7, 0xB9, 0x77) : Rgb(0xB4, 0x53, 0x09);
        Type = dark ? Rgb(0x7D, 0xD3, 0xFC) : Rgb(0x0E, 0x74, 0x90);
        Function = dark ? Rgb(0xF0, 0xD5, 0x8A) : Rgb(0x8A, 0x5A, 0x00);
        Tag = dark ? Rgb(0xF4, 0x9A, 0xC1) : Rgb(0xBE, 0x18, 0x5D);
        Attribute = dark ? Rgb(0xE6, 0xC0, 0x7B) : Rgb(0x9A, 0x6A, 0x00);
        Key = dark ? Rgb(0xA5, 0xB4, 0xFC) : Rgb(0x3B, 0x4F, 0xC4);
        Meta = dark ? Rgb(0xC4, 0xB5, 0xFD) : Rgb(0x6D, 0x28, 0xD9);
        Error = dark ? Rgb(0xFF, 0x8A, 0x8A) : Rgb(0xC6, 0x28, 0x28);
    }

    public bool IsDark { get; }
    public Color Text { get; }
    public Color Background { get; }
    public Color Accent { get; }
    public Color Dim { get; }
    /// <summary>Text outside the current paragraph in focus mode.</summary>
    public Color Faded { get; }
    public Color Quote { get; }
    public Color Comment { get; }
    public Color String { get; }
    public Color Number { get; }
    public Color Type { get; }
    public Color Function { get; }
    public Color Tag { get; }
    public Color Attribute { get; }
    public Color Key { get; }
    public Color Meta { get; }
    public Color Error { get; }

    public static SyntaxPalette For(bool dark) =>
        new(dark, AccentPalette.Resolve(SettingsService.Current.Accent));

    /// <summary>Color for a code token, or null when the kind only changes the font.</summary>
    public Color? ColorOf(TokenKind kind) => kind switch
    {
        TokenKind.Comment => Comment,
        TokenKind.String => String,
        TokenKind.Number => Number,
        TokenKind.Keyword => Accent,
        TokenKind.Type => Type,
        TokenKind.Function => Function,
        TokenKind.Tag => Tag,
        TokenKind.Attribute => Attribute,
        TokenKind.Key => Key,
        TokenKind.Meta => Meta,
        TokenKind.Error => Error,
        TokenKind.Warning => Number,
        TokenKind.Marker => Dim,
        TokenKind.Link => Accent,
        TokenKind.Quote => Quote,
        TokenKind.ListMarker => Accent,
        TokenKind.CodeInline => Key,
        TokenKind.Strike => Dim,
        _ => null,
    };

    /// <summary>CSS color for the Markdown preview.</summary>
    public static string Css(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    private static Color Rgb(byte r, byte g, byte b) => Color.FromArgb(255, r, g, b);
}
