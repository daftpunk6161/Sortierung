using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.Contracts.Ports;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using ConflictPolicy = RomCleanup.UI.Wpf.Models.ConflictPolicy;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-023: Setup/Configuration ViewModel — extracted from MainViewModel.Settings.cs + Validation.cs.
/// Manages paths, tool settings, region preferences, UI mode, filters, DAT config, and validation.
/// </summary>
public sealed partial class SetupViewModel : ObservableObject, INotifyDataErrorInfo
{
    private readonly IDialogService _dialog;
    private readonly IThemeService _theme;
    private readonly ISettingsService _settings;
    private readonly ILocalizationService _loc;

    /// <summary>Raised when a path/tool property changes that requires status refresh.</summary>
    public event Action? StatusRefreshRequested;

    public SetupViewModel(IThemeService theme, IDialogService dialog, ISettingsService settings, ILocalizationService? loc = null)
    {
        _theme = theme;
        _dialog = dialog;
        _settings = settings;
        _loc = loc ?? new LocalizationService();

        InitExtensionFilters();
        InitConsoleFilters();
        InitRegionItems();
    }

    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot { get => _trashRoot; set { if (SetProperty(ref _trashRoot, value)) ValidateDirectoryPath(value, nameof(TrashRoot)); } }

    private string _datRoot = "";
    public string DatRoot { get => _datRoot; set { if (SetProperty(ref _datRoot, value)) { ValidateDirectoryPath(value, nameof(DatRoot)); StatusRefreshRequested?.Invoke(); } } }

    private string _auditRoot = "";
    public string AuditRoot { get => _auditRoot; set { if (SetProperty(ref _auditRoot, value)) ValidateDirectoryPath(value, nameof(AuditRoot)); } }

    private string _ps3DupesRoot = "";
    public string Ps3DupesRoot { get => _ps3DupesRoot; set { if (SetProperty(ref _ps3DupesRoot, value)) ValidateDirectoryPath(value, nameof(Ps3DupesRoot)); } }

    // ═══ TOOL PATHS (persisted) ═════════════════════════════════════════
    private string _toolChdman = "";
    public string ToolChdman { get => _toolChdman; set { if (SetProperty(ref _toolChdman, value)) { ValidateToolPath(value, nameof(ToolChdman)); StatusRefreshRequested?.Invoke(); } } }

    private string _toolDolphin = "";
    public string ToolDolphin { get => _toolDolphin; set { if (SetProperty(ref _toolDolphin, value)) { ValidateToolPath(value, nameof(ToolDolphin)); StatusRefreshRequested?.Invoke(); } } }

    private string _tool7z = "";
    public string Tool7z { get => _tool7z; set { if (SetProperty(ref _tool7z, value)) { ValidateToolPath(value, nameof(Tool7z)); StatusRefreshRequested?.Invoke(); } } }

    private string _toolPsxtract = "";
    public string ToolPsxtract { get => _toolPsxtract; set { if (SetProperty(ref _toolPsxtract, value)) { ValidateToolPath(value, nameof(ToolPsxtract)); StatusRefreshRequested?.Invoke(); } } }

    private string _toolCiso = "";
    public string ToolCiso { get => _toolCiso; set { if (SetProperty(ref _toolCiso, value)) { ValidateToolPath(value, nameof(ToolCiso)); StatusRefreshRequested?.Invoke(); } } }

    // ═══ BOOLEAN FLAGS (persisted) ══════════════════════════════════════
    [ObservableProperty]
    private bool _sortConsole = true;

    [ObservableProperty]
    private bool _aliasKeying;

    private bool _useDat;
    public bool UseDat { get => _useDat; set { if (SetProperty(ref _useDat, value)) StatusRefreshRequested?.Invoke(); } }

    [ObservableProperty]
    private bool _datFallback;

    [ObservableProperty]
    private bool _dryRun = true;

    private bool _convertEnabled;
    public bool ConvertEnabled { get => _convertEnabled; set { if (SetProperty(ref _convertEnabled, value)) StatusRefreshRequested?.Invoke(); } }

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

