using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Vellum.Services;

namespace Vellum.Controls;

/// <summary>The Settings tab. Every change is saved and applied right away.</summary>
public sealed partial class SettingsView : UserControl
{
    private static readonly string[] Fonts =
    [
        "Segoe UI Variable Text", "Segoe UI", "Cascadia Code", "Cascadia Mono", "Consolas",
        "Calibri", "Cambria", "Georgia", "Times New Roman", "Arial", "Verdana", "Courier New",
    ];

    // Language that was active when Vellum started; changing it needs a restart
    private static readonly string StartupLanguage = SettingsService.Current.Language;

    private bool _loading;

    public SettingsView()
    {
        InitializeComponent();
        foreach (string font in Fonts)
        {
            FontBox.Items.Add(font);
        }
        LoadValues();
        BuildAccentSwatches();
        BuildShortcuts();
        Loaded += (_, _) => SettingsService.Changed += SyncFromSettings;
        Loaded += (_, _) => NameControls(this);
        Unloaded += (_, _) => SettingsService.Changed -= SyncFromSettings;
        VersionText.Text = Strings.Format("SetVersion", AppVersion());
        CopyrightText.Text = AppLinks.Copyright;
        BuildAboutLinks();
    }

    /// <summary>
    /// Screen readers read a switch or list by its name; here that is the title of its row,
    /// so Narrator says "Word wrap, on" instead of just "on".
    /// </summary>
    private static void NameControls(DependencyObject node)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(node, i);
            if (child is ToggleSwitch or ComboBox or Slider or NumberBox)
            {
                var control = (Control)child;
                if (string.IsNullOrEmpty(Microsoft.UI.Xaml.Automation.AutomationProperties.GetName(control)) && RowTitle(control) is { } title)
                {
                    Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(control, title);
                }
                continue;
            }
            NameControls(child);
        }
    }

    /// <summary>The first text of the nearest row around the control (its title), if any.</summary>
    private static string? RowTitle(FrameworkElement control)
    {
        FrameworkElement current = control;
        for (int level = 0; level < 3 && current.Parent is FrameworkElement parent; level++, current = parent)
        {
            if (parent is not Panel panel) continue;
            foreach (UIElement sibling in panel.Children)
            {
                if (sibling == current) continue;
                if (sibling is StackPanel stack && stack.Children.FirstOrDefault() is TextBlock { Text.Length: > 0 } title) return title.Text;
            }
        }
        return null;
    }

    public event EventHandler? RestartRequested;

    /// <summary>The close button of the side panel.</summary>
    public event EventHandler? CloseRequested;

    private bool _navFromScroll;
    private bool _scrollFromNav;

    private (FrameworkElement Header, string Glyph)[] Sections =>
    [
        (SecAppearance, "\uE790"),
        (SecEditor, "\uE8D2"),
        (SecWriting, "\uE70F"),
        (SecEditing, "\uE943"),
        (SecNotes, "\uE70B"),
        (SecTray, "\uE8A7"),
        (SecFiles, "\uE8B7"),
        (SecLanguage, "\uE775"),
        (SecShortcuts, "\uE765"),
        (SecAbout, "\uE946"),
    ];

    private void BuildNav()
    {
        SectionNav.Items.Clear();
        foreach (var (header, glyph) in Sections)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12 };
            row.Children.Add(new FontIcon { Glyph = glyph, FontSize = 15 });
            row.Children.Add(new TextBlock { Text = ((TextBlock)header).Text, VerticalAlignment = VerticalAlignment.Center });
            SectionNav.Items.Add(new ListViewItem { Content = row, Tag = header, Padding = new Thickness(12, 0, 12, 0) });
        }
        _navFromScroll = true;
        SectionNav.SelectedIndex = 0;
        _navFromScroll = false;
    }

    /// <summary>Opens the panel at the top, or at a given category.</summary>
    public void ShowSection(int index = 0)
    {
        if (SectionNav.Items.Count == 0) BuildNav();
        SectionNav.SelectedIndex = Math.Clamp(index, 0, SectionNav.Items.Count - 1);
        if (index == 0) Scroller.ChangeView(null, 0, null, true);
    }

    private void SectionNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navFromScroll || SectionNav.SelectedItem is not ListViewItem { Tag: FrameworkElement header }) return;
        double y = header.TransformToVisual(ContentPanel).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
        _scrollFromNav = true;
        Scroller.ChangeView(null, Math.Max(0, y - 8), null, false);
    }

    /// <summary>The category under the top of the view stays highlighted while scrolling.</summary>
    private void Scroller_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
    {
        if (_scrollFromNav)
        {
            if (!e.IsIntermediate) _scrollFromNav = false;
            return;
        }
        int current = 0;
        var sections = Sections;
        for (int i = 0; i < sections.Length; i++)
        {
            double y = sections[i].Header.TransformToVisual(ContentPanel).TransformPoint(new Windows.Foundation.Point(0, 0)).Y;
            if (y - 40 <= Scroller.VerticalOffset) current = i;
        }
        if (Scroller.VerticalOffset >= Scroller.ScrollableHeight - 2) current = sections.Length - 1;
        if (SectionNav.SelectedIndex == current) return;
        _navFromScroll = true;
        SectionNav.SelectedIndex = current;
        _navFromScroll = false;
    }

    private void ClosePanel_Click(object sender, RoutedEventArgs e) => CloseRequested?.Invoke(this, EventArgs.Empty);

    // ---------- About: links ----------

    private void BuildAboutLinks()
    {
        string version = AppVersion();
        string mail = $"mailto:{AppLinks.Email}?subject={Uri.EscapeDataString("Vellum " + version)}";
        string bug = $"mailto:{AppLinks.Email}?subject={Uri.EscapeDataString("Vellum " + version + " — bug")}"
                     + $"&body={Uri.EscapeDataString($"Vellum {version}\nWindows {Environment.OSVersion.Version}\n\n")}";

        AddLink("\uE774", "AboutWebsite", AppLinks.Website.Replace("https://", ""), AppLinks.Website);
        AddLink("\uE734", "AboutRate", "Microsoft Store", AppLinks.StoreReview);
        AddLink("\uEB51", "AboutSupport", Strings.Get("AboutSupportDesc"), AppLinks.Support);
        AddLink("\uE715", "AboutContact", AppLinks.Email, mail);
        AddLink("\uEBE8", "AboutBug", Strings.Get("AboutBugDesc"), bug);
        AddLink("\uE72E", "AboutPrivacy", Strings.Get("AboutPrivacyDesc"), AppLinks.Privacy);
        AddLink("\uE80F", "AboutStudio", AppLinks.Studio.Replace("https://", ""), AppLinks.Studio);
    }

    /// <summary>A whole row that opens a link; looks like the other settings rows.</summary>
    private void AddLink(string glyph, string titleKey, string description, string url)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(new FontIcon { Glyph = glyph, Style = (Style)Application.Current.Resources["SettingsIconStyle"] });
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = Strings.Get(titleKey), Style = (Style)Application.Current.Resources["SettingsTitleStyle"] });
        text.Children.Add(new TextBlock { Text = description, Style = (Style)Application.Current.Resources["SettingsDescriptionStyle"] });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        var arrow = new FontIcon { Glyph = "\uE8A7", FontSize = 12, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.7 };
        Grid.SetColumn(arrow, 2);
        grid.Children.Add(arrow);

        var button = new Button
        {
            Content = grid,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(18, 12, 16, 12),
            Margin = new Thickness(0, 0, 0, 4),
            MinHeight = 60,
            CornerRadius = new CornerRadius(10),
            Background = ThemeBrushes.Get<Brush>(this, "GlassCardBrush"),
            BorderBrush = ThemeBrushes.Get<Brush>(this, "GlassStrokeBrush"),
            BorderThickness = new Thickness(1),
        };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(button, Strings.Get(titleKey));
        ToolTipService.SetToolTip(button, url.StartsWith("mailto:") ? AppLinks.Email : url);
        button.Click += async (_, _) =>
        {
            try
            {
                await Windows.System.Launcher.LaunchUriAsync(new Uri(url));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Open link", ex);
            }
        };
        AboutLinks.Children.Add(button);
    }

    private void LoadValues()
    {
        _loading = true;
        AppSettings s = SettingsService.Current;
        SelectByTag(ThemeBox, s.Theme.ToString());
        SelectByTag(BackdropBox, s.Backdrop.ToString());
        SelectByTag(LanguageBox, s.Language);
        GlassSlider.Value = s.GlassTransparency;
        GlassSlider.IsEnabled = s.Backdrop != BackdropKind.Solid;
        FontBox.SelectedItem = Fonts.Contains(s.FontFamily) ? s.FontFamily : null;
        FontBox.Text = s.FontFamily;
        FontSizeBox.Value = s.FontSize;
        WrapSwitch.IsOn = s.WordWrap;
        StatusSwitch.IsOn = s.ShowStatusBar;
        SpellSwitch.IsOn = s.SpellCheck;
        SessionSwitch.IsOn = s.RestoreSession;
        AutoSaveSwitch.IsOn = s.AutoSaveFiles;
        LineSpacingSlider.Value = s.LineSpacing;
        ReadableSwitch.IsOn = s.ReadableWidth;
        MarkdownSwitch.IsOn = s.MarkdownStyling;
        PreviewSwitch.IsOn = s.MarkdownPreview;
        SyntaxSwitch.IsOn = s.SyntaxHighlighting;
        MonoSwitch.IsOn = s.CodeMonospace;
        FocusSwitch.IsOn = s.FocusMode;
        TypewriterSwitch.IsOn = s.TypewriterMode;
        GoalBox.Value = s.WordGoal;
        LineNumbersSwitch.IsOn = s.LineNumbers;
        LineBandSwitch.IsOn = s.HighlightCurrentLine;
        AutoIndentSwitch.IsOn = s.AutoIndent;
        AutoCloseSwitch.IsOn = s.AutoCloseBrackets;
        SmartHighlightSwitch.IsOn = s.SmartHighlight;
        WatchFilesSwitch.IsOn = s.WatchFiles;
        ExitLastSwitch.IsOn = s.ExitWithLastTab;
        LibrarySwitch.IsOn = s.NotesLibrary;
        HistorySwitch.IsOn = s.VersionHistory;
        FileHistorySwitch.IsOn = s.FileHistory;
        FileHistorySwitch.IsEnabled = s.VersionHistory;
        NoteLocksSwitch.IsOn = s.NoteLocks;
        ExportSwitch.IsOn = s.Export;
        TraySwitch.IsOn = s.TrayIcon;
        CloseToTraySwitch.IsOn = s.CloseToTray;
        CloseToTraySwitch.IsEnabled = s.TrayIcon;
        QuickNoteSwitch.IsOn = s.QuickNote;
        MiniSwitch.IsOn = s.MiniWindow;
        StartupSwitch.IsOn = s.StartWithWindows;
        _ = RefreshStartupAsync();
        UpdateQuickNoteNote();
        UpdateHistorySize();
        SelectByTag(TabSizeBox, s.TabSize.ToString());
        SelectByTag(SearchEngineBox, s.SearchEngine.ToString());
        SelectByTag(NewEncodingBox, $"{s.NewFileCodePage}:{(s.NewFileBom ? 1 : 0)}");
        SelectByTag(NewEolBox, s.NewFileLineEnding.ToString());
        RestartBar.IsOpen = s.Language != StartupLanguage;
        _loading = false;
    }

    private static void SelectByTag(ComboBox box, string tag)
    {
        box.SelectedItem = box.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == tag) ?? box.Items[0];
    }

    private static string? SelectedTag(ComboBox box) => (box.SelectedItem as ComboBoxItem)?.Tag as string;

    // ---------- Accent swatches ----------

    private void BuildAccentSwatches()
    {
        AccentPanel.Children.Clear();
        var keys = AccentPalette.Presets.Select(p => p.Key).Append(AccentPalette.SystemKey);
        foreach (string key in keys)
        {
            bool selected = SettingsService.Current.Accent == key;
            var swatch = new Button
            {
                Width = 30,
                Height = 30,
                Padding = new Thickness(0),
                CornerRadius = new CornerRadius(15),
                BorderThickness = new Thickness(selected ? 2 : 0),
                BorderBrush = ThemeBrushes.Get<Brush>(this, "TextFillColorPrimaryBrush"),
                Background = new SolidColorBrush(AccentPalette.Resolve(key)),
                Tag = key,
                Content = key == AccentPalette.SystemKey
                    ? new FontIcon { Glyph = "", FontSize = 13, Foreground = new SolidColorBrush(Colors.White) }
                    : selected
                        ? new FontIcon { Glyph = "", FontSize = 13, Foreground = new SolidColorBrush(Colors.White) }
                        : null,
            };
            string name = Strings.Get("Accent_" + key);
            ToolTipService.SetToolTip(swatch, name);
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(swatch, name);
            swatch.Resources["ButtonBackgroundPointerOver"] = swatch.Background;
            swatch.Resources["ButtonBackgroundPressed"] = swatch.Background;
            swatch.Click += (_, _) =>
            {
                SettingsService.Update(s => s.Accent = key);
                BuildAccentSwatches();
            };
            AccentPanel.Children.Add(swatch);
        }
    }

    // ---------- Handlers ----------

    private void ThemeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !Enum.TryParse(SelectedTag(ThemeBox), out AppTheme theme)) return;
        SettingsService.Update(s => s.Theme = theme);
    }

    private void BackdropBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !Enum.TryParse(SelectedTag(BackdropBox), out BackdropKind kind)) return;
        GlassSlider.IsEnabled = kind != BackdropKind.Solid;
        SettingsService.Update(s => s.Backdrop = kind);
    }

    private void GlassSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Update(s => s.GlassTransparency = (int)e.NewValue);
    }

    private void FontBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || FontBox.SelectedItem is not string font) return;
        SettingsService.Update(s => s.FontFamily = font);
    }

    private void FontBox_TextSubmitted(ComboBox sender, ComboBoxTextSubmittedEventArgs args)
    {
        // Any installed font can be typed in by name
        string font = args.Text.Trim();
        if (font.Length > 0)
        {
            SettingsService.Update(s => s.FontFamily = font);
        }
    }

    private void FontSizeBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading || double.IsNaN(args.NewValue)) return;
        double size = Math.Clamp(args.NewValue, 8, 48);
        SettingsService.Update(s => s.FontSize = size);
    }

    private void Switch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Update(s =>
        {
            s.WordWrap = WrapSwitch.IsOn;
            s.ShowStatusBar = StatusSwitch.IsOn;
            s.SpellCheck = SpellSwitch.IsOn;
            s.RestoreSession = SessionSwitch.IsOn;
            s.AutoSaveFiles = AutoSaveSwitch.IsOn;
            s.ReadableWidth = ReadableSwitch.IsOn;
            s.MarkdownStyling = MarkdownSwitch.IsOn;
            s.MarkdownPreview = PreviewSwitch.IsOn;
            s.SyntaxHighlighting = SyntaxSwitch.IsOn;
            s.CodeMonospace = MonoSwitch.IsOn;
            s.FocusMode = FocusSwitch.IsOn;
            s.TypewriterMode = TypewriterSwitch.IsOn;
            s.LineNumbers = LineNumbersSwitch.IsOn;
            s.HighlightCurrentLine = LineBandSwitch.IsOn;
            s.AutoIndent = AutoIndentSwitch.IsOn;
            s.AutoCloseBrackets = AutoCloseSwitch.IsOn;
            s.SmartHighlight = SmartHighlightSwitch.IsOn;
            s.WatchFiles = WatchFilesSwitch.IsOn;
            s.ExitWithLastTab = ExitLastSwitch.IsOn;
            s.NotesLibrary = LibrarySwitch.IsOn;
            s.VersionHistory = HistorySwitch.IsOn;
            s.FileHistory = FileHistorySwitch.IsOn;
            s.NoteLocks = NoteLocksSwitch.IsOn;
            s.Export = ExportSwitch.IsOn;
            s.TrayIcon = TraySwitch.IsOn;
            s.CloseToTray = CloseToTraySwitch.IsOn;
            s.QuickNote = QuickNoteSwitch.IsOn;
            s.MiniWindow = MiniSwitch.IsOn;
        });
        FileHistorySwitch.IsEnabled = HistorySwitch.IsOn;
        CloseToTraySwitch.IsEnabled = TraySwitch.IsOn;
        UpdateQuickNoteNote();
    }

    /// <summary>Asks the app window whether Ctrl+Alt+N could be taken (another app may own it).</summary>
    public static Func<bool>? QuickNoteTaken { get; set; }

    private void UpdateQuickNoteNote()
    {
        bool taken = SettingsService.Current.QuickNote && QuickNoteTaken?.Invoke() == true;
        QuickNoteDescText.Text = Strings.Get(taken ? "SetQuickNoteTaken" : "SetQuickNoteNote");
    }

    /// <summary>Shows the real state: the user may have turned Vellum off in Task Manager's startup apps.</summary>
    private async Task RefreshStartupAsync()
    {
        StartupState state = await StartupService.GetStateAsync();
        bool wasLoading = _loading;
        _loading = true;
        StartupSwitch.IsOn = state == StartupState.On;
        StartupSwitch.IsEnabled = state is StartupState.On or StartupState.Off;
        StartupDescText.Text = Strings.Get(state switch
        {
            StartupState.BlockedByUser => "SetStartupBlocked",
            StartupState.BlockedByPolicy => "SetStartupPolicy",
            _ => "SetStartupNote",
        });
        _loading = wasLoading;
        if (SettingsService.Current.StartWithWindows != (state == StartupState.On))
        {
            SettingsService.Update(s => s.StartWithWindows = state == StartupState.On);
        }
    }

    private async void StartupSwitch_Toggled(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        await StartupService.SetAsync(StartupSwitch.IsOn);
        await RefreshStartupAsync();
    }

    private void UpdateHistorySize()
    {
        long bytes = HistoryService.Size();
        string size = bytes < 1024 * 1024
            ? Strings.Format("SizeKb", (bytes / 1024.0).ToString("0.#"))
            : Strings.Format("SizeMb", (bytes / 1024.0 / 1024.0).ToString("0.##"));
        SetClearHistoryDescText.Text = Strings.Format("SetClearHistorySize", size);
        ClearHistoryButton.IsEnabled = bytes > 0;
    }

    /// <summary>Removes every saved version (the notes themselves stay).</summary>
    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var flyout = new Flyout();
        var panel = new StackPanel { Spacing = 12, MaxWidth = 280 };
        panel.Children.Add(new TextBlock { Text = Strings.Get("SetClearHistoryConfirm"), TextWrapping = TextWrapping.Wrap });
        var yes = new Button { Content = Strings.Get("SetClearHistoryYes"), Style = (Style)Application.Current.Resources["AccentButtonStyle"] };
        yes.Click += (_, _) =>
        {
            flyout.Hide();
            HistoryService.ForgetAll();
            UpdateHistorySize();
        };
        panel.Children.Add(yes);
        flyout.Content = panel;
        flyout.ShowAt(ClearHistoryButton);
    }

    private void Combo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SettingsService.Update(s =>
        {
            if (int.TryParse(SelectedTag(TabSizeBox), out int tab)) s.TabSize = tab;
            if (Enum.TryParse(SelectedTag(SearchEngineBox), out SearchEngine engine)) s.SearchEngine = engine;
            if (SelectedTag(NewEncodingBox)?.Split(':') is [var cp, var bom] && int.TryParse(cp, out int codePage))
            {
                s.NewFileCodePage = codePage;
                s.NewFileBom = bom == "1";
            }
            if (Enum.TryParse(SelectedTag(NewEolBox), out Models.LineEnding eol)) s.NewFileLineEnding = eol;
        });
    }

    private void LineSpacingSlider_ValueChanged(object sender, Microsoft.UI.Xaml.Controls.Primitives.RangeBaseValueChangedEventArgs e)
    {
        if (_loading) return;
        double spacing = Math.Round(e.NewValue, 2);
        SettingsService.Update(s => s.LineSpacing = spacing);
    }

    private void GoalBox_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
    {
        if (_loading) return;
        int goal = double.IsNaN(args.NewValue) ? 0 : (int)Math.Clamp(args.NewValue, 0, 1_000_000);
        SettingsService.Update(s => s.WordGoal = goal);
    }

    /// <summary>Focus and typewriter can also be switched from the palette; keep the switches in step.</summary>
    private void SyncFromSettings(object? sender, EventArgs e)
    {
        AppSettings s = SettingsService.Current;
        _loading = true;
        FocusSwitch.IsOn = s.FocusMode;
        TypewriterSwitch.IsOn = s.TypewriterMode;
        ReadableSwitch.IsOn = s.ReadableWidth;
        MarkdownSwitch.IsOn = s.MarkdownStyling;
        SyntaxSwitch.IsOn = s.SyntaxHighlighting;
        WrapSwitch.IsOn = s.WordWrap;
        StatusSwitch.IsOn = s.ShowStatusBar;
        LineNumbersSwitch.IsOn = s.LineNumbers;
        LineBandSwitch.IsOn = s.HighlightCurrentLine;
        _loading = false;
    }

    private void LanguageBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        string language = SelectedTag(LanguageBox) ?? "";
        SettingsService.Update(s => s.Language = language);
        RestartBar.IsOpen = language != StartupLanguage;
    }

    private void Restart_Click(object sender, RoutedEventArgs e) => RestartRequested?.Invoke(this, EventArgs.Empty);

    private static string AppVersion()
    {
        try
        {
            var v = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{v.Major}.{v.Minor}.{v.Build}";
        }
        catch (Exception)
        {
            return "dev";
        }
    }

    private static readonly (string Key, string Keys)[] ShortcutRows =
    [
        ("CmdPalette", "Ctrl+Shift+P"),
        ("CmdNewTab", "Ctrl+T / Ctrl+N"),
        ("CmdOpen", "Ctrl+O"),
        ("CmdSave", "Ctrl+S"),
        ("CmdSaveAs", "Ctrl+Shift+S"),
        ("CmdCloseTab", "Ctrl+W"),
        ("CmdSwitchTabs", "Ctrl+1 … Ctrl+9"),
        ("CmdFind", "Ctrl+F"),
        ("CmdReplace", "Ctrl+H"),
        ("CmdFindNextPrev", "F3 / Shift+F3"),
        ("CmdGoToLine", "Ctrl+G"),
        ("CmdPreview", "Ctrl+Shift+V"),
        ("CmdZenOn", "F11"),
        ("CmdUndoRedo", "Ctrl+Z / Ctrl+Y"),
        ("CmdSaveAll", "Ctrl+Alt+S"),
        ("ShortcutQuickNote", "Ctrl+Alt+N"),
        ("CmdMini", "Ctrl+Shift+M"),
        ("ToolDuplicateLine", "Ctrl+D"),
        ("ToolDeleteLine", "Ctrl+Shift+K"),
        ("ShortcutMoveLines", "Alt+↑ / Alt+↓"),
        ("ShortcutCase", "Ctrl+Shift+U / Ctrl+U"),
        ("ToolJoinLines", "Ctrl+J"),
        ("ToolComment", "Ctrl+/"),
        ("ShortcutIndent", "Tab / Shift+Tab"),
        ("ToolDateTime", "F5"),
        ("ToolSearchWeb", "Ctrl+E"),
        ("ShortcutNewLine", "Ctrl+Enter / Ctrl+Shift+Enter"),
        ("ToolMatchingBracket", "Ctrl+B"),
        ("ToolBookmark", "Ctrl+F2"),
        ("ShortcutBookmarkJump", "F2 / Shift+F2"),
        ("CmdReopenClosed", "Ctrl+Shift+T"),
        ("CmdLibrary", "Ctrl+Shift+L"),
        ("CmdHistory", "Ctrl+Shift+H"),
        ("CmdZoom", "Ctrl+= / Ctrl+- / Ctrl+0"),
        ("CmdSettings", "Ctrl+,"),
    ];

    /// <summary>Read-only list of keyboard shortcuts.</summary>
    private void BuildShortcuts()
    {
        foreach (var (key, keys) in ShortcutRows)
        {
            var row = new Grid { Padding = new Thickness(0, 7, 0, 7) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.Children.Add(new TextBlock { Text = Strings.Get(key), Style = (Style)Resources["ShortcutNameStyle"] });
            var chip = new TextBlock { Text = keys, Style = (Style)Resources["ShortcutKeysStyle"] };
            Grid.SetColumn(chip, 1);
            row.Children.Add(chip);
            ShortcutsPanel.Children.Add(row);
        }
    }
}
