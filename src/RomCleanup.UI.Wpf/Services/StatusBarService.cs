using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Updates status indicators through ViewModel properties only (no direct UI access).
/// Port of WpfHost.ps1 Update-WpfStatusBar.
/// </summary>
public static class StatusBarService
{
    /// <summary>
    /// Refresh all status indicators by updating ViewModel properties.
    /// The XAML binds to these properties — no direct Ellipse manipulation needed.
    /// </summary>
    public static void Refresh(MainViewModel vm) => vm.RefreshStatus();
}
