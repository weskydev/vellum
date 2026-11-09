using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;
using Windows.Storage;

namespace Vellum;

public partial class App : Application
{
    private MainWindow? _window;

    public App()
    {
        Instance = this;
        // Old ANSI code pages such as Windows-1251
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // Settings first: the chosen language must be set before any text is loaded
        Services.SettingsService.Load();
        try
        {
            string language = Services.SettingsService.Current.Language;
            if (language.Length > 0)
            {
                Microsoft.Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = language;
            }
            else
            {
                // Clear any earlier choice so the Windows language is used again
                Windows.Globalization.ApplicationLanguages.PrimaryLanguageOverride = "";
            }
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write("Setting language", ex);
        }

        InitializeComponent();

        UnhandledException += (_, e) => Services.CrashLog.Write("UI thread", new Exception(e.Message, e.Exception));
        DebugSettings.XamlResourceReferenceFailed += (_, e) => Services.CrashLog.Write("XAML resource", new Exception(e.Message));
        DebugSettings.BindingFailed += (_, e) => Services.CrashLog.Write("XAML binding", new Exception(e.Message));
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Services.CrashLog.Write("Background", e.ExceptionObject as Exception);
        TaskScheduler.UnobservedTaskException += (_, e) => Services.CrashLog.Write("Task", e.Exception);
    }

    public static App? Instance { get; private set; }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        Services.AccentPalette.Apply(Services.AccentPalette.Resolve(Services.SettingsService.Current.Accent));
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        _window = new MainWindow();
        if (activation.Kind == ExtendedActivationKind.StartupTask)
        {
            // Started with Windows: stay out of the way, in the tray or minimized
            _window.StartQuietly();
            return;
        }
        _window.Activate();
        _window.OpenFiles(SafeFilesFrom(activation));
    }

    private static List<string> SafeFilesFrom(AppActivationArguments args)
    {
        try
        {
            return FilesFrom(args);
        }
        catch (Exception ex)
        {
            Services.CrashLog.Write($"Reading activation ({args.Kind})", ex);
            return [];
        }
    }

    /// <summary>Called when Vellum is started again (e.g. "Open with") while already running.</summary>
    internal void OnRedirectedActivation(AppActivationArguments args)
    {
        List<string> files = SafeFilesFrom(args);
        _window?.DispatcherQueue.TryEnqueue(() =>
        {
            _window.BringToFront();
            _window.OpenFiles(files);
        });
    }

    private static List<string> FilesFrom(AppActivationArguments args)
    {
        // "Open with" / double-click on a file
        if (args.Data is IFileActivatedEventArgs fileArgs)
        {
            return fileArgs.Files
                .Select(item => item.Path)
                .Where(path => !string.IsNullOrEmpty(path))
                .ToList();
        }

        // "vellum notes.txt" from a terminal
        string commandLine;
        string directory = Environment.CurrentDirectory;
        if (args.Data is ICommandLineActivatedEventArgs cmd)
        {
            commandLine = cmd.Operation.Arguments;
            if (!string.IsNullOrEmpty(cmd.Operation.CurrentDirectoryPath))
            {
                directory = cmd.Operation.CurrentDirectoryPath;
            }
        }
        else if (args.Data is Windows.ApplicationModel.Activation.ILaunchActivatedEventArgs launch)
        {
            commandLine = launch.Arguments;
        }
        else
        {
            return [];
        }

        return Services.Native.SplitCommandLine(commandLine)
            .Where(a => !a.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) && !a.StartsWith('-'))
            .Select(a => Path.GetFullPath(a, directory))
            .ToList();
    }
}
