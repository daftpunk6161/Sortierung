using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Collections;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
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
public sealed partial class MainViewModel : ObservableObject, INotifyDataErrorInfo
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
    /// <summary>Shell state: navigation, overlays, wizard, notifications.</summary>
    public ShellViewModel Shell { get; }
    /// <summary>Configuration, paths, regions, filters, validation.</summary>
    public SetupViewModel Setup { get; }
    /// <summary>Tool catalog, quick access, search, categories.</summary>
    public ToolsViewModel Tools { get; }
    /// <summary>Run pipeline state, progress, dashboard, rollback.</summary>
    public RunViewModel Run { get; }
    /// <summary>MissionControl dashboard: Sources, Intent, Health, LastRun.</summary>
    public MissionControlViewModel MissionControl { get; }
    /// <summary>Library area: Analysis state, sub-tabs, search/filter.</summary>
    public LibraryViewModel Library { get; }
    /// <summary>Config area: Profile state, validation summary, sub-tabs.</summary>
    public ConfigViewModel Config { get; }
    /// <summary>Context-dependent Inspector for the Context Wing.</summary>
    public InspectorViewModel Inspector { get; }
    /// <summary>System area: Activity Log, Appearance, About/Health.</summary>
    public SystemViewModel SystemArea { get; }
    /// <summary>Command Palette overlay (Ctrl+K): fuzzy search + execute.</summary>
    public CommandPaletteViewModel CommandPalette { get; }

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
        Shell = new ShellViewModel(_loc, DeferCommandRequery);
        Setup = new SetupViewModel(_theme, _dialog, _settings, _loc);
        Tools = new ToolsViewModel(_loc);
        Run = new RunViewModel(_loc);
        Inspector = new InspectorViewModel(_loc);
        MissionControl = new MissionControlViewModel(_loc);
        Library = new LibraryViewModel(_loc);
        Config = new ConfigViewModel(_loc);
        SystemArea = new SystemViewModel(_loc);
        CommandPalette = new CommandPaletteViewModel(_loc);

        // Wire child VM events
        Setup.StatusRefreshRequested += () => RefreshStatus();
        Run.CommandRequeryRequested += DeferCommandRequery;
        Run.RunRequested += (_, _) => OnRun();

        // Wire collection changes to status refresh
        Roots.CollectionChanged += OnRootsChanged;
        PropertyChanged += OnConfigurationPropertyChanged;

        // ── Commands (CommunityToolkit.Mvvm.Input) ────────────────────
        RunCommand = new RelayCommand(OnRun, () => CanStartCurrentRun);
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
            () => Roots.Count > 0 && !IsBusy && !HasBlockingValidationErrors);
        ConvertOnlyCommand = new RelayCommand(
            () => { ConvertOnly = true; DryRun = false; RunCommand.Execute(null); },
            () => Roots.Count > 0 && !IsBusy && !HasBlockingValidationErrors);

        // GUI-063: Navigation history commands (delegate to Shell)
        GoToSetupCommand = new RelayCommand(() => Shell.NavigateTo("Config"));
        GoToAnalyseCommand = new RelayCommand(() => Shell.NavigateTo("Library"));
        GoToConfigCommand = new RelayCommand(() => Shell.NavigateTo("Config"));
        GoToLibraryCommand = new RelayCommand(() => Shell.NavigateTo("Library"));
        GoToToolsCommand = new RelayCommand(() => Shell.NavigateTo("Tools"));

        // GUI-081: Wizard commands live on Shell

        // GUI-101: Shortcut cheatsheet toggle lives on Shell

        // GUI-Phase4 4.1: Command Palette toggle (Ctrl+K) — bridges Shell + CommandPalette
        ToggleCommandPaletteCommand = new RelayCommand(() => CommandPalette.IsOpen = !CommandPalette.IsOpen);

        // GUI-Phase4 4.4: Detail Drawer toggle lives on Shell

        // Inline confirm commands delegate to Shell.ShowMoveInlineConfirm
        RequestStartMoveCommand = new RelayCommand(
            () => Shell.ShowMoveInlineConfirm = true,
            () => ShowStartMoveButton && !Shell.ShowMoveInlineConfirm);
        CancelStartMoveCommand = new RelayCommand(
            () => Shell.ShowMoveInlineConfirm = false,
            () => Shell.ShowMoveInlineConfirm);
        StartMoveCommand = new RelayCommand(
            () =>
            {
                Shell.ShowMoveInlineConfirm = false;
                if (HasBlockingValidationErrors)
                {
                    var blockingValidationMessage = GetBlockingValidationMessage();
                    AddLog(blockingValidationMessage, "WARN");
                    _dialog.Info(blockingValidationMessage, "Start gesperrt");
                    return;
                }

                if (!CanStartMoveWithCurrentPreview)
                {
                    AddLog(MoveApplyGateText, "WARN");
                    _dialog.Info(MoveApplyGateText, "Move gesperrt");
                    return;
                }

                DryRun = false;
                RunCommand.Execute(null);
            },
            () => CanStartMoveWithCurrentPreview && !HasBlockingValidationErrors);

        // Extension filter collection (UX-004)
        InitExtensionFilters();
        WirePreviewGateObservers();

        // Console filter collection (Runde 7: replaces 30 x:Name checkboxes)
        InitConsoleFilters();

        // Tool items collection (RD-004: Werkzeuge tab)
        InitToolItems();

        // Feature commands (TASK-111: replaces Click event handlers)
        InitFeatureCommands();

        // Wire watch-mode auto-run trigger
        _watchService.RunTriggered += OnWatchRunTriggered;
        _watchService.WatcherError += OnWatcherError;
    }

    /// <summary>GUI-115: Named handler for proper unsubscription in CleanupWatchers.</summary>
    private void OnWatcherError(string msg) => AddLog(msg, "WARN");

    /// <summary>GUI-082: Auto-detect region from OS locale on wizard start.</summary>
    public void ApplyLocaleRegionDefaults()
    {
        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var region = culture.TwoLetterISOLanguageName.ToUpperInvariant() switch
        {
            "DE" or "FR" or "IT" or "ES" or "NL" or "PT" or "SV" or "DA" or "FI" or "NB" or "PL" => "EU",
            "EN" => culture.Name.Contains("GB", StringComparison.OrdinalIgnoreCase) ? "EU" : "US",
            "JA" => "JP",
            _ => "US"
        };
        if (region == "EU") PreferEU = true;
        else if (region == "US") PreferUS = true;
        else if (region == "JP") PreferJP = true;
    }

    /// <summary>Updates Shell.WizardRegionSummary from current region preferences.</summary>
    internal void UpdateWizardRegionSummary()
    {
        var parts = new List<string>();
        if (PreferEU) parts.Add("EU");
        if (PreferUS) parts.Add("US");
        if (PreferJP) parts.Add("JP");
        if (PreferWORLD) parts.Add("World");
        Shell.WizardRegionSummary = parts.Count > 0 ? string.Join(", ", parts) : "–";
    }

    public RelayCommand ToggleCommandPaletteCommand { get; private set; } = null!;

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
    public IRelayCommand ConvertOnlyCommand { get; }
    public IRelayCommand RequestStartMoveCommand { get; }
    public IRelayCommand CancelStartMoveCommand { get; }
    public IRelayCommand StartMoveCommand { get; }
    public IRelayCommand SaveSettingsCommand { get; }
    public IRelayCommand LoadSettingsCommand { get; }
    public IRelayCommand WatchApplyCommand { get; }

    // GUI-063: Navigation commands (delegate to Shell)
    public IRelayCommand GoToSetupCommand { get; }
    public IRelayCommand GoToAnalyseCommand { get; }
    public IRelayCommand GoToConfigCommand { get; }
    public IRelayCommand GoToLibraryCommand { get; }
    public IRelayCommand GoToToolsCommand { get; }

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
        // GUI-021: Tool catalog is owned by child ToolsViewModel.
        foreach (var kvp in FeatureCommands)
            Tools.FeatureCommands[kvp.Key] = kvp.Value;
        Tools.WireToolItemCommands();
    }

    /// <summary>Add a log entry (thread-safe via Dispatcher if needed). Caps at 10,000 entries with batch trimming.</summary>
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
    private const int LogTrimBatchSize = 256;

    private void AddLogCore(string text, string level)
    {
        if (LogEntries.Count >= MaxLogEntries)
        {
            var removeCount = Math.Min(LogTrimBatchSize, LogEntries.Count);
            for (var i = 0; i < removeCount; i++)
                LogEntries.RemoveAt(0);

            // Keep _runLogStartIndex consistent after trimming oldest entries
            _runLogStartIndex = Math.Max(0, _runLogStartIndex - removeCount);
        }
        LogEntries.Add(new LogEntry(text, level));
    }

    // ═══ INPC INFRASTRUCTURE ════════════════════════════════════════════
    // Provided by ObservableObject base class (SetProperty, OnPropertyChanged).

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshStatus();
        OnPropertyChanged(nameof(HasNoRoots));
        OnPropertyChanged(nameof(HasRootsConfigured));
        MissionControl.UpdateSourceCount(Roots.Count);
        DeferCommandRequery();
    }

    public bool HasRootsConfigured => Roots.Count > 0;

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
        ConvertOnlyCommand.NotifyCanExecuteChanged();
        RequestStartMoveCommand.NotifyCanExecuteChanged();
        CancelStartMoveCommand.NotifyCanExecuteChanged();
        StartMoveCommand.NotifyCanExecuteChanged();
    }

    // ═══ VALIDATION (moved from MainViewModel.Validation.cs) ═══════════
    private readonly Dictionary<string, ValidationIssue> _validationErrors = new();

    private enum ValidationSeverity
    {
        Warning,
        Blocker
    }

    private sealed record ValidationIssue(string Message, ValidationSeverity Severity);

    private readonly record struct ValidationSummary(IReadOnlyList<string> Blockers, IReadOnlyList<string> Warnings)
    {
        public int BlockerCount => Blockers.Count;
        public int WarningCount => Warnings.Count;
        public bool HasBlockers => BlockerCount > 0;
        public bool HasWarnings => WarningCount > 0;
    }

    public bool HasErrors => _validationErrors.Count > 0;
    public bool HasBlockingValidationErrors => _validationErrors.Values.Any(issue => issue.Severity == ValidationSeverity.Blocker);
    public bool HasValidationWarnings => _validationErrors.Values.Any(issue => issue.Severity == ValidationSeverity.Warning);

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is not null && _validationErrors.TryGetValue(propertyName, out var error))
            return new[] { error.Message };
        return Array.Empty<string>();
    }

    private void ValidateToolPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!TryNormalizePath(value, out _))
            SetError(propertyName, "Ungültiger Dateipfad", ValidationSeverity.Blocker);
        else if (!File.Exists(value))
            SetError(propertyName, $"Datei nicht gefunden: {Path.GetFileName(value)}", ValidationSeverity.Warning);
        else
            ClearError(propertyName);
    }

    private void ValidateDirectoryPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!TryNormalizePath(value, out _))
            SetError(propertyName, "Ungültiger Verzeichnispfad", ValidationSeverity.Blocker);
        else if (!Directory.Exists(value))
            SetError(propertyName, "Verzeichnis existiert nicht", ValidationSeverity.Warning);
        else
            ClearError(propertyName);
    }

    private ValidationSummary GetValidationSummary()
    {
        var blockers = _validationErrors.Values
            .Where(issue => issue.Severity == ValidationSeverity.Blocker)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var warnings = _validationErrors.Values
            .Where(issue => issue.Severity == ValidationSeverity.Warning)
            .Select(issue => issue.Message)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new ValidationSummary(blockers, warnings);
    }

    private string GetBlockingValidationMessage()
    {
        var summary = GetValidationSummary();
        if (!summary.HasBlockers)
            return "Start gesperrt: Konfiguration enthält blockierende Fehler.";

        var builder = new StringBuilder("Start gesperrt: Konfiguration enthält blockierende Fehler.");
        foreach (var blocker in summary.Blockers.Take(3))
        {
            builder.AppendLine();
            builder.Append("- ").Append(blocker);
        }

        if (summary.BlockerCount > 3)
        {
            builder.AppendLine();
            builder.Append($"- weitere {summary.BlockerCount - 3} Fehler");
        }

        return builder.ToString();
    }

    private static bool TryNormalizePath(string value, out string normalizedPath)
    {
        try
        {
            normalizedPath = Path.GetFullPath(value);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            normalizedPath = string.Empty;
            return false;
        }
    }

    private void SetError(string propertyName, string error, ValidationSeverity severity)
    {
        if (_validationErrors.TryGetValue(propertyName, out var existing)
            && existing.Message == error
            && existing.Severity == severity)
            return;

        _validationErrors[propertyName] = new ValidationIssue(error, severity);
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
        OnValidationStateChanged();
    }

    private void ClearError(string propertyName)
    {
        if (_validationErrors.Remove(propertyName))
        {
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
            OnValidationStateChanged();
        }
    }

    private void OnValidationStateChanged()
    {
        OnPropertyChanged(nameof(HasErrors));
        OnPropertyChanged(nameof(HasBlockingValidationErrors));
        OnPropertyChanged(nameof(HasValidationWarnings));
        RefreshStatus();
        DeferCommandRequery();
    }
}