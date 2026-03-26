using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Library area (Results + Report sub-tabs).
/// Phase 3 Task 3.3: Groups analysis state, sub-tab tracking, and filter state.
/// Currently additive — ResultView/LibraryReportView still bind to MainViewModel.
/// </summary>
public sealed partial class LibraryViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public LibraryViewModel(ILocalizationService loc)
    {
        _loc = loc;
    }

    // ═══ SUB-TAB STATE ══════════════════════════════════════════════════
    private string _selectedSubTab = "Results";
    /// <summary>Active Library sub-tab (Results, Report, Safety).</summary>
    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set => SetProperty(ref _selectedSubTab, value);
    }

    // ═══ ANALYSIS SUMMARY ═══════════════════════════════════════════════
    private bool _hasAnalysis;
    /// <summary>True when a completed run result exists for display.</summary>
    public bool HasAnalysis
    {
        get => _hasAnalysis;
        set
        {
            if (SetProperty(ref _hasAnalysis, value))
                OnPropertyChanged(nameof(AnalysisSummary));
        }
    }

    private int _totalGames;
    public int TotalGames { get => _totalGames; set => SetProperty(ref _totalGames, value); }

    private int _totalDupes;
    public int TotalDupes { get => _totalDupes; set => SetProperty(ref _totalDupes, value); }

    private int _totalJunk;
    public int TotalJunk { get => _totalJunk; set => SetProperty(ref _totalJunk, value); }

    /// <summary>One-line analysis summary for the Library header area.</summary>
    public string AnalysisSummary =>
        _hasAnalysis
            ? $"{_totalGames} Games · {_totalDupes} Dupes · {_totalJunk} Junk"
            : _loc["Library.NoAnalysis"];

    // ═══ FILTER STATE ═══════════════════════════════════════════════════
    private string _searchFilter = string.Empty;
    /// <summary>Text filter for the result tree/list.</summary>
    public string SearchFilter
    {
        get => _searchFilter;
        set => SetProperty(ref _searchFilter, value);
    }

    private string _consoleFilter = string.Empty;
    /// <summary>Console filter for the result display.</summary>
    public string ConsoleFilter
    {
        get => _consoleFilter;
        set => SetProperty(ref _consoleFilter, value);
    }

    /// <summary>
    /// Syncs analysis counters from a completed run.
    /// Called by the shell when run results arrive.
    /// </summary>
    public void UpdateAnalysis(int games, int dupes, int junk)
    {
        TotalGames = games;
        TotalDupes = dupes;
        TotalJunk = junk;
        HasAnalysis = true;
        OnPropertyChanged(nameof(AnalysisSummary));
    }
}
