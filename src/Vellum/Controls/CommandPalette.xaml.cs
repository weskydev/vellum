using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Vellum.Models;
using Vellum.Services;
using Windows.System;

namespace Vellum.Controls;

/// <summary>
/// Ctrl+Shift+P: type to find any command. Typing ":" and a number jumps to that line.
/// </summary>
public sealed partial class CommandPalette : UserControl
{
    private IReadOnlyList<PaletteCommand> _all = [];

    public CommandPalette()
    {
        InitializeComponent();
        Services.Motion.FadeSlide(this, -10);
    }

    /// <summary>Called with a line number when the user types ":42" and presses Enter.</summary>
    public Action<int>? GoToLine { get; set; }

    public event EventHandler? Closed;

    public bool IsOpen => Visibility == Visibility.Visible;

    public void Show(IReadOnlyList<PaletteCommand> commands, string initialText = "")
    {
        _all = commands;
        Visibility = Visibility.Visible;
        QueryBox.Text = initialText;
        QueryBox.SelectionStart = initialText.Length;
        Refresh();
        QueryBox.Focus(FocusState.Programmatic);
    }

    public void Hide()
    {
        if (!IsOpen) return;
        Visibility = Visibility.Collapsed;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        string query = QueryBox.Text.Trim();

        if (query.StartsWith(':'))
        {
            var items = new List<PaletteCommand>();
            if (int.TryParse(query[1..].Trim(), out int line) && line > 0)
            {
                items.Add(new PaletteCommand(Strings.Format("PaletteGoToLine", line), "", "Enter", () => GoToLine?.Invoke(line)));
            }
            Results.ItemsSource = items;
        }
        else
        {
            Results.ItemsSource = Filter(query);
        }

        if (Results.Items.Count > 0)
        {
            Results.SelectedIndex = 0;
        }
    }

    /// <summary>Every word typed must appear in the title; titles starting with the query come first.</summary>
    private List<PaletteCommand> Filter(string query)
    {
        if (query.Length == 0) return _all.ToList();

        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return _all
            .Where(c => words.All(w => c.Title.Contains(w, StringComparison.CurrentCultureIgnoreCase)))
            .OrderByDescending(c => c.Title.StartsWith(query, StringComparison.CurrentCultureIgnoreCase))
            .ToList();
    }

    private void Run(PaletteCommand? command)
    {
        if (command is null) return;
        Hide();
        command.Execute();
    }

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh();

    private void QueryBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        switch (e.Key)
        {
            case VirtualKey.Down:
                if (Results.Items.Count > 0)
                {
                    Results.SelectedIndex = (Results.SelectedIndex + 1) % Results.Items.Count;
                    Results.ScrollIntoView(Results.SelectedItem);
                }
                e.Handled = true;
                break;
            case VirtualKey.Up:
                if (Results.Items.Count > 0)
                {
                    Results.SelectedIndex = (Results.SelectedIndex - 1 + Results.Items.Count) % Results.Items.Count;
                    Results.ScrollIntoView(Results.SelectedItem);
                }
                e.Handled = true;
                break;
            case VirtualKey.Enter:
                Run(Results.SelectedItem as PaletteCommand);
                e.Handled = true;
                break;
            case VirtualKey.Escape:
                Hide();
                e.Handled = true;
                break;
        }
    }

    private void Results_ItemClick(object sender, ItemClickEventArgs e) => Run(e.ClickedItem as PaletteCommand);

    private void Backdrop_Tapped(object sender, TappedRoutedEventArgs e) => Hide();

    private void Panel_Tapped(object sender, TappedRoutedEventArgs e) => e.Handled = true;
}
