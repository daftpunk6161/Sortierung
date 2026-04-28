namespace Romulus.UI.Avalonia.Services;

public sealed class AvaloniaThemeService
{
    private readonly IAvaloniaThemeHost _themeHost;

    public AvaloniaThemeService(IAvaloniaThemeHost themeHost)
    {
        _themeHost = themeHost ?? throw new ArgumentNullException(nameof(themeHost));
    }

    public AvaloniaThemeKind Current => _themeHost.CurrentTheme;

    public string CurrentLabel => Current switch
    {
        AvaloniaThemeKind.Dark => "Dunkel",
        AvaloniaThemeKind.Light => "Hell",
        _ => "System"
    };

    public void Apply(AvaloniaThemeKind theme)
        => _themeHost.CurrentTheme = theme;

    public void Toggle()
    {
        var next = Current switch
        {
            AvaloniaThemeKind.System => AvaloniaThemeKind.Dark,
            AvaloniaThemeKind.Dark => AvaloniaThemeKind.Light,
            _ => AvaloniaThemeKind.System,
        };

        Apply(next);
    }
}
