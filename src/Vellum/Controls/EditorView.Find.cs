using System.Text.RegularExpressions;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Windows.System;

namespace Vellum.Controls;

/// <summary>Find / replace panel, go to line and scrolling to a position.</summary>
public sealed partial class EditorView
{
    public bool IsFindOpen => FindPanel.Visibility == Visibility.Visible;

    private SearchOptions Options => new(
        MatchCaseToggle.IsChecked == true,
        WholeWordToggle.IsChecked == true,
        RegexToggle.IsChecked == true);

    public void ShowFind(bool replace)
    {
        FindPanel.Visibility = Visibility.Visible;
        SetReplaceVisible(replace);

        // Start with the selected word, like most editors
        string selected = SelectedText;
        if (selected.Length > 0 && !selected.Contains('\r'))
        {
            FindBox.Text = selected;
        }
        FindBox.Focus(FocusState.Programmatic);
        FindBox.SelectAll();
        UpdateMatchInfo();
    }

    public void HideFind()
    {
        FindPanel.Visibility = Visibility.Collapsed;
        Editor.Focus(FocusState.Programmatic);
    }

    /// <summary>F3 / Shift+F3: next or previous match; opens the panel if there is nothing to search for yet.</summary>
    public void FindNext(bool forward)
    {
        if (FindBox.Text.Length == 0)
        {
            ShowFind(replace: false);
            return;
        }

        Regex? regex = TextSearch.Build(FindBox.Text, Options);
        if (regex is null)
        {
            UpdateMatchInfo();
            return;
        }

        int start = forward ? SelStart + SelLength : SelStart;
        Match? match = TextSearch.Find(TextSearch.Prepare(_text), regex, start, forward);
        if (match is not null)
        {
            SelectRange(match.Index, match.Length);
        }
        UpdateMatchInfo();
    }

    public int LineCount
    {
        get
        {
            int lines = 1;
            foreach (char c in _text)
            {
                if (c == '\r') lines++;
            }
            return lines;
        }
    }

    public void GoToLine(int line)
    {
        string text = _text;
        int index = 0;
        for (int current = 1; current < line && index < text.Length; index++)
        {
            if (text[index] == '\r') current++;
        }
        Editor.Focus(FocusState.Programmatic);
        SelectRange(index, 0);
    }

    private void SelectRange(int index, int length)
    {
        Select(index, length);
        RevealIndex(index);
    }

    private void UpdateMatchInfo()
    {
        if (!IsFindOpen) return;
        if (_pendingInfo is not null)
        {
            MatchInfo.Text = _pendingInfo;
            _pendingInfo = null;
            return;
        }
        if (FindBox.Text.Length == 0)
        {
            MatchInfo.Text = "";
            return;
        }

        Regex? regex = TextSearch.Build(FindBox.Text, Options);
        if (regex is null)
        {
            MatchInfo.Text = Strings.Get("FindRegexError");
            return;
        }

        var (total, current) = TextSearch.Count(TextSearch.Prepare(_text), regex, SelStart);
        MatchInfo.Text = total == 0
            ? Strings.Get("FindNoResults")
            : current > 0 ? Strings.Format("FindCount", current, total) : Strings.Format("FindTotal", total);
    }

    private void SetReplaceVisible(bool visible)
    {
        ReplaceToggle.IsChecked = visible;
        ReplaceChevron.Glyph = visible ? "" : "";
        ReplaceBox.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        ReplaceButtons.Visibility = ReplaceBox.Visibility;
    }

    private void ReplaceCurrent()
    {
        Regex? regex = TextSearch.Build(FindBox.Text, Options);
        if (regex is null) return;

        // Replace the selection only if it is exactly a match; then move on
        string text = TextSearch.Prepare(_text);
        Match m = regex.Match(text, SelStart);
        if (m.Success && m.Index == SelStart && m.Length == SelLength && m.Length > 0)
        {
            ReplaceRange(m.Index, m.Length, TextSearch.Expand(m, ReplaceBox.Text, Options));
        }
        FindNext(forward: true);
    }

    private void ReplaceEverything()
    {
        Regex? regex = TextSearch.Build(FindBox.Text, Options);
        if (regex is null) return;

        var (result, count) = TextSearch.ReplaceAll(TextSearch.Prepare(_text), regex, ReplaceBox.Text, Options);
        string message = Strings.Format("ReplacedCount", count);
        if (count > 0)
        {
            int caret = Math.Min(SelStart, result.Length);
            _pendingInfo = message; // the text change would otherwise overwrite the message
            ReplaceRange(0, _text.Length, result); // one undo step
            Select(caret, 0);
        }
        MatchInfo.Text = message;
    }

    private string? _pendingInfo;

    // ---------- Handlers ----------

    private void FindBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        // Search as you type, starting from where the current match begins
        Regex? regex = TextSearch.Build(FindBox.Text, Options);
        if (regex is not null)
        {
            Match? m = TextSearch.Find(TextSearch.Prepare(_text), regex, SelStart, forward: true);
            if (m is not null) SelectRange(m.Index, m.Length);
        }
        UpdateMatchInfo();
    }

    private void FindBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            bool shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
            FindNext(!shift);
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            HideFind();
            e.Handled = true;
        }
    }

    private void ReplaceBox_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            ReplaceCurrent();
            e.Handled = true;
        }
        else if (e.Key == VirtualKey.Escape)
        {
            HideFind();
            e.Handled = true;
        }
    }

    // Esc closes the panel wherever the focus is inside it (a toggle, a button)
    private void FindPanel_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key != VirtualKey.Escape) return;
        HideFind();
        e.Handled = true;
    }

    private void Option_Click(object sender, RoutedEventArgs e) => FindBox_TextChanged(FindBox, null!);

    private void ReplaceToggle_Click(object sender, RoutedEventArgs e) => SetReplaceVisible(ReplaceToggle.IsChecked == true);

    private void FindPrevious_Click(object sender, RoutedEventArgs e) => FindNext(forward: false);

    private void FindNext_Click(object sender, RoutedEventArgs e) => FindNext(forward: true);

    private void FindClose_Click(object sender, RoutedEventArgs e) => HideFind();

    private void ReplaceOne_Click(object sender, RoutedEventArgs e) => ReplaceCurrent();

    private void ReplaceAll_Click(object sender, RoutedEventArgs e) => ReplaceEverything();
}
