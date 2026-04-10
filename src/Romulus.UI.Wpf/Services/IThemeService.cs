namespace Romulus.UI.Wpf.Services;

/// <summary>RF-008: Testable interface for theme switching.</summary>
public interface IThemeService
{
    AppTheme Current { get; }
    bool IsDark { get; }
    IReadOnlyList<AppTheme> AvailableThemes { get; }
    void ApplyTheme(AppTheme theme);
    void ApplyTheme(bool dark);
    void Toggle();
}
