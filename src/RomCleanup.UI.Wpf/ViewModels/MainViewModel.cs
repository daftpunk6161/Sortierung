using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// Main ViewModel — clean MVVM with Commands and computed status indicators.
/// No direct UI element access. All data flows through bindings.
/// </summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    private readonly ThemeService _theme;
    private CancellationTokenSource? _cts;

    public MainViewModel() : this(new ThemeService()) { }

    public MainViewModel(ThemeService theme)
    {
        _theme = theme;

        // Wire collection changes to status refresh
        Roots.CollectionChanged += OnRootsChanged;

        // ── Commands ────────────────────────────────────────────────────
        RunCommand = new RelayCommand(OnRun, () => !IsBusy && Roots.Count > 0);
        CancelCommand = new RelayCommand(OnCancel, () => IsBusy);
        RollbackCommand = new RelayCommand(OnRollback, () => !IsBusy && CanRollback);
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
    }

    // ═══ COMMANDS ═══════════════════════════════════════════════════════
    public ICommand RunCommand { get; }
    public ICommand CancelCommand { get; }
    public ICommand RollbackCommand { get; }
    public ICommand AddRootCommand { get; }
    public ICommand RemoveRootCommand { get; }
    public ICommand OpenReportCommand { get; }
    public ICommand ClearLogCommand { get; }
    public ICommand ThemeToggleCommand { get; }
    public ICommand GameKeyPreviewCommand { get; }
    public ICommand PresetSafeDryRunCommand { get; }
    public ICommand PresetFullSortCommand { get; }
    public ICommand PresetConvertCommand { get; }

    // ═══ COLLECTIONS ════════════════════════════════════════════════════
    public ObservableCollection<string> Roots { get; } = [];
    public ObservableCollection<LogEntry> LogEntries { get; } = [];
    public ObservableCollection<DatMapRow> DatMappings { get; } = [];

    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot { get => _trashRoot; set => SetField(ref _trashRoot, value); }

    private string _datRoot = "";
    public string DatRoot { get => _datRoot; set { if (SetField(ref _datRoot, value)) RefreshStatus(); } }

    private string _auditRoot = "";
    public string AuditRoot { get => _auditRoot; set => SetField(ref _auditRoot, value); }

    private string _ps3DupesRoot = "";
    public string Ps3DupesRoot { get => _ps3DupesRoot; set => SetField(ref _ps3DupesRoot, value); }

    // ═══ TOOL PATHS (persisted) ═════════════════════════════════════════
    private string _toolChdman = "";
    public string ToolChdman { get => _toolChdman; set { if (SetField(ref _toolChdman, value)) RefreshStatus(); } }

    private string _toolDolphin = "";
    public string ToolDolphin { get => _toolDolphin; set { if (SetField(ref _toolDolphin, value)) RefreshStatus(); } }

    private string _tool7z = "";
    public string Tool7z { get => _tool7z; set { if (SetField(ref _tool7z, value)) RefreshStatus(); } }

    private string _toolPsxtract = "";
    public string ToolPsxtract { get => _toolPsxtract; set => SetField(ref _toolPsxtract, value); }

    private string _toolCiso = "";
    public string ToolCiso { get => _toolCiso; set => SetField(ref _toolCiso, value); }

    // ═══ BOOLEAN FLAGS (persisted) ══════════════════════════════════════
    private bool _sortConsole = true;
    public bool SortConsole { get => _sortConsole; set => SetField(ref _sortConsole, value); }

    private bool _aliasKeying;
    public bool AliasKeying { get => _aliasKeying; set => SetField(ref _aliasKeying, value); }

    private bool _useDat;
    public bool UseDat { get => _useDat; set { if (SetField(ref _useDat, value)) RefreshStatus(); } }

    private bool _datFallback;
    public bool DatFallback { get => _datFallback; set => SetField(ref _datFallback, value); }

    private bool _dryRun = true;
    public bool DryRun { get => _dryRun; set => SetField(ref _dryRun, value); }

    private bool _convertEnabled;
    public bool ConvertEnabled { get => _convertEnabled; set { if (SetField(ref _convertEnabled, value)) RefreshStatus(); } }

    private bool _confirmMove;
    public bool ConfirmMove { get => _confirmMove; set => SetField(ref _confirmMove, value); }

    private bool _aggressiveJunk;
    public bool AggressiveJunk { get => _aggressiveJunk; set => SetField(ref _aggressiveJunk, value); }

    private bool _crcVerifyScan;
    public bool CrcVerifyScan { get => _crcVerifyScan; set => SetField(ref _crcVerifyScan, value); }

    private bool _crcVerifyDat;
    public bool CrcVerifyDat { get => _crcVerifyDat; set => SetField(ref _crcVerifyDat, value); }

    private bool _safetyStrict;
    public bool SafetyStrict { get => _safetyStrict; set => SetField(ref _safetyStrict, value); }

    private bool _safetyPrompts;
    public bool SafetyPrompts { get => _safetyPrompts; set => SetField(ref _safetyPrompts, value); }

    private bool _jpOnlySelected;
    public bool JpOnlySelected { get => _jpOnlySelected; set => SetField(ref _jpOnlySelected, value); }

    // ═══ STRING CONFIG (persisted) ══════════════════════════════════════
    private string _protectedPaths = "";
    public string ProtectedPaths { get => _protectedPaths; set => SetField(ref _protectedPaths, value); }

    private string _safetySandbox = "";
    public string SafetySandbox { get => _safetySandbox; set => SetField(ref _safetySandbox, value); }

    private string _jpKeepConsoles = "";
    public string JpKeepConsoles { get => _jpKeepConsoles; set => SetField(ref _jpKeepConsoles, value); }

    private string _logLevel = "Info";
    public string LogLevel { get => _logLevel; set => SetField(ref _logLevel, value); }

    private string _locale = "de";
    public string Locale { get => _locale; set => SetField(ref _locale, value); }

    private string _datHashType = "SHA1";
    public string DatHashType { get => _datHashType; set => SetField(ref _datHashType, value); }

    // ═══ REGION PREFERENCES (persisted) ═════════════════════════════════
    private bool _preferEU = true;
    public bool PreferEU { get => _preferEU; set => SetField(ref _preferEU, value); }

    private bool _preferUS = true;
    public bool PreferUS { get => _preferUS; set => SetField(ref _preferUS, value); }

    private bool _preferJP = true;
    public bool PreferJP { get => _preferJP; set => SetField(ref _preferJP, value); }

    private bool _preferWORLD = true;
    public bool PreferWORLD { get => _preferWORLD; set => SetField(ref _preferWORLD, value); }

    private bool _preferDE;
    public bool PreferDE { get => _preferDE; set => SetField(ref _preferDE, value); }

    private bool _preferFR;
    public bool PreferFR { get => _preferFR; set => SetField(ref _preferFR, value); }

    private bool _preferIT;
    public bool PreferIT { get => _preferIT; set => SetField(ref _preferIT, value); }

    private bool _preferES;
    public bool PreferES { get => _preferES; set => SetField(ref _preferES, value); }

    private bool _preferAU;
    public bool PreferAU { get => _preferAU; set => SetField(ref _preferAU, value); }

    private bool _preferASIA;
    public bool PreferASIA { get => _preferASIA; set => SetField(ref _preferASIA, value); }

    private bool _preferKR;
    public bool PreferKR { get => _preferKR; set => SetField(ref _preferKR, value); }

    private bool _preferCN;
    public bool PreferCN { get => _preferCN; set => SetField(ref _preferCN, value); }

    private bool _preferBR;
    public bool PreferBR { get => _preferBR; set => SetField(ref _preferBR, value); }

    private bool _preferNL;
    public bool PreferNL { get => _preferNL; set => SetField(ref _preferNL, value); }

    private bool _preferSE;
    public bool PreferSE { get => _preferSE; set => SetField(ref _preferSE, value); }

    private bool _preferSCAN;
    public bool PreferSCAN { get => _preferSCAN; set => SetField(ref _preferSCAN, value); }

    // ═══ UI MODE ════════════════════════════════════════════════════════
    private bool _isSimpleMode = true;
    public bool IsSimpleMode
    {
        get => _isSimpleMode;
        set { if (SetField(ref _isSimpleMode, value)) OnPropertyChanged(nameof(IsExpertMode)); }
    }
    public bool IsExpertMode => !_isSimpleMode;

    // Simple-mode options (not persisted — derived from main options at run time)
    private int _simpleRegionIndex;
    public int SimpleRegionIndex { get => _simpleRegionIndex; set => SetField(ref _simpleRegionIndex, value); }

    private bool _simpleDupes = true;
    public bool SimpleDupes { get => _simpleDupes; set => SetField(ref _simpleDupes, value); }

    private bool _simpleJunk = true;
    public bool SimpleJunk { get => _simpleJunk; set => SetField(ref _simpleJunk, value); }

    private bool _simpleSort = true;
    public bool SimpleSort { get => _simpleSort; set => SetField(ref _simpleSort, value); }

    // ═══ UI STATE (not persisted) ═══════════════════════════════════════
    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (SetField(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(IsIdle));
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }
    public bool IsIdle => !_isBusy;

    private double _progress;
    public double Progress { get => _progress; set => SetField(ref _progress, value); }

    private string _progressText = "";
    public string ProgressText { get => _progressText; set => SetField(ref _progressText, value); }

    private string _busyHint = "";
    public string BusyHint { get => _busyHint; set => SetField(ref _busyHint, value); }

    private string? _selectedRoot;
    public string? SelectedRoot
    {
        get => _selectedRoot;
        set { SetField(ref _selectedRoot, value); CommandManager.InvalidateRequerySuggested(); }
    }

    private bool _canRollback;
    public bool CanRollback
    {
        get => _canRollback;
        set { SetField(ref _canRollback, value); CommandManager.InvalidateRequerySuggested(); }
    }

    private string _lastReportPath = "";
    public string LastReportPath
    {
        get => _lastReportPath;
        set { SetField(ref _lastReportPath, value); CommandManager.InvalidateRequerySuggested(); }
    }

    private bool _showDryRunBanner = true;
    public bool ShowDryRunBanner { get => _showDryRunBanner; set => SetField(ref _showDryRunBanner, value); }

    private bool _showMoveCompleteBanner;
    public bool ShowMoveCompleteBanner { get => _showMoveCompleteBanner; set => SetField(ref _showMoveCompleteBanner, value); }

    // GameKey preview
    private string _gameKeyPreviewInput = "";
    public string GameKeyPreviewInput { get => _gameKeyPreviewInput; set => SetField(ref _gameKeyPreviewInput, value); }

    private string _gameKeyPreviewOutput = "–";
    public string GameKeyPreviewOutput { get => _gameKeyPreviewOutput; set => SetField(ref _gameKeyPreviewOutput, value); }

    // Theme
    public string ThemeToggleText => _theme.IsDark ? "☀ Hell" : "☾ Dunkel";

    // ═══ STATUS INDICATORS ══════════════════════════════════════════════
    private string _statusRoots = "Roots: –";
    public string StatusRoots { get => _statusRoots; set => SetField(ref _statusRoots, value); }

    private string _statusTools = "Tools: –";
    public string StatusTools { get => _statusTools; set => SetField(ref _statusTools, value); }

    private string _statusDat = "DAT: –";
    public string StatusDat { get => _statusDat; set => SetField(ref _statusDat, value); }

    private string _statusReady = "Status: –";
    public string StatusReady { get => _statusReady; set => SetField(ref _statusReady, value); }

    private string _statusRuntime = "Laufzeit: –";
    public string StatusRuntime { get => _statusRuntime; set => SetField(ref _statusRuntime, value); }

    private StatusLevel _rootsStatusLevel = StatusLevel.Missing;
    public StatusLevel RootsStatusLevel { get => _rootsStatusLevel; set => SetField(ref _rootsStatusLevel, value); }

    private StatusLevel _toolsStatusLevel = StatusLevel.Missing;
    public StatusLevel ToolsStatusLevel { get => _toolsStatusLevel; set => SetField(ref _toolsStatusLevel, value); }

    private StatusLevel _datStatusLevel = StatusLevel.Missing;
    public StatusLevel DatStatusLevel { get => _datStatusLevel; set => SetField(ref _datStatusLevel, value); }

    private StatusLevel _readyStatusLevel = StatusLevel.Missing;
    public StatusLevel ReadyStatusLevel { get => _readyStatusLevel; set => SetField(ref _readyStatusLevel, value); }

    // ═══ DASHBOARD COUNTERS ═════════════════════════════════════════════
    private string _dashMode = "–";
    public string DashMode { get => _dashMode; set => SetField(ref _dashMode, value); }

    private string _dashWinners = "0";
    public string DashWinners { get => _dashWinners; set => SetField(ref _dashWinners, value); }

    private string _dashDupes = "0";
    public string DashDupes { get => _dashDupes; set => SetField(ref _dashDupes, value); }

    private string _dashJunk = "0";
    public string DashJunk { get => _dashJunk; set => SetField(ref _dashJunk, value); }

    private string _dashDuration = "00:00";
    public string DashDuration { get => _dashDuration; set => SetField(ref _dashDuration, value); }

    private string _healthScore = "–";
    public string HealthScore { get => _healthScore; set => SetField(ref _healthScore, value); }

    // ═══ STEP INDICATOR ═════════════════════════════════════════════════
    private string _stepLabel1 = "Keine Ordner";
    public string StepLabel1 { get => _stepLabel1; set => SetField(ref _stepLabel1, value); }

    private string _stepLabel2 = "Bereit";
    public string StepLabel2 { get => _stepLabel2; set => SetField(ref _stepLabel2, value); }

    private string _stepLabel3 = "F5 drücken";
    public string StepLabel3 { get => _stepLabel3; set => SetField(ref _stepLabel3, value); }

    // ═══ PUBLIC METHODS ═════════════════════════════════════════════════

    /// <summary>Build the preferred regions array from all boolean flags.</summary>
    public string[] GetPreferredRegions()
    {
        var regions = new List<string>(16);
        if (PreferEU) regions.Add("EU");
        if (PreferUS) regions.Add("US");
        if (PreferWORLD) regions.Add("WORLD");
        if (PreferJP) regions.Add("JP");
        if (PreferDE) regions.Add("DE");
        if (PreferFR) regions.Add("FR");
        if (PreferIT) regions.Add("IT");
        if (PreferES) regions.Add("ES");
        if (PreferAU) regions.Add("AU");
        if (PreferASIA) regions.Add("ASIA");
        if (PreferKR) regions.Add("KR");
        if (PreferCN) regions.Add("CN");
        if (PreferBR) regions.Add("BR");
        if (PreferNL) regions.Add("NL");
        if (PreferSE) regions.Add("SE");
        if (PreferSCAN) regions.Add("SCAN");
        return [.. regions];
    }

    /// <summary>Add a log entry (thread-safe via Dispatcher if needed).</summary>
    public void AddLog(string text, string level = "INFO")
    {
        LogEntries.Add(new LogEntry(text, level));
    }

    /// <summary>Recompute all status dot indicators.</summary>
    public void RefreshStatus()
    {
        // Roots
        bool hasRoots = Roots.Count > 0;
        RootsStatusLevel = hasRoots ? StatusLevel.Ok : StatusLevel.Missing;
        StatusRoots = hasRoots ? $"Roots: {Roots.Count}" : "Roots: –";
        StepLabel1 = hasRoots ? $"{Roots.Count} Ordner" : "Keine Ordner";

        // Tools
        bool hasChdman = !string.IsNullOrWhiteSpace(ToolChdman);
        bool has7z = !string.IsNullOrWhiteSpace(Tool7z);
        ToolsStatusLevel = (hasChdman || has7z) ? StatusLevel.Ok
            : ConvertEnabled ? StatusLevel.Warning
            : StatusLevel.Missing;
        StatusTools = ToolsStatusLevel == StatusLevel.Ok ? "Tools: OK"
            : ToolsStatusLevel == StatusLevel.Warning ? "Tools: ⚠" : "Tools: –";

        // DAT
        DatStatusLevel = !UseDat ? StatusLevel.Missing
            : !string.IsNullOrWhiteSpace(DatRoot) ? StatusLevel.Ok
            : StatusLevel.Warning;
        StatusDat = DatStatusLevel == StatusLevel.Ok ? "DAT: ✓"
            : DatStatusLevel == StatusLevel.Warning ? "DAT: ⚠" : "DAT: –";

        // Overall readiness
        ReadyStatusLevel = !hasRoots ? StatusLevel.Blocked
            : ToolsStatusLevel == StatusLevel.Warning ? StatusLevel.Warning
            : StatusLevel.Ok;
        StatusReady = ReadyStatusLevel switch
        {
            StatusLevel.Ok => "Bereit ✓",
            StatusLevel.Warning => "Warnung ⚠",
            StatusLevel.Blocked => "Blockiert ✗",
            _ => "Status: –"
        };
    }

    // ═══ COMMAND HANDLERS ═══════════════════════════════════════════════

    private void OnRun()
    {
        IsBusy = true;
        BusyHint = DryRun ? "DryRun läuft…" : "Move läuft…";
        DashMode = DryRun ? "DryRun" : "Move";
        Progress = 0;
        ProgressText = "0%";
        ShowMoveCompleteBanner = false;

        // Actual run orchestration is wired in MainWindow.xaml.cs
        // via the RunRequested event
        RunRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnCancel()
    {
        _cts?.Cancel();
        BusyHint = "Abbruch angefordert…";
    }

    private void OnRollback()
    {
        RollbackRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnAddRoot()
    {
        var folder = DialogService.BrowseFolder("ROM-Ordner auswählen");
        if (folder is not null && !Roots.Contains(folder))
        {
            Roots.Add(folder);
        }
    }

    private void OnRemoveRoot()
    {
        if (SelectedRoot is not null)
            Roots.Remove(SelectedRoot);
    }

    private void OnOpenReport()
    {
        if (!string.IsNullOrEmpty(LastReportPath) && System.IO.File.Exists(LastReportPath))
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = LastReportPath,
                UseShellExecute = true
            });
        }
    }

    private void OnThemeToggle()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeToggleText));
    }

    private void OnGameKeyPreview()
    {
        try
        {
            GameKeyPreviewOutput = Core.GameKeys.GameKeyNormalizer.Normalize(GameKeyPreviewInput);
        }
        catch (Exception ex)
        {
            GameKeyPreviewOutput = $"Fehler: {ex.Message}";
        }
    }

    private void OnPresetSafeDryRun()
    {
        DryRun = true;
        ConvertEnabled = false;
        AggressiveJunk = false;
        PreferEU = true; PreferUS = true; PreferJP = true; PreferWORLD = true;
        RefreshStatus();
    }

    private void OnPresetFullSort()
    {
        DryRun = true;
        SortConsole = true;
        PreferEU = true; PreferUS = true; PreferJP = true; PreferWORLD = true;
        RefreshStatus();
    }

    private void OnPresetConvert()
    {
        DryRun = true;
        ConvertEnabled = true;
        RefreshStatus();
    }

    /// <summary>Complete a run (call from UI thread when orchestration finishes).</summary>
    public void CompleteRun(bool success, string? reportPath = null)
    {
        IsBusy = false;
        BusyHint = "";
        if (reportPath is not null)
            LastReportPath = reportPath;
        if (success && !DryRun)
        {
            CanRollback = true;
            ShowMoveCompleteBanner = true;
        }
        RefreshStatus();
    }

    /// <summary>Set up a new CancellationTokenSource for a run.</summary>
    public CancellationToken CreateRunCancellation()
    {
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
        return _cts.Token;
    }

    // ═══ EVENTS (for code-behind orchestration wiring) ══════════════════
    public event EventHandler? RunRequested;
    public event EventHandler? RollbackRequested;

    // ═══ INPC INFRASTRUCTURE ════════════════════════════════════════════
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged(string? propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private void OnRootsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshStatus();
        CommandManager.InvalidateRequerySuggested();
    }
}
