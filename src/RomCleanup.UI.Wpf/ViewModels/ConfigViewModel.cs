using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// ViewModel for the Config area (Workflow/Filters/Profiles/Advanced sub-tabs).
/// Phase 3 Task 3.4: Groups config area state.
/// Currently additive — Config sub-views still bind to MainViewModel/SetupViewModel.
/// Settings properties remain on MainViewModel.Settings to avoid breaking save/load.
/// </summary>
public sealed partial class ConfigViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public ConfigViewModel(ILocalizationService loc)
    {
        _loc = loc;
    }

    // ═══ SUB-TAB STATE ══════════════════════════════════════════════════
    private string _selectedSubTab = "Workflow";
    /// <summary>Active Config sub-tab (Workflow, Filters, Profiles, Advanced).</summary>
    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set => SetProperty(ref _selectedSubTab, value);
    }

    // ═══ PROFILE STATE ══════════════════════════════════════════════════
    private string _profileName = "default";
    /// <summary>Current profile name for save/load operations.</summary>
    public string ProfileName
    {
        get => _profileName;
        set => SetProperty(ref _profileName, value);
    }

    private bool _isDirty;
    /// <summary>True when unsaved changes exist.</summary>
    public bool IsDirty
    {
        get => _isDirty;
        set
        {
            if (SetProperty(ref _isDirty, value))
                OnPropertyChanged(nameof(DirtyIndicator));
        }
    }

    /// <summary>Visual indicator text for unsaved state.</summary>
    public string DirtyIndicator => _isDirty ? "●" : string.Empty;

    // ═══ VALIDATION SUMMARY ═════════════════════════════════════════════
    private int _validationErrorCount;
    /// <summary>Number of validation errors in config area.</summary>
    public int ValidationErrorCount
    {
        get => _validationErrorCount;
        set
        {
            if (SetProperty(ref _validationErrorCount, value))
                OnPropertyChanged(nameof(HasValidationErrors));
        }
    }

    /// <summary>True when config has validation errors.</summary>
    public bool HasValidationErrors => _validationErrorCount > 0;

    /// <summary>Updates validation error count from MainViewModel validation state.</summary>
    public void UpdateValidation(int errorCount) => ValidationErrorCount = errorCount;

    /// <summary>Marks config as dirty after a settings change.</summary>
    public void MarkDirty() => IsDirty = true;

    /// <summary>Clears dirty flag after save.</summary>
    public void MarkClean() => IsDirty = false;
}
