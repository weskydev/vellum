using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using Vellum.Controls;
using Vellum.Services;
using Windows.Storage;
using Windows.Storage.Pickers;
using IOPath = System.IO.Path;

namespace Vellum;

/// <summary>Export of a tab to PDF or to a standalone HTML page.</summary>
public sealed partial class MainWindow
{
    private void ExportPdf_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEditor is { } editor) _ = ExportAsync(editor, pdf: true);
    }

    private void ExportHtml_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedEditor is { } editor) _ = ExportAsync(editor, pdf: false);
    }

    private static string ExportTitle(EditorView editor)
    {
        var doc = editor.Document;
        if (doc.IsNote) return doc.NoteTitle.Length > 0 ? doc.NoteTitle : Strings.Get("NoteUntitled");
        if (doc.FilePath is not null) return IOPath.GetFileNameWithoutExtension(doc.FilePath);
        string title = NotesLibrary.TitleOf(editor.Text);
        return title.Length > 0 ? title : Strings.Get("UntitledTab");
    }

    private async Task ExportAsync(EditorView editor, bool pdf)
    {
        string title = ExportTitle(editor);
        var picker = new FileSavePicker();
        WinRT.Interop.InitializeWithWindow.Initialize(picker, Hwnd);
        if (pdf) picker.FileTypeChoices.Add("PDF", [".pdf"]);
        else picker.FileTypeChoices.Add("HTML", [".html"]);
        picker.SuggestedFileName = SuggestName(title);
        StorageFile? file = await picker.PickSaveFileAsync();
        if (file is null) return;

        string? folder = editor.Document.FilePath is { } path ? IOPath.GetDirectoryName(path) : null;
        string html = ExportService.BuildHtml(title, editor.Text, editor.Language, folder);
        try
        {
            if (pdf)
            {
                await PrintToPdfAsync(html, file.Path);
            }
            else
            {
                await File.WriteAllTextAsync(file.Path, html, new System.Text.UTF8Encoding(false));
            }
            editor.Notify(Strings.Format("ExportDone", file.Name));
        }
        catch (Exception ex)
        {
            CrashLog.Write("Export", ex);
            await ShowErrorAsync(Strings.Format("ErrorSaveTitle", file.Name), ex.Message);
        }
    }

    /// <summary>Prints the page to a PDF with an invisible browser view, the same engine as Edge.</summary>
    private async Task PrintToPdfAsync(string html, string target)
    {
        string temp = IOPath.Combine(IOPath.GetTempPath(), "vellum-export-" + Guid.NewGuid().ToString("N") + ".html");
        await File.WriteAllTextAsync(temp, html, new System.Text.UTF8Encoding(false));
        var view = new WebView2
        {
            Width = 800,
            Height = 600,
            Opacity = 0,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        Grid.SetRowSpan(view, 10);
        Grid.SetColumnSpan(view, 10);
        Root.Children.Insert(0, view); // behind everything
        try
        {
            await view.EnsureCoreWebView2Async();
            CoreWebView2 core = view.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsScriptEnabled = false;

            var loaded = new TaskCompletionSource<bool>();
            core.NavigationCompleted += (_, args) => loaded.TrySetResult(args.IsSuccess);
            core.Navigate(new Uri(temp).AbsoluteUri);
            if (!await loaded.Task) throw new InvalidOperationException(Strings.Get("ExportFailed"));

            CoreWebView2PrintSettings settings = core.Environment.CreatePrintSettings();
            settings.ShouldPrintBackgrounds = true;
            settings.ShouldPrintHeaderAndFooter = false;
            if (System.Globalization.RegionInfo.CurrentRegion.IsMetric)
            {
                // A4 outside the US; WebView2 defaults to Letter
                settings.PageWidth = 8.27;
                settings.PageHeight = 11.69;
            }
            settings.MarginTop = settings.MarginBottom = 0.7; // inches
            settings.MarginLeft = settings.MarginRight = 0.65;
            if (!await core.PrintToPdfAsync(target, settings)) throw new InvalidOperationException(Strings.Get("ExportFailed"));
        }
        finally
        {
            Root.Children.Remove(view);
            view.Close();
            try
            {
                File.Delete(temp);
            }
            catch (Exception)
            {
                // a temp file left behind is harmless
            }
        }
    }
}
