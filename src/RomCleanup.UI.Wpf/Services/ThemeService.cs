using System.Windows;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Theme switching service. Swaps MergedDictionaries between Dark/Light themes.
/// Port of WpfHost.ps1 Set-WpfThemeResourceDictionary + Initialize-DesignSystem.
/// </summary>
public sealed class ThemeService
{
    private const string DarkThemeUri = "pack://application:,,,/Themes/SynthwaveDark.xaml";
    private const string LightThemeUri = "pack://application:,,,/Themes/Light.xaml";

    private bool _isDark = true;
    public bool IsDark => _isDark;

    /// <summary>Apply dark or light theme to the application.</summary>
    public void ApplyTheme(bool dark)
    {
        _isDark = dark;
        var uri = dark ? DarkThemeUri : LightThemeUri;

        var dict = new ResourceDictionary
        {
            Source = new Uri(uri, UriKind.Absolute)
        };

        var mergedDicts = Application.Current.Resources.MergedDictionaries;
        // Remove existing theme dictionaries (keep only the new one)
        for (int i = mergedDicts.Count - 1; i >= 0; i--)
        {
            var existing = mergedDicts[i];
            if (existing.Source is not null &&
                (existing.Source.OriginalString.Contains("SynthwaveDark") ||
                 existing.Source.OriginalString.Contains("Light")))
            {
                mergedDicts.RemoveAt(i);
            }
        }

        mergedDicts.Add(dict);
    }

    /// <summary>Toggle between dark and light theme.</summary>
    public void Toggle() => ApplyTheme(!_isDark);
}
