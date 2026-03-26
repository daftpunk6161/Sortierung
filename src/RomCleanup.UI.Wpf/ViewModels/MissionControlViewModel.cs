using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// ViewModel for the MissionControl (Start/Dashboard) area.
/// Phase 3 Task 3.2: Provides computed dashboard properties grouped by
/// Sources, Intent, Health, and LastRun as defined in the redesign spec.
/// Currently additive — StartView still binds to MainViewModel directly.
/// </summary>
public sealed partial class MissionControlViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public MissionControlViewModel(ILocalizationService loc)
    {
        _loc = loc;
    }

    // ═══ SOURCES ════════════════════════════════════════════════════════
    private int _sourceCount;
    /// <summary>Number of configured ROM source roots.</summary>
    public int SourceCount
    {
        get => _sourceCount;
        set
        {
            if (SetProperty(ref _sourceCount, value))
            {
                OnPropertyChanged(nameof(HasSources));
                OnPropertyChanged(nameof(SourcesSummary));
            }
        }
    }

    /// <summary>True when at least one root is configured.</summary>
    public bool HasSources => _sourceCount > 0;

    /// <summary>Human-readable summary, e.g. "3 Quellen konfiguriert".</summary>
    public string SourcesSummary => _sourceCount switch
    {
        0 => _loc["MissionControl.NoSources"],
        1 => _loc["MissionControl.OneSource"],
        _ => string.Format(_loc["MissionControl.NSources"], _sourceCount)
    };

    // ═══ INTENT ═════════════════════════════════════════════════════════
    private string? _activeIntent;
    /// <summary>Currently active preset key (SafeDryRun, FullSort, Convert).</summary>
    public string? ActiveIntent
    {
        get => _activeIntent;
        set
        {
            if (SetProperty(ref _activeIntent, value))
                OnPropertyChanged(nameof(ActiveIntentLabel));
        }
    }

    /// <summary>Localized label for the active intent.</summary>
    public string ActiveIntentLabel => _activeIntent switch
    {
        "SafeDryRun" => _loc["Start.IntentClean"],
        "FullSort" => _loc["Start.IntentSort"],
        "Convert" => _loc["Start.IntentConvert"],
        _ => "–"
    };

    // ═══ HEALTH ═════════════════════════════════════════════════════════
    private HealthLevel _healthStatus = HealthLevel.Unknown;
    /// <summary>Overall system health indicator for the dashboard.</summary>
    public HealthLevel HealthStatus
    {
        get => _healthStatus;
        set
        {
            if (SetProperty(ref _healthStatus, value))
                OnPropertyChanged(nameof(HealthIcon));
        }
    }

    private string _healthMessage = string.Empty;
    /// <summary>Human-readable health message.</summary>
    public string HealthMessage
    {
        get => _healthMessage;
        set => SetProperty(ref _healthMessage, value);
    }

    /// <summary>Segoe MDL2 icon glyph for the health status.</summary>
    public string HealthIcon => _healthStatus switch
    {
        HealthLevel.Good => "\uE930",     // CheckMark
        HealthLevel.Warning => "\uE7BA",  // Warning
        HealthLevel.Error => "\uEA39",    // ErrorBadge
        _ => "\uE946"                     // StatusCircleQuestionMark
    };

    // ═══ LAST RUN ═══════════════════════════════════════════════════════
    private bool _hasLastRun;
    /// <summary>True if a completed run result exists.</summary>
    public bool HasLastRun
    {
        get => _hasLastRun;
        set => SetProperty(ref _hasLastRun, value);
    }

    private string _lastRunWinners = "0";
    public string LastRunWinners { get => _lastRunWinners; set => SetProperty(ref _lastRunWinners, value); }

    private string _lastRunDupes = "0";
    public string LastRunDupes { get => _lastRunDupes; set => SetProperty(ref _lastRunDupes, value); }

    private string _lastRunJunk = "0";
    public string LastRunJunk { get => _lastRunJunk; set => SetProperty(ref _lastRunJunk, value); }

    private string _lastRunDuration = "00:00";
    public string LastRunDuration { get => _lastRunDuration; set => SetProperty(ref _lastRunDuration, value); }

    /// <summary>Formatted one-line summary of the last run.</summary>
    public string LastRunSummary =>
        _hasLastRun
            ? $"{LastRunWinners} Winners · {LastRunDupes} Dupes · {LastRunJunk} Junk · {LastRunDuration}"
            : string.Empty;

    /// <summary>
    /// Syncs dashboard counters from MainViewModel after a run completes.
    /// Called by the shell to keep MissionControl state consistent.
    /// </summary>
    public void UpdateLastRun(string winners, string dupes, string junk, string duration)
    {
        LastRunWinners = winners;
        LastRunDupes = dupes;
        LastRunJunk = junk;
        LastRunDuration = duration;
        HasLastRun = true;
        OnPropertyChanged(nameof(LastRunSummary));
    }

    /// <summary>Syncs source count from Roots collection change.</summary>
    public void UpdateSourceCount(int count) => SourceCount = count;

    /// <summary>
    /// Updates overall health from validation state.
    /// Call after settings change or root change.
    /// </summary>
    public void UpdateHealth(bool hasErrors, int errorCount)
    {
        if (!HasSources)
        {
            HealthStatus = HealthLevel.Warning;
            HealthMessage = _loc["MissionControl.NoSources"];
        }
        else if (hasErrors)
        {
            HealthStatus = HealthLevel.Error;
            HealthMessage = string.Format(_loc["MissionControl.ErrorCount"], errorCount);
        }
        else
        {
            HealthStatus = HealthLevel.Good;
            HealthMessage = _loc["MissionControl.AllGood"];
        }
    }
}

/// <summary>Health status levels for the MissionControl dashboard indicator.</summary>
public enum HealthLevel
{
    Unknown,
    Good,
    Warning,
    Error
}
