using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Windows.Storage;

namespace Vellum.Services;

/// <summary>One saved version of a note or file.</summary>
public sealed record HistoryVersion(string Path, DateTime Time, long Size)
{
    public string ReadText()
    {
        try
        {
            return File.ReadAllText(Path, Encoding.UTF8);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reading version", ex);
            return "";
        }
    }
}

/// <summary>
/// Version history: copies of a note or file taken while you work, so an older text can be brought back.
/// Each note or file has its own folder in the app's local folder; the newest 50 versions are kept.
/// </summary>
public static class HistoryService
{
    private const int MaxVersions = 50;
    private const string TimeFormat = "yyyyMMdd-HHmmss";

    /// <summary>While typing, a new version is taken at most this often; saving or closing always takes one.</summary>
    public static readonly TimeSpan AutoInterval = TimeSpan.FromMinutes(5);

    private static readonly string Dir = Path.Combine(ApplicationData.Current.LocalFolder.Path, "history");

    public static string NoteKey(Guid id) => "n-" + id.ToString("N");

    /// <summary>Files are known by their full path (case does not matter on Windows).</summary>
    public static string FileKey(string path)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(path.ToLowerInvariant()));
        return "f-" + Convert.ToHexString(hash, 0, 12).ToLowerInvariant();
    }

    public static bool IsOn => SettingsService.Current.VersionHistory;

    /// <summary>Versions of one note or file, newest first.</summary>
    public static List<HistoryVersion> List(string key)
    {
        string folder = Path.Combine(Dir, key);
        if (!Directory.Exists(folder)) return [];
        var list = new List<HistoryVersion>();
        foreach (FileInfo file in new DirectoryInfo(folder).EnumerateFiles("*.txt"))
        {
            if (DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file.Name), TimeFormat, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTime time))
            {
                list.Add(new HistoryVersion(file.FullName, time, file.Length));
            }
        }
        return list.OrderByDescending(v => v.Time).ToList();
    }

    public static DateTime? Latest(string key) => List(key).FirstOrDefault()?.Time;

    /// <summary>
    /// Keeps a copy of the text. Nothing is written if it is the same as the newest version.
    /// With <paramref name="auto"/>, a copy is taken only if the newest one is older than <see cref="AutoInterval"/>.
    /// </summary>
    public static void Snapshot(string key, string text, bool auto = false, string? label = null)
    {
        if (!IsOn || text.Trim().Length == 0) return;
        try
        {
            List<HistoryVersion> versions = List(key);
            HistoryVersion? newest = versions.FirstOrDefault();
            if (auto && newest is not null && DateTime.UtcNow - newest.Time < AutoInterval) return;
            if (newest is not null && newest.Size == Encoding.UTF8.GetByteCount(text) && newest.ReadText() == text) return;

            string folder = Path.Combine(Dir, key);
            Directory.CreateDirectory(folder);
            if (label is not null) File.WriteAllText(Path.Combine(folder, "name.txt"), label, Encoding.UTF8);

            DateTime now = DateTime.UtcNow;
            if (newest is not null && now <= newest.Time) now = newest.Time.AddSeconds(1); // one file per second
            string path = Path.Combine(folder, now.ToString(TimeFormat, CultureInfo.InvariantCulture) + ".txt");
            TextFileService.WriteAtomically(path, Encoding.UTF8.GetBytes(text));

            foreach (HistoryVersion old in List(key).Skip(MaxVersions))
            {
                File.Delete(old.Path);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Saving version", ex);
        }
    }

    /// <summary>Removes all versions of a note or file.</summary>
    public static void Forget(string key)
    {
        try
        {
            string folder = Path.Combine(Dir, key);
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Deleting history", ex);
        }
    }

    /// <summary>Removes the whole history (button in Settings).</summary>
    public static void ForgetAll()
    {
        try
        {
            if (Directory.Exists(Dir)) Directory.Delete(Dir, recursive: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Clearing history", ex);
        }
    }

    /// <summary>Disk space used by the history, in bytes.</summary>
    public static long Size()
    {
        try
        {
            return Directory.Exists(Dir)
                ? new DirectoryInfo(Dir).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
                : 0;
        }
        catch (Exception)
        {
            return 0;
        }
    }
}
