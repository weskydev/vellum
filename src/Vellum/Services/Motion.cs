using System.Numerics;
using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Hosting;
using Windows.UI.ViewManagement;

namespace Vellum.Services;

/// <summary>
/// Small, quick animations: panels fade and slide in when they appear, a tab's page rises in when selected.
/// Turned off when the user switched off animation effects in Windows.
/// </summary>
public static class Motion
{
    private static readonly UISettings Ui = new();

    public static bool Enabled
    {
        get
        {
            try
            {
                return Ui.AnimationsEnabled;
            }
            catch (Exception)
            {
                return true;
            }
        }
    }

    /// <summary>Plays a fade + slide every time the element becomes visible.</summary>
    /// <param name="offsetY">Where the element slides in from (negative = from above).</param>
    public static void FadeSlide(UIElement element, float offsetY = -8)
    {
        element.RegisterPropertyChangedCallback(UIElement.VisibilityProperty, (_, _) =>
        {
            if (element.Visibility == Visibility.Visible) Enter(element, offsetY, fromOpacity: 0);
        });
    }

    /// <summary>A short fade for elements that come and go in place (e.g. the preview pane).</summary>
    public static void Fade(UIElement element) => FadeSlide(element, 0);

    /// <summary>Plays a quick fade + rise once, e.g. on the page of a tab that was just selected.</summary>
    public static void Enter(UIElement element, float offsetY = 6, float fromOpacity = 0.35f, float offsetX = 0)
    {
        if (!Enabled) return;
        try
        {
            Visual visual = ElementCompositionPreview.GetElementVisual(element);
            Compositor c = visual.Compositor;
            CubicBezierEasingFunction ease = c.CreateCubicBezierEasingFunction(new Vector2(0.1f, 0.9f), new Vector2(0.2f, 1f));

            ScalarKeyFrameAnimation fade = c.CreateScalarKeyFrameAnimation();
            fade.InsertKeyFrame(0, fromOpacity);
            fade.InsertKeyFrame(1, 1, ease);
            fade.Duration = TimeSpan.FromMilliseconds(180);
            visual.StartAnimation("Opacity", fade);

            if (Math.Abs(offsetY) > 0.01f || Math.Abs(offsetX) > 0.01f)
            {
                ElementCompositionPreview.SetIsTranslationEnabled(element, true);
                Vector3KeyFrameAnimation rise = c.CreateVector3KeyFrameAnimation();
                rise.InsertKeyFrame(0, new Vector3(offsetX, offsetY, 0));
                rise.InsertKeyFrame(1, Vector3.Zero, ease);
                rise.Duration = TimeSpan.FromMilliseconds(240);
                visual.StartAnimation("Translation", rise);
            }
        }
        catch (Exception ex)
        {
            CrashLog.Write("Motion", ex);
        }
    }
}
