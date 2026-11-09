using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Vellum.Services;
using Windows.UI;
using WinRT;

namespace Vellum;

/// <summary>Window chrome: size and position, title bar, theme and glass. Tabs and files are in MainWindow.Documents.cs.</summary>
public sealed partial class MainWindow : Window
{
    private readonly SessionService _session = new();
    private SystemBackdropConfiguration? _backdropConfig;
    private DesktopAcrylicController? _acrylic;
    private MicaController? _mica;
    private WindowBounds _normalBounds = new();
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        SessionState? saved = _session.Load();

        SetupWindow(saved?.Window);
        SetupLook();
        SetupTitleBar();
        try
        {
            SetupCaption();
        }
        catch (Exception ex)
        {
            CrashLog.Write("Caption setup", ex);
        }
        SetupDocuments(SettingsService.Current.RestoreSession ? saved : null);
        SetupCommands();
        SetupWriting();
        SetupTray();
        SetupMini(saved);

        _backdropConfig = new SystemBackdropConfiguration { IsInputActive = true };
        Root.RequestedTheme = ToElementTheme(SettingsService.Current.Theme);
        Root.Loaded += (_, _) => ApplyAppearance();
        Root.ActualThemeChanged += (_, _) => ApplyAppearance();
        Root.SizeChanged += (_, _) => UpdateDragRegion();
        Activated += MainWindow_Activated;
        Activated += (_, args) =>
        {
            if (args.WindowActivationState != WindowActivationState.Deactivated) _ = CheckDiskChangesAsync();
        };

