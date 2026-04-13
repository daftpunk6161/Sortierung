using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using ConflictPolicy = Romulus.UI.Wpf.Models.ConflictPolicy;

namespace Romulus.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private int _setupSyncDepth;
    private readonly SetupSyncMirrorState _setupSyncMirrorState = new();
    private bool _mainViewModelDisposed;

    private bool IsSetupSyncInProgress => _setupSyncDepth > 0;

    private IDisposable EnterSetupSyncScope()
    {
        Interlocked.Increment(ref _setupSyncDepth);
        return new SetupSyncScope(this);
    }

    private sealed class SetupSyncScope : IDisposable
    {
        private MainViewModel? _owner;

        public SetupSyncScope(MainViewModel owner)
        {
            _owner = owner;
        }

        public void Dispose()
        {
            var owner = Interlocked.Exchange(ref _owner, null);
            if (owner is not null)
                Interlocked.Decrement(ref owner._setupSyncDepth);
        }
    }

    private sealed class SetupSyncMirrorState
    {
        private readonly object _sync = new();
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public bool TryUpdate(string key, string? rawValue, out string normalizedValue)
        {
            normalizedValue = rawValue ?? string.Empty;

            lock (_sync)
            {
                if (_values.TryGetValue(key, out var current)
                    && string.Equals(current, normalizedValue, StringComparison.Ordinal))
                {
                    return false;
                }

                _values[key] = normalizedValue;
                return true;
            }
        }
    }

    // ═══ AUTO-SAVE (debounced 2s after last persisted property change) ═══
    private System.Threading.Timer? _autoSaveTimer;
    private bool _settingsLoaded;
    private readonly object _settingsSaveLock = new();

    private static readonly HashSet<string> AutoSavePropertyNames = new(StringComparer.Ordinal)
    {
        nameof(TrashRoot), nameof(DatRoot), nameof(AuditRoot), nameof(Ps3DupesRoot),
        nameof(ToolChdman), nameof(ToolDolphin), nameof(Tool7z), nameof(ToolPsxtract), nameof(ToolCiso),
        nameof(UseDat), nameof(DatHashType), nameof(DatFallback),
        nameof(EnableDatRename), nameof(EnableDatAudit),
        nameof(SortConsole), nameof(RemoveJunk), nameof(OnlyGames), nameof(KeepUnknownWhenOnlyGames), nameof(AliasKeying), nameof(AggressiveJunk),
        nameof(DryRun), nameof(ConvertEnabled), nameof(ConfirmMove), nameof(ConflictPolicy),
        nameof(PreferEU), nameof(PreferUS), nameof(PreferJP), nameof(PreferWORLD),
        nameof(PreferDE), nameof(PreferFR), nameof(PreferIT), nameof(PreferES),
        nameof(PreferAU), nameof(PreferASIA), nameof(PreferKR), nameof(PreferCN),
        nameof(PreferBR), nameof(PreferNL), nameof(PreferSE), nameof(PreferSCAN),
        nameof(LogLevel), nameof(MinimizeToTray),
        nameof(WatchAutoStart),
    };

    /// <summary>Schedule an auto-save 2 seconds after the last persisted property change.</summary>
    private void ScheduleAutoSave()
    {
        if (!_settingsLoaded)
            return;

        lock (_settingsSaveLock)
        {
            _autoSaveTimer ??= new System.Threading.Timer(OnAutoSaveTimerElapsed, null,
                System.Threading.Timeout.InfiniteTimeSpan,
                System.Threading.Timeout.InfiniteTimeSpan);

            _autoSaveTimer.Change(TimeSpan.FromSeconds(2), System.Threading.Timeout.InfiniteTimeSpan);
        }
    }

    private void OnAutoSaveTimerElapsed(object? _)
    {
        // Must run on UI thread — SaveFrom reads ObservableCollection<string> Roots
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        dispatcher.BeginInvoke(() =>
        {
            try
            {
                SaveSettings();
                AddLog(_loc["Log.SettingsAutoSaved"], "DEBUG");
            }
            catch (Exception ex)
            {
                AddLog(_loc.Format("Log.AutoSaveFailed", ex.Message), "WARN");
            }
        });
    }
    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot
    {
        get => _trashRoot;
        set
        {
            if (!SetProperty(ref _trashRoot, value))
                return;

            ValidateDirectoryPath(value, nameof(TrashRoot));
            SyncSetupProperty(nameof(SetupViewModel.TrashRoot), value);
        }
    }

    private string _datRoot = "";
    public string DatRoot
    {
        get => _datRoot;
        set
        {
            if (SetProperty(ref _datRoot, value))
            {
                ValidateDirectoryPath(value, nameof(DatRoot));
                RefreshStatus();
                _autoDetectDatMappingsCommand?.NotifyCanExecuteChanged();
                SyncSetupProperty(nameof(SetupViewModel.DatRoot), value);
            }
        }
    }

    private string _auditRoot = "";
    public string AuditRoot
    {
        get => _auditRoot;
        set
        {
            if (!SetProperty(ref _auditRoot, value))
                return;

            ValidateDirectoryPath(value, nameof(AuditRoot));
            SyncSetupProperty(nameof(SetupViewModel.AuditRoot), value);
        }
    }

    private string _ps3DupesRoot = "";
    public string Ps3DupesRoot
    {
        get => _ps3DupesRoot;
        set
        {
            if (!SetProperty(ref _ps3DupesRoot, value))
                return;

            ValidateDirectoryPath(value, nameof(Ps3DupesRoot));
            SyncSetupProperty(nameof(SetupViewModel.Ps3DupesRoot), value);
        }
    }

    // ═══ TOOL PATHS (persisted) ═════════════════════════════════════════
    private string _toolChdman = "";
    public string ToolChdman
    {
        get => _toolChdman;
        set
        {
            if (!SetProperty(ref _toolChdman, value))
                return;

            ValidateToolPath(value, nameof(ToolChdman));
            RefreshStatus();
            SyncSetupProperty(nameof(SetupViewModel.ToolChdman), value);
        }
    }

    private string _toolDolphin = "";
    public string ToolDolphin
    {
        get => _toolDolphin;
        set
        {
            if (!SetProperty(ref _toolDolphin, value))
                return;

            ValidateToolPath(value, nameof(ToolDolphin));
            RefreshStatus();
            SyncSetupProperty(nameof(SetupViewModel.ToolDolphin), value);
        }
    }

    private string _tool7z = "";
    public string Tool7z
    {
        get => _tool7z;
        set
        {
            if (!SetProperty(ref _tool7z, value))
                return;

            ValidateToolPath(value, nameof(Tool7z));
            RefreshStatus();
            SyncSetupProperty(nameof(SetupViewModel.Tool7z), value);
        }
    }

    private string _toolPsxtract = "";
    public string ToolPsxtract
    {
        get => _toolPsxtract;
        set
        {
            if (!SetProperty(ref _toolPsxtract, value))
                return;

            ValidateToolPath(value, nameof(ToolPsxtract));
            RefreshStatus();
            SyncSetupProperty(nameof(SetupViewModel.ToolPsxtract), value);
        }
    }

    private string _toolCiso = "";
    public string ToolCiso
    {
        get => _toolCiso;
        set
        {
            if (!SetProperty(ref _toolCiso, value))
                return;

            ValidateToolPath(value, nameof(ToolCiso));
            RefreshStatus();
            SyncSetupProperty(nameof(SetupViewModel.ToolCiso), value);
        }
    }

    private void SyncSetupProperty(string propertyName, string value)
    {
        if (IsSetupSyncInProgress)
            return;

        if (Setup is null)
            return;

        if (!_setupSyncMirrorState.TryUpdate(propertyName, value, out var normalizedValue))
            return;

        using var _ = EnterSetupSyncScope();
        switch (propertyName)
        {
            case nameof(SetupViewModel.TrashRoot):
                if (!string.Equals(Setup.TrashRoot, normalizedValue, StringComparison.Ordinal))
                    Setup.TrashRoot = normalizedValue;
                break;
            case nameof(SetupViewModel.DatRoot):
                if (!string.Equals(Setup.DatRoot, normalizedValue, StringComparison.Ordinal))
                    Setup.DatRoot = normalizedValue;
                break;
            case nameof(SetupViewModel.AuditRoot):
                if (!string.Equals(Setup.AuditRoot, normalizedValue, StringComparison.Ordinal))
                    Setup.AuditRoot = normalizedValue;
                break;
            case nameof(SetupViewModel.Ps3DupesRoot):
                if (!string.Equals(Setup.Ps3DupesRoot, normalizedValue, StringComparison.Ordinal))
                    Setup.Ps3DupesRoot = normalizedValue;
                break;
            case nameof(SetupViewModel.ToolChdman):
                if (!string.Equals(Setup.ToolChdman, normalizedValue, StringComparison.Ordinal))
                    Setup.ToolChdman = normalizedValue;
                break;
            case nameof(SetupViewModel.ToolDolphin):
                if (!string.Equals(Setup.ToolDolphin, normalizedValue, StringComparison.Ordinal))
                    Setup.ToolDolphin = normalizedValue;
                break;
            case nameof(SetupViewModel.Tool7z):
                if (!string.Equals(Setup.Tool7z, normalizedValue, StringComparison.Ordinal))
                    Setup.Tool7z = normalizedValue;
                break;
            case nameof(SetupViewModel.ToolPsxtract):
                if (!string.Equals(Setup.ToolPsxtract, normalizedValue, StringComparison.Ordinal))
                    Setup.ToolPsxtract = normalizedValue;
                break;
            case nameof(SetupViewModel.ToolCiso):
                if (!string.Equals(Setup.ToolCiso, normalizedValue, StringComparison.Ordinal))
                    Setup.ToolCiso = normalizedValue;
                break;
        }
    }

    private void OnSetupSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsSetupSyncInProgress || Setup is null || string.IsNullOrEmpty(e.PropertyName))
            return;

        using var _ = EnterSetupSyncScope();
        switch (e.PropertyName)
        {
            case nameof(SetupViewModel.TrashRoot):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.TrashRoot, out var trashRoot))
                    TrashRoot = trashRoot;
                break;
            case nameof(SetupViewModel.DatRoot):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.DatRoot, out var datRoot))
                    DatRoot = datRoot;
                break;
            case nameof(SetupViewModel.AuditRoot):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.AuditRoot, out var auditRoot))
                    AuditRoot = auditRoot;
                break;
            case nameof(SetupViewModel.Ps3DupesRoot):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.Ps3DupesRoot, out var ps3DupesRoot))
                    Ps3DupesRoot = ps3DupesRoot;
                break;
            case nameof(SetupViewModel.ToolChdman):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.ToolChdman, out var toolChdman))
                    ToolChdman = toolChdman;
                break;
            case nameof(SetupViewModel.ToolDolphin):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.ToolDolphin, out var toolDolphin))
                    ToolDolphin = toolDolphin;
                break;
            case nameof(SetupViewModel.Tool7z):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.Tool7z, out var tool7z))
                    Tool7z = tool7z;
                break;
            case nameof(SetupViewModel.ToolPsxtract):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.ToolPsxtract, out var toolPsxtract))
                    ToolPsxtract = toolPsxtract;
                break;
            case nameof(SetupViewModel.ToolCiso):
                if (_setupSyncMirrorState.TryUpdate(e.PropertyName, Setup.ToolCiso, out var toolCiso))
                    ToolCiso = toolCiso;
                break;
        }
    }

    // ═══ D-01: Bidirectional Region Preference Sync ═════════════════════

    private static readonly Dictionary<string, string> PreferPropertyToRegionCode = new(StringComparer.Ordinal)
    {
        [nameof(PreferEU)] = "EU", [nameof(PreferUS)] = "US", [nameof(PreferJP)] = "JP", [nameof(PreferWORLD)] = "WORLD",
        [nameof(PreferDE)] = "DE", [nameof(PreferFR)] = "FR", [nameof(PreferIT)] = "IT", [nameof(PreferES)] = "ES",
        [nameof(PreferAU)] = "AU", [nameof(PreferASIA)] = "ASIA", [nameof(PreferKR)] = "KR", [nameof(PreferCN)] = "CN",
        [nameof(PreferBR)] = "BR", [nameof(PreferNL)] = "NL", [nameof(PreferSE)] = "SE", [nameof(PreferSCAN)] = "SCAN",
    };

    /// <summary>D-01: Sync MainViewModel.Prefer* booleans → SetupViewModel.RegionItems.IsActive.</summary>
    private void OnRegionPreferencePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsSetupSyncInProgress || Setup is null || e.PropertyName is null)
            return;

        if (!PreferPropertyToRegionCode.TryGetValue(e.PropertyName, out var code))
            return;

        var value = GetRegionBool(code);
        using var _ = EnterSetupSyncScope();
        var item = Setup.RegionItems.FirstOrDefault(r => string.Equals(r.Code, code, StringComparison.Ordinal));
        if (item is not null && item.IsActive != value)
            item.IsActive = value;
    }

    /// <summary>D-01: Subscribe to each SetupViewModel.RegionItem.PropertyChanged for reverse sync.</summary>
    internal void SubscribeSetupRegionItems()
    {
        if (Setup is null) return;
        foreach (var item in Setup.RegionItems)
            item.PropertyChanged += OnSetupRegionItemPropertyChanged;
    }

    private void OnSetupRegionItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (IsSetupSyncInProgress || sender is not RegionItem item || e.PropertyName != nameof(RegionItem.IsActive))
            return;

        using var _ = EnterSetupSyncScope();
        SetRegionBool(item.Code, item.IsActive);
    }

    // ═══ BOOLEAN FLAGS (persisted) ══════════════════════════════════════
    [ObservableProperty]
    private bool _sortConsole = true;

    [ObservableProperty]
    private bool _removeJunk = true;

    [ObservableProperty]
    private bool _onlyGames;

    [ObservableProperty]
    private bool _keepUnknownWhenOnlyGames = true;

    [ObservableProperty]
    private bool _aliasKeying;

    private bool _useDat;
    public bool UseDat { get => _useDat; set { if (SetProperty(ref _useDat, value)) RefreshStatus(); } }

    private bool _enableDatAudit = true;
    public bool EnableDatAudit { get => _enableDatAudit; set => SetProperty(ref _enableDatAudit, value); }

    [ObservableProperty]
    private bool _enableDatRename = true;

    [ObservableProperty]
    private bool _datFallback;

    [ObservableProperty]
    private bool _dryRun = true;

    private bool _convertEnabled;
    public bool ConvertEnabled { get => _convertEnabled; set { if (SetProperty(ref _convertEnabled, value)) RefreshStatus(); } }

    /// <summary>Transient flag — set by ConvertOnlyCommand, reset after run completes. Not persisted.</summary>
    public bool ConvertOnly { get; set; }

    [ObservableProperty]
    private bool _confirmMove = true;

    [ObservableProperty]
    private bool _aggressiveJunk;

    [ObservableProperty]
    private bool _approveReviews;

    [ObservableProperty]
    private bool _approveConversionReview;

    [ObservableProperty]
    private bool _crcVerifyScan;

    [ObservableProperty]
    private bool _crcVerifyDat;

    [ObservableProperty]
    private bool _safetyStrict;

    [ObservableProperty]
    private bool _safetyPrompts;

    [ObservableProperty]
    private bool _jpOnlySelected;

    // ═══ STRING CONFIG (persisted) ══════════════════════════════════════
    [ObservableProperty]
    private string _protectedPaths = "";

    [ObservableProperty]
    private string _safetySandbox = "";

    [ObservableProperty]
    private string _jpKeepConsoles = "";

    [ObservableProperty]
    private string _logLevel = "Info";

    [ObservableProperty]
    private string _locale = "de";

    [ObservableProperty]
    private bool _isWatchModeActive;

    [ObservableProperty]
    private bool _watchAutoStart;

    /// <summary>GUI-109: Scheduled run interval in minutes (0 = disabled).</summary>
    private int _schedulerIntervalMinutes;
    public int SchedulerIntervalMinutes
    {
        get => _schedulerIntervalMinutes;
        set
        {
            if (SetProperty(ref _schedulerIntervalMinutes, value))
                OnPropertyChanged(nameof(SchedulerIntervalDisplay));
        }
    }

    public string SchedulerIntervalDisplay => SchedulerIntervalMinutes switch
    {
        0 => "–",
        < 60 => $"{SchedulerIntervalMinutes} min",
        _ => $"{SchedulerIntervalMinutes / 60} h",
    };

    /// <summary>GUI-110: Minimize to system tray on close.</summary>
    [ObservableProperty]
    private bool _minimizeToTray;

    [ObservableProperty]
    private string _datHashType = "SHA1";

    // ═══ REGION PREFERENCES (persisted) ═════════════════════════════════
    private bool _preferEU = true;
    public bool PreferEU { get => _preferEU; set => SetProperty(ref _preferEU, value); }

    private bool _preferUS = true;
    public bool PreferUS { get => _preferUS; set => SetProperty(ref _preferUS, value); }

    private bool _preferJP = true;
    public bool PreferJP { get => _preferJP; set => SetProperty(ref _preferJP, value); }

    private bool _preferWORLD = true;
    public bool PreferWORLD { get => _preferWORLD; set => SetProperty(ref _preferWORLD, value); }

    private bool _preferDE;
    public bool PreferDE { get => _preferDE; set => SetProperty(ref _preferDE, value); }

    private bool _preferFR;
    public bool PreferFR { get => _preferFR; set => SetProperty(ref _preferFR, value); }

    private bool _preferIT;
    public bool PreferIT { get => _preferIT; set => SetProperty(ref _preferIT, value); }

    private bool _preferES;
    public bool PreferES { get => _preferES; set => SetProperty(ref _preferES, value); }

    private bool _preferAU;
    public bool PreferAU { get => _preferAU; set => SetProperty(ref _preferAU, value); }

    private bool _preferASIA;
    public bool PreferASIA { get => _preferASIA; set => SetProperty(ref _preferASIA, value); }

    private bool _preferKR;
    public bool PreferKR { get => _preferKR; set => SetProperty(ref _preferKR, value); }

    private bool _preferCN;
    public bool PreferCN { get => _preferCN; set => SetProperty(ref _preferCN, value); }

    private bool _preferBR;
    public bool PreferBR { get => _preferBR; set => SetProperty(ref _preferBR, value); }

    private bool _preferNL;
    public bool PreferNL { get => _preferNL; set => SetProperty(ref _preferNL, value); }

    private bool _preferSE;
    public bool PreferSE { get => _preferSE; set => SetProperty(ref _preferSE, value); }

    private bool _preferSCAN;
    public bool PreferSCAN { get => _preferSCAN; set => SetProperty(ref _preferSCAN, value); }

    // ═══ REGION PRIORITY RANKER (E2: ordered region list with priority) ══
    public ObservableCollection<RegionPriorityItem> RegionPriorities { get; } = [];

    private static readonly (string Code, string Display, string Group)[] AllRegionDefs =
    [
        ("EU", "Europa", "Primary"),
        ("US", "USA", "Primary"),
        ("WORLD", "World", "Primary"),
        ("JP", "Japan", "Primary"),
        ("DE", "Deutschland", "Secondary"),
        ("FR", "Frankreich", "Secondary"),
        ("IT", "Italien", "Secondary"),
        ("ES", "Spanien", "Secondary"),
        ("AU", "Australien", "Secondary"),
        ("ASIA", "Asien", "Secondary"),
        ("KR", "Südkorea", "Secondary"),
        ("CN", "China", "Secondary"),
        ("BR", "Brasilien", "Secondary"),
        ("NL", "Niederlande", "Secondary"),
        ("SE", "Schweden", "Secondary"),
        ("SCAN", "Skandinavien", "Secondary"),
    ];

    /// <summary>Build RegionPriorities from current PreferXX boolean state. Called after settings load.</summary>
    public void InitRegionPriorities()
    {
        RegionPriorities.Clear();
        var enabled = new List<RegionPriorityItem>();
        var disabled = new List<RegionPriorityItem>();
        foreach (var (code, display, group) in AllRegionDefs)
        {
            var isOn = GetRegionBool(code);
            var item = new RegionPriorityItem { Code = code, DisplayName = display, Group = group, IsEnabled = isOn };
            if (isOn) enabled.Add(item);
            else disabled.Add(item);
        }
        int pos = 1;
        foreach (var r in enabled) { r.Position = pos++; RegionPriorities.Add(r); }
        foreach (var r in disabled) { r.Position = 0; RegionPriorities.Add(r); }
        OnPropertyChanged(nameof(EnabledRegionCount));
    }

    /// <summary>Number of enabled regions for counter badge.</summary>
    public int EnabledRegionCount => RegionPriorities.Count(r => r.IsEnabled);

    /// <summary>Sync PreferXX booleans from the current RegionPriorities state.</summary>
    private void SyncRegionBooleans()
    {
        foreach (var item in RegionPriorities)
            SetRegionBool(item.Code, item.IsEnabled);
        OnPropertyChanged(nameof(EnabledRegionCount));
        UpdateWizardRegionSummary();
    }

    private bool GetRegionBool(string code) => code switch
    {
        "EU" => PreferEU, "US" => PreferUS, "WORLD" => PreferWORLD, "JP" => PreferJP,
        "DE" => PreferDE, "FR" => PreferFR, "IT" => PreferIT, "ES" => PreferES,
        "AU" => PreferAU, "ASIA" => PreferASIA, "KR" => PreferKR, "CN" => PreferCN,
        "BR" => PreferBR, "NL" => PreferNL, "SE" => PreferSE, "SCAN" => PreferSCAN,
        _ => false,
    };

    private void SetRegionBool(string code, bool value)
    {
        switch (code)
        {
            case "EU": PreferEU = value; break;
            case "US": PreferUS = value; break;
            case "WORLD": PreferWORLD = value; break;
            case "JP": PreferJP = value; break;
            case "DE": PreferDE = value; break;
            case "FR": PreferFR = value; break;
            case "IT": PreferIT = value; break;
            case "ES": PreferES = value; break;
            case "AU": PreferAU = value; break;
            case "ASIA": PreferASIA = value; break;
            case "KR": PreferKR = value; break;
            case "CN": PreferCN = value; break;
            case "BR": PreferBR = value; break;
            case "NL": PreferNL = value; break;
            case "SE": PreferSE = value; break;
            case "SCAN": PreferSCAN = value; break;
        }
    }

    /// <summary>TASK-117: Move a region from one index to another (Drag &amp; Drop reorder).
    /// Only enabled items within the enabled section may be reordered.</summary>
    public void MoveRegionTo(int fromIndex, int toIndex)
    {
        if (fromIndex == toIndex) return;
        if (fromIndex < 0 || fromIndex >= RegionPriorities.Count) return;
        if (toIndex < 0 || toIndex >= RegionPriorities.Count) return;

        var item = RegionPriorities[fromIndex];
        if (!item.IsEnabled) return;
        if (!RegionPriorities[toIndex].IsEnabled) return;

        RegionPriorities.Move(fromIndex, toIndex);
        RenumberRegions();
        SyncRegionBooleans();
    }

    [RelayCommand]
    private void MoveRegionUp(RegionPriorityItem? item)
    {
        if (item is null || !item.IsEnabled) return;
        int idx = RegionPriorities.IndexOf(item);
        if (idx <= 0) return;
        var prev = RegionPriorities[idx - 1];
        if (!prev.IsEnabled) return;
        RegionPriorities.Move(idx, idx - 1);
        RenumberRegions();
        SyncRegionBooleans();
    }

    [RelayCommand]
    private void MoveRegionDown(RegionPriorityItem? item)
    {
        if (item is null || !item.IsEnabled) return;
        int idx = RegionPriorities.IndexOf(item);
        int nextIdx = idx + 1;
        if (nextIdx >= RegionPriorities.Count) return;
        if (!RegionPriorities[nextIdx].IsEnabled) return;
        RegionPriorities.Move(idx, nextIdx);
        RenumberRegions();
        SyncRegionBooleans();
    }

    [RelayCommand]
    private void ToggleRegion(RegionPriorityItem? item)
    {
        if (item is null) return;
        item.IsEnabled = !item.IsEnabled;
        int idx = RegionPriorities.IndexOf(item);
        RegionPriorities.RemoveAt(idx);

        if (item.IsEnabled)
        {
            // Insert at end of enabled items
            int insertAt = RegionPriorities.Count(r => r.IsEnabled);
            RegionPriorities.Insert(insertAt, item);
        }
        else
        {
            // Move to disabled section
            RegionPriorities.Add(item);
        }
        RenumberRegions();
        SyncRegionBooleans();
    }

    [RelayCommand]
    private void RegionPresetEuFocus()
    {
        ApplyRegionPreset(["EU", "DE", "FR", "IT", "ES", "NL", "SE", "SCAN", "WORLD"]);
    }

    [RelayCommand]
    private void RegionPresetUsFocus()
    {
        ApplyRegionPreset(["US", "WORLD", "EU"]);
    }

    [RelayCommand]
    private void RegionPresetMultiRegion()
    {
        ApplyRegionPreset(["EU", "US", "JP", "WORLD"]);
    }

    [RelayCommand]
    private void RegionPresetAll()
    {
        ApplyRegionPreset(AllRegionDefs.Select(r => r.Code).ToArray());
    }

    private void ApplyRegionPreset(string[] enabledCodes)
    {
        RegionPriorities.Clear();
        int pos = 1;
        // Add enabled in preset order
        foreach (var code in enabledCodes)
        {
            var def = AllRegionDefs.FirstOrDefault(r => r.Code == code);
            if (def.Code is null) continue;
            RegionPriorities.Add(new RegionPriorityItem { Code = def.Code, DisplayName = def.Display, Group = def.Group, IsEnabled = true, Position = pos++ });
        }
        // Add remaining disabled
        foreach (var (code, display, group) in AllRegionDefs)
        {
            if (!enabledCodes.Contains(code))
                RegionPriorities.Add(new RegionPriorityItem { Code = code, DisplayName = display, Group = group, IsEnabled = false, Position = 0 });
        }
        SyncRegionBooleans();
    }

    private void RenumberRegions()
    {
        int pos = 1;
        foreach (var item in RegionPriorities)
            item.Position = item.IsEnabled ? pos++ : 0;
    }

    // ═══ UI MODE ════════════════════════════════════════════════════════
    private bool _isSimpleMode = true;
    public bool IsSimpleMode
    {
        get => _isSimpleMode;
        set
        {
            if (SetProperty(ref _isSimpleMode, value))
            {
                Shell.IsSimpleMode = value;
                Tools.SetSimpleMode(value);
                OnPropertyChanged(nameof(IsExpertMode));
                OnPropertyChanged(nameof(CurrentUiModeLabel));
            }
        }
    }
    public bool IsExpertMode => !_isSimpleMode;
    public string CurrentUiModeLabel => IsSimpleMode ? "Einfach" : "Experte";

    // Simple-mode options (not persisted — derived from main options at run time)
    [ObservableProperty]
    private int _simpleRegionIndex;

    [ObservableProperty]
    private bool _simpleDupes = true;

    [ObservableProperty]
    private bool _simpleJunk = true;

    [ObservableProperty]
    private bool _simpleSort = true;

    // Quick profile selector (STATUS BAR)
    private int _quickProfileIndex;
    public int QuickProfileIndex { get => _quickProfileIndex; set => SetProperty(ref _quickProfileIndex, value); }

    // RF-011: Profile name bound to Einstellungen ComboBox
    private string _profileName = "Standard";
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    // P1-005 / RD-005: Sidebar-Navigation im Einstellungen-Tab
    private string _selectedSettingsSection = "Sortieroptionen";
    public string SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        set => SetProperty(ref _selectedSettingsSection, value);
    }

    // Sidebar-Navigation: Werkzeuge-Tab
    private string _selectedToolsSection = "Schnellzugriff";
    public string SelectedToolsSection
    {
        get => _selectedToolsSection;
        set => SetProperty(ref _selectedToolsSection, value);
    }

    // Sidebar-Navigation: Ergebnis-Tab
    private string _selectedResultSection = "Dashboard";
    public string SelectedResultSection
    {
        get => _selectedResultSection;
        set => SetProperty(ref _selectedResultSection, value);
    }

    // RD-006: Aktiver Preset-Name für SegmentedControl-Selektion
    private string? _activePreset;
    public string? ActivePreset
    {
        get => _activePreset;
        set
        {
            if (!SetProperty(ref _activePreset, value))
                return;

            OnPropertyChanged(nameof(IsConvertPresetActive));
            OnPropertyChanged(nameof(DashboardPrimaryCommand));
            OnPropertyChanged(nameof(DashboardPrimaryActionText));
            OnPropertyChanged(nameof(DashboardPrimaryActionIcon));
            OnPropertyChanged(nameof(DashboardPrimaryActionHintText));
            OnPropertyChanged(nameof(DashboardPrimaryActionAcceleratorKey));
        }
    }

    // ═══ CONFLICT POLICY (UX-007: was YesNoCancel hack, now VM property) ═
    private ConflictPolicy _conflictPolicy = ConflictPolicy.Rename;
    public ConflictPolicy ConflictPolicy
    {
        get => _conflictPolicy;
        set => SetProperty(ref _conflictPolicy, value);
    }

    /// <summary>Index for ComboBox binding (0=Rename, 1=Skip, 2=Overwrite).</summary>
    public int ConflictPolicyIndex
    {
        get => (int)_conflictPolicy;
        set => ConflictPolicy = (ConflictPolicy)value;
    }

    // GameKey preview — GUI-116: Live preview auto-triggers on input change
    private string _gameKeyPreviewInput = "";
    public string GameKeyPreviewInput
    {
        get => _gameKeyPreviewInput;
        set
        {
            if (SetProperty(ref _gameKeyPreviewInput, value))
                OnGameKeyPreview();
        }
    }

    private string _gameKeyPreviewOutput = "–";
    public string GameKeyPreviewOutput { get => _gameKeyPreviewOutput; set => SetProperty(ref _gameKeyPreviewOutput, value); }

    // Theme
    public string CurrentThemeName => _theme.Current.ToString();

    public string CurrentThemeLabel => _theme.Current switch
    {
        AppTheme.Dark         => "Synthwave",
        AppTheme.CleanDarkPro => "Clean Dark",
        AppTheme.RetroCRT     => "Retro CRT",
        AppTheme.ArcadeNeon   => "Arcade Neon",
        AppTheme.Light        => "Hell",
        AppTheme.HighContrast  => "Kontrast",
        _                     => _theme.Current.ToString(),
    };

    /// <summary>TASK-129: All available themes for the theme-switcher dropdown.</summary>
    public IReadOnlyList<AppTheme> AvailableThemes => _theme.AvailableThemes;

    /// <summary>TASK-129: Currently selected theme (two-way binding for ComboBox).</summary>
    public AppTheme SelectedTheme
    {
        get => _theme.Current;
        set
        {
            if (_theme.Current == value) return;
            _theme.ApplyTheme(value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThemeToggleText));
            OnPropertyChanged(nameof(CurrentThemeName));
            OnPropertyChanged(nameof(CurrentThemeLabel));
        }
    }

    public string ThemeToggleText => _theme.Current switch
    {
        AppTheme.Dark         => "⮞ Clean Dark",
        AppTheme.CleanDarkPro => "⮞ Retro CRT",
        AppTheme.RetroCRT     => "⮞ Arcade Neon",
        AppTheme.ArcadeNeon   => "⮞ Hell",
        AppTheme.Light        => "⮞ Kontrast",
        AppTheme.HighContrast => "⮞ Synthwave",
        _                     => "⮞ Synthwave",
    };

    // ═══ SETTINGS HANDLERS ══════════════════════════════════════════════

    private void OnBrowseToolPath(string? parameter)
    {
        var path = _dialog.BrowseFile(_loc["Dialog.BrowseFile.ExeTitle"], "Executables (*.exe)|*.exe|Alle (*.*)|*.*");
        if (path is null) return;
        switch (parameter)
        {
            case "Chdman": ToolChdman = path; break;
            case "Dolphin": ToolDolphin = path; break;
            case "7z": Tool7z = path; break;
            case "Psxtract": ToolPsxtract = path; break;
            case "Ciso": ToolCiso = path; break;
        }
        SaveSettings();
    }

    private void OnBrowseFolderPath(string? parameter)
    {
        var path = _dialog.BrowseFolder(_loc["Dialog.BrowseFolder.FolderTitle"]);
        if (path is null) return;
        switch (parameter)
        {
            case "Dat": DatRoot = path; break;
            case "Trash": TrashRoot = path; break;
            case "Audit": AuditRoot = path; break;
            case "Ps3": Ps3DupesRoot = path; break;
        }
        SaveSettings();
    }

    private void OnBrowseDatMappingFileCommand(object? parameter)
    {
        if (parameter is not DatMapRow row)
            return;

        OnBrowseDatMappingFile(row);
    }

    private void OnBrowseDatMappingFile(DatMapRow row)
    {
        var path = _dialog.BrowseFile(
            "DAT-Datei auswählen",
            "DAT-Dateien (*.dat;*.xml)|*.dat;*.xml|Alle Dateien|*.*");

        if (path is null)
            return;

        row.DatFile = path;
        SaveSettings();
    }

    private void OnPresetSafeDryRun()
    {
        DryRun = true;
        ConvertEnabled = false;
        AggressiveJunk = false;
        PreferEU = true; PreferUS = true; PreferJP = true; PreferWORLD = true;
        ActivePreset = "SafeDryRun";
        RefreshStatus();
    }

    private void OnPresetFullSort()
    {
        DryRun = true;
        SortConsole = true;
        PreferEU = true; PreferUS = true; PreferJP = true; PreferWORLD = true;
        ActivePreset = "FullSort";
        RefreshStatus();
    }

    private void OnPresetConvert()
    {
        DryRun = true;
        ConvertEnabled = true;
        ActivePreset = "Convert";
        RefreshStatus();
    }

    private void OnSaveSettings()
    {
        if (TrySaveSettings())
            AddLog(_loc["Log.SettingsSaved"], "INFO");
        else
            AddLog(_loc["Log.SettingsSaveFailed"], "ERROR");
    }

    private void OnLoadSettings()
    {
        _settings.LoadInto(this);
        RefreshStatus();
        AddLog(_loc["Log.SettingsLoaded"], "INFO");
    }

    /// <summary>Load settings into VM on startup (called from code-behind OnLoaded).</summary>
    public void LoadInitialSettings()
    {
        _settings.LoadInto(this);
        LastAuditPath = _settings.LastAuditPath;

        // RD-009: Restore persisted theme preference
        if (Enum.TryParse<AppTheme>(_settings.LastTheme, true, out var savedTheme) && savedTheme != _theme.Current)
        {
            _theme.ApplyTheme(savedTheme);
            OnPropertyChanged(nameof(ThemeToggleText));
            OnPropertyChanged(nameof(CurrentThemeName));
            OnPropertyChanged(nameof(CurrentThemeLabel));
        }

        // E2: Build region priorities from loaded boolean flags
        InitRegionPriorities();

        // Enable auto-save AFTER initial load so property writes during load don't trigger saves
        _settingsLoaded = true;

        // Subscribe to property changes for auto-save
        PropertyChanged += OnAutoSavePropertyChanged;
        Roots.CollectionChanged += (_, _) => ScheduleAutoSave();

        // Optionally activate watch mode right after settings have been loaded.
        if (WatchAutoStart && Roots.Count > 0 && !IsWatchModeActive)
        {
            SetWatchMode(enabled: true, showDialog: false);
        }

        RefreshStatus();
    }

    private void OnAutoSavePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not null && AutoSavePropertyNames.Contains(e.PropertyName))
            ScheduleAutoSave();
    }

    /// <summary>Save settings (called from code-behind on close / timer). Thread-safe.</summary>
    public void SaveSettings()
    {
        _ = TrySaveSettings();
    }

    private bool TrySaveSettings()
    {
        lock (_settingsSaveLock)
        {
            return _settings.SaveFrom(this, LastAuditPath);
        }
    }

    private void OnThemeToggle()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(CurrentThemeName));
        OnPropertyChanged(nameof(CurrentThemeLabel));
        OnPropertyChanged(nameof(SelectedTheme));
    }

    private void OnGameKeyPreview()
    {
        if (string.IsNullOrWhiteSpace(GameKeyPreviewInput))
        {
            GameKeyPreviewOutput = "–";
            return;
        }
        try
        {
            GameKeyPreviewOutput = Core.GameKeys.GameKeyNormalizer.Normalize(
                GameKeyPreviewInput,
                Infrastructure.Orchestration.GameKeyNormalizationProfile.TagPatterns ?? [],
                Infrastructure.Orchestration.GameKeyNormalizationProfile.AlwaysAliasMap);
        }
        catch (Exception ex)
        {
            GameKeyPreviewOutput = $"Fehler: {ex.Message}";
        }
    }

    /// <summary>Build the preferred regions array from RegionPriorities collection (respects user order).</summary>
    public string[] GetPreferredRegions()
    {
        if (RegionPriorities.Count > 0)
            return RegionPriorities.Where(r => r.IsEnabled).Select(r => r.Code).ToArray();

        // Fallback: legacy boolean-based approach
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

    /// <summary>Build a flat key-value config map from current VM state (for diff/export).</summary>
    public Dictionary<string, string> GetCurrentConfigMap()
    {
        return new Dictionary<string, string>
        {
            ["sortConsole"] = SortConsole.ToString(),
            ["aliasKeying"] = AliasKeying.ToString(),
            ["aggressiveJunk"] = AggressiveJunk.ToString(),
            ["dryRun"] = DryRun.ToString(),
            ["useDat"] = UseDat.ToString(),
            ["datRoot"] = DatRoot ?? "",
            ["datHashType"] = DatHashType ?? "SHA1",
            ["convertEnabled"] = ConvertEnabled.ToString(),
            ["trashRoot"] = TrashRoot ?? "",
            ["auditRoot"] = AuditRoot ?? "",
            ["toolChdman"] = ToolChdman ?? "",
            ["toolDolphin"] = ToolDolphin ?? "",
            ["tool7z"] = Tool7z ?? "",
            ["locale"] = Locale ?? "de",
            ["logLevel"] = LogLevel ?? "Info"
        };
    }

    // ═══ CONSOLE KEYS (from consoles.json) ══════════════════════════════

    private string[]? _allConsoleKeys;

    /// <summary>All console keys from consoles.json, lazily loaded.</summary>
    public string[] AllConsoleKeys => _allConsoleKeys ??= LoadAllConsoleKeys();

    private static string[] LoadAllConsoleKeys()
    {
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var consolesPath = Path.Combine(dataDir, "consoles.json");
        if (!File.Exists(consolesPath))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(consolesPath));
            if (!doc.RootElement.TryGetProperty("consoles", out var arr) || arr.ValueKind != JsonValueKind.Array)
                return [];

            var keys = new List<string>();
            foreach (var item in arr.EnumerateArray())
            {
                if (item.TryGetProperty("key", out var k) && k.GetString() is { Length: > 0 } key)
                    keys.Add(key);
            }
            keys.Sort(StringComparer.OrdinalIgnoreCase);
            return [.. keys];
        }
        catch
        {
            return [];
        }
    }

    // ═══ DAT-MAPPING AUTO-DETECT ════════════════════════════════════════

    private RelayCommand? _autoDetectDatMappingsCommand;
    public IRelayCommand AutoDetectDatMappingsCommand
        => _autoDetectDatMappingsCommand ??= new RelayCommand(OnAutoDetectDatMappings, CanAutoDetectDatMappings);

    private bool CanAutoDetectDatMappings()
        => !string.IsNullOrWhiteSpace(DatRoot) && Directory.Exists(DatRoot);

    private void OnAutoDetectDatMappings()
    {
        if (string.IsNullOrWhiteSpace(DatRoot) || !Directory.Exists(DatRoot))
        {
            AddLog(_loc["Log.DatDirNotFound"], "WARN");
            return;
        }

        // Load dat-catalog.json for console key → system name mapping
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        if (!File.Exists(catalogPath))
        {
            AddLog(_loc["Log.DatCatalogNotFound"], "WARN");
            return;
        }

        try
        {
            // Scan DatRoot for .dat and .xml files
            var datFiles = Directory.GetFiles(DatRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (datFiles.Count == 0)
            {
                AddLog(_loc["Log.DatNoDatFiles"], "WARN");
                return;
            }

            // Keep existing manual mappings that have a valid DatFile
            var existingByConsole = DatMappings
                .Where(m => !string.IsNullOrWhiteSpace(m.Console) && !string.IsNullOrWhiteSpace(m.DatFile))
                .ToDictionary(m => m.Console, m => m.DatFile, StringComparer.OrdinalIgnoreCase);

            var knownConsoleKeys = new HashSet<string>(AllConsoleKeys, StringComparer.OrdinalIgnoreCase);
            var detectedMappings = RunEnvironmentBuilder
                .BuildConsoleMap(dataDir, DatRoot)
                .Where(kv => knownConsoleKeys.Contains(kv.Key)
                          && !string.IsNullOrWhiteSpace(kv.Value)
                          && File.Exists(kv.Value))
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

            // Merge: keep manual, add detected
            foreach (var (console, datFile) in existingByConsole)
            {
                if (!detectedMappings.ContainsKey(console))
                    detectedMappings[console] = datFile;
            }

            DatMappings.Clear();
            foreach (var (console, datFile) in detectedMappings.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase))
                DatMappings.Add(new DatMapRow { Console = console, DatFile = datFile });

            AddLog($"DAT-Mapping: {detectedMappings.Count} Zuordnungen erkannt.", "INFO");
            SaveSettings();
        }
        catch (Exception ex)
        {
            AddLog($"DAT-Auto-Erkennung fehlgeschlagen: {ex.Message}", "ERROR");
        }
    }
}
