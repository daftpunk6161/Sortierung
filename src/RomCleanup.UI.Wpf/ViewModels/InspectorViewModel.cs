using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.Contracts.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-Phase3 Task 3.6: Context-dependent Inspector ViewModel for the Context Wing.
/// Adapts displayed details based on the currently active navigation section
/// and the selected item (e.g., a specific ROM file in the Decisions tree).
/// </summary>
public sealed class InspectorViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public InspectorViewModel(ILocalizationService? loc = null)
    {
        _loc = loc ?? new LocalizationService();
    }

    // ═══ SELECTED ITEM (Library → Decisions tree selection) ═════════════
    private RomCandidate? _selectedCandidate;
    public RomCandidate? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            if (SetProperty(ref _selectedCandidate, value))
            {
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(FileName));
                OnPropertyChanged(nameof(ConsoleKey));
                OnPropertyChanged(nameof(RegionDisplay));
                OnPropertyChanged(nameof(RegionScore));
                OnPropertyChanged(nameof(FormatScore));
                OnPropertyChanged(nameof(VersionScore));
                OnPropertyChanged(nameof(TotalScore));
                OnPropertyChanged(nameof(FileSizeDisplay));
                OnPropertyChanged(nameof(DatMatch));
                OnPropertyChanged(nameof(CrcDisplay));
            }
        }
    }

    public bool HasSelection => _selectedCandidate is not null;

    // ═══ COMPUTED ITEM PROPERTIES ═══════════════════════════════════════
    public string FileName => _selectedCandidate is not null
        ? System.IO.Path.GetFileName(_selectedCandidate.MainPath)
        : "–";

    public string ConsoleKey => _selectedCandidate?.ConsoleKey ?? "–";
    public string RegionDisplay => _selectedCandidate?.Region ?? "–";
    public string RegionScore => _selectedCandidate?.RegionScore.ToString() ?? "–";
    public string FormatScore => _selectedCandidate?.FormatScore.ToString() ?? "–";
    public string VersionScore => _selectedCandidate?.VersionScore.ToString() ?? "–";

    public string TotalScore
    {
        get
        {
            if (_selectedCandidate is null) return "–";
            return (_selectedCandidate.RegionScore
                  + _selectedCandidate.FormatScore
                  + _selectedCandidate.VersionScore).ToString();
        }
    }

    public string FileSizeDisplay
    {
        get
        {
            if (_selectedCandidate is null) return "–";
            var bytes = _selectedCandidate.SizeBytes;
            return bytes switch
            {
                < 1024 => $"{bytes} B",
                < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
                < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):F1} MB",
                _ => $"{bytes / (1024.0 * 1024 * 1024):F2} GB"
            };
        }
    }

    public string DatMatch => _selectedCandidate?.DatMatch == true ? "✓ DAT Hit" : "–";
    public string CrcDisplay => "–"; // CRC not stored on RomCandidate — requires DAT lookup

    // ═══ SELECTED DEDUPE GROUP ══════════════════════════════════════════
    private DedupeGroup? _selectedGroup;
    public DedupeGroup? SelectedGroup
    {
        get => _selectedGroup;
        set
        {
            if (SetProperty(ref _selectedGroup, value))
            {
                OnPropertyChanged(nameof(HasGroupSelection));
                OnPropertyChanged(nameof(GroupGameKey));
                OnPropertyChanged(nameof(GroupMemberCount));
                OnPropertyChanged(nameof(GroupWinnerName));
            }
        }
    }

    public bool HasGroupSelection => _selectedGroup is not null;
    public string GroupGameKey => _selectedGroup?.GameKey ?? "–";

    public string GroupMemberCount =>
        _selectedGroup is not null
            ? (1 + _selectedGroup.Losers.Count).ToString() // Winner + Losers
            : "–";

    public string GroupWinnerName =>
        _selectedGroup?.Winner is not null
            ? System.IO.Path.GetFileName(_selectedGroup.Winner.MainPath)
            : "–";
}
