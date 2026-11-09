using System.Runtime.InteropServices;

namespace Vellum.Services;

/// <summary>
/// The Vellum icon next to the clock, and the global "quick note" hotkey (Ctrl+Alt+N), which works even when
/// another app is in front. Both talk to Windows directly through the main window's message loop.
/// </summary>
internal sealed class TrayService : IDisposable
{
    public enum MenuCommand
    {
        None = 0,
        Open = 1,
        NewNote = 2,
        Exit = 3,
    }

    private const int WM_APP_TRAY = 0x8000 + 41; // WM_APP + 41
    private const int WM_HOTKEY = 0x0312;
    private const int WM_LBUTTONUP = 0x0202;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;
    private const int HotkeyId = 0x5645; // "VE"

    private const int NIM_ADD = 0, NIM_MODIFY = 1, NIM_DELETE = 2;
    private const int NIF_MESSAGE = 0x1, NIF_ICON = 0x2, NIF_TIP = 0x4;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;
    private const uint TPM_RETURNCMD = 0x0100, TPM_RIGHTBUTTON = 0x0002, TPM_BOTTOMALIGN = 0x0020;
    private const uint MF_STRING = 0x0, MF_SEPARATOR = 0x800;

    private readonly IntPtr _hwnd;
    private readonly SubclassProc _proc; // kept alive while Windows can call it
    private readonly uint _taskbarCreated;
    private IntPtr _icon;
    private bool _iconShown;
    private bool _hotkeyOn;

    public TrayService(IntPtr hwnd)
    {
        _hwnd = hwnd;
        _proc = WndProc;
        _taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        SetWindowSubclass(_hwnd, _proc, (UIntPtr)1, UIntPtr.Zero);
        AllowDarkMenus();
    }

    /// <summary>Left click on the icon.</summary>
    public event EventHandler? IconClicked;

    /// <summary>A command picked from the icon's menu.</summary>
    public event EventHandler<MenuCommand>? CommandPicked;

    /// <summary>The quick note hotkey was pressed.</summary>
    public event EventHandler? HotkeyPressed;

    /// <summary>Texts of the icon's menu: open, new note, exit.</summary>
    public Func<(string Open, string NewNote, string Exit)>? MenuTexts { get; set; }

    /// <summary>False when another app already uses Ctrl+Alt+N.</summary>
    public bool HotkeyAvailable { get; private set; } = true;

    public void ShowIcon(bool show)
    {
        if (show == _iconShown) return;
        if (show) AddIcon();
        else RemoveIcon();
    }

    public void EnableHotkey(bool on)
    {
        if (on == _hotkeyOn) return;
        if (on)
        {
            _hotkeyOn = RegisterHotKey(_hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_NOREPEAT, 'N');
            HotkeyAvailable = _hotkeyOn;
            if (!_hotkeyOn) CrashLog.Write("Quick note hotkey", new InvalidOperationException("Ctrl+Alt+N is taken by another app"));
        }
        else
        {
            UnregisterHotKey(_hwnd, HotkeyId);
            _hotkeyOn = false;
            HotkeyAvailable = true;
        }
    }

    private void AddIcon()
    {
        if (_icon == IntPtr.Zero)
        {
            string path = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
            int size = GetSystemMetrics(49); // SM_CXSMICON
            _icon = LoadImage(IntPtr.Zero, path, 1 /* IMAGE_ICON */, size, size, 0x10 /* LR_LOADFROMFILE */);
        }
        var data = NewData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        data.uCallbackMessage = WM_APP_TRAY;
        data.hIcon = _icon;
        data.szTip = "Vellum";
        _iconShown = Shell_NotifyIcon(NIM_ADD, ref data) || Shell_NotifyIcon(NIM_MODIFY, ref data);
    }

    private void RemoveIcon()
    {
        var data = NewData();
        Shell_NotifyIcon(NIM_DELETE, ref data);
        _iconShown = false;
    }

    private NOTIFYICONDATA NewData() => new()
    {
        cbSize = Marshal.SizeOf<NOTIFYICONDATA>(),
        hWnd = _hwnd,
        uID = 1,
        szTip = "",
        szInfo = "",
        szInfoTitle = "",
    };

