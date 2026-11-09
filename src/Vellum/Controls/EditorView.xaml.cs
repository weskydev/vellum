using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vellum.Models;
using Vellum.Services;
using Vellum.Services.Syntax;

namespace Vellum.Controls;

/// <summary>Text editor for one tab, with line/column, counts and file format in the status line.</summary>
public sealed partial class EditorView : UserControl
{
    // Text as it is on disk; null when there is nothing to compare with (e.g. the file was deleted)
    private string? _baseline;

    public EditorView(TextDocument document, string text = "", string? baseline = "")
    {
        Document = document;
        Document.StampDisk();
        _baseline = baseline;
        InitializeComponent();
        Motion.FadeSlide(FindPanel, -8);
        Motion.Fade(PreviewHost);
        SetupEngine(text);
        SetupStyling();
        RefreshDirty();
        UpdateStatus();
        UpdateFileInfo();
        ApplySettings();

        // Only listen while on screen: hidden tabs catch up when they are shown again
        Loaded += (_, _) =>
        {
            ApplySettings();
            SettingsService.Changed += OnSettingsChanged;
            ZoomService.Changed += OnSettingsChanged;
            WritingModes.Changed += OnSettingsChanged;
        };
        Unloaded += (_, _) =>
        {
            SettingsService.Changed -= OnSettingsChanged;
            ZoomService.Changed -= OnSettingsChanged;
            WritingModes.Changed -= OnSettingsChanged;
        };
        ActualThemeChanged += (_, _) => ApplySettings();

        // Ctrl + mouse wheel zooms the text (the text box would otherwise swallow the wheel)
        Editor.AddHandler(PointerWheelChangedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler(Editor_PointerWheelChanged), true);
    }

    private void Editor_PointerWheelChanged(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if (!e.KeyModifiers.HasFlag(Windows.System.VirtualKeyModifiers.Control)) return;
        int delta = e.GetCurrentPoint(Editor).Properties.MouseWheelDelta;
        if (delta > 0) ZoomService.ZoomIn();
        else if (delta < 0) ZoomService.ZoomOut();
        e.Handled = true;
    }

    private void OnSettingsChanged(object? sender, EventArgs e) => ApplySettings();

    private void ApplySettings()
    {
        AppSettings s = SettingsService.Current;
        Editor.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(EditorFont);
        Editor.FontSize = EditorFontSize;
        ZoomText.Text = Math.Abs(ZoomService.Factor - 1) < 0.001 ? "" : $"{ZoomService.Factor * 100:0}%";
        ZoomText.Visibility = ZoomText.Text.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
        Editor.TextWrapping = s.WordWrap ? TextWrapping.Wrap : TextWrapping.NoWrap;
        Editor.IsSpellCheckEnabled = s.SpellCheck;
        StatusBar.Visibility = s.ShowStatusBar && !WritingModes.Zen ? Visibility.Visible : Visibility.Collapsed;
        UpdateStatus();
        UpdateLanguageInfo();
        UpdatePadding();
        RestyleAll();
        if (_previewMode != PreviewMode.Off) RefreshPreviewPage();
    }

    public TextDocument Document { get; }

    /// <summary>Highlighting for this tab: picked by hand, or from the file extension.</summary>
    public SyntaxLanguage Language => Document.SyntaxOverride is { } id ? SyntaxLanguage.FromId(id) : SyntaxLanguage.ForFile(Document.FilePath);

    private string EditorFont =>
        Language.IsCode && SettingsService.Current.CodeMonospace ? "Cascadia Mono, Consolas" : SettingsService.Current.FontFamily;

    private double EditorFontSize => SettingsService.Current.FontSize * ZoomService.Factor;

    /// <summary>Goes up on every edit; used to skip rewriting unchanged text to the session.</summary>
    public int ContentVersion { get; private set; }

    public event EventHandler? DirtyChanged;

    public event EventHandler? ContentChanged;

    /// <summary>A link to a local file was clicked in the Markdown preview.</summary>
    public event EventHandler<string>? OpenFileRequested;

    public bool IsPristine => Document.FilePath is null && _text.Length == 0;

    public int SelectionStart
    {
        get => SelStart;
        set => Select(value, 0);
    }

    /// <summary>Call after the text was written to disk.</summary>
    /// <summary>A short note in the status line.</summary>
    public void Notify(string message) => Flash(message);

    public void MarkSaved()
    {
        Document.StampDisk();
        _baseline = _text;
        _formatChanged = false;
        RefreshDirty();
        UpdateFileInfo();
    }

    public void UpdateFileInfo()
    {
        LineEndingText.Text = TextFileService.Describe(Document.LineEnding);
        EncodingText.Text = TextFileService.Describe(Document.Encoding);
        BuildFormatMenus();
        // Save As may have changed the extension, and with it the language
        UpdateLanguageInfo();
        Editor.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(EditorFont);
        RestyleAll();
    }

