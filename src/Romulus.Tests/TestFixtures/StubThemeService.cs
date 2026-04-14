using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// Reusable theme stub for view-model and service unit tests.
/// </summary>
internal sealed class StubThemeService : IThemeService
{
    public AppTheme Current => AppTheme.Dark;
    public bool IsDark => true;
    public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
    public void ApplyTheme(AppTheme theme) { }
    public void ApplyTheme(bool dark) { }
    public void Toggle() { }
    public void ApplyDensity(UiDensityMode density) { }
}
