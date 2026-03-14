using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using ConflictPolicy = RomCleanup.UI.Wpf.Models.ConflictPolicy;
using RunState = RomCleanup.UI.Wpf.Models.RunState;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// Main ViewModel — clean MVVM with Commands and computed status indicators.
/// No direct UI element access. All data flows through bindings.
/// Partial class: core + Settings + Filters + RunPipeline.
/// </summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly IThemeService _theme;
    private readonly IDialogService _dialog;
    private readonly ISettingsService _settings;
    private readonly IRunService _runService;
    private readonly ILocalizationService _loc;
    private readonly SynchronizationContext? _syncContext;
    private readonly WatchService _watchService = new();
    private CancellationTokenSource? _cts;
    // V2-THR-H02: Lock for consistent CTS access between OnCancel and CreateRunCancellation
    private readonly object _ctsLock = new();

    // ═══ CHILD VIEWMODELS (GUI-021: shell ViewModel pattern) ════════════
    /// <summary>Configuration, paths, regions, filters, validation.</summary>
    public SetupViewModel Setup { get; }
    /// <summary>Tool catalog, quick access, search, categories.</summary>
    public ToolsViewModel Tools { get; }
    /// <summary>Run pipeline state, progress, dashboard, rollback.</summary>
    public RunViewModel Run { get; }

    public MainViewModel() : this(new ThemeService(), new WpfDialogService()) { }

    public MainViewModel(IThemeService theme, IDialogService dialog, ISettingsService? settings = null, IRunService? runService = null, ILocalizationService? loc = null)
    {
        _theme = theme;
        _dialog = dialog;
        _settings = settings ?? new SettingsService();
        _runService = runService ?? new RunService();
        _loc = loc ?? new LocalizationService();
        _syncContext = SynchronizationContext.Current;

        // ── Child ViewModels (GUI-021) ────────────────────────────────
        Setup = new SetupViewModel(_theme, _dialog, _settings);
        Tools = new ToolsViewModel();
        Run = new RunViewModel();

        // Wire child VM events
        Setup.StatusRefreshRequested += () => RefreshStatus();
        Run.CommandRequeryRequested += DeferCommandRequery;
        Run.RunRequested += (_, _) => OnRun();

        // Wire collection changes to status refresh
        Roots.CollectionChanged += OnRootsChanged;

        // ── Commands (CommunityToolkit.Mvvm.Input) ────────────────────
        RunCommand = new RelayCommand(OnRun, () => !IsBusy && Roots.Count > 0);
        CancelCommand = new RelayCommand(OnCancel, () => IsBusy);
        RollbackCommand = new AsyncRelayCommand(OnRollbackAsync, () => !IsBusy && CanRollback);
        AddRootCommand = new RelayCommand(OnAddRoot, () => !IsBusy);
        RemoveRootCommand = new RelayCommand(OnRemoveRoot, () => !IsBusy && SelectedRoot is not null);
        OpenReportCommand = new RelayCommand(OnOpenReport, () => !string.IsNullOrEmpty(LastReportPath));
        ClearLogCommand = new RelayCommand(() => LogEntries.Clear());
        ThemeToggleCommand = new RelayCommand(OnThemeToggle);
        GameKeyPreviewCommand = new RelayCommand(OnGameKeyPreview, () => !string.IsNullOrWhiteSpace(GameKeyPreviewInput));

        // Presets
        PresetSafeDryRunCommand = new RelayCommand(OnPresetSafeDryRun);
        PresetFullSortCommand = new RelayCommand(OnPresetFullSort);
        PresetConvertCommand = new RelayCommand(OnPresetConvert);

        // Browse commands (parameter = property name to set)
        BrowseToolPathCommand = new RelayCommand<string>(OnBrowseToolPath);
        BrowseFolderPathCommand = new RelayCommand<string>(OnBrowseFolderPath);

        // Settings commands
        SaveSettingsCommand = new RelayCommand(OnSaveSettings);
        LoadSettingsCommand = new RelayCommand(OnLoadSettings);
        WatchApplyCommand = new RelayCommand(ToggleWatchMode, () => !IsBusy);

        // Quick workflow commands
        QuickPreviewCommand = new RelayCommand(
            () => { DryRun = true; RunCommand.Execute(null); },
            () => Roots.Count > 0 && !IsBusy);
        StartMoveCommand = new RelayCommand(
            () => { DryRun = false; RunCommand.Execute(null); },
            () => Roots.Count > 0 && !IsBusy);

        // Extension filter collection (UX-004)
        InitExtensionFilters();

        // Console filter collection (Runde 7: replaces 30 x:Name checkboxes)
        InitConsoleFilters();

        // Tool items collection (RD-004: Werkzeuge tab)
        InitToolItems();

        // Feature commands (TASK-111: replaces Click event handlers)
        InitFeatureCommands();

        // Wire watch-mode auto-run trigger
        _watchService.RunTriggered += OnWatchRunTriggered;
        _watchService.WatcherError += msg => AddLog(msg, "WARN");
    }

    // ═══ LOCALIZATION (GUI-047) ═════════════════════════════════════════
    /// <summary>XAML-bindable localization: {Binding Loc[Key]}.</summary>
    public ILocalizationService Loc => _loc;

    // ═══ COMMANDS ═══════════════════════════════════════════════════════
    public IRelayCommand RunCommand { get; }
    public IRelayCommand CancelCommand { get; }
    public IAsyncRelayCommand RollbackCommand { get; }
    public IRelayCommand AddRootCommand { get; }
    public IRelayCommand RemoveRootCommand { get; }
    public IRelayCommand OpenReportCommand { get; }
    public IRelayCommand ClearLogCommand { get; }
    public IRelayCommand ThemeToggleCommand { get; }
    public IRelayCommand GameKeyPreviewCommand { get; }
    public IRelayCommand PresetSafeDryRunCommand { get; }
    public IRelayCommand PresetFullSortCommand { get; }
    public IRelayCommand PresetConvertCommand { get; }
    public IRelayCommand BrowseToolPathCommand { get; }
    public IRelayCommand BrowseFolderPathCommand { get; }
    public IRelayCommand QuickPreviewCommand { get; }
    public IRelayCommand StartMoveCommand { get; }
    public IRelayCommand SaveSettingsCommand { get; }
    public IRelayCommand LoadSettingsCommand { get; }
    public IRelayCommand WatchApplyCommand { get; }

    // ═══ FEATURE COMMANDS (TASK-111: replaces Click event handlers) ═══════
    public Dictionary<string, ICommand> FeatureCommands { get; } = new();

    private void InitFeatureCommands()
    {
        // All feature commands are registered by FeatureCommandService.RegisterCommands()
    }

    // ═══ COLLECTIONS ════════════════════════════════════════════════════
    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<DatMapRow> DatMappings { get; } = [];
    public ObservableCollection<UiError> ErrorSummaryItems { get; } = [];

    /// <summary>Assigns FeatureCommands to matching ToolItems. Call after FeatureCommandService.RegisterCommands().</summary>
    public void WireToolItemCommands()
    {
        // Legacy: wire to old MainViewModel.ToolItems (until fully migrated)
        foreach (var item in ToolItems)
        {
            if (FeatureCommands.TryGetValue(item.Key, out var cmd))
                item.Command = cmd;
        }

        // GUI-021: Also wire to child ToolsViewModel
        foreach (var kvp in FeatureCommands)
            Tools.FeatureCommands[kvp.Key] = kvp.Value;
        Tools.WireToolItemCommands();
    }

    /// <summary>Add a log entry (thread-safe via Dispatcher if needed). Caps at 10,000 entries.</summary>
    public void AddLog(string text, string level = "INFO")
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            // V2-M03: No WPF Application (unit tests or post-shutdown) — write directly
            // only if called from the thread that created the VM; discard otherwise.
            if (_syncContext is null || SynchronizationContext.Current == _syncContext)
                AddLogCore(text, level);
            else
                System.Diagnostics.Debug.WriteLine($"[RomCleanup] Log discarded (no Dispatcher): [{level}] {text}");
            return;
        }
        if (dispatcher.CheckAccess())
        {
            AddLogCore(text, level);
        }
        else
        {
            dispatcher.InvokeAsync(
                () => AddLogCore(text, level));
        }
    }

    private const int MaxLogEntries = 10_000;

    private void AddLogCore(string text, string level)
    {
        if (LogEntries.Count >= MaxLogEntries)
        {
            LogEntries.RemoveAt(0);
            // Keep _runLogStartIndex consistent after removing oldest entry
            if (_runLogStartIndex > 0)
                _runLogStartIndex--;
        }
        LogEntries.Add(new LogEntry(text, level));
    }

    // ═══ INPC INFRASTRUCTURE ════════════════════════════════════════════
    // Provided by ObservableObject base class (SetProperty, OnPropertyChanged).

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshStatus();
        DeferCommandRequery();
    }

    private bool _requeryScheduled;
    /// <summary>P1-008: Batches command CanExecute re-evaluation to one call per dispatcher cycle.</summary>
    private void DeferCommandRequery()
    {
        if (_requeryScheduled) return;
        _requeryScheduled = true;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            _requeryScheduled = false;
            NotifyAllCommands();
            return;
        }
        dispatcher.InvokeAsync(() =>
        {
            _requeryScheduled = false;
            NotifyAllCommands();
        }, System.Windows.Threading.DispatcherPriority.Background);
    }

    private void NotifyAllCommands()
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        RollbackCommand.NotifyCanExecuteChanged();
        AddRootCommand.NotifyCanExecuteChanged();
        RemoveRootCommand.NotifyCanExecuteChanged();
        OpenReportCommand.NotifyCanExecuteChanged();
        GameKeyPreviewCommand.NotifyCanExecuteChanged();
        WatchApplyCommand.NotifyCanExecuteChanged();
        QuickPreviewCommand.NotifyCanExecuteChanged();
        StartMoveCommand.NotifyCanExecuteChanged();
    }
}