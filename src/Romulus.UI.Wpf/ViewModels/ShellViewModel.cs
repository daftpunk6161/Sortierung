using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// GUI-021 Phase 3.1: Shell state — navigation, overlays, wizard, notifications.
/// Extracted from MainViewModel to keep shell concerns isolated.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
    private const string MissionControlTag = "MissionControl";
    private const string LibraryTag = "Library";
    private const string ConfigTag = "Config";
    private const string ToolsTag = "Tools";
    private const string SystemTag = "System";

    private readonly ILocalizationService _loc;
    private readonly Action? _commandRequery;

    public ShellViewModel(ILocalizationService loc, Action? commandRequery = null)
    {
        _loc = loc;
        _commandRequery = commandRequery;

        // Navigation commands
        NavBackCommand = new RelayCommand(NavGoBack, () => CanNavBack);
        NavForwardCommand = new RelayCommand(NavGoForward, () => CanNavForward);

        // Overlay toggle commands
        ToggleContextWingCommand = new RelayCommand(() => ShowContextWing = !ShowContextWing);
        ToggleShortcutSheetCommand = new RelayCommand(() => ShowShortcutSheet = !ShowShortcutSheet);
        ToggleDetailDrawerCommand = new RelayCommand(() => ShowDetailDrawer = !ShowDetailDrawer);

        // Wizard commands
        WizardNextCommand = new RelayCommand(WizardNext);
        WizardBackCommand = new RelayCommand(WizardBack, () => WizardStep > 0);
        WizardSkipCommand = new RelayCommand(WizardSkip);
    }

    // ═══ NAVIGATION (GUI-061 → Phase 1: 5-area shell) ═══════════════════
    private int _selectedNavIndex;
    public int SelectedNavIndex
    {
        get => _selectedNavIndex;
        set
        {
            var coercedIndex = CoerceNavIndex(value);
            if (SetProperty(ref _selectedNavIndex, coercedIndex))
            {
                OnPropertyChanged(nameof(SelectedNavTag));
                OnPropertyChanged(nameof(CanNavBack));
                OnPropertyChanged(nameof(CanNavForward));
                ApplyDefaultSubTab();
            }
        }
    }

    public string SelectedNavTag
    {
        get => _selectedNavIndex switch
        {
            0 => MissionControlTag,
            1 => LibraryTag,
            2 => ConfigTag,
            3 => ToolsTag,
            4 => SystemTag,
            _ => MissionControlTag
        };
        set
        {
            int idx = NormalizeNavTag(value) switch
            {
                MissionControlTag => 0,
                LibraryTag => 1,
                ConfigTag => 2,
                ToolsTag => 3,
                SystemTag => 4,
                _ => 0
            };
            SelectedNavIndex = idx;
        }
    }

    // ═══ SUB-TAB NAVIGATION ═════════════════════════════════════════════
    private string _selectedSubTab = "Dashboard";
    public string SelectedSubTab
    {
        get => _selectedSubTab;
        set
        {
            if (SetProperty(ref _selectedSubTab, CoerceSubTab(SelectedNavTag, value)))
                NotifyWorkspaceProjectionChanged();
        }
    }

    private void ApplyDefaultSubTab()
    {
        SelectedSubTab = GetDefaultSubTab(SelectedNavTag);
        NotifyWorkspaceProjectionChanged();
    }

    // ═══ UI MODES ══════════════════════════════════════════════════════
    private bool _isSimpleMode = true;
    public bool IsSimpleMode
    {
        get => _isSimpleMode;
        set
        {
            if (SetProperty(ref _isSimpleMode, value))
            {
                OnPropertyChanged(nameof(IsExpertMode));
                NotifyNavigationVisibilityChanged();
                CoerceNavigationForMode();
            }
        }
    }

    public bool IsExpertMode => !IsSimpleMode;

    public bool ShowMissionControlNav => true;
    public bool ShowLibraryNav => true;
    public bool ShowConfigNav => true;
    public bool ShowToolsNav => true;
    public bool ShowSystemNav => true;

    public bool ShowMissionDashboardTab => true;
    public bool ShowMissionQuickStartTab => false;
    public bool ShowMissionRecentRunsTab => true;

    public bool ShowLibraryResultsTab => true;
    public bool ShowLibraryDecisionsTab => !IsSimpleMode;
    public bool ShowLibrarySafetyTab => true;
    public bool ShowLibraryReportTab => false;
    public bool ShowLibraryDatAuditTab => !IsSimpleMode;

    public bool ShowConfigRegionsTab => true;
    public bool ShowConfigFilteringTab => false;
    public bool ShowConfigOptionsTab => true;
    public bool ShowConfigProfilesTab => !IsSimpleMode;

    public bool ShowToolsExternalToolsTab => true;
    public bool ShowToolsFeaturesTab => true;
    public bool ShowToolsDatManagementTab => true;
    public bool ShowToolsConversionTab => false;
    public bool ShowToolsGameKeyLabTab => false;

    public bool ShowSystemActivityLogTab => !IsSimpleMode;
    public bool ShowSystemAppearanceTab => true;
    public bool ShowSystemAboutTab => true;

    public string CurrentWorkspaceTitle => NormalizeNavTag(SelectedNavTag) switch
    {
        MissionControlTag => "Mission Control",
        LibraryTag => "Library",
        ConfigTag => "Konfiguration",
        ToolsTag => "Werkzeugkatalog",
        SystemTag => "System",
        _ => "Mission Control"
    };

    public string CurrentWorkspaceSection => SelectedSubTab switch
    {
        "Dashboard" => "Dashboard",
        "QuickStart" => "Quick Start",
        "RecentRuns" => "Recent Runs",
        "Results" => "Ergebnisse",
        "Decisions" => "Entscheidungen",
        "Safety" => "Safety Review",
        "DatAudit" => "DAT Audit",
        "Regions" => "Regionen",
        "Options" => "Optionen",
        "Profiles" => "Profile",
        "ExternalTools" => "Externe Tools",
        "Features" => "Werkzeuge & Features",
        "DatManagement" => "DAT-Verwaltung",
        "ActivityLog" => "Aktivitaet",
        "Appearance" => "Darstellung",
        "About" => "Info",
        _ => "Uebersicht"
    };

    public string CurrentWorkspaceBreadcrumb => $"{CurrentWorkspaceTitle} / {CurrentWorkspaceSection}";

    private static string NormalizeNavTag(string? tag) => tag switch
    {
        MissionControlTag or "Start" => MissionControlTag,
        LibraryTag or "Analyse" => LibraryTag,
        ConfigTag or "Setup" => ConfigTag,
        ToolsTag => ToolsTag,
        SystemTag or "Log" => SystemTag,
        _ => MissionControlTag
    };

    private int CoerceNavIndex(int requestedIndex)
    {
        if (!IsSimpleMode)
            return requestedIndex is >= 0 and <= 4 ? requestedIndex : 0;

        return requestedIndex is >= 0 and <= 4 ? requestedIndex : 0;
    }

    private string GetDefaultSubTab(string navTag) => NormalizeNavTag(navTag) switch
    {
        MissionControlTag => "Dashboard",
        LibraryTag => "Results",
        ConfigTag => "Regions",
        ToolsTag => "Features",
        SystemTag => IsSimpleMode ? "Appearance" : "ActivityLog",
        _ => "Dashboard"
    };

    private string CoerceSubTab(string navTag, string? subTab)
    {
        var normalizedNavTag = NormalizeNavTag(navTag);
        var requestedSubTab = subTab ?? string.Empty;
        if (IsSubTabAllowed(normalizedNavTag, requestedSubTab))
            return requestedSubTab;

        return GetDefaultSubTab(normalizedNavTag);
    }

    private bool IsSubTabAllowed(string navTag, string subTab) => NormalizeNavTag(navTag) switch
    {
        MissionControlTag => subTab is "Dashboard" or "RecentRuns",
        LibraryTag => !IsSimpleMode
            ? subTab is "Results" or "Decisions" or "Safety" or "DatAudit"
            : subTab is "Results" or "Safety",
        ConfigTag => !IsSimpleMode
            ? subTab is "Regions" or "Options" or "Profiles"
            : subTab is "Regions" or "Options",
        ToolsTag => !IsSimpleMode
            ? subTab is "Features" or "ExternalTools" or "DatManagement"
            : subTab is "Features" or "ExternalTools" or "DatManagement",
        SystemTag => !IsSimpleMode
            ? subTab is "ActivityLog" or "Appearance" or "About"
            : subTab is "Appearance" or "About",
        _ => false
    };

    private void CoerceNavigationForMode()
    {
        var currentNavTag = NormalizeNavTag(SelectedNavTag);
        var coercedNavTag = currentNavTag;

        if (!string.Equals(coercedNavTag, currentNavTag, StringComparison.Ordinal))
        {
            SelectedNavTag = coercedNavTag;
            return;
        }

        var coercedSubTab = CoerceSubTab(coercedNavTag, SelectedSubTab);
        if (!string.Equals(coercedSubTab, SelectedSubTab, StringComparison.Ordinal))
            SelectedSubTab = coercedSubTab;
    }

    private void NotifyNavigationVisibilityChanged()
    {
        OnPropertyChanged(nameof(ShowMissionControlNav));
        OnPropertyChanged(nameof(ShowLibraryNav));
        OnPropertyChanged(nameof(ShowConfigNav));
        OnPropertyChanged(nameof(ShowToolsNav));
        OnPropertyChanged(nameof(ShowSystemNav));

        OnPropertyChanged(nameof(ShowMissionDashboardTab));
        OnPropertyChanged(nameof(ShowMissionQuickStartTab));
        OnPropertyChanged(nameof(ShowMissionRecentRunsTab));

        OnPropertyChanged(nameof(ShowLibraryResultsTab));
        OnPropertyChanged(nameof(ShowLibraryDecisionsTab));
        OnPropertyChanged(nameof(ShowLibrarySafetyTab));
        OnPropertyChanged(nameof(ShowLibraryReportTab));
        OnPropertyChanged(nameof(ShowLibraryDatAuditTab));

        OnPropertyChanged(nameof(ShowConfigRegionsTab));
        OnPropertyChanged(nameof(ShowConfigFilteringTab));
        OnPropertyChanged(nameof(ShowConfigOptionsTab));
        OnPropertyChanged(nameof(ShowConfigProfilesTab));

        OnPropertyChanged(nameof(ShowToolsExternalToolsTab));
        OnPropertyChanged(nameof(ShowToolsFeaturesTab));
        OnPropertyChanged(nameof(ShowToolsDatManagementTab));
        OnPropertyChanged(nameof(ShowToolsConversionTab));
        OnPropertyChanged(nameof(ShowToolsGameKeyLabTab));

        OnPropertyChanged(nameof(ShowSystemActivityLogTab));
        OnPropertyChanged(nameof(ShowSystemAppearanceTab));
        OnPropertyChanged(nameof(ShowSystemAboutTab));
        NotifyWorkspaceProjectionChanged();
    }

    private void NotifyWorkspaceProjectionChanged()
    {
        OnPropertyChanged(nameof(CurrentWorkspaceTitle));
        OnPropertyChanged(nameof(CurrentWorkspaceSection));
        OnPropertyChanged(nameof(CurrentWorkspaceBreadcrumb));
    }

    // ═══ CONTEXT WING (Inspector) ═══════════════════════════════════════
    private bool _showContextWing;
    public bool ShowContextWing
    {
        get => _showContextWing;
        set
        {
            if (SetProperty(ref _showContextWing, value))
                OnPropertyChanged(nameof(ContextToggleLabel));
        }
    }

    public string ContextToggleLabel => ShowContextWing ? "Inspector ausblenden" : "Inspector einblenden";

    // ═══ NAV COMPACT MODE (TASK-113: responsive breakpoint) ═════════════
    private bool _isCompactNav;
    public bool IsCompactNav
    {
        get => _isCompactNav;
        set => SetProperty(ref _isCompactNav, value);
    }

    // ═══ GUI-063: NAVIGATION HISTORY ════════════════════════════════════
    private readonly Stack<int> _navBack = new();
    private readonly Stack<int> _navForward = new();
    private bool _isNavigatingHistory;

    public bool CanNavBack => _navBack.Count > 0;
    public bool CanNavForward => _navForward.Count > 0;

    public void NavigateTo(string tag)
    {
        int newIndex = NormalizeNavTag(tag) switch
        {
            MissionControlTag => 0,
            LibraryTag => 1,
            ConfigTag => 2,
            ToolsTag => 3,
            SystemTag => 4,
            _ => 0
        };

        newIndex = CoerceNavIndex(newIndex);

        if (!_isNavigatingHistory && newIndex != _selectedNavIndex)
        {
            _navBack.Push(_selectedNavIndex);
            _navForward.Clear();
        }
        SelectedNavIndex = newIndex;
    }

    public void NavGoBack()
    {
        if (_navBack.Count == 0) return;
        _isNavigatingHistory = true;
        _navForward.Push(_selectedNavIndex);
        SelectedNavIndex = _navBack.Pop();
        _isNavigatingHistory = false;
    }

    public void NavGoForward()
    {
        if (_navForward.Count == 0) return;
        _isNavigatingHistory = true;
        _navBack.Push(_selectedNavIndex);
        SelectedNavIndex = _navForward.Pop();
        _isNavigatingHistory = false;
    }

    // ═══ FIRST-RUN WIZARD (GUI-081) ═════════════════════════════════════
    private bool _showFirstRunWizard;
    public bool ShowFirstRunWizard
    {
        get => _showFirstRunWizard;
        set => SetProperty(ref _showFirstRunWizard, value);
    }

    private int _wizardStep;
    public int WizardStep
    {
        get => _wizardStep;
        set
        {
            if (SetProperty(ref _wizardStep, value))
            {
                OnPropertyChanged(nameof(WizardStepIs0));
                OnPropertyChanged(nameof(WizardStepIs1));
                OnPropertyChanged(nameof(WizardStepIs2));
                OnPropertyChanged(nameof(WizardNextLabel));
                WizardBackCommand.NotifyCanExecuteChanged();
                WizardNextCommand.NotifyCanExecuteChanged();
            }
        }
    }

    public bool WizardStepIs0 => WizardStep == 0;
    public bool WizardStepIs1 => WizardStep == 1;
    public bool WizardStepIs2 => WizardStep == 2;

    private string _wizardRegionSummary = "–";
    /// <summary>Set by MainViewModel when region preferences change.</summary>
    public string WizardRegionSummary
    {
        get => _wizardRegionSummary;
        set => SetProperty(ref _wizardRegionSummary, value);
    }

    public string WizardNextLabel => WizardStep < 2 ? _loc["Wizard.Next"] : _loc["Wizard.Finish"];

    public RelayCommand WizardNextCommand { get; }
    public RelayCommand WizardBackCommand { get; }
    public RelayCommand WizardSkipCommand { get; }

    private void WizardNext()
    {
        if (WizardStep < 2)
        {
            WizardStep++;
        }
        else
        {
            ShowFirstRunWizard = false;
            WizardStep = 0;
        }
    }

    private void WizardBack()
    {
        if (WizardStep > 0) WizardStep--;
    }

    private void WizardSkip()
    {
        ShowFirstRunWizard = false;
        WizardStep = 0;
    }

    // ═══ GUI-101: SHORTCUT CHEATSHEET OVERLAY ═══════════════════════════
    private bool _showShortcutSheet;
    public bool ShowShortcutSheet
    {
        get => _showShortcutSheet;
        set => SetProperty(ref _showShortcutSheet, value);
    }

    private bool _showMoveInlineConfirm;
    public bool ShowMoveInlineConfirm
    {
        get => _showMoveInlineConfirm;
        set
        {
            if (SetProperty(ref _showMoveInlineConfirm, value))
                _commandRequery?.Invoke();
        }
    }

    // ═══ DETAIL DRAWER (Phase 4.4) ═════════════════════════════════════
    private bool _showDetailDrawer;
    public bool ShowDetailDrawer
    {
        get => _showDetailDrawer;
        set => SetProperty(ref _showDetailDrawer, value);
    }

    // ═══ OVERLAY TOGGLE COMMANDS ════════════════════════════════════════
    public RelayCommand ToggleContextWingCommand { get; }
    public RelayCommand ToggleShortcutSheetCommand { get; }
    public RelayCommand ToggleDetailDrawerCommand { get; }

    // ═══ NAVIGATION COMMANDS ════════════════════════════════════════════
    public IRelayCommand NavBackCommand { get; }
    public IRelayCommand NavForwardCommand { get; }

    // ═══ NOTIFICATIONS (GUI-055) ════════════════════════════════════════
    public ObservableCollection<NotificationItem> Notifications { get; } = [];

    public void ShowNotification(string message, string type = "Success", int autoCloseMs = 5000)
    {
        var item = new NotificationItem { Message = message, Type = type, AutoCloseMs = autoCloseMs };
        Notifications.Add(item);
        if (autoCloseMs > 0)
        {
            _ = Task.Delay(autoCloseMs).ContinueWith(_ =>
            {
                var d = System.Windows.Application.Current?.Dispatcher;
                d?.InvokeAsync(() => Notifications.Remove(item));
            });
        }
    }

    public void DismissNotification(NotificationItem item) => Notifications.Remove(item);

    // ═══ ACCESSIBILITY ══════════════════════════════════════════════════
    public bool ReduceMotion => !System.Windows.SystemParameters.ClientAreaAnimation;
}
