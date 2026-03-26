using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-021 Phase 3.1: Shell state — navigation, overlays, wizard, notifications.
/// Extracted from MainViewModel to keep shell concerns isolated.
/// </summary>
public sealed class ShellViewModel : ObservableObject
{
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
            if (SetProperty(ref _selectedNavIndex, value))
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
            0 => "MissionControl",
            1 => "Library",
            2 => "Config",
            3 => "Tools",
            4 => "System",
            _ => "MissionControl"
        };
        set
        {
            int idx = value switch
            {
                "MissionControl" => 0,
                "Library" => 1,
                "Config" => 2,
                "Tools" => 3,
                "System" => 4,
                "Start" => 0,
                "Analyse" => 1,
                "Setup" => 2,
                "Log" => 4,
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
        set => SetProperty(ref _selectedSubTab, value);
    }

    private void ApplyDefaultSubTab()
    {
        SelectedSubTab = _selectedNavIndex switch
        {
            0 => "Dashboard",
            1 => "Results",
            2 => "Regions",
            3 => "ExternalTools",
            4 => "ActivityLog",
            _ => "Dashboard"
        };
    }

    // ═══ CONTEXT WING (Inspector) ═══════════════════════════════════════
    private bool _showContextWing = true;
    public bool ShowContextWing
    {
        get => _showContextWing;
        set => SetProperty(ref _showContextWing, value);
    }

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
        int newIndex = tag switch
        {
            "MissionControl" or "Start" => 0,
            "Library" or "Analyse" => 1,
            "Config" or "Setup" => 2,
            "Tools" => 3,
            "System" or "Log" => 4,
            _ => 0
        };

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
