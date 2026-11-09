using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Vellum.Models;
using Windows.Storage;

namespace Vellum.Services;

public sealed class SessionState
{
    public List<TabState> Tabs { get; set; } = [];
    public int SelectedIndex { get; set; }
    public WindowBounds? Window { get; set; }
    /// <summary>The small window on top of others was on.</summary>
    public bool Mini { get; set; }
    /// <summary>Where the small window was last time.</summary>
    public WindowBounds? MiniBounds { get; set; }
}

public sealed class TabState
{
    public Guid Id { get; set; }
    public string? FilePath { get; set; }
    public bool IsDirty { get; set; }
    /// <summary>A note from the library: its text is read from there.</summary>
    public bool IsNote { get; set; }
    public int CodePage { get; set; } = 65001;
    public bool Bom { get; set; }
    public LineEnding LineEnding { get; set; }
    public int SelectionStart { get; set; }
    /// <summary>Highlighting picked by hand for this tab; null = by file extension.</summary>
    public string? Syntax { get; set; }
}

public sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Maximized { get; set; }
}

[JsonSerializable(typeof(SessionState))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
internal sealed partial class SessionJsonContext : JsonSerializerContext;

/// <summary>
/// Remembers open tabs, unsaved text and the window position between launches,
/// so nothing is lost when Vellum is closed.
/// Stored in the app's private local folder.
/// </summary>
public sealed class SessionService
{
    private readonly string _dir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "session");
    private readonly Dictionary<Guid, int> _writtenVersions = [];

    private string StatePath => Path.Combine(_dir, "session.json");

    private string TextPath(Guid id) => Path.Combine(_dir, id.ToString("N") + ".txt");

    public SessionState? Load()
    {
        // If the file was damaged (e.g. a power cut while saving), fall back to the previous copy
        return Read(StatePath) ?? Read(StatePath + ".bak");
    }

    private static SessionState? Read(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), SessionJsonContext.Default.SessionState)
                : null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reading session " + Path.GetFileName(path), ex);
            return null; // a broken session file should never stop the app from starting
        }
    }

    public string? LoadText(Guid id)
    {
        try
        {
            string path = TextPath(id);
            return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Saves the session. <paramref name="unsaved"/> holds the text of every tab with unsaved changes;
    /// a tab's text is only rewritten when its version changed since the last save.
    /// </summary>
    public void Save(SessionState state, IReadOnlyList<(Guid Id, int Version, Func<string> Text)> unsaved)
    {
        try
        {
            Directory.CreateDirectory(_dir);

            var keep = new HashSet<Guid>();
            foreach (var (id, version, text) in unsaved)
            {
                keep.Add(id);
                if (_writtenVersions.TryGetValue(id, out int written) && written == version && File.Exists(TextPath(id)))
                {
                    continue;
                }
                TextFileService.WriteAtomically(TextPath(id), Encoding.UTF8.GetBytes(text()));
                _writtenVersions[id] = version;
            }

            // Drop cached text of tabs that were closed or saved
            foreach (string file in Directory.EnumerateFiles(_dir, "*.txt"))
            {
                if (Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out Guid id) && !keep.Contains(id))
                {
                    File.Delete(file);
                    _writtenVersions.Remove(id);
                }
            }

            string json = JsonSerializer.Serialize(state, SessionJsonContext.Default.SessionState);
            TextFileService.WriteAtomically(StatePath, Encoding.UTF8.GetBytes(json), backup: true);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Session save failed: {ex}");
        }
    }
}