    [ObservableProperty]
    private string _datHashType = "SHA1";

    // ═══ REGION PREFERENCES ═════════════════════════════════════════════
    /// <summary>GUI-029: Region preferences as bindable collection with priority ordering.</summary>
    public ObservableCollection<RegionItem> RegionItems { get; } = [];

    private void InitRegionItems()
    {
        var regions = new (string code, string key, string flag, bool active)[]
        {
            ("EU",    "Region.EU",    "🇪🇺", true),
            ("US",    "Region.US",    "🇺🇸", true),
            ("JP",    "Region.JP",    "🇯🇵", true),
            ("WORLD", "Region.WORLD", "🌍", true),
            ("DE",    "Region.DE",    "🇩🇪", false),
            ("FR",    "Region.FR",    "🇫🇷", false),
            ("IT",    "Region.IT",    "🇮🇹", false),
            ("ES",    "Region.ES",    "🇪🇸", false),
            ("AU",    "Region.AU",    "🇦🇺", false),
            ("ASIA",  "Region.ASIA",  "🌏", false),
            ("KR",    "Region.KR",    "🇰🇷", false),
            ("CN",    "Region.CN",    "🇨🇳", false),
            ("BR",    "Region.BR",    "🇧🇷", false),
            ("NL",    "Region.NL",    "🇳🇱", false),
            ("SE",    "Region.SE",    "🇸🇪", false),
            ("SCAN",  "Region.SCAN",  "🇳🇴", false),
        };
        for (int i = 0; i < regions.Length; i++)
        {
            var (code, key, flag, active) = regions[i];
            RegionItems.Add(new RegionItem
            {
                Code = code,
                DisplayName = _loc[key],
                FlagEmoji = flag,
                IsActive = active,
                Priority = i
            });
        }
    }

    /// <summary>Build the preferred regions array from RegionItems (active only, ordered by priority).</summary>
    public string[] GetPreferredRegions()
    {
        return RegionItems
            .Where(r => r.IsActive)
            .OrderBy(r => r.Priority)
            .Select(r => r.Code)
            .ToArray();
    }

    // ═══ UI MODE ════════════════════════════════════════════════════════
    private bool _isSimpleMode = true;
    public bool IsSimpleMode
    {
        get => _isSimpleMode;
        set { if (SetProperty(ref _isSimpleMode, value)) OnPropertyChanged(nameof(IsExpertMode)); }
    }
    public bool IsExpertMode => !_isSimpleMode;

    [ObservableProperty]
    private int _simpleRegionIndex;

    [ObservableProperty]
    private bool _simpleDupes = true;

    [ObservableProperty]
    private bool _simpleJunk = true;

    [ObservableProperty]
    private bool _simpleSort = true;

    private int _quickProfileIndex;
    public int QuickProfileIndex { get => _quickProfileIndex; set => SetProperty(ref _quickProfileIndex, value); }

    private string _profileName = "Standard";
    public string ProfileName { get => _profileName; set => SetProperty(ref _profileName, value); }

    // ═══ SIDEBAR NAVIGATION ═════════════════════════════════════════════
    private string _selectedSettingsSection = "Sortieroptionen";
    public string SelectedSettingsSection { get => _selectedSettingsSection; set => SetProperty(ref _selectedSettingsSection, value); }

    private string _selectedResultSection = "Dashboard";
    public string SelectedResultSection { get => _selectedResultSection; set => SetProperty(ref _selectedResultSection, value); }

    private string? _activePreset;
    public string? ActivePreset { get => _activePreset; set => SetProperty(ref _activePreset, value); }

    // ═══ CONFLICT POLICY ═════════════════════════════════════════════════
    private ConflictPolicy _conflictPolicy = ConflictPolicy.Rename;
    public ConflictPolicy ConflictPolicy { get => _conflictPolicy; set => SetProperty(ref _conflictPolicy, value); }
    public int ConflictPolicyIndex { get => (int)_conflictPolicy; set => ConflictPolicy = (ConflictPolicy)value; }

