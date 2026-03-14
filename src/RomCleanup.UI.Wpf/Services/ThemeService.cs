using System.Windows;

namespace RomCleanup.UI.Wpf.Services;

public enum AppTheme { Dark, Light, HighContrast }

/// <summary>
/// Theme switching service. Swaps MergedDictionaries between Dark/Light/HighContrast themes.
/// Port of WpfHost.ps1 Set-WpfThemeResourceDictionary + Initialize-DesignSystem.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string DarkThemeUri = "pack://application:,,,/Themes/SynthwaveDark.xaml";
    private const string LightThemeUri = "pack://application:,,,/Themes/Light.xaml";
    private const string HighContrastThemeUri = "pack://application:,,,/Themes/HighContrast.xaml";

    private AppTheme _current = AppTheme.Dark;
    public AppTheme Current => _current;
    public bool IsDark => _current == AppTheme.Dark;

    /// <summary>Apply the specified theme to the application.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        _current = theme;

        var uri = theme switch
        {
            AppTheme.Light => LightThemeUri,
            AppTheme.HighContrast => HighContrastThemeUri,
            _ => DarkThemeUri,
        };
        var dict = new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) };

        var mergedDicts = Application.Current.Resources.MergedDictionaries;
        for (int i = mergedDicts.Count - 1; i >= 0; i--)
        {
            var existing = mergedDicts[i];
            if (IsThemeDictionary(existing))
                mergedDicts.RemoveAt(i);
        }

        mergedDicts.Add(dict);
    }

    /// <summary>Apply dark or light theme (legacy overload).</summary>
    public void ApplyTheme(bool dark) => ApplyTheme(dark ? AppTheme.Dark : AppTheme.Light);

    /// <summary>Cycle through Dark → Light → HighContrast → Dark.</summary>
    public void Toggle()
    {
        var next = _current switch
        {
            AppTheme.Dark => AppTheme.Light,
            AppTheme.Light => AppTheme.HighContrast,
            AppTheme.HighContrast => AppTheme.Dark,
            _ => AppTheme.Dark,
        };
        ApplyTheme(next);
    }

    private static bool IsThemeDictionary(ResourceDictionary rd)
    {
        if (rd.Source is not null)
        {
            var s = rd.Source.OriginalString;
            return s.Contains("SynthwaveDark") || s.Contains("Light.xaml") || s.Contains("HighContrast");
        }
        // Legacy sentinel key from programmatic HC dictionary
        return rd.Contains("__HighContrastTheme");
    }
}
