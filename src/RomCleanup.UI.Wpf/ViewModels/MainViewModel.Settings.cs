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
        nameof(EnableDatRename),
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
                        AddLog("Einstellungen automatisch gespeichert.", "DEBUG");
                    }
                    catch (Exception ex)
                    {
                        AddLog($"Auto-Save fehlgeschlagen: {ex.Message}", "WARN");
                    }
                });
            },
            null,
            TimeSpan.FromSeconds(2),
            System.Threading.Timeout.InfiniteTimeSpan);
    }
    // ═══ PATH PROPERTIES (persisted) ════════════════════════════════════
    private string _trashRoot = "";
    public string TrashRoot { get => _trashRoot; set { if (SetProperty(ref _trashRoot, value)) ValidateDirectoryPath(value, nameof(TrashRoot)); } }

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
    public string ToolChdman { get => _toolChdman; set { if (SetProperty(ref _toolChdman, value)) { ValidateToolPath(value, nameof(ToolChdman)); RefreshStatus(); } } }

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
        if (TrySaveSettings())
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
            AddLog("DAT-Ordner nicht gefunden. Bitte DAT-Ordner zuerst setzen.", "WARN");
            return;
        }

        // Load dat-catalog.json for console key → system name mapping
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        if (!File.Exists(catalogPath))
        {
            AddLog("dat-catalog.json nicht gefunden.", "WARN");
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
                AddLog("Keine DAT-Dateien im DAT-Ordner gefunden.", "WARN");
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
