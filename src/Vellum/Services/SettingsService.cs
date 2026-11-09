using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Windows.Storage;

namespace Vellum.Services;

public enum AppTheme
{
    System,
    Light,
    Dark,
}

public enum BackdropKind
{
    Acrylic,
    Mica,
    Solid,
}

/// <summary>Everything the user can change in Settings. New options get a sensible default here.</summary>
public sealed class AppSettings
{
    // Appearance
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Accent { get; set; } = AccentPalette.DefaultKey;
    public BackdropKind Backdrop { get; set; } = BackdropKind.Acrylic;
    /// <summary>0 = almost solid, 100 = most see-through.</summary>
    public int GlassTransparency { get; set; } = 60;

    // Language: "" follows Windows, otherwise a tag with its own Strings folder, e.g. "uk-UA"
    public string Language { get; set; } = "";

    // Editor
    public string FontFamily { get; set; } = "Segoe UI Variable Text";
    public double FontSize { get; set; } = 15;
    public bool WordWrap { get; set; } = true;
    public bool ShowStatusBar { get; set; } = true;
    public bool SpellCheck { get; set; }
    /// <summary>Space between lines, as a multiple of the font size.</summary>
    public double LineSpacing { get; set; } = 1.3;
    /// <summary>Keep lines to a comfortable reading width in wide windows.</summary>
    public bool ReadableWidth { get; set; }

    // Writing and code
    public bool MarkdownStyling { get; set; } = true;
    public bool MarkdownPreview { get; set; } = true;
    public bool SyntaxHighlighting { get; set; } = true;
    public bool CodeMonospace { get; set; } = true;
    public bool FocusMode { get; set; }
    public bool TypewriterMode { get; set; }
    /// <summary>Word goal shown in the status line; 0 = off.</summary>
    public int WordGoal { get; set; }

    // Code and editing tools
    public bool LineNumbers { get; set; }
    public bool HighlightCurrentLine { get; set; } = true;
    /// <summary>0 = a real tab character, otherwise that many spaces.</summary>
    public int TabSize { get; set; }
    /// <summary>Enter keeps the indent; Markdown lists continue.</summary>
    public bool AutoIndent { get; set; } = true;

    /// <summary>Typing ( [ { or " in code adds the closing one.</summary>
    public bool AutoCloseBrackets { get; set; } = true;

    /// <summary>Selecting a word highlights it everywhere on screen; the bracket pair at the caret is marked.</summary>
    public bool SmartHighlight { get; set; } = true;

    /// <summary>Notice when an open file is changed by another program.</summary>
    public bool WatchFiles { get; set; } = true;
    public SearchEngine SearchEngine { get; set; } = SearchEngine.Google;

    // Notes library and history
    /// <summary>New tabs are notes in the library: saved by themselves, kept after the tab is closed.</summary>
    public bool NotesLibrary { get; set; } = true;
    /// <summary>Keep older versions of notes, to look at or bring back.</summary>
    public bool VersionHistory { get; set; } = true;
    /// <summary>Also keep versions of files saved in Vellum.</summary>
    public bool FileHistory { get; set; } = true;
    /// <summary>Notes can be put under a password.</summary>
    public bool NoteLocks { get; set; } = true;
    /// <summary>Export to PDF and HTML in the menus.</summary>
    public bool Export { get; set; } = true;

    // Tray, quick note and the small window
    /// <summary>Vellum's icon next to the clock.</summary>
    public bool TrayIcon { get; set; }
    /// <summary>With the icon on, closing the window keeps Vellum running there.</summary>
    public bool CloseToTray { get; set; } = true;
    /// <summary>Vellum starts with Windows (quietly, in the tray when the icon is on).</summary>
    public bool StartWithWindows { get; set; }
    /// <summary>Ctrl+Alt+N from any app opens a new note.</summary>
    public bool QuickNote { get; set; } = true;
    /// <summary>The small window on top of others (Ctrl+Shift+M).</summary>
    public bool MiniWindow { get; set; } = true;

    // Files and session
    public bool RestoreSession { get; set; } = true;
    public bool AutoSaveFiles { get; set; }
    public bool ExitWithLastTab { get; set; }
    /// <summary>Encoding of new files: code page, with or without a byte order mark.</summary>
    public int NewFileCodePage { get; set; } = 65001;
    public bool NewFileBom { get; set; }
    public Models.LineEnding NewFileLineEnding { get; set; } = Models.LineEnding.CRLF;
    public List<string> RecentFiles { get; set; } = [];
}

public enum SearchEngine
{
    Google,
    Bing,
    DuckDuckGo,
    Yandex,
}

[JsonSerializable(typeof(AppSettings))]
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>Loads and saves settings.json in the app's local folder and tells the UI when something changed.</summary>
public static class SettingsService
{
    private static string FilePath => Path.Combine(ApplicationData.Current.LocalFolder.Path, "settings.json");

    public static AppSettings Current { get; private set; } = new();

    public static event EventHandler? Changed;

    public static void Load()
    {
        // The previous copy is the fallback if the file got damaged (e.g. by a power cut)
        Current = Read(FilePath) ?? Read(FilePath + ".bak") ?? new AppSettings();
    }

    private static AppSettings? Read(string path)
    {
        try
        {
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), SettingsJsonContext.Default.AppSettings) : null;
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reading settings " + Path.GetFileName(path), ex);
            return null;
        }
    }

    /// <summary>Changes settings, saves them and notifies the UI.</summary>
    public static void Update(Action<AppSettings> change)
    {
        change(Current);
        try
        {
            string json = JsonSerializer.Serialize(Current, SettingsJsonContext.Default.AppSettings);
            TextFileService.WriteAtomically(FilePath, Encoding.UTF8.GetBytes(json), backup: true);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Saving settings", ex);
        }
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
