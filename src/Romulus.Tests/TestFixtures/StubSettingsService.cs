using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// Reusable settings stub for FeatureCommandService/MainViewModel tests.
/// </summary>
internal sealed class StubSettingsService : ISettingsService
{
    public string? LastAuditPath => null;
    public string LastTheme => "Dark";
    public SettingsDto? Load() => new();
    public void LoadInto(MainViewModel vm) { }
    public bool SaveFrom(MainViewModel vm, string? lastAuditPath = null) => true;
}
