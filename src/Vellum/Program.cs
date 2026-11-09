using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace Vellum;

/// <summary>
/// Entry point. Vellum runs as a single window: opening another file while it is running
/// hands the file over to the running window as a new tab.
/// </summary>
public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        if (RedirectToRunningInstance())
        {
            return 0;
        }

        Application.Start(callbackParams =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }

    private static bool RedirectToRunningInstance()
    {
        AppActivationArguments activation = AppInstance.GetCurrent().GetActivatedEventArgs();
        AppInstance main = AppInstance.FindOrRegisterForKey("main");

        if (main.IsCurrent)
        {
            main.Activated += (_, e) =>
            {
                try
                {
                    App.Instance?.OnRedirectedActivation(e);
                }
                catch (Exception ex)
                {
                    Services.CrashLog.Write("Redirected activation", ex);
                }
            };
            return false;
        }

        // Run on a background thread so the UI thread is not blocked while waiting
        Task.Run(() => main.RedirectActivationToAsync(activation).AsTask()).Wait();
        return true;
    }
}
