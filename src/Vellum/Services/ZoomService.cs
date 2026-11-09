namespace Vellum.Services;

/// <summary>Text zoom for all tabs (Ctrl + plus/minus/0, Ctrl + mouse wheel). Not saved: every launch starts at 100%.</summary>
public static class ZoomService
{
    private static readonly double[] Steps = [0.5, 0.6, 0.7, 0.8, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0];

    public static double Factor { get; private set; } = 1.0;

    public static event EventHandler? Changed;

    public static void ZoomIn() => Set(Steps.FirstOrDefault(s => s > Factor + 0.001, Steps[^1]));

    public static void ZoomOut() => Set(Steps.LastOrDefault(s => s < Factor - 0.001, Steps[0]));

    public static void Reset() => Set(1.0);

    private static void Set(double factor)
    {
        if (Math.Abs(factor - Factor) < 0.001) return;
        Factor = factor;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
