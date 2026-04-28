using Avalonia;
using Avalonia.Styling;

namespace Romulus.UI.Avalonia.Services;

public sealed class AvaloniaApplicationThemeHost : IAvaloniaThemeHost
{
    public AvaloniaThemeKind CurrentTheme
    {
        get => FromThemeVariant(Application.Current?.RequestedThemeVariant);
        set
        {
            if (Application.Current is null)
                return;

            Application.Current.RequestedThemeVariant = ToThemeVariant(value);
        }
    }

    private static AvaloniaThemeKind FromThemeVariant(ThemeVariant? variant)
    {
        if (variant == ThemeVariant.Dark)
            return AvaloniaThemeKind.Dark;

        if (variant == ThemeVariant.Light)
            return AvaloniaThemeKind.Light;

        return AvaloniaThemeKind.System;
    }

    private static ThemeVariant ToThemeVariant(AvaloniaThemeKind theme)
        => theme switch
        {
            AvaloniaThemeKind.Dark => ThemeVariant.Dark,
            AvaloniaThemeKind.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default,
        };
}