    private IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        try
        {
            if (msg == WM_APP_TRAY)
            {
                int mouse = (int)(lParam.ToInt64() & 0xFFFF);
                if (mouse == WM_LBUTTONUP) IconClicked?.Invoke(this, EventArgs.Empty);
                else if (mouse is WM_RBUTTONUP or WM_CONTEXTMENU) ShowMenu();
                return IntPtr.Zero;
            }
            if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                return IntPtr.Zero;
            }
            if (msg == _taskbarCreated && _iconShown)
            {
                // Explorer restarted: the icon has to be added again
                _iconShown = false;
                AddIcon();
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Tray message", ex);
        }
        return DefSubclassProc(hwnd, msg, wParam, lParam);
    }

    private void ShowMenu()
    {
        var texts = MenuTexts?.Invoke() ?? ("Open Vellum", "New note", "Exit");
        IntPtr menu = CreatePopupMenu();
        try
        {
            AppendMenu(menu, MF_STRING, (UIntPtr)(int)MenuCommand.Open, texts.Open);
            AppendMenu(menu, MF_STRING, (UIntPtr)(int)MenuCommand.NewNote, texts.NewNote + "\tCtrl+Alt+N");
            AppendMenu(menu, MF_SEPARATOR, UIntPtr.Zero, null);
            AppendMenu(menu, MF_STRING, (UIntPtr)(int)MenuCommand.Exit, texts.Exit);
            SetMenuDefaultItem(menu, (uint)MenuCommand.Open, 0);

            GetCursorPos(out POINT pt);
            // Without this the menu would not close when clicking elsewhere
            SetForegroundWindow(_hwnd);
            int picked = TrackPopupMenuEx(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_BOTTOMALIGN, pt.X, pt.Y, _hwnd, IntPtr.Zero);
            if (picked != 0) CommandPicked?.Invoke(this, (MenuCommand)picked);
        }
        finally
        {
            DestroyMenu(menu);
        }
    }

    /// <summary>Lets the icon's menu follow the dark theme of Windows (an older, unnamed uxtheme function).</summary>
    private static void AllowDarkMenus()
    {
        try
        {
            SetPreferredAppMode(1); // allow dark
            FlushMenuThemes();
        }
        catch (Exception)
        {
            // old Windows: the menu stays light
        }
    }

    public void Dispose()
    {
        EnableHotkey(false);
        RemoveIcon();
        RemoveWindowSubclass(_hwnd, _proc, (UIntPtr)1);
        if (_icon != IntPtr.Zero) DestroyIcon(_icon);
        _icon = IntPtr.Zero;
    }

    // ---------- Windows functions ----------

    private delegate IntPtr SubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATA
    {
        public int cbSize;
        public IntPtr hWnd;
        public int uID;
        public int uFlags;
        public int uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public int dwState;
        public int dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public int uVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public int dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Shell_NotifyIcon(int message, ref NOTIFYICONDATA data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id, UIntPtr data);

    [DllImport("comctl32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RemoveWindowSubclass(IntPtr hwnd, SubclassProc proc, UIntPtr id);

    [DllImport("comctl32.dll")]
    private static extern IntPtr DefSubclassProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string name);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint modifiers, uint key);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr LoadImage(IntPtr hinst, string name, uint type, int cx, int cy, uint load);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr icon);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern IntPtr CreatePopupMenu();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr id, string? text);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetMenuDefaultItem(IntPtr menu, uint item, uint byPosition);

    [DllImport("user32.dll")]
    private static extern int TrackPopupMenuEx(IntPtr menu, uint flags, int x, int y, IntPtr hwnd, IntPtr tpm);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyMenu(IntPtr menu);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hwnd);

    [DllImport("uxtheme.dll", EntryPoint = "#135")]
    private static extern int SetPreferredAppMode(int mode);

    [DllImport("uxtheme.dll", EntryPoint = "#136")]
    private static extern void FlushMenuThemes();
}