    public void FocusEditor()
    {
        if (Editor.IsLoaded)
        {
            Editor.Focus(FocusState.Programmatic);
        }
        else
        {
            Editor.Loaded += FocusWhenLoaded;
        }
    }

    private void FocusWhenLoaded(object sender, RoutedEventArgs e)
    {
        Editor.Loaded -= FocusWhenLoaded;
        Editor.Focus(FocusState.Programmatic);
    }

    /// <summary>Runs after every change of the text (typing, paste, undo, replace).</summary>
    private void OnTextChanged()
    {
        ContentVersion++;
        RefreshDirty();
        UpdateStatus();
        UpdateMatchInfo();
        ScheduleRestyle();
        SchedulePreview();
        ScheduleGutter();
        if (SettingsService.Current.LineNumbers) UpdatePadding(); // more digits may need more room
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    private int _lastLine = -1;

    private void Editor_SelectionChanged(object sender, RoutedEventArgs e)
    {
        UpdateStatus();
        ScheduleGutter();
        int line = LineIndexAt(SelStart);
        if (line != _lastLine)
        {
            _lastLine = line;
            if (SettingsService.Current.FocusMode) ScheduleRestyle(soon: true);
        }
        if (SettingsService.Current.TypewriterMode) CenterCaret();
    }

    private int LineIndexAt(int index)
    {
        int line = 0;
        int end = Math.Min(index, _text.Length);
        for (int i = 0; i < end; i++)
        {
            if (_text[i] == '\r') line++;
        }
        return line;
    }

    private void RefreshDirty()
    {
        bool dirty = _formatChanged || !string.Equals(_text, _baseline, StringComparison.Ordinal);
        if (dirty != Document.IsDirty)
        {
            Document.IsDirty = dirty;
            DirtyChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void UpdateStatus()
    {
        string text = _text;
        int caret = Math.Clamp(SelStart, 0, text.Length);

        int line = 1, lineStart = 0;
        for (int i = 0; i < caret; i++)
        {
            if (text[i] == '\r')
            {
                line++;
                lineStart = i + 1;
            }
        }
        int column = caret - lineStart + 1;

        PositionText.Text = Strings.Format("StatusPosition", line, column);
        int selected = SelLength;
        if (selected > 0)
        {
            int end = Math.Min(caret + selected, text.Length);
            int selectedLines = 1;
            for (int i = caret; i < end; i++)
            {
                if (text[i] == '\r') selectedLines++;
            }
            PositionText.Text += "   " + Strings.Format("StatusSelection", end - caret - (selectedLines - 1), selectedLines);
        }

        int words = CountWords(text);
        int chars = text.Length - CountLineBreaks(text);
        int goal = SettingsService.Current.WordGoal;
        if (goal > 0)
        {
            CountText.Text = Strings.Format("StatusCountsGoal", words, goal, chars);
            GoalBar.Value = Math.Min(1, words / (double)goal);
            GoalBar.Visibility = Visibility.Visible;
        }
        else
        {
            CountText.Text = Strings.Format("StatusCounts", words, chars);
            GoalBar.Visibility = Visibility.Collapsed;
        }
    }

    private static int CountLineBreaks(string text)
    {
        int n = 0;
        foreach (char c in text)
        {
            if (c == '\r') n++;
        }
        return n;
    }

    private static int CountWords(string text)
    {
        int words = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                inWord = true;
                words++;
            }
        }
        return words;
    }

    // ---------- Language ----------

    private void UpdateLanguageInfo()
    {
        SyntaxLanguage language = Language;
        LanguageText.Text = language.DisplayName;

        LanguageMenu.Items.Clear();
        foreach (SyntaxLanguage l in SyntaxLanguage.All)
        {
            var item = new RadioMenuFlyoutItem { Text = l.DisplayName, GroupName = "syntax", IsChecked = l.Id == language.Id };
            SyntaxLanguage chosen = l;
            item.Click += (_, _) => SetLanguage(chosen);
            LanguageMenu.Items.Add(item);
        }

        bool canPreview = language.IsMarkdown && SettingsService.Current.MarkdownPreview;
        PreviewButton.Visibility = canPreview ? Visibility.Visible : Visibility.Collapsed;
        HelpButton.Visibility = language.IsMarkdown ? Visibility.Visible : Visibility.Collapsed;
        if (!canPreview && _previewMode != PreviewMode.Off) SetPreviewMode(PreviewMode.Off);
        UpdatePreviewButton();
    }

    public void SetLanguage(SyntaxLanguage language)
    {
        string? byExtension = SyntaxLanguage.ForFile(Document.FilePath).Id;
        Document.SyntaxOverride = language.Id == byExtension ? null : language.Id;
        Editor.FontFamily = new Microsoft.UI.Xaml.Media.FontFamily(EditorFont);
        UpdateLanguageInfo();
        RestyleAll();
        ContentChanged?.Invoke(this, EventArgs.Empty); // remember it in the session
        DirtyChanged?.Invoke(this, EventArgs.Empty); // the tab icon follows the language
    }
}
