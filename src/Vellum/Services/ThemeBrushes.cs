using Microsoft.UI.Xaml;

namespace Vellum.Services;

/// <summary>
/// Theme resources for brushes made in code. Application.Current.Resources follows the Windows theme,
/// but Vellum can be light while Windows is dark (or the other way round), so the look-up
/// goes by the theme of the element the brush is for.
/// </summary>
public static class ThemeBrushes
{
    public static T Get<T>(FrameworkElement scope, string key) where T : class
    {
        string theme = scope.ActualTheme == ElementTheme.Light ? "Light" : "Dark";
        if (Find(Application.Current.Resources, key, theme) is T found) return found;
        return (T)Application.Current.Resources[key];
    }

    private static object? Find(ResourceDictionary dictionary, string key, string theme)
    {
        if (dictionary.ThemeDictionaries.TryGetValue(theme, out object? themed)
            && themed is ResourceDictionary themedDictionary
            && themedDictionary.TryGetValue(key, out object? value))
        {
            return value;
        }
        foreach (ResourceDictionary merged in dictionary.MergedDictionaries)
        {
            if (Find(merged, key, theme) is { } inner) return inner;
        }
        return null;
    }
}