    // ═══ GAMEKEY PREVIEW ═════════════════════════════════════════════════
    private string _gameKeyPreviewInput = "";
    public string GameKeyPreviewInput { get => _gameKeyPreviewInput; set => SetProperty(ref _gameKeyPreviewInput, value); }

    private string _gameKeyPreviewOutput = "–";
    public string GameKeyPreviewOutput { get => _gameKeyPreviewOutput; set => SetProperty(ref _gameKeyPreviewOutput, value); }

    // ═══ THEME ═══════════════════════════════════════════════════════════
    public string CurrentThemeName => _theme.Current.ToString();
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

    // ═══ EXTENSION FILTERS ═══════════════════════════════════════════════
    public ObservableCollection<ExtensionFilterItem> ExtensionFilters { get; } = [];
    public ICollectionView ExtensionFiltersView { get; private set; } = null!;

    public string[] GetSelectedExtensions() =>
        ExtensionFilters.Where(e => e.IsChecked).Select(e => e.Extension).ToArray();

    private void InitExtensionFilters()
    {
        var items = new (string ext, string cat, string tip)[]
        {
            (".chd", "Disc-Images", "CHD Disk-Image"),
            (".iso", "Disc-Images", "ISO-Abbild"),
            (".cue", "Disc-Images", "CUE Steuerdatei"),
            (".gdi", "Disc-Images", "GDI (Dreamcast)"),
            (".img", "Disc-Images", "IMG Disk-Image"),
            (".bin", "Disc-Images", "BIN (CD-Image)"),
            (".cso", "Disc-Images", "Compressed ISO (PSP)"),
            (".pbp", "Disc-Images", "PBP-Paket (PSP)"),
            (".zip", "Archive", "ZIP-Archiv"),
            (".7z",  "Archive", "7-Zip-Archiv"),
            (".rar", "Archive", "RAR-Archiv"),
            (".nes", "Cartridge / Modern", "NES ROM"),
            (".gba", "Cartridge / Modern", "Game Boy Advance ROM"),
            (".nds", "Cartridge / Modern", "Nintendo DS ROM"),
            (".nsp", "Cartridge / Modern", "NSP (Nintendo Switch)"),
            (".xci", "Cartridge / Modern", "XCI Cartridge-Image"),
            (".wbfs","Cartridge / Modern", "WBFS (Wii Backup)"),
            (".rvz", "Cartridge / Modern", "RVZ (GC/Wii, Dolphin)"),
        };
        foreach (var (ext, cat, tip) in items)
            ExtensionFilters.Add(new ExtensionFilterItem { Extension = ext, Category = cat, ToolTip = tip });

        ExtensionFiltersView = CollectionViewSource.GetDefaultView(ExtensionFilters);
        ExtensionFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExtensionFilterItem.Category)));
    }

    // ═══ CONSOLE FILTERS ═════════════════════════════════════════════════
    public ObservableCollection<ConsoleFilterItem> ConsoleFilters { get; } = [];
    public ICollectionView ConsoleFiltersView { get; private set; } = null!;

    public string[] GetSelectedConsoles() =>
        ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToArray();

    private void InitConsoleFilters()
    {
        var items = new (string key, string display, string cat)[]
        {
            ("PS1",    "PlayStation",               "Sony"),
            ("PS2",    "PlayStation 2",             "Sony"),
            ("PS3",    "PlayStation 3",             "Sony"),
            ("PSP",    "PSP",                       "Sony"),
            ("NES",    "NES / Famicom",             "Nintendo"),
            ("SNES",   "SNES / Super Famicom",      "Nintendo"),
            ("N64",    "Nintendo 64",               "Nintendo"),
            ("GC",     "GameCube",                  "Nintendo"),
            ("WII",    "Wii",                       "Nintendo"),
            ("WIIU",   "Wii U",                     "Nintendo"),
            ("SWITCH", "Nintendo Switch",           "Nintendo"),
            ("GB",     "Game Boy",                  "Nintendo"),
            ("GBC",    "Game Boy Color",            "Nintendo"),
            ("GBA",    "Game Boy Advance",          "Nintendo"),
            ("NDS",    "Nintendo DS",               "Nintendo"),
            ("3DS",    "Nintendo 3DS",              "Nintendo"),
            ("MD",     "Mega Drive / Genesis",      "Sega"),
            ("SCD",    "Mega-CD / Sega CD",         "Sega"),
            ("SAT",    "Saturn",                    "Sega"),
            ("DC",     "Dreamcast",                 "Sega"),
            ("SMS",    "Master System",             "Sega"),
            ("GG",     "Game Gear",                 "Sega"),
            ("ARCADE", "Arcade / MAME / FBNeo",     "Andere"),
            ("NEOGEO", "Neo Geo",                   "Andere"),
            ("NEOCD",  "Neo Geo CD",                "Andere"),
            ("PCE",    "PC Engine / TurboGrafx-16", "Andere"),
            ("PCECD",  "PC Engine CD",              "Andere"),
            ("DOS",    "DOS / PC",                  "Andere"),
            ("3DO",    "3DO",                       "Andere"),
            ("JAG",    "Atari Jaguar",              "Andere"),
        };
        foreach (var (key, display, cat) in items)
            ConsoleFilters.Add(new ConsoleFilterItem { Key = key, DisplayName = display, Category = cat });

        ConsoleFiltersView = CollectionViewSource.GetDefaultView(ConsoleFilters);
        ConsoleFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConsoleFilterItem.Category)));
    }

    // ═══ SETTINGS HANDLERS ══════════════════════════════════════════════
    public void OnBrowseToolPath(string? parameter)
    {
        var path = _dialog.BrowseFile(_loc["Dialog.SelectExe"], _loc["Dialog.ExeFilter"]);
        if (path is null) return;
        switch (parameter)
        {
            case "Chdman": ToolChdman = path; break;
            case "Dolphin": ToolDolphin = path; break;
            case "7z": Tool7z = path; break;
            case "Psxtract": ToolPsxtract = path; break;
            case "Ciso": ToolCiso = path; break;
        }
    }

    public void OnBrowseFolderPath(string? parameter)
    {
        var path = _dialog.BrowseFolder(_loc["Dialog.SelectFolder"]);
        if (path is null) return;
        switch (parameter)
        {
            case "Dat": DatRoot = path; break;
            case "Trash": TrashRoot = path; break;
            case "Audit": AuditRoot = path; break;
            case "Ps3": Ps3DupesRoot = path; break;
        }
    }

    public void OnPresetSafeDryRun()
    {
        DryRun = true;
        ConvertEnabled = false;
        AggressiveJunk = false;
        foreach (var r in RegionItems)
            r.IsActive = r.Code is "EU" or "US" or "JP" or "WORLD";
        ActivePreset = "SafeDryRun";
        StatusRefreshRequested?.Invoke();
    }

    public void OnPresetFullSort()
    {
        DryRun = true;
        SortConsole = true;
        foreach (var r in RegionItems)
            r.IsActive = r.Code is "EU" or "US" or "JP" or "WORLD";
        ActivePreset = "FullSort";
        StatusRefreshRequested?.Invoke();
    }

    public void OnPresetConvert()
    {
        DryRun = true;
        ConvertEnabled = true;
        ActivePreset = "Convert";
        StatusRefreshRequested?.Invoke();
    }

    public void OnThemeToggle()
    {
        _theme.Toggle();
        OnPropertyChanged(nameof(ThemeToggleText));
        OnPropertyChanged(nameof(CurrentThemeName));
    }

    public void OnGameKeyPreview()
    {
        try
        {
            GameKeyPreviewOutput = Core.GameKeys.GameKeyNormalizer.Normalize(GameKeyPreviewInput);
        }
        catch (Exception ex)
        {
            GameKeyPreviewOutput = _loc.Format("Error.Generic", ex.Message);
        }
    }

    public void LoadInitialSettings(string? lastAuditPath)
    {
        var dto = _settings.Load();
        if (dto is not null)
            ApplySettingsDto(dto);

        if (Enum.TryParse<AppTheme>(_settings.LastTheme, true, out var savedTheme) && savedTheme != _theme.Current)
        {
            _theme.ApplyTheme(savedTheme);
            OnPropertyChanged(nameof(ThemeToggleText));
            OnPropertyChanged(nameof(CurrentThemeName));
        }

        StatusRefreshRequested?.Invoke();
    }

    /// <summary>Apply a SettingsDto to this ViewModel's properties.</summary>
    public void ApplySettingsDto(SettingsDto dto)
    {
        LogLevel = dto.LogLevel;
        AggressiveJunk = dto.AggressiveJunk;
        AliasKeying = dto.AliasKeying;

        // Regions
        var active = new HashSet<string>(dto.PreferredRegions, StringComparer.OrdinalIgnoreCase);
        foreach (var region in RegionItems)
            region.IsActive = active.Contains(region.Code);

        // Tool paths
        ToolChdman = dto.ToolChdman;
        Tool7z = dto.Tool7z;
        ToolDolphin = dto.ToolDolphin;
        ToolPsxtract = dto.ToolPsxtract;
        ToolCiso = dto.ToolCiso;

        // DAT
        UseDat = dto.UseDat;
        DatRoot = dto.DatRoot;
        DatHashType = dto.DatHashType;
        DatFallback = dto.DatFallback;

        // Paths
        TrashRoot = dto.TrashRoot;
        AuditRoot = dto.AuditRoot;
        Ps3DupesRoot = dto.Ps3DupesRoot;

        // UI
        SortConsole = dto.SortConsole;
        DryRun = dto.DryRun;
        ConvertEnabled = dto.ConvertEnabled;
        ConfirmMove = dto.ConfirmMove;
        ConflictPolicy = dto.ConflictPolicy;

        // Roots loaded separately by MainViewModel
    }

    /// <summary>Build a SettingsDto from current state for saving.</summary>
    public SettingsDto ToSettingsDto(string? lastAuditPath = null, string[]? roots = null) => new()
    {
        LogLevel = LogLevel,
        AggressiveJunk = AggressiveJunk,
        AliasKeying = AliasKeying,
        PreferredRegions = GetPreferredRegions(),
        ToolChdman = ToolChdman,
        Tool7z = Tool7z,
        ToolDolphin = ToolDolphin,
        ToolPsxtract = ToolPsxtract,
        ToolCiso = ToolCiso,
        UseDat = UseDat,
        DatRoot = DatRoot,
        DatHashType = DatHashType,
        DatFallback = DatFallback,
        TrashRoot = TrashRoot,
        AuditRoot = AuditRoot,
        Ps3DupesRoot = Ps3DupesRoot,
        LastAuditPath = lastAuditPath,
        Roots = roots ?? [],
        SortConsole = SortConsole,
        DryRun = DryRun,
        ConvertEnabled = ConvertEnabled,
        ConfirmMove = ConfirmMove,
        ConflictPolicy = ConflictPolicy,
        Theme = CurrentThemeName,
    };

    /// <summary>Build a flat key-value config map from current state (for diff/export).</summary>
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

    // ═══ VALIDATION (INotifyDataErrorInfo) ═══════════════════════════════
    private readonly Dictionary<string, string> _validationErrors = new();

    public bool HasErrors => _validationErrors.Count > 0;

    public event EventHandler<DataErrorsChangedEventArgs>? ErrorsChanged;

    public IEnumerable GetErrors(string? propertyName)
    {
        if (propertyName is not null && _validationErrors.TryGetValue(propertyName, out var error))
            return new[] { error };
        return Array.Empty<string>();
    }

    private void ValidateToolPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!File.Exists(value))
            SetError(propertyName, _loc.Format("Error.FileNotFound", Path.GetFileName(value)));
        else
            ClearError(propertyName);
    }

    private void ValidateDirectoryPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (!Directory.Exists(value))
            SetError(propertyName, _loc["Error.DirNotFound"]);
        else
            ClearError(propertyName);
    }

    private void SetError(string propertyName, string error)
    {
        _validationErrors[propertyName] = error;
        ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }

    private void ClearError(string propertyName)
    {
        if (_validationErrors.Remove(propertyName))
            ErrorsChanged?.Invoke(this, new DataErrorsChangedEventArgs(propertyName));
    }
}
