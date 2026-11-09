using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Vellum.Services.Syntax;
using Windows.System;

namespace Vellum.Controls;

/// <summary>Markdown cheat sheet: every example is shown as typed and as it looks in the preview.</summary>
public sealed partial class MarkdownHelp : UserControl
{
    // Each topic: a title and an example, both in the resources so they follow the UI language
    private static readonly string[] Topics =
    [
        "Headings", "Emphasis", "Strike", "Code", "Link", "Bullets", "Numbers", "Tasks", "Quote", "CodeBlock", "Table", "Rule",
    ];

    private bool _built;

    public MarkdownHelp()
    {
        InitializeComponent();
        Motion.FadeSlide(this, 12);
        ActualThemeChanged += (_, _) => _built = false;
    }

    public event EventHandler? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show()
    {
        if (!_built) Build();
        Visibility = Visibility.Visible;
        CloseButton.Focus(FocusState.Programmatic);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Build()
    {
        Rows.Children.Clear();
        bool dark = ActualTheme == ElementTheme.Dark;
        var palette = SyntaxPalette.For(dark);
        double size = 14;

        foreach (string topic in Topics)
        {
            string sample = Strings.Get("HelpMd" + topic + "Sample").Replace("\\n", "\r");

            var row = new Grid
            {
                ColumnSpacing = 16,
                Padding = new Thickness(14, 10, 14, 10),
                CornerRadius = new CornerRadius(10),
                Background = ThemeBrushes.Get<Brush>(this, "GlassCardBrush"),
                BorderBrush = ThemeBrushes.Get<Brush>(this, "GlassStrokeBrush"),
                BorderThickness = new Thickness(1),
            };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var left = new StackPanel { Spacing = 6 };
            left.Children.Add(new TextBlock
            {
                Text = Strings.Get("HelpMd" + topic),
                FontSize = 12,
                Foreground = ThemeBrushes.Get<Brush>(this, "StatusTextBrush"),
            });
            left.Children.Add(new TextBlock
            {
                Text = sample.Replace("\r", "\n"),
                FontFamily = new FontFamily("Cascadia Mono, Consolas"),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                IsTextSelectionEnabled = true,
            });
            row.Children.Add(left);

            var result = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            try
            {
                var builder = new MarkdownPreviewBuilder(palette, size, SettingsService.Current.FontFamily, null, _ => { });
                builder.Fill(result, MarkdownPreviewBuilder.Parse(sample));
            }
            catch (Exception ex)
            {
                CrashLog.Write("Markdown help", ex);
            }
            Grid.SetColumn(result, 1);
            row.Children.Add(result);

            Rows.Children.Add(row);
        }
        _built = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Hide();

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => Hide();

    private void Panel_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }
}
