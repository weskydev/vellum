using System.Text;
using Vellum.Services;

namespace Vellum.Models;

public enum LineEnding
{
    CRLF,
    LF,
    CR,
}

/// <summary>What we know about the file behind one tab.</summary>
public sealed class TextDocument
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>A note in the library (not a file): saved by itself, named after its first line.</summary>
    public bool IsNote { get; set; }

    /// <summary>The key of a note under a password, in memory only while the tab is open; null = not locked.</summary>
    public NoteKey? Lock { get; set; }

    /// <summary>Full path on disk, or null for a new, never-saved note.</summary>
    public string? FilePath { get; set; }

    // New notes use the encoding and line breaks chosen in Settings; opened files keep their own
    public Encoding Encoding { get; set; } = DefaultEncoding();

    public LineEnding LineEnding { get; set; } = SettingsService.Current.NewFileLineEnding;

    private static Encoding DefaultEncoding()
    {
        try
        {
            return TextFileService.FromCodePage(SettingsService.Current.NewFileCodePage, SettingsService.Current.NewFileBom);
        }
        catch (Exception)
        {
            return new UTF8Encoding(false);
        }
    }

    public bool IsDirty { get; set; }

    /// <summary>When the file on disk was last written, as Vellum last saw it (to notice changes by other programs).</summary>
    public DateTime? DiskTime { get; set; }

    public void StampDisk()
    {
        try
        {
            DiskTime = FilePath is not null && File.Exists(FilePath) ? File.GetLastWriteTimeUtc(FilePath) : null;
        }
        catch (Exception)
        {
            DiskTime = null;
        }
    }

    /// <summary>Highlighting picked by hand (a <see cref="Syntax.SyntaxLanguage"/> id); null = by file extension.</summary>
    public string? SyntaxOverride { get; set; }

    /// <summary>Title of a note, set by the editor as the first line changes.</summary>
    public string NoteTitle { get; set; } = "";

    public string FileName => FilePath is not null
        ? Path.GetFileName(FilePath)
        : IsNote ? (NoteTitle.Length > 0 ? NoteTitle : Strings.Get("NoteUntitled")) : Strings.Get("UntitledTab");

    /// <summary>Key of this document in the version history; null for an unsaved, untitled tab.</summary>
    public string? HistoryKey => IsNote ? HistoryService.NoteKey(Id) : FilePath is not null ? HistoryService.FileKey(FilePath) : null;
}
