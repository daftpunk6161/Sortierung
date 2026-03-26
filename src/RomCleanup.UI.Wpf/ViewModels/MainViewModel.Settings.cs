using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using ConflictPolicy = RomCleanup.UI.Wpf.Models.ConflictPolicy;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
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
    };

    /// <summary>Schedule an auto-save 2 seconds after the last persisted property change.</summary>
    private void ScheduleAutoSave()
    {
        if (!_settingsLoaded) return;
        _autoSaveTimer?.Dispose();
        _autoSaveTimer = new System.Threading.Timer(
            _ =>
            {
                // Must run on UI thread — SaveFrom reads ObservableCollection<string> Roots
                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher is null) return;
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
            },
            null,
            TimeSpan.FromSeconds(2),
            System.Threading.Timeout.InfiniteTimeSpan);
    }
    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot { get => _trashRoot; set { if (SetProperty(ref _trashRoot, value)) { ValidateDirectoryPath(value, nameof(TrashRoot)); SyncToSetup(nameof(TrashRoot), value); } } }

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
            }
        }
    }

    private string _auditRoot = "";
    public string AuditRoot { get => _auditRoot; set { if (SetProperty(ref _auditRoot, value)) ValidateDirectoryPath(value, nameof(AuditRoot)); } }

    private string _ps3DupesRoot = "";
    public string Ps3DupesRoot { get => _ps3DupesRoot; set { if (SetProperty(ref _ps3DupesRoot, value)) ValidateDirectoryPath(value, nameof(Ps3DupesRoot)); } }

    // ═══ TOOL PATHS (persisted) ═════════════════════════════════════════
    private string _toolChdman = "";
    public string ToolChdman { get => _toolChdman; set { if (SetProperty(ref _toolChdman, value)) { ValidateToolPath(value, nameof(ToolChdman)); RefreshStatus(); SyncToSetup(nameof(ToolChdman), value); } } }

    private string _toolDolphin = "";
    public string ToolDolphin { get => _toolDolphin; set { if (SetProperty(ref _toolDolphin, value)) { ValidateToolPath(value, nameof(ToolDolphin)); RefreshStatus(); } } }

    private string _tool7z = "";
    public string Tool7z { get => _tool7z; set { if (SetProperty(ref _tool7z, value)) { ValidateToolPath(value, nameof(Tool7z)); RefreshStatus(); } } }

    private string _toolPsxtract = "";
    public string ToolPsxtract { get => _toolPsxtract; set { if (SetProperty(ref _toolPsxtract, value)) { ValidateToolPath(value, nameof(ToolPsxtract)); RefreshStatus(); } } }

    private string _toolCiso = "";
    public string ToolCiso { get => _toolCiso; set { if (SetProperty(ref _toolCiso, value)) { ValidateToolPath(value, nameof(ToolCiso)); RefreshStatus(); } } }

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
    private bool _enableDatRename;

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
        set { if (SetProperty(ref _isSimpleMode, value)) OnPropertyChanged(nameof(IsExpertMode)); }
    }
    public bool IsExpertMode => !_isSimpleMode;

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
        set => SetProperty(ref _activePreset, value);
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

    // ═══ SETTINGS SYNC (TASK-123) ═══════════════════════════════════════

    private bool _syncingSettings;

    /// <summary>Push a property value from MainViewModel to Setup (avoids infinite loop).</summary>
    private void SyncToSetup(string propertyName, string value)
    {
        if (_syncingSettings) return;
        _syncingSettings = true;
        try
        {
            var prop = Setup.GetType().GetProperty(propertyName);
            if (prop is not null && prop.PropertyType == typeof(string))
            {
                var current = (string?)prop.GetValue(Setup);
                if (!string.Equals(current, value, StringComparison.Ordinal))
                    prop.SetValue(Setup, value);
            }
        }
        finally { _syncingSettings = false; }
    }

    /// <summary>Pull changed property from Setup back to MainViewModel.</summary>
    private void OnSetupPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_syncingSettings || e.PropertyName is null) return;
        var prop = GetType().GetProperty(e.PropertyName);
        var setupProp = Setup.GetType().GetProperty(e.PropertyName);
        if (prop is null || setupProp is null || prop.PropertyType != typeof(string)) return;

        var setupValue = (string?)setupProp.GetValue(Setup) ?? "";
        var localValue = (string?)prop.GetValue(this) ?? "";
        if (!string.Equals(setupValue, localValue, StringComparison.Ordinal))
        {
            _syncingSettings = true;
            try { prop.SetValue(this, setupValue); }
            finally { _syncingSettings = false; }
        }
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
            GameKeyPreviewOutput = Core.GameKeys.GameKeyNormalizer.Normalize(GameKeyPreviewInput);
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
            var catalogJson = File.ReadAllText(catalogPath);
            using var doc = JsonDocument.Parse(catalogJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return;

            // Build lookup: system name (lowercase) → console key
            var systemToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var consoleKey = entry.TryGetProperty("ConsoleKey", out var ck) ? ck.GetString() : null;
                var system = entry.TryGetProperty("System", out var sys) ? sys.GetString() : null;
                if (consoleKey is { Length: > 0 } && system is { Length: > 0 })
                    systemToKey.TryAdd(system, consoleKey);
            }

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

            var detectedMappings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var datFile in datFiles)
            {
                var fileName = Path.GetFileNameWithoutExtension(datFile) ?? "";

                // Strategy 1: Match by system name from catalog
                foreach (var (system, consoleKey) in systemToKey)
                {
                    if (fileName.Contains(system, StringComparison.OrdinalIgnoreCase)
                        || fileName.Contains(consoleKey, StringComparison.OrdinalIgnoreCase))
                    {
                        // If multiple DATs match, prefer the one with more entries (larger file)
                        if (!detectedMappings.ContainsKey(consoleKey))
                            detectedMappings[consoleKey] = datFile;
                        else if (new FileInfo(datFile).Length > new FileInfo(detectedMappings[consoleKey]).Length)
                            detectedMappings[consoleKey] = datFile;
                    }
                }

                // Strategy 2: Match common console name patterns in filename  
                foreach (var key in AllConsoleKeys)
                {
                    if (!detectedMappings.ContainsKey(key) && MatchesConsoleName(fileName, key))
                        detectedMappings[key] = datFile;
                }
            }

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

    private static bool MatchesConsoleName(string fileName, string consoleKey)
    {
        // Common display name patterns for well-known consoles
        var patterns = consoleKey.ToUpperInvariant() switch
        {
            "PSX" or "PS1" => new[] { "playstation", "psx", "ps1" },
            "PS2" => new[] { "playstation 2", "ps2" },
            "PS3" => new[] { "playstation 3", "ps3" },
            "PSP" => new[] { "playstation portable", "psp" },
            "DC" => new[] { "dreamcast", "dc" },
            "SAT" => new[] { "saturn", "sat" },
            "GC" => new[] { "gamecube", "gc", "ngc" },
            "WII" => new[] { "wii" },
            "NES" => new[] { "nintendo entertainment system", "nes" },
            "SNES" => new[] { "super nintendo", "snes", "super nes" },
            "N64" => new[] { "nintendo 64", "n64" },
            "GB" => new[] { "game boy", "gameboy" },
            "GBA" => new[] { "game boy advance", "gba" },
            "GBC" => new[] { "game boy color", "gbc" },
            "NDS" => new[] { "nintendo ds", "nds" },
            "3DS" => new[] { "nintendo 3ds", "3ds" },
            "NSW" => new[] { "nintendo switch", "nsw" },
            "MD" => new[] { "mega drive", "genesis", "megadrive" },
            "SMS" => new[] { "master system", "sms" },
            "GG" => new[] { "game gear", "gamegear" },
            "MCD" => new[] { "mega cd", "sega cd" },
            "ARCADE" => new[] { "arcade", "mame", "fbneo", "fbalpha" },
            "LYNX" => new[] { "lynx" },
            "JAG" => new[] { "jaguar" },
            "COLECO" => new[] { "colecovision", "coleco" },
            "INTV" => new[] { "intellivision" },
            "VEC" => new[] { "vectrex" },
            "PCE" => new[] { "pc engine", "turbografx" },
            "PCECD" => new[] { "pc engine cd", "turbografx cd" },
            "NEO" => new[] { "neo geo", "neogeo" },
            "NEOCD" => new[] { "neo geo cd" },
            _ => new[] { consoleKey.ToLowerInvariant() }
        };

        return patterns.Any(p => fileName.Contains(p, StringComparison.OrdinalIgnoreCase));
    }
}