        SettingsService.Changed += OnSettingsChanged;
        Closed += (_, _) =>
        {
            SettingsService.Changed -= OnSettingsChanged;
            DisposeBackdrop();
            _backdropConfig = null;
        };
    }

    public IntPtr Hwnd => WinRT.Interop.WindowNative.GetWindowHandle(this);

    /// <summary>Shows the window on top, e.g. when a file is opened while Vellum is already running.</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Minimized } presenter)
        {
            presenter.Restore();
        }
        if (!AppWindow.IsVisible) AppWindow.Show(); // hidden in the tray
        Activate();
        Native.SetForegroundWindow(Hwnd);
    }

    // ---------- Window ----------

    private void SetupWindow(WindowBounds? saved)
    {
        AppWindow.SetIcon("Assets/AppIcon.ico");

        if (saved is not null && saved.Width > 200 && saved.Height > 150 &&
            DisplayArea.GetFromRect(new Windows.Graphics.RectInt32(saved.X, saved.Y, saved.Width, saved.Height), DisplayAreaFallback.None) is not null)
        {
            // Reopen where it was last time
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(saved.X, saved.Y, saved.Width, saved.Height));
            if (saved.Maximized && AppWindow.Presenter is OverlappedPresenter p)
            {
                p.Maximize();
            }
        }
        else
        {
            // First launch: ~60% x 70% of the screen, centered
            var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;
            int width = Math.Min(area.Width, Math.Max(900, area.Width * 6 / 10));
            int height = Math.Min(area.Height, Math.Max(600, area.Height * 7 / 10));
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
                area.X + (area.Width - width) / 2,
                area.Y + (area.Height - height) / 2,
                width,
                height));
        }

        RememberNormalBounds();
        AppWindow.Changed += (_, args) =>
        {
            if (args.DidPositionChange || args.DidSizeChange || args.DidPresenterChange)
            {
                RememberNormalBounds();
            }
        };
        AppWindow.Closing += AppWindow_Closing;
        Closed += (_, _) => SaveSessionNow();
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (!_exiting && CloseHidesToTray)
        {
            // The icon stays next to the clock; Vellum keeps running
            args.Cancel = true;
            HideToTray();
            return;
        }
        SetZen(false);
        if (!_closeConfirmed && NeedsCloseConfirmation())
        {
            args.Cancel = true;
            if (await ConfirmCloseAsync())
            {
                _closeConfirmed = true;
                Close();
            }
            return;
        }
        SaveSessionNow();
    }

    /// <summary>Tracks the size of the window when it is not maximized or minimized.</summary>
    private void RememberNormalBounds()
    {
        if (WritingModes.Zen) return;
        if (_mini)
        {
            RememberMiniBounds();
            return;
        }
        if (AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Restored })
        {
            _normalBounds = new WindowBounds
            {
                X = AppWindow.Position.X,
                Y = AppWindow.Position.Y,
                Width = AppWindow.Size.Width,
                Height = AppWindow.Size.Height,
            };
        }
    }

    private WindowBounds CurrentBounds() => new()
    {
        X = _normalBounds.X,
        Y = _normalBounds.Y,
        Width = _normalBounds.Width,
        Height = _normalBounds.Height,
        Maximized = AppWindow.Presenter is OverlappedPresenter { State: OverlappedPresenterState.Maximized },
    };

    private void MainWindow_Activated(object sender, WindowActivatedEventArgs args)
    {
        // Put the caret in the editor as soon as the window first appears
        if (args.WindowActivationState != WindowActivationState.Deactivated)
        {
            Activated -= MainWindow_Activated;
            FocusSelectedEditor();
        }
    }

    private void SetupTitleBar()
    {
        ExtendsContentIntoTitleBar = true;
        // Same height as the tab strip, so the caption buttons line up with the tabs
        AppWindow.TitleBar.PreferredHeightOption = TitleBarHeightOption.Standard;
        SetTitleBar(DragRegion);
    }

    /// <summary>Keeps the minimize/maximize/close buttons from covering the drag area.</summary>
    private void UpdateDragRegion()
    {
        double scale = Root.XamlRoot?.RasterizationScale ?? 1.0;
        // The mini window keeps only a small grip, so its tab still fits
        DragRegion.MinWidth = _mini ? 24 : AppWindow.TitleBar.RightInset / scale + 64;
    }

    // ---------- Theme, accent and glass ----------

    private static ElementTheme ToElementTheme(AppTheme theme) => theme switch
    {
        AppTheme.Light => ElementTheme.Light,
        AppTheme.Dark => ElementTheme.Dark,
        _ => ElementTheme.Default,
    };

    private void OnSettingsChanged(object? sender, EventArgs e)
    {
        AppSettings s = SettingsService.Current;
        AccentPalette.Apply(AccentPalette.Resolve(s.Accent));

        // Re-apply the theme so every control picks up the new accent shades
        ElementTheme wanted = ToElementTheme(s.Theme);
        Root.RequestedTheme = Root.ActualTheme == ElementTheme.Dark ? ElementTheme.Light : ElementTheme.Dark;
        Root.RequestedTheme = wanted;

        ApplyAppearance();
    }

    private void ApplyAppearance()
    {
        if (_backdropConfig is null) return; // window is closing

        AppSettings s = SettingsService.Current;
        bool dark = Root.ActualTheme == ElementTheme.Dark;
        Color accent = AccentPalette.Resolve(s.Accent);
        Color solid = dark ? Color.FromArgb(255, 0x1C, 0x1B, 0x1A) : Color.FromArgb(255, 0xF6, 0xF5, 0xF2);

        // The glass picks up a little of the accent color
        Color tint = dark
            ? AccentPalette.Mix(Color.FromArgb(255, 0x14, 0x13, 0x12), accent, 0.10)
            : AccentPalette.Mix(Color.FromArgb(255, 0xFA, 0xFA, 0xF8), accent, 0.06);
        double t = s.GlassTransparency / 100.0;

        _backdropConfig.Theme = dark ? SystemBackdropTheme.Dark : SystemBackdropTheme.Light;
        UpdateTabGlow(accent, dark);

        BackdropKind kind = s.Backdrop;
        if (kind == BackdropKind.Acrylic && !DesktopAcrylicController.IsSupported()) kind = BackdropKind.Solid;
        if (kind == BackdropKind.Mica && !MicaController.IsSupported()) kind = BackdropKind.Solid;

        switch (kind)
        {
            case BackdropKind.Acrylic:
                if (_acrylic is null)
                {
                    DisposeBackdrop();
                    _acrylic = new DesktopAcrylicController();
                    _acrylic.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                    _acrylic.SetSystemBackdropConfiguration(_backdropConfig);
                }
                _acrylic.TintColor = tint;
                _acrylic.TintOpacity = (float)(dark ? Lerp(0.75, 0.12, t) : Lerp(0.8, 0.2, t));
                _acrylic.LuminosityOpacity = (float)(dark ? Lerp(0.96, 0.45, t) : Lerp(0.98, 0.6, t));
                _acrylic.FallbackColor = solid;
                Root.Background = null;
                break;

            case BackdropKind.Mica:
                if (_mica is null)
                {
                    DisposeBackdrop();
                    _mica = new MicaController { Kind = MicaKind.BaseAlt };
                    _mica.AddSystemBackdropTarget(this.As<ICompositionSupportsSystemBackdrop>());
                    _mica.SetSystemBackdropConfiguration(_backdropConfig);
                }
                _mica.TintColor = tint;
                _mica.TintOpacity = (float)Lerp(0.85, 0.3, t);
                _mica.LuminosityOpacity = (float)Lerp(1.0, 0.7, t);
                _mica.FallbackColor = solid;
                Root.Background = null;
                break;

            default:
                DisposeBackdrop();
                Root.Background = new SolidColorBrush(solid);
                break;
        }

        // Caption buttons blend into the glass
        var bar = AppWindow.TitleBar;
        Color fg = dark ? Colors.White : Color.FromArgb(255, 0x1D, 0x1B, 0x29);
        bar.ButtonBackgroundColor = Colors.Transparent;
        bar.ButtonInactiveBackgroundColor = Colors.Transparent;
        bar.ButtonForegroundColor = fg;
        bar.ButtonInactiveForegroundColor = Color.FromArgb(0x88, fg.R, fg.G, fg.B);
        bar.ButtonHoverBackgroundColor = dark ? Color.FromArgb(0x1A, 255, 255, 255) : Color.FromArgb(0x14, 0, 0, 0);
        bar.ButtonHoverForegroundColor = fg;
        bar.ButtonPressedBackgroundColor = dark ? Color.FromArgb(0x10, 255, 255, 255) : Color.FromArgb(0x0C, 0, 0, 0);
        bar.ButtonPressedForegroundColor = fg;

        UpdateDragRegion();
    }

    private void DisposeBackdrop()
    {
        _acrylic?.Dispose();
        _acrylic = null;
        _mica?.Dispose();
        _mica = null;
    }

    private static double Lerp(double from, double to, double t) => from + (to - from) * t;
}
