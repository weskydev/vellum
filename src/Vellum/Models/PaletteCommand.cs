namespace Vellum.Models;

/// <summary>One entry in the command palette.</summary>
public sealed class PaletteCommand(string title, string glyph, string shortcut, Action execute)
{
    public string Title { get; } = title;

    public string Glyph { get; } = glyph;

    public string Shortcut { get; } = shortcut;

    public Action Execute { get; } = execute;
}
