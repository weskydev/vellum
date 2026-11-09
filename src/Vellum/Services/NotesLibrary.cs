using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Windows.Storage;

namespace Vellum.Services;

/// <summary>What the library knows about one note. The text itself lives in its own file.</summary>
public sealed class NoteInfo
{
    public Guid Id { get; set; }
    public string Title { get; set; } = "";
    /// <summary>The first words after the title, for the list.</summary>
    public string Snippet { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public DateTime Created { get; set; }
    public DateTime Modified { get; set; }
    public bool Pinned { get; set; }
    /// <summary>When the note was moved to the trash; null = not in the trash.</summary>
    public DateTime? Deleted { get; set; }
    public int Words { get; set; }
    /// <summary>Under a password: the file holds only encrypted text; the list shows the title, nothing else.</summary>
    public bool Locked { get; set; }
}

public sealed class NotesIndex
{
    public List<NoteInfo> Notes { get; set; } = [];
}

[JsonSerializable(typeof(NotesIndex))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class NotesJsonContext : JsonSerializerContext;

/// <summary>
/// The notes library: notes written in Vellum that are not files on disk.
/// Every note is a UTF-8 text file in the app's local folder, plus one index with titles, tags and pins.
/// Notes deleted from the library wait 30 days in the trash.
/// </summary>
public static partial class NotesLibrary
{
    private const int TrashDays = 30;

    private static readonly string Dir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "notes");
    private static readonly Dictionary<Guid, string> Texts = [];
    // The open text of unlocked notes, only to skip writing the same text twice
    private static readonly Dictionary<Guid, string> LockedPlain = [];
    private static NotesIndex _index = new();
    private static bool _loaded;

    private static string IndexPath => Path.Combine(Dir, "index.json");

    private static string TextPath(Guid id) => Path.Combine(Dir, id.ToString("N") + ".md");

    /// <summary>Something in the library changed (a note was added, edited, pinned, deleted...).</summary>
    public static event EventHandler? Changed;

    public static IReadOnlyList<NoteInfo> All
    {
        get
        {
            EnsureLoaded();
            return _index.Notes;
        }
    }

    public static IEnumerable<NoteInfo> Active => All.Where(n => n.Deleted is null);

    public static IEnumerable<NoteInfo> Trash => All.Where(n => n.Deleted is not null);

    public static NoteInfo? Find(Guid id) => All.FirstOrDefault(n => n.Id == id);

    public static bool Contains(Guid id) => Find(id) is { Deleted: null };

    private static void EnsureLoaded()
    {
        if (_loaded) return;
        _loaded = true;
        _index = Read(IndexPath) ?? Read(IndexPath + ".bak") ?? new NotesIndex();

        // Old notes in the trash are removed for good
        DateTime limit = DateTime.UtcNow.AddDays(-TrashDays);
        foreach (NoteInfo old in _index.Notes.Where(n => n.Deleted < limit).ToList())
        {
            RemoveFiles(old.Id);
            _index.Notes.Remove(old);
        }
    }

    private static NotesIndex? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), NotesJsonContext.Default.NotesIndex) : null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reading notes index " + Path.GetFileName(path), ex);
            return null;
        }
    }

    private static void WriteIndex()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            string json = JsonSerializer.Serialize(_index, NotesJsonContext.Default.NotesIndex);
            TextFileService.WriteAtomically(IndexPath, Encoding.UTF8.GetBytes(json), backup: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Saving notes index", ex);
        }
    }

    /// <summary>The text of a note, or null if it is not in the library.</summary>
    public static string? LoadText(Guid id)
    {
        EnsureLoaded();
        if (Texts.TryGetValue(id, out string? cached)) return cached;
        try
        {
            string path = TextPath(id);
            if (!File.Exists(path)) return null;
            string text = File.ReadAllText(path, Encoding.UTF8);
            Texts[id] = text;
            return text;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reading note", ex);
            return null;
        }
    }

    /// <summary>Writes a note, adding it to the library if it is new. Empty new notes are not kept.</summary>
    public static void Save(Guid id, string text) => Write(id, text, null);

    /// <summary>Writes a note under a password: only the encrypted text reaches the disk.</summary>
    public static void SaveLocked(Guid id, string text, NoteKey key) => Write(id, text, key);

    public static bool IsLocked(Guid id) => Find(id)?.Locked == true;

    private static void Write(Guid id, string text, NoteKey? key)
    {
        EnsureLoaded();
        NoteInfo? note = Find(id);
        if (note is null)
        {
            if (text.Trim().Length == 0) return;
            note = new NoteInfo { Id = id, Created = DateTime.UtcNow };
            _index.Notes.Add(note);
        }
        else if (key is null && !note.Locked && Texts.TryGetValue(id, out string? old) && old == text && note.Deleted is null)
        {
            return;
        }
        else if (key is not null && note.Locked && LockedPlain.TryGetValue(id, out string? oldPlain) && oldPlain == text && note.Deleted is null)
        {
            return;
        }

        string onDisk = key is null ? text : NoteLock.Encrypt(text, key);
        try
        {
            Directory.CreateDirectory(Dir);
            TextFileService.WriteAtomically(TextPath(id), Encoding.UTF8.GetBytes(onDisk));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Saving note", ex);
            return;
        }

        Texts[id] = onDisk;
        if (key is null) LockedPlain.Remove(id);
        else LockedPlain[id] = text;
        note.Locked = key is not null;
        Describe(note, text);
        note.Modified = DateTime.UtcNow;
        note.Deleted = null; // typing into a note from the trash brings it back
        WriteIndex();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void SetPinned(Guid id, bool pinned)
    {
        if (Find(id) is not { } note || note.Pinned == pinned) return;
        note.Pinned = pinned;
        WriteIndex();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void MoveToTrash(Guid id)
    {
        if (Find(id) is not { Deleted: null } note) return;
        note.Deleted = DateTime.UtcNow;
        note.Pinned = false;
        WriteIndex();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void Restore(Guid id)
    {
        if (Find(id) is not { Deleted: not null } note) return;
        note.Deleted = null;
        WriteIndex();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>Removes a note for good, with its history.</summary>
    public static void DeleteForever(Guid id)
    {
        if (Find(id) is not { } note) return;
        _index.Notes.Remove(note);
        RemoveFiles(id);
        WriteIndex();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void EmptyTrash()
    {
        foreach (NoteInfo note in Trash.ToList())
        {
            _index.Notes.Remove(note);
            RemoveFiles(note.Id);
        }
        WriteIndex();
        Changed?.Invoke(null, EventArgs.Empty);
    }

    private static void RemoveFiles(Guid id)
    {
        Texts.Remove(id);
        LockedPlain.Remove(id);
        try
        {
            File.Delete(TextPath(id));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Deleting note", ex);
        }
        HistoryService.Forget(HistoryService.NoteKey(id));
    }

    // ---------- Search ----------

    /// <summary>Notes matching the words typed in the search box and the chosen tag; pinned first, then newest.</summary>
    public static List<NoteInfo> Search(string query, string? tag, bool trash)
    {
        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (trash ? Trash : Active)
            .Where(n => tag is null || n.Tags.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Where(n => words.Length == 0 || words.All(w =>
                n.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase) ||
                (!n.Locked && LoadText(n.Id)?.Contains(w, StringComparison.CurrentCultureIgnoreCase) == true)))
            .OrderByDescending(n => n.Pinned)
            .ThenByDescending(n => trash ? n.Deleted : n.Modified)
            .ToList();
    }

    /// <summary>All tags of notes outside the trash, with how many notes use each, most used first.</summary>
    public static List<(string Tag, int Count)> AllTags() =>
        Active.SelectMany(n => n.Tags)
            .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
            .Select(g => (g.First(), g.Count()))
            .OrderByDescending(x => x.Item2)
            .ThenBy(x => x.Item1, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    // ---------- Title, snippet, tags ----------

    /// <summary>Title of a note: its first line without Markdown marks, like "Shopping list".</summary>
    public static string TitleOf(string text)
    {
        // Reads line by line: this runs on every key press, the text can be long
        int pos = 0;
        while (pos < text.Length)
        {
            int end = text.IndexOfAny(['\r', '\n'], pos);
            if (end < 0) end = text.Length;
            string line = CleanLine(text[pos..Math.Min(end, pos + 400)]);
            if (line.Length > 0) return line.Length > 80 ? line[..80].TrimEnd() + "…" : line;
            pos = end + 1;
        }
        return "";
    }

    private static string CleanLine(string line)
    {
        string s = line.Trim();
        s = s.TrimStart('#', '>', '-', '*', '+', ' ', '\t');
        if (s.StartsWith("[ ] ") || s.StartsWith("[x] ", StringComparison.OrdinalIgnoreCase)) s = s[4..];
        s = s.Replace("**", "").Replace("__", "").Replace("`", "");
        return s.Trim();
    }

    private static void Describe(NoteInfo note, string text)
    {
        note.Title = TitleOf(text);
        note.Words = CountWords(text);
        if (note.Locked)
        {
            // Tags and the first lines would give the text away
            note.Tags = [];
            note.Snippet = "";
            return;
        }
        note.Tags = TagsOf(text);

        // The snippet: the next lines after the title, on one line
        var sb = new StringBuilder();
        bool titleSeen = false;
        foreach (string raw in text.Split('\r', '\n'))
        {
            string line = CleanLine(raw);
            if (line.Length == 0) continue;
            if (!titleSeen)
            {
                titleSeen = true;
                continue;
            }
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(line);
            if (sb.Length > 160) break;
        }
        note.Snippet = sb.Length > 160 ? sb.ToString(0, 160).TrimEnd() + "…" : sb.ToString();
    }

    private static int CountWords(string text)
    {
        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            bool letter = char.IsLetterOrDigit(c);
            if (letter && !inWord) words++;
            inWord = letter;
        }
        return words;
    }

    /// <summary>
    /// Tags are written right in the text: #work, #ideas/2026. A "#" followed by a space is a Markdown heading,
    /// and #123 is a number, not a tag.
    /// </summary>
    public static List<string> TagsOf(string text) =>
        TagRegex().Matches(text)
            .Select(m => m.Groups[1].Value.TrimEnd('-', '/', '_'))
            .Where(t => t.Length > 0 && !t.All(char.IsDigit))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    [GeneratedRegex(@"(?<![\p{L}\p{N}_#&/\\])#([\p{L}\p{N}][\p{L}\p{N}_\-/]*)")]
    public static partial Regex TagRegex();
}
