using Windows.ApplicationModel;

namespace Vellum.Services;

public enum StartupState
{
    Off,
    On,
    /// <summary>Turned off in Task Manager or Settings; only the user can turn it back on there.</summary>
    BlockedByUser,
    /// <summary>Not allowed by the organization.</summary>
    BlockedByPolicy,
    Unavailable,
}

/// <summary>Starting Vellum together with Windows (the "VellumStartup" task in the package manifest).</summary>
public static class StartupService
{
    private const string TaskId = "VellumStartup";

    public static async Task<StartupState> GetStateAsync()
    {
        try
        {
            StartupTask task = await StartupTask.GetAsync(TaskId);
            return ToState(task.State);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup task", ex);
            return StartupState.Unavailable;
        }
    }

    public static async Task<StartupState> SetAsync(bool on)
    {
        try
        {
            StartupTask task = await StartupTask.GetAsync(TaskId);
            if (on)
            {
                return ToState(await task.RequestEnableAsync());
            }
            task.Disable();
            return ToState(task.State);
        }
        catch (Exception ex)
        {
            CrashLog.Write("Startup task", ex);
            return StartupState.Unavailable;
        }
    }

    private static StartupState ToState(StartupTaskState state) => state switch
    {
        StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy => StartupState.On,
        StartupTaskState.DisabledByUser => StartupState.BlockedByUser,
        StartupTaskState.DisabledByPolicy => StartupState.BlockedByPolicy,
        _ => StartupState.Off,
    };
}
