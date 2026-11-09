using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Vellum.Services;

namespace Vellum;

/// <summary>
/// The icon next to the clock, closing to it, the quick note hotkey (Ctrl+Alt+N) and starting with Windows.
/// </summary>
public sealed partial class MainWindow
{
    private TrayService? _tray;

    /// <summary>Set when Vellum really quits (menu Exit, or Exit in the icon's menu), not just hides.</summary>
    private bool _exiting;

    private void SetupTray()
    {
        try
        {
            _tray = new TrayService(Hwnd)
            {
                MenuTexts = () => (Strings.Get("TrayOpen"), Strings.Get("TrayNewNote"), Strings.Get("TrayExit")),
            };
            _tray.IconClicked += (_, _) => BringToFront();
            _tray.HotkeyPressed += (_, _) => QuickNote();
            _tray.CommandPicked += (_, command) =>
            {
                switch (command)
                {
                    case TrayService.MenuCommand.Open:
                        BringToFront();
                        break;
                    case TrayService.MenuCommand.NewNote:
                        QuickNote();
                        break;
                    case TrayService.MenuCommand.Exit:
                        ExitFromTray();
                        break;
                }
            };
            Controls.SettingsView.QuickNoteTaken = () => QuickNoteHotkeyTaken;
            ApplyTraySettings();
            SettingsService.Changed += (_, _) => ApplyTraySettings();
            Closed += (_, _) =>
            {
                _tray?.Dispose();
                _tray = null;
            };
        }
        catch (Exception ex)
        {
            CrashLog.Write("Tray setup", ex);
        }
    }

    private void ApplyTraySettings()
    {
        if (_tray is null) return;
        AppSettings s = SettingsService.Current;
        _tray.ShowIcon(s.TrayIcon);
        _tray.EnableHotkey(s.QuickNote);
    }

    /// <summary>False when another app already took Ctrl+Alt+N (shown in Settings).</summary>
    internal bool QuickNoteHotkeyTaken => _tray is { HotkeyAvailable: false };

    private bool CloseHidesToTray => _tray is not null && SettingsService.Current.TrayIcon && SettingsService.Current.CloseToTray;

    /// <summary>Hides the window; Vellum keeps running with its icon next to the clock.</summary>
    private void HideToTray()
    {
        SetZen(false);
        SaveNotesNow();
        SaveSessionNow();
        AppWindow.Hide();
    }

    private async void ExitFromTray()
    {
        if (NeedsCloseConfirmation())
        {
            BringToFront(); // the question needs the window
            if (!await ConfirmCloseAsync()) return;
        }
        _closeConfirmed = true;
        _exiting = true;
        Close();
    }

    /// <summary>Started with Windows: hidden in the tray when the icon is on, otherwise minimized on the taskbar.</summary>
    public void StartQuietly()
    {
        if (SettingsService.Current.TrayIcon)
        {
            return; // never shown until the icon or the hotkey asks for it
        }
        Activate();
        if (AppWindow.Presenter is OverlappedPresenter presenter) presenter.Minimize();
    }

    /// <summary>Ctrl+Alt+N from anywhere: the window comes up with a fresh note, ready to type.</summary>
    private void QuickNote()
    {
        BringToFront();
        CloseLibrary();
        CloseHistory();
        if (Tabs.SelectedItem is TabViewItem { Tag: TabParts p } && p.Editor.IsPristine)
        {
            p.Editor.FocusEditor();
            return;
        }
        AddTab(NewEditor(note: LibraryOn));
    }
}
