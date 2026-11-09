using Microsoft.UI.Xaml;

namespace Vellum.Models;

/// <summary>One row in the notes library list.</summary>
public sealed class NoteListItem
{
    public required Guid Id { get; init; }
    public required string Title { get; init; }
    public required string Snippet { get; init; }
    /// <summary>"Today, 14:05 · 120 words".</summary>
    public required string Meta { get; init; }
    /// <summary>"#work #ideas".</summary>
    public required string Tags { get; init; }
    public required bool Pinned { get; init; }
    /// <summary>The note is open in a tab right now.</summary>
    public required bool IsOpen { get; init; }
    /// <summary>The note is under a password.</summary>
    public bool Locked { get; init; }

    public Visibility PinVisibility => Pinned ? Visibility.Visible : Visibility.Collapsed;
    public Visibility LockVisibility => Locked ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OpenVisibility => IsOpen ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SnippetVisibility => Snippet.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TagsVisibility => Tags.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
}
