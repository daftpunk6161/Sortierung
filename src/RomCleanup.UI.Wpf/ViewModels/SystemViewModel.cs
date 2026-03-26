using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-Phase3 Task 3.5: System area ViewModel for Activity Log, Appearance, About sub-views.
/// Current scope: owns system-area-specific state (log filter, about info).
/// Shared settings (LogLevel, Locale, TrashRoot etc.) remain on MainViewModel for now
/// to avoid breaking existing save/load and bindings. Those will migrate in a future phase.
/// </summary>
public sealed class SystemViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public SystemViewModel(ILocalizationService? loc = null)
    {
        _loc = loc ?? new LocalizationService();
    }

    // ═══ ACTIVITY LOG FILTER ════════════════════════════════════════════
    private string _logFilter = "";
    /// <summary>Free-text filter for the activity log.</summary>
    public string LogFilter
    {
        get => _logFilter;
        set => SetProperty(ref _logFilter, value);
    }

    private string _logLevelFilter = "ALL";
    /// <summary>Level filter for the activity log (ALL, INFO, WARN, ERROR, DEBUG).</summary>
    public string LogLevelFilter
    {
        get => _logLevelFilter;
        set => SetProperty(ref _logLevelFilter, value);
    }

    // ═══ ABOUT / HEALTH ═════════════════════════════════════════════════
    public string AppVersion => typeof(SystemViewModel).Assembly
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        is [System.Reflection.AssemblyInformationalVersionAttribute attr, ..]
            ? attr.InformationalVersion
            : "dev";

    public string RuntimeVersion => System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;

    public string SettingsPath
    {
        get
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return System.IO.Path.Combine(appData, RomCleanup.Contracts.AppIdentity.AppFolderName, "settings.json");
        }
    }

    /// <summary>Disk space on the system drive.</summary>
    public string DiskSpaceDisplay
    {
        get
        {
            try
            {
                var drive = new System.IO.DriveInfo(System.IO.Path.GetPathRoot(Environment.SystemDirectory)!);
                var freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                var totalGb = drive.TotalSize / (1024.0 * 1024 * 1024);
                return $"{freeGb:F1} GB free / {totalGb:F0} GB total";
            }
            catch
            {
                return "–";
            }
        }
    }
}
