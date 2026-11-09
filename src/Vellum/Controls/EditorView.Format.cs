using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Vellum.Models;
using Vellum.Services;

namespace Vellum.Controls;

/// <summary>Line breaks and encoding: shown in the status line, changed from its menus.</summary>
public sealed partial class EditorView
{
    // Encoding or line breaks changed since the last save (the text itself may be the same)
    private bool _formatChanged;

    /// <summary>After restoring a session where only the format had changed.</summary>
    public void MarkFormatChanged()
    {
        _formatChanged = true;
        RefreshDirty();
    }

    private void BuildFormatMenus()
    {
        LineEndingMenu.Items.Clear();
        foreach (var (ending, key) in new[] { (LineEnding.CRLF, "EolWindows"), (LineEnding.LF, "EolUnix"), (LineEnding.CR, "EolMac") })
        {
            var item = new RadioMenuFlyoutItem { Text = Strings.Get(key), GroupName = "eol", IsChecked = Document.LineEnding == ending };
            LineEnding chosen = ending;
            item.Click += (_, _) => SetLineEnding(chosen);
            LineEndingMenu.Items.Add(item);
        }

        EncodingMenu.Items.Clear();
        var saveIn = new MenuFlyoutSubItem { Text = Strings.Get("EncSaveAs"), Icon = new FontIcon { Glyph = "" } };
        var reopen = new MenuFlyoutSubItem { Text = Strings.Get("EncReopen"), Icon = new FontIcon { Glyph = "" }, IsEnabled = Document.FilePath is not null };
        foreach (var choice in TextFileService.Choices)
        {
            if (choice is not { } c)
            {
                saveIn.Items.Add(new MenuFlyoutSeparator());
                reopen.Items.Add(new MenuFlyoutSeparator());
                continue;
            }
            Encoding encoding;
            try
            {
                encoding = TextFileService.FromCodePage(c.CodePage, c.Bom);
            }
            catch (Exception)
            {
                continue; // code page not available on this PC
            }
            var save = new RadioMenuFlyoutItem { Text = c.Name, GroupName = "enc", IsChecked = TextFileService.SameEncoding(encoding, Document.Encoding) };
            save.Click += async (_, _) => await ConvertEncodingAsync(encoding, c.Name);
            saveIn.Items.Add(save);

            var open = new MenuFlyoutItem { Text = c.Name };
            open.Click += async (_, _) => await ReloadAsync(encoding);
            reopen.Items.Add(open);
        }
        EncodingMenu.Items.Add(saveIn);
        EncodingMenu.Items.Add(reopen);
    }

    private void SetLineEnding(LineEnding ending)
    {
        if (Document.LineEnding == ending) return;
        Document.LineEnding = ending;
        _formatChanged = true;
        RefreshDirty();
        UpdateFileInfo();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Saves in another encoding from now on (Notepad++ "Convert to ...").</summary>
    private async Task ConvertEncodingAsync(Encoding encoding, string name)
    {
        if (TextFileService.SameEncoding(encoding, Document.Encoding)) return;
        if (!TextFileService.CanEncode(_text, encoding)
            && !await ConfirmAsync(Strings.Get("EncLossTitle"), Strings.Format("EncLossText", name), Strings.Get("EncLossConfirm")))
        {
            BuildFormatMenus(); // put the check mark back
            return;
        }
        Document.Encoding = encoding;
        _formatChanged = true;
        RefreshDirty();
        UpdateFileInfo();
        ContentChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Reads the file from disk again, in the given encoding (or a fresh guess when null).</summary>
    public async Task ReloadAsync(Encoding? encoding = null, bool confirmed = false)
    {
        if (Document.FilePath is not { } path || !File.Exists(path)) return;
        if (!confirmed && Document.IsDirty && !await ConfirmAsync(Strings.Get("ReloadTitle"), Strings.Get("ReloadText"), Strings.Get("ReloadConfirm")))
        {
            return;
        }

        LoadedText loaded;
        try
        {
            loaded = encoding is null ? TextFileService.Load(path) : TextFileService.LoadWith(path, encoding);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Reload " + path, ex);
            Flash(ex.Message);
            return;
        }

        int caret = SelStart;
        Document.Encoding = loaded.Encoding;
        Document.LineEnding = loaded.LineEnding;
        LoadText(loaded.Text);
        _baseline = loaded.Text;
        _formatChanged = false;
        Document.StampDisk();
        OnTextChanged();
        Select(Math.Min(caret, _text.Length), 0);
        UpdateFileInfo();
    }

    private async Task<bool> ConfirmAsync(string title, string text, string confirm)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Style = (Style)Application.Current.Resources["DefaultContentDialogStyle"],
            RequestedTheme = ActualTheme,
            Title = title,
            Content = text,
            PrimaryButtonText = confirm,
            CloseButtonText = Strings.Get("ButtonCancel"),
            DefaultButton = ContentDialogButton.Close,
        };
        try
        {
            return await dialog.ShowAsync() == ContentDialogResult.Primary;
        }
        catch (Exception)
        {
            return false; // another dialog is already open
        }
    }
}
