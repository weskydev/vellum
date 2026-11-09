namespace Vellum.Services;

/// <summary>Zen mode (F11): full screen, no tabs or status line, text in a calm centered column. Not saved.</summary>
public static class WritingModes
{
    public static bool Zen { get; private set; }

    public static event EventHandler? Changed;

    public static void SetZen(bool on)
    {
        if (Zen == on) return;
        Zen = on;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
