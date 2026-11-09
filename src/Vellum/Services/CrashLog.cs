using Windows.Storage;

namespace Vellum.Services;

/// <summary>
/// Writes unexpected errors to crash.log in the app's private local folder (never sent anywhere),
/// so problems can be investigated later.
/// </summary>
internal static class CrashLog
{
    public static void Write(string source, Exception? ex)
    {
        try
        {
            string path = Path.Combine(ApplicationData.Current.LocalFolder.Path, "crash.log");
            File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {source}{Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception)
        {
            // logging must never crash the app
        }
    }
}
