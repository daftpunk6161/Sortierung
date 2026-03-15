using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using ConflictPolicy = RomCleanup.UI.Wpf.Models.ConflictPolicy;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot { get => _trashRoot; set { if (SetProperty(ref _trashRoot, value)) ValidateDirectoryPath(value, nameof(TrashRoot)); } }

    private string _datRoot = "";
    public string DatRoot { get => _datRoot; set { if (SetProperty(ref _datRoot, value)) { ValidateDirectoryPath(value, nameof(DatRoot)); RefreshStatus(); } } }

    private string _auditRoot = "";
    public string AuditRoot { get => _auditRoot; set { if (SetProperty(ref _auditRoot, value)) ValidateDirectoryPath(value, nameof(AuditRoot)); } }

    private string _ps3DupesRoot = "";
    public string Ps3DupesRoot { get => _ps3DupesRoot; set { if (SetProperty(ref _ps3DupesRoot, value)) ValidateDirectoryPath(value, nameof(Ps3DupesRoot)); } }

    // ═══ TOOL PATHS (persisted) ═════════════════════════════════════════
    private string _toolChdman = "";
    public string ToolChdman { get => _toolChdman; set { if (SetProperty(ref _toolChdman, value)) { ValidateToolPath(value, nameof(ToolChdman)); RefreshStatus(); } } }

    private string _toolDolphin = "";
    public string ToolDolphin { get => _toolDolphin; set { if (SetProperty(ref _toolDolphin, value)) { ValidateToolPath(value, nameof(ToolDolphin)); RefreshStatus(); } } }

    private string _tool7z = "";
    public string Tool7z { get => _tool7z; set { if (SetProperty(ref _tool7z, value)) { ValidateToolPath(value, nameof(Tool7z)); RefreshStatus(); } } }

    private string _toolPsxtract = "";
    public string ToolPsxtract { get => _toolPsxtract; set { if (SetProperty(ref _toolPsxtract, value)) ValidateToolPath(value, nameof(ToolPsxtract)); } }

    private string _toolCiso = "";
    public string ToolCiso { get => _toolCiso; set { if (SetProperty(ref _toolCiso, value)) ValidateToolPath(value, nameof(ToolCiso)); } }

    // ═══ BOOLEAN FLAGS (persisted) ══════════════════════════════════════
    [ObservableProperty]
    private bool _sortConsole = true;

    [ObservableProperty]
    private bool _aliasKeying;

    private bool _useDat;
    public bool UseDat { get => _useDat; set { if (SetProperty(ref _useDat, value)) RefreshStatus(); } }

    [ObservableProperty]
    private bool _datFallback;

    [ObservableProperty]
    private bool _dryRun = true;

    private bool _convertEnabled;
    public bool ConvertEnabled { get => _convertEnabled; set { if (SetProperty(ref _convertEnabled, value)) RefreshStatus(); } }

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

    public string ThemeToggleText => _theme.Current switch
    {
        AppTheme.Dark => "☀ Hell",
        AppTheme.Light => "◐ Kontrast",
        AppTheme.HighContrast => "☾ Dunkel",
        _ => "☾ Dunkel",
    };

    // ═══ SETTINGS HANDLERS ══════════════════════════════════════════════

    private void OnBrowseToolPath(string? parameter)
    {
        var path = _dialog.BrowseFile("Executable auswählen", "Executables (*.exe)|*.exe|Alle (*.*)|*.*");
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
        var path = _dialog.BrowseFolder("Ordner auswählen");
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
        if (_settings.SaveFrom(this, LastAuditPath))
            AddLog("Einstellungen gespeichert.", "INFO");
        else
            AddLog("Einstellungen konnten nicht gespeichert werden.", "ERROR");
    }

    private void OnLoadSettings()
    {
        _settings.LoadInto(this);
        RefreshStatus();
        AddLog("Einstellungen geladen.", "INFO");
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
        }

        RefreshStatus();
    }

    /// <summary>Save settings (called from code-behind on close / timer).</summary>
    public void SaveSettings() => _settings.SaveFrom(this, LastAuditPath);

    private void OnThemeToggle()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(CurrentThemeName));
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
}
