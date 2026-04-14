using System.Windows;
using Romulus.UI.Wpf.Models;

namespace Romulus.UI.Wpf.Services;

public enum AppTheme { Dark, Light, HighContrast, CleanDarkPro, RetroCRT, ArcadeNeon }

/// <summary>
/// Theme switching service. Swaps MergedDictionaries between theme palettes.
/// Templates live in _ControlTemplates.xaml (always loaded); only the colour
/// palette dictionary is swapped on theme change.
/// </summary>
public sealed class ThemeService : IThemeService
{
    private const string DarkThemeUri         = "pack://application:,,,/Themes/SynthwaveDark.xaml";
    private const string LightThemeUri        = "pack://application:,,,/Themes/CleanDaylight.xaml";
    private const string LegacyLightThemeUri  = "pack://application:,,,/Themes/Light.xaml";
    private const string HighContrastThemeUri  = "pack://application:,,,/Themes/HighContrast.xaml";
    private const string CleanDarkProThemeUri  = "pack://application:,,,/Themes/CleanDarkPro.xaml";
    private const string RetroCrtThemeUri      = "pack://application:,,,/Themes/RetroCRT.xaml";
    private const string ArcadeNeonThemeUri    = "pack://application:,,,/Themes/ArcadeNeon.xaml";

    internal static readonly List<AppTheme> AllThemes =
    [
        AppTheme.Dark,
        AppTheme.CleanDarkPro,
        AppTheme.RetroCRT,
        AppTheme.ArcadeNeon,
        AppTheme.Light,
        AppTheme.HighContrast,
    ];

    private AppTheme _current = AppTheme.Dark;
    public AppTheme Current => _current;
    public bool IsDark => _current != AppTheme.Light && _current != AppTheme.HighContrast;
    public IReadOnlyList<AppTheme> AvailableThemes => AllThemes;

    /// <summary>Apply the specified theme to the application.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        _current = theme;

        var uri = theme switch
        {
            AppTheme.Light         => LightThemeUri,
            AppTheme.HighContrast  => HighContrastThemeUri,
            AppTheme.CleanDarkPro  => CleanDarkProThemeUri,
            AppTheme.RetroCRT      => RetroCrtThemeUri,
            AppTheme.ArcadeNeon    => ArcadeNeonThemeUri,
            _                      => DarkThemeUri,
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

    /// <summary>Cycle through all themes in display order.</summary>
    public void Toggle()
    {
        var idx = AllThemes.IndexOf(_current);
        var next = AllThemes[(idx + 1) % AllThemes.Count];
        ApplyTheme(next);
    }

    // GUI-114: Robust theme detection via known URI set
    private static readonly HashSet<string> ThemeUris = new(StringComparer.OrdinalIgnoreCase)
    {
        DarkThemeUri,
        LightThemeUri,
        LegacyLightThemeUri,
        HighContrastThemeUri,
        CleanDarkProThemeUri,
        RetroCrtThemeUri,
        ArcadeNeonThemeUri,
    };

    private static bool IsThemeDictionary(ResourceDictionary rd)
    {
        if (rd.Source is not null)
            return ThemeUris.Contains(rd.Source.OriginalString);
        // Legacy sentinel key from programmatic HC dictionary
        return rd.Contains("__HighContrastTheme");
    }

    // ── Density dictionary swapping ─────────────────────────────────────
    private const string DensityCompactUri    = "pack://application:,,,/Themes/_DensityCompact.xaml";
    private const string DensityComfortableUri = "pack://application:,,,/Themes/_DensityComfortable.xaml";

    private static readonly HashSet<string> DensityUris = new(StringComparer.OrdinalIgnoreCase)
    {
        DensityCompactUri,
        DensityComfortableUri,
    };

    /// <summary>Apply density-specific spacing overrides. Normal removes all density overrides.</summary>
    public void ApplyDensity(UiDensityMode density)
    {
        var mergedDicts = Application.Current.Resources.MergedDictionaries;
        for (int i = mergedDicts.Count - 1; i >= 0; i--)
        {
            if (mergedDicts[i].Source is { } src && DensityUris.Contains(src.OriginalString))
                mergedDicts.RemoveAt(i);
        }

        var uri = density switch
        {
            UiDensityMode.Compact     => DensityCompactUri,
            UiDensityMode.Comfortable => DensityComfortableUri,
            _                         => null,
        };

        if (uri is not null)
            mergedDicts.Add(new ResourceDictionary { Source = new Uri(uri, UriKind.Absolute) });
    }
}
