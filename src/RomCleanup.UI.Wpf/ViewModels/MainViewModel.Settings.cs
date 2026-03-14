using System.IO;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using ConflictPolicy = RomCleanup.UI.Wpf.Models.ConflictPolicy;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot { get => _trashRoot; set { if (SetField(ref _trashRoot, value)) ValidateDirectoryPath(value, nameof(TrashRoot)); } }

    private string _datRoot = "";
    public string DatRoot { get => _datRoot; set { if (SetField(ref _datRoot, value)) { ValidateDirectoryPath(value, nameof(DatRoot)); RefreshStatus(); } } }

    private string _auditRoot = "";
    public string AuditRoot { get => _auditRoot; set { if (SetField(ref _auditRoot, value)) ValidateDirectoryPath(value, nameof(AuditRoot)); } }

    private string _ps3DupesRoot = "";
    public string Ps3DupesRoot { get => _ps3DupesRoot; set { if (SetField(ref _ps3DupesRoot, value)) ValidateDirectoryPath(value, nameof(Ps3DupesRoot)); } }

    // ═══ TOOL PATHS (persisted) ═════════════════════════════════════════
    private string _toolChdman = "";
    public string ToolChdman { get => _toolChdman; set { if (SetField(ref _toolChdman, value)) { ValidateToolPath(value, nameof(ToolChdman)); RefreshStatus(); } } }

    private string _toolDolphin = "";
    public string ToolDolphin { get => _toolDolphin; set { if (SetField(ref _toolDolphin, value)) { ValidateToolPath(value, nameof(ToolDolphin)); RefreshStatus(); } } }

    private string _tool7z = "";
    public string Tool7z { get => _tool7z; set { if (SetField(ref _tool7z, value)) { ValidateToolPath(value, nameof(Tool7z)); RefreshStatus(); } } }

    private string _toolPsxtract = "";
    public string ToolPsxtract { get => _toolPsxtract; set { if (SetField(ref _toolPsxtract, value)) ValidateToolPath(value, nameof(ToolPsxtract)); } }

    private string _toolCiso = "";
    public string ToolCiso { get => _toolCiso; set { if (SetField(ref _toolCiso, value)) ValidateToolPath(value, nameof(ToolCiso)); } }

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

    private bool _confirmMove = true;
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

    private bool _isWatchModeActive;
    public bool IsWatchModeActive { get => _isWatchModeActive; set => SetField(ref _isWatchModeActive, value); }

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

    // Quick profile selector (STATUS BAR)
    private int _quickProfileIndex;
    public int QuickProfileIndex { get => _quickProfileIndex; set => SetField(ref _quickProfileIndex, value); }

    // RF-011: Profile name bound to Einstellungen ComboBox
    private string _profileName = "Standard";
    public string ProfileName { get => _profileName; set => SetField(ref _profileName, value); }

    // P1-005 / RD-005: Sidebar-Navigation im Einstellungen-Tab
    private string _selectedSettingsSection = "Sortieroptionen";
    public string SelectedSettingsSection
    {
        get => _selectedSettingsSection;
        set => SetField(ref _selectedSettingsSection, value);
    }

    // Sidebar-Navigation: Werkzeuge-Tab
    private string _selectedToolsSection = "Schnellzugriff";
    public string SelectedToolsSection
    {
        get => _selectedToolsSection;
        set => SetField(ref _selectedToolsSection, value);
    }

    // Sidebar-Navigation: Ergebnis-Tab
    private string _selectedResultSection = "Dashboard";
    public string SelectedResultSection
    {
        get => _selectedResultSection;
        set => SetField(ref _selectedResultSection, value);
    }

    // RD-006: Aktiver Preset-Name für SegmentedControl-Selektion
    private string? _activePreset;
    public string? ActivePreset
    {
        get => _activePreset;
        set => SetField(ref _activePreset, value);
    }

    // ═══ CONFLICT POLICY (UX-007: was YesNoCancel hack, now VM property) ═
    private ConflictPolicy _conflictPolicy = ConflictPolicy.Rename;
    public ConflictPolicy ConflictPolicy
    {
        get => _conflictPolicy;
        set => SetField(ref _conflictPolicy, value);
    }

    /// <summary>Index for ComboBox binding (0=Rename, 1=Skip, 2=Overwrite).</summary>
    public int ConflictPolicyIndex
    {
        get => (int)_conflictPolicy;
        set => ConflictPolicy = (ConflictPolicy)value;
    }

    // GameKey preview
    private string _gameKeyPreviewInput = "";
    public string GameKeyPreviewInput { get => _gameKeyPreviewInput; set => SetField(ref _gameKeyPreviewInput, value); }

    private string _gameKeyPreviewOutput = "–";
    public string GameKeyPreviewOutput { get => _gameKeyPreviewOutput; set => SetField(ref _gameKeyPreviewOutput, value); }

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

    private void OnBrowseToolPath(object? parameter)
    {
        var path = _dialog.BrowseFile("Executable auswählen", "Executables (*.exe)|*.exe|Alle (*.*)|*.*");
        if (path is null) return;
        switch (parameter as string)
        {
            case "Chdman": ToolChdman = path; break;
            case "Dolphin": ToolDolphin = path; break;
            case "7z": Tool7z = path; break;
            case "Psxtract": ToolPsxtract = path; break;
            case "Ciso": ToolCiso = path; break;
        }
    }

    private void OnBrowseFolderPath(object? parameter)
    {
        var path = _dialog.BrowseFolder("Ordner auswählen");
        if (path is null) return;
        switch (parameter as string)
        {
            case "Dat": DatRoot = path; break;
            case "Trash": TrashRoot = path; break;
            case "Audit": AuditRoot = path; break;
            case "Ps3": Ps3DupesRoot = path; break;
        }
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
        // TASK-032: In simple mode, translate SimpleRegionIndex to region preferences
        if (IsSimpleMode)
        {
            return SimpleRegionIndex switch
            {
                0 => ["EU", "DE", "WORLD", "US", "JP"],    // Europa
                1 => ["US", "WORLD", "EU", "JP"],           // Nordamerika
                2 => ["JP", "ASIA", "WORLD", "US", "EU"],   // Japan
                3 => ["WORLD", "EU", "US", "JP"],            // Weltweit
                _ => ["EU", "US", "WORLD", "JP"]
            };
        }

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
