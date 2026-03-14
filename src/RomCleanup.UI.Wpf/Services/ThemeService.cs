using System.Windows;
using System.Windows.Media;

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

    private AppTheme _current = AppTheme.Dark;
    public AppTheme Current => _current;
    public bool IsDark => _current == AppTheme.Dark;

    /// <summary>Apply the specified theme to the application.</summary>
    public void ApplyTheme(AppTheme theme)
    {
        _current = theme;

        var dict = theme == AppTheme.HighContrast
            ? BuildHighContrastDictionary()
            : new ResourceDictionary { Source = new Uri(theme == AppTheme.Dark ? DarkThemeUri : LightThemeUri, UriKind.Absolute) };

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
            return s.Contains("SynthwaveDark") || s.Contains("Light");
        }
        // Programmatic HC dictionary has a sentinel key
        return rd.Contains("__HighContrastTheme");
    }

    /// <summary>Build a WCAG AAA high-contrast ResourceDictionary programmatically.</summary>
    private static ResourceDictionary BuildHighContrastDictionary()
    {
        var d = new ResourceDictionary();

        // Sentinel marker for identification
        d["__HighContrastTheme"] = true;

        // ═══ COLOR PALETTE (WCAG AAA: ≥7:1 contrast) ═══
        Brush(d, "BrushBackground",   "#000000");
        Brush(d, "BrushSurface",      "#1A1A1A");
        Brush(d, "BrushSurfaceAlt",   "#0D0D0D");
        Brush(d, "BrushSurfaceLight", "#2A2A2A");
        Brush(d, "BrushAccentCyan",   "#FFFF00"); // Yellow – classic HC accent
        Brush(d, "BrushAccentPurple", "#FF00FF"); // Magenta
        Brush(d, "BrushDanger",       "#FF3333"); // Bright red
        Brush(d, "BrushSuccess",      "#33FF33"); // Bright green
        Brush(d, "BrushWarning",      "#FFCC00"); // Amber
        Brush(d, "BrushTextPrimary",  "#FFFFFF");
        Brush(d, "BrushTextMuted",    "#CCCCCC");
        Brush(d, "BrushBorder",       "#FFFFFF");

        // Semantic backgrounds
        Brush(d, "BrushDangerBg",  "#330000");
        Brush(d, "BrushSuccessBg", "#003300");
        Brush(d, "BrushInfoBg",    "#000033");

        // Interaction brushes
        Brush(d, "BrushButtonPressed",  "#FFFF0044");
        Brush(d, "BrushInputSelection", "#FFFF0066");
        Brush(d, "BrushBorderHover",    "#FFFF00");
        Brush(d, "BrushPrimaryBg",      "#FFFF00");
        Brush(d, "BrushTextOnAccent",   "#000000");
        Brush(d, "BrushPrimaryHover",   "#CCCC00");
        Brush(d, "BrushPrimaryActive",  "#999900");
        Brush(d, "BrushDangerHover",    "#FF333333");

        // ═══ SPACING (identical to other themes) ═══
        d["SpaceXS"]  = 4.0;
        d["SpaceSM"]  = 8.0;
        d["SpaceMD"]  = 12.0;
        d["SpaceLG"]  = 16.0;
        d["SpaceXL"]  = 24.0;
        d["SpaceXXL"] = 32.0;
        d["SpaceBottomXS"]  = new Thickness(0, 0, 0, 4);
        d["SpaceBottomSM"]  = new Thickness(0, 0, 0, 8);
        d["SpaceBottomMD"]  = new Thickness(0, 0, 0, 12);
        d["SpaceBottomLG"]  = new Thickness(0, 0, 0, 16);
        d["SpaceBottomXL"]  = new Thickness(0, 0, 0, 24);
        d["PaddingBar"]     = new Thickness(12, 8, 12, 8);
        d["PaddingSection"] = new Thickness(16);
        d["SpaceRightSM"]   = new Thickness(0, 0, 8, 0);
        d["SpaceRightMD"]   = new Thickness(0, 0, 12, 0);
        d["SpaceDotInline"] = new Thickness(4, 0, 4, 0);
        d["SpaceDivider"]   = new Thickness(8, 0, 8, 0);

        return d;
    }

    private static void Brush(ResourceDictionary d, string key, string color)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
        brush.Freeze();
        d[key] = brush;
    }
}
