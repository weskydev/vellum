using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Windows.UI;

namespace Vellum.Models;

/// <summary>One line in the version preview: unchanged, only in the old version (red) or only in the current text (green).</summary>
public sealed class DiffRow
{
    private static readonly SolidColorBrush None = new(Color.FromArgb(0, 0, 0, 0));
    private static readonly SolidColorBrush RemovedBack = new(Color.FromArgb(0x30, 0xE5, 0x48, 0x4D));
    private static readonly SolidColorBrush AddedBack = new(Color.FromArgb(0x2C, 0x46, 0xB8, 0x6A));

    public DiffRow(DiffKind kind, string text)
    {
        Kind = kind;
        Text = text.Length == 0 ? " " : text.Replace('\t', ' ');
    }

    public DiffKind Kind { get; }
    public string Text { get; }

    public string Mark => Kind switch
    {
        DiffKind.Removed => "−",
        DiffKind.Added => "+",
        _ => "",
    };

    public Brush Background => Kind switch
    {
        DiffKind.Removed => RemovedBack,
        DiffKind.Added => AddedBack,
        _ => None,
    };

    public Windows.UI.Text.TextDecorations Decorations =>
        Kind == DiffKind.Removed ? Windows.UI.Text.TextDecorations.Strikethrough : Windows.UI.Text.TextDecorations.None;
}

/// <summary>One saved version in the history list.</summary>
public sealed class VersionItem
{
    public required HistoryVersion Version { get; init; }
    public required string When { get; init; }
    public required string Detail { get; init; }
}
