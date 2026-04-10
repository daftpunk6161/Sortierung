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
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Watch;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Profiles;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using ConflictPolicy = Romulus.UI.Wpf.Models.ConflictPolicy;
using RunState = Romulus.UI.Wpf.Models.RunState;

namespace Romulus.UI.Wpf.ViewModels;

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
    private readonly ScheduleService _scheduleService = new();
    private CancellationTokenSource? _cts;
    // V2-THR-H02: Lock for consistent CTS access between OnCancel and CreateRunCancellation
    private readonly object _ctsLock = new();
    private static readonly TimeSpan InlineMoveConfirmDebounceDelay = TimeSpan.FromMilliseconds(1500);
    private DateTime _inlineMoveUnlockAtUtc = DateTime.MinValue;
    private int _inlineMoveConfirmDebounceToken;

    // ═══ CHILD VIEWMODELS (GUI-021: shell ViewModel pattern) ════════════
    /// <summary>Shell state: navigation, overlays, wizard, notifications.</summary>
    public ShellViewModel Shell { get; }
    /// <summary>Configuration, paths, regions, filters, validation.</summary>
    public SetupViewModel Setup { get; }
    /// <summary>Tool catalog, quick access, search, categories.</summary>
    public ToolsViewModel Tools { get; }
    /// <summary>Run pipeline state, progress, dashboard, rollback.</summary>
    public RunViewModel Run { get; }
    /// <summary>Command Palette overlay (Ctrl+K): fuzzy search + execute.</summary>
    public CommandPaletteViewModel CommandPalette { get; }
    /// <summary>DatAudit results: read-only audit table with filter/sort.</summary>
    public DatAuditViewModel DatAudit { get; }
    /// <summary>ADR-0020: DAT Catalog management — browsable grid with status + batch actions.</summary>
    public DatCatalogViewModel DatCatalog { get; }
    /// <summary>TASK-125: Conversion preview before execution.</summary>
    public ConversionPreviewViewModel ConversionPreview { get; }

    public MainViewModel() : this(new ThemeService(), new WpfDialogService()) { }

    public MainViewModel(
        IThemeService theme,
        IDialogService dialog,
        ISettingsService? settings = null,
        IRunService? runService = null,
        ILocalizationService? loc = null,
        RunProfileService? runProfileService = null,
        RunConfigurationMaterializer? runConfigurationMaterializer = null)
    {
        _theme = theme;
        _dialog = dialog;
        _settings = settings ?? new SettingsService();
        _runService = runService ?? new RunService();
        _loc = loc ?? new LocalizationService();
        _syncContext = SynchronizationContext.Current;

        // ── Child ViewModels (GUI-021) ────────────────────────────────
        Shell = new ShellViewModel(_loc, DeferCommandRequery);
        Shell.IsSimpleMode = _isSimpleMode;
        Shell.PropertyChanged += OnShellStatePropertyChanged;
        Setup = new SetupViewModel(_theme, _dialog, _settings, _loc);
        Tools = new ToolsViewModel(_loc);
        Tools.SetSimpleMode(_isSimpleMode);
        Run = new RunViewModel();
        CommandPalette = new CommandPaletteViewModel(_loc);
        DatAudit = new DatAuditViewModel(_loc, _dialog);
        DatCatalog = new DatCatalogViewModel(_loc, _dialog, () => DatRoot, AddLog);
        ConversionPreview = new ConversionPreviewViewModel(_loc);
        InitializeRunConfigurationServices(runProfileService, runConfigurationMaterializer);

        // Wire child VM events
        Setup.StatusRefreshRequested += () => RefreshStatus();
        Setup.PropertyChanged += OnSetupPropertyChanged;
        Run.CommandRequeryRequested += DeferCommandRequery;
        Run.PropertyChanged += OnRunPropertyChanged;

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
        BrowseDatMappingFileCommand = new RelayCommand<object?>(OnBrowseDatMappingFileCommand);

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
            ArmInlineMoveConfirmDebounce,
            () => ShowStartMoveButton && !Shell.ShowMoveInlineConfirm);
        CancelStartMoveCommand = new RelayCommand(
            ResetInlineMoveConfirmDebounce,
            () => Shell.ShowMoveInlineConfirm);
        StartMoveCommand = new RelayCommand(
            () =>
            {
                ResetInlineMoveConfirmDebounce();
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

        // Wire watch-mode auto-run trigger
        _watchService.RunTriggered += OnWatchRunTriggered;
        _watchService.WatcherError += OnWatcherError;
        _scheduleService.Triggered += OnScheduledRunTriggered;
    }

    private void ArmInlineMoveConfirmDebounce()
    {
        Shell.ShowMoveInlineConfirm = true;
        _inlineMoveUnlockAtUtc = DateTime.UtcNow.Add(InlineMoveConfirmDebounceDelay);
        var token = Interlocked.Increment(ref _inlineMoveConfirmDebounceToken);

        OnPropertyChanged(nameof(CanExecuteInlineStartMove));
        OnPropertyChanged(nameof(InlineMoveConfirmHint));
        DeferCommandRequery();

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(InlineMoveConfirmDebounceDelay).ConfigureAwait(false);
            }
            catch
            {
                return;
            }

            if (token != _inlineMoveConfirmDebounceToken || !Shell.ShowMoveInlineConfirm)
                return;

            if (_syncContext is null)
            {
                OnPropertyChanged(nameof(CanExecuteInlineStartMove));
                OnPropertyChanged(nameof(InlineMoveConfirmHint));
                DeferCommandRequery();
                return;
            }

            _syncContext.Post(_ =>
            {
                OnPropertyChanged(nameof(CanExecuteInlineStartMove));
                OnPropertyChanged(nameof(InlineMoveConfirmHint));
                DeferCommandRequery();
            }, null);
        });
    }

    private void ResetInlineMoveConfirmDebounce()
    {
        Interlocked.Increment(ref _inlineMoveConfirmDebounceToken);
        _inlineMoveUnlockAtUtc = DateTime.MinValue;
        Shell.ShowMoveInlineConfirm = false;
        OnPropertyChanged(nameof(CanExecuteInlineStartMove));
        OnPropertyChanged(nameof(InlineMoveConfirmHint));
        DeferCommandRequery();
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
        OnPropertyChanged(nameof(CanAdvanceWizard));
    }

    public RelayCommand ToggleCommandPaletteCommand { get; private set; } = null!;

    // ═══ EXTENSION FILTERS (UX-004: VM-bound, replaces code-behind x:Name checkboxes) ═══
    public ObservableCollection<ExtensionFilterItem> ExtensionFilters { get; } = [];

    /// <summary>Grouped view for XAML binding with category headers.</summary>
    public ICollectionView ExtensionFiltersView { get; private set; } = null!;

    /// <summary>Returns checked extensions, or empty array if none selected (= scan all).</summary>
    public string[] GetSelectedExtensions() =>
        ExtensionFilters.Where(e => e.IsChecked).Select(e => e.Extension).ToArray();

    private void InitExtensionFilters()
    {
        var items = new (string ext, string cat, string tip)[]
        {
            // Disc images
            (".chd", "Disc-Images", "CHD Disk-Image"),
            (".iso", "Disc-Images", "ISO-Abbild"),
            (".cue", "Disc-Images", "CUE Steuerdatei"),
            (".gdi", "Disc-Images", "GDI (Dreamcast)"),
            (".cdi", "Disc-Images", "CDI (Dreamcast)"),
            (".img", "Disc-Images", "IMG Disk-Image"),
            (".bin", "Disc-Images", "BIN (CD-Image)"),
            (".cso", "Disc-Images", "Compressed ISO (PSP)"),
            (".pbp", "Disc-Images", "PBP-Paket (PSP)"),
            (".ecm", "Disc-Images", "ECM (Error Code Modeler)"),
            (".ccd", "Disc-Images", "CloneCD Control File"),
            // Archives
            (".zip", "Archive", "ZIP-Archiv"),
            (".7z",  "Archive", "7-Zip-Archiv"),
            (".rar", "Archive", "RAR-Archiv"),
            // Nintendo
            (".nes", "Nintendo", "NES ROM"),
            (".fds", "Nintendo", "Famicom Disk System"),
            (".sfc", "Nintendo", "Super Famicom ROM"),
            (".smc", "Nintendo", "Super Nintendo ROM"),
            (".gb",  "Nintendo", "Game Boy ROM"),
            (".gbc", "Nintendo", "Game Boy Color ROM"),
            (".gba", "Nintendo", "Game Boy Advance ROM"),
            (".vb",  "Nintendo", "Virtual Boy ROM"),
            (".n64", "Nintendo", "Nintendo 64 ROM"),
            (".z64", "Nintendo", "Nintendo 64 ROM (z64)"),
            (".nds", "Nintendo", "Nintendo DS ROM"),
            (".3ds", "Nintendo", "Nintendo 3DS ROM"),
            (".nsp", "Nintendo", "NSP (Nintendo Switch)"),
            (".xci", "Nintendo", "XCI Cartridge-Image"),
            (".wbfs", "Nintendo", "WBFS (Wii Backup)"),
            (".rvz", "Nintendo", "RVZ (GC/Wii, Dolphin)"),
            (".wux", "Nintendo", "WUX (Wii U compressed)"),
            (".wad", "Nintendo", "WAD (Wii)"),
            // Sega
            (".md",  "Sega", "Mega Drive / Genesis ROM"),
            (".gen", "Sega", "Genesis ROM"),
            (".sms", "Sega", "Master System ROM"),
            (".gg",  "Sega", "Game Gear ROM"),
            (".sg",  "Sega", "SG-1000 ROM"),
            (".32x", "Sega", "Sega 32X ROM"),
            // NEC / SNK / Bandai
            (".pce", "NEC / SNK / Bandai", "PC Engine / TurboGrafx ROM"),
            (".pcfx", "NEC / SNK / Bandai", "PC-FX ROM"),
            (".sgx", "NEC / SNK / Bandai", "SuperGrafx ROM"),
            (".ngp", "NEC / SNK / Bandai", "Neo Geo Pocket ROM"),
            (".ws",  "NEC / SNK / Bandai", "WonderSwan ROM"),
            (".wsc", "NEC / SNK / Bandai", "WonderSwan Color ROM"),
            // Atari
            (".a26", "Atari", "Atari 2600 ROM"),
            (".a52", "Atari", "Atari 5200 ROM"),
            (".a78", "Atari", "Atari 7800 ROM"),
            (".lnx", "Atari", "Atari Lynx ROM"),
            (".j64", "Atari", "Atari Jaguar ROM"),
            (".st",  "Atari", "Atari ST Floppy"),
            (".atr", "Atari", "Atari 8-Bit Disk"),
            (".xex", "Atari", "Atari XEGS Executable"),
            // Computer / Retro
            (".tzx", "Computer / Retro", "ZX Spectrum Tape"),
            (".adf", "Computer / Retro", "Amiga Disk File"),
            (".d64", "Computer / Retro", "C64 Disk Image"),
            (".t64", "Computer / Retro", "C64 Tape Image"),
            (".mx1", "Computer / Retro", "MSX1 ROM"),
            (".mx2", "Computer / Retro", "MSX2 ROM"),
            // Classic / Nische
            (".col", "Klassik / Nische", "ColecoVision ROM"),
            (".int", "Klassik / Nische", "Intellivision ROM"),
            (".o2",  "Klassik / Nische", "Odyssey 2 ROM"),
            (".vec", "Klassik / Nische", "Vectrex ROM"),
            (".min", "Klassik / Nische", "Pokemon Mini ROM"),
            (".tgc", "Klassik / Nische", "PC Engine TurboChip"),
        };
        foreach (var (ext, cat, tip) in items)
        {
            var item = new ExtensionFilterItem { Extension = ext, Category = cat, ToolTip = tip };
            item.PropertyChanged += OnExtensionCheckedChanged;
            ExtensionFilters.Add(item);
        }

        ExtensionFiltersView = CollectionViewSource.GetDefaultView(ExtensionFilters);
        ExtensionFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ExtensionFilterItem.Category)));
    }

    private void OnExtensionCheckedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ExtensionFilterItem.IsChecked))
        {
            OnPropertyChanged(nameof(SelectedExtensionCount));
            OnPropertyChanged(nameof(ExtensionCountDisplay));
        }
    }

    public int SelectedExtensionCount => ExtensionFilters.Count(e => e.IsChecked);

    public string ExtensionCountDisplay
    {
        get
        {
            int sel = SelectedExtensionCount;
            return sel == 0 ? $"0 / {ExtensionFilters.Count}" : $"{sel} / {ExtensionFilters.Count}";
        }
    }

    [RelayCommand]
    private void SelectAllExtensions()
    {
        foreach (var e in ExtensionFilters) e.IsChecked = true;
        OnPropertyChanged(nameof(SelectedExtensionCount));
        OnPropertyChanged(nameof(ExtensionCountDisplay));
    }

    [RelayCommand]
    private void ClearAllExtensions()
    {
        foreach (var e in ExtensionFilters) e.IsChecked = false;
        OnPropertyChanged(nameof(SelectedExtensionCount));
        OnPropertyChanged(nameof(ExtensionCountDisplay));
    }

    [RelayCommand]
    private void SelectExtensionGroup(string? category)
    {
        if (category is null) return;
        foreach (var e in ExtensionFilters.Where(e => e.Category == category)) e.IsChecked = true;
        OnPropertyChanged(nameof(SelectedExtensionCount));
        OnPropertyChanged(nameof(ExtensionCountDisplay));
    }

    [RelayCommand]
    private void DeselectExtensionGroup(string? category)
    {
        if (category is null) return;
        foreach (var e in ExtensionFilters.Where(e => e.Category == category)) e.IsChecked = false;
        OnPropertyChanged(nameof(SelectedExtensionCount));
        OnPropertyChanged(nameof(ExtensionCountDisplay));
    }

    // ═══ CONSOLE FILTERS (VM-bound, replaces code-behind x:Name checkboxes) ═══
    public ObservableCollection<ConsoleFilterItem> ConsoleFilters { get; } = [];

    /// <summary>Grouped view for XAML binding with category headers (Sony, Nintendo, Sega, Andere).</summary>
    public ICollectionView ConsoleFiltersView { get; private set; } = null!;

    /// <summary>Returns checked console keys, or empty array if none selected (= all consoles).</summary>
    public string[] GetSelectedConsoles() =>
        ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToArray();

    private void InitConsoleFilters()
    {
        var items = new (string key, string display, string cat)[]
        {
            ("PS1", "PlayStation", "Sony"),
            ("PS2", "PlayStation 2", "Sony"),
            ("PS3", "PlayStation 3", "Sony"),
            ("PSP", "PSP", "Sony"),
            ("NES", "NES / Famicom", "Nintendo"),
            ("SNES", "SNES / Super Famicom", "Nintendo"),
            ("N64", "Nintendo 64", "Nintendo"),
            ("GC", "GameCube", "Nintendo"),
            ("WII", "Wii", "Nintendo"),
            ("WIIU", "Wii U", "Nintendo"),
            ("SWITCH", "Nintendo Switch", "Nintendo"),
            ("GB", "Game Boy", "Nintendo"),
            ("GBC", "Game Boy Color", "Nintendo"),
            ("GBA", "Game Boy Advance", "Nintendo"),
            ("NDS", "Nintendo DS", "Nintendo"),
            ("3DS", "Nintendo 3DS", "Nintendo"),
            ("MD", "Mega Drive / Genesis", "Sega"),
            ("SCD", "Mega-CD / Sega CD", "Sega"),
            ("SAT", "Saturn", "Sega"),
            ("DC", "Dreamcast", "Sega"),
            ("SMS", "Master System", "Sega"),
            ("GG", "Game Gear", "Sega"),
            ("ARCADE", "Arcade / MAME / FBNeo", "Andere"),
            ("NEOGEO", "Neo Geo", "Andere"),
            ("NEOCD", "Neo Geo CD", "Andere"),
            ("PCE", "PC Engine / TurboGrafx-16", "Andere"),
            ("PCECD", "PC Engine CD", "Andere"),
            ("DOS", "DOS / PC", "Andere"),
            ("3DO", "3DO", "Andere"),
            ("JAG", "Atari Jaguar", "Andere"),
        };
        foreach (var (key, display, cat) in items)
        {
            var item = new ConsoleFilterItem { Key = key, DisplayName = display, Category = cat };
            item.PropertyChanged += OnConsoleCheckedChanged;
            ConsoleFilters.Add(item);
        }

        ConsoleFiltersView = CollectionViewSource.GetDefaultView(ConsoleFilters);
        ConsoleFiltersView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ConsoleFilterItem.Category)));
        ConsoleFiltersView.Filter = FilterConsoleItem;
    }

    private string _consoleFilterText = string.Empty;
    public string ConsoleFilterText
    {
        get => _consoleFilterText;
        set
        {
            if (_consoleFilterText == value) return;
            _consoleFilterText = value;
            OnPropertyChanged();
            ConsoleFiltersView.Refresh();
        }
    }

    private bool FilterConsoleItem(object obj)
    {
        if (string.IsNullOrWhiteSpace(_consoleFilterText)) return true;
        if (obj is not ConsoleFilterItem item) return false;
        return item.DisplayName.Contains(_consoleFilterText, StringComparison.OrdinalIgnoreCase)
            || item.Key.Contains(_consoleFilterText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Number of checked consoles for counter badge. 0 = all active (implicit).</summary>
    public int SelectedConsoleCount => ConsoleFilters.Count(c => c.IsChecked);

    /// <summary>Display text like "5 von 30 ausgewählt" or "Alle (keine Auswahl = alle)".</summary>
    public string ConsoleCountDisplay
    {
        get
        {
            int sel = SelectedConsoleCount;
            return sel == 0
                ? $"Alle ({ConsoleFilters.Count}) — Anzeige-Filter (kein Pipeline-Filter)"
                : $"{sel} von {ConsoleFilters.Count} ausgewählt";
        }
    }

    private void OnConsoleCheckedChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConsoleFilterItem.IsChecked))
        {
            OnPropertyChanged(nameof(SelectedConsoleCount));
            OnPropertyChanged(nameof(ConsoleCountDisplay));
        }
    }

    [RelayCommand]
    private void SelectAllConsoles()
    {
        foreach (var c in ConsoleFilters) c.IsChecked = true;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void ClearAllConsoles()
    {
        foreach (var c in ConsoleFilters) c.IsChecked = false;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void SelectConsoleGroup(string? category)
    {
        if (category is null) return;
        foreach (var c in ConsoleFilters.Where(c => c.Category == category)) c.IsChecked = true;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void DeselectConsoleGroup(string? category)
    {
        if (category is null) return;
        foreach (var c in ConsoleFilters.Where(c => c.Category == category)) c.IsChecked = false;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void ConsolePresetTop10()
    {
        foreach (var c in ConsoleFilters) c.IsChecked = false;
        var top10 = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "PS1", "PS2", "SNES", "NES", "N64", "GC", "WII", "GBA", "MD", "DC" };
        foreach (var c in ConsoleFilters.Where(c => top10.Contains(c.Key))) c.IsChecked = true;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void ConsolePresetDiscBased()
    {
        foreach (var c in ConsoleFilters) c.IsChecked = false;
        var disc = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "PS1", "PS2", "PS3", "PSP", "GC", "WII", "WIIU", "SCD", "SAT", "DC", "3DO", "PCECD", "NEOCD" };
        foreach (var c in ConsoleFilters.Where(c => disc.Contains(c.Key))) c.IsChecked = true;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void ConsolePresetHandhelds()
    {
        foreach (var c in ConsoleFilters) c.IsChecked = false;
        var handhelds = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "GB", "GBC", "GBA", "NDS", "3DS", "PSP", "GG", "SWITCH" };
        foreach (var c in ConsoleFilters.Where(c => handhelds.Contains(c.Key))) c.IsChecked = true;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    [RelayCommand]
    private void ConsolePresetRetro()
    {
        foreach (var c in ConsoleFilters) c.IsChecked = false;
        var retro = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "NES", "SNES", "N64", "GB", "GBC", "GBA", "MD", "SMS", "GG", "SAT", "PCE", "NEOGEO" };
        foreach (var c in ConsoleFilters.Where(c => retro.Contains(c.Key))) c.IsChecked = true;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    /// <summary>Remove a single console from selection (for chip dismiss).</summary>
    [RelayCommand]
    private void RemoveConsoleSelection(ConsoleFilterItem? item)
    {
        if (item is not null) item.IsChecked = false;
        OnPropertyChanged(nameof(SelectedConsoleCount));
        OnPropertyChanged(nameof(ConsoleCountDisplay));
    }

    // ═══ TOOL ITEMS (delegated to ToolsViewModel) ══════════════════════
    public ObservableCollection<ToolItem> ToolItems => Tools.ToolItems;
    public ICollectionView ToolItemsView => Tools.ToolItemsView;
    public ObservableCollection<ToolCategory> ToolCategories => Tools.ToolCategories;
    public ObservableCollection<ToolItem> QuickAccessItems => Tools.QuickAccessItems;
    public ObservableCollection<ToolItem> RecentToolItems => Tools.RecentToolItems;
    public ObservableCollection<ToolItem> RecommendedToolItems => Tools.RecommendedToolItems;

    public bool IsToolSearchActive => Tools.IsToolSearchActive;
    public bool HasRecommendedTools => Tools.HasRecommendedTools;

    public string ToolFilterText
    {
        get => Tools.ToolFilterText;
        set
        {
            if (Tools.ToolFilterText == value)
                return;

            Tools.ToolFilterText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsToolSearchActive));
        }
    }

    public bool HasRecentTools => Tools.HasRecentTools;

    private void InitToolItems()
    {
        // Intentionally empty: ToolsViewModel initializes tool catalog in its constructor.
    }

    public void RecordToolUsage(string toolKey)
    {
        Tools.RecordToolUsage(toolKey);
        OnPropertyChanged(nameof(HasRecentTools));
    }

    public void ToggleToolPin(string toolKey) => Tools.ToggleToolPin(toolKey);

    public void RefreshToolLockState() => RefreshToolSurfaceState();

    private void RefreshToolSurfaceState()
    {
        Tools.RefreshContext(BuildToolContextSnapshot());
        OnPropertyChanged(nameof(HasRecommendedTools));
        OnPropertyChanged(nameof(HasRecentTools));
    }

    private ToolContextSnapshot BuildToolContextSnapshot()
    {
        var candidateCount = LastCandidates.Count;
        var unverifiedCount = LastCandidates.Count(static candidate => !candidate.DatMatch);

        return new ToolContextSnapshot(
            HasRoots: Roots.Count > 0,
            RootCount: Roots.Count,
            HasRunResult: HasRunResult,
            CandidateCount: candidateCount,
            DedupeGroupCount: LastDedupeGroups.Count,
            JunkCount: LastCandidates.Count(static candidate => candidate.Category == FileCategory.Junk),
            UnverifiedCount: unverifiedCount,
            UseDat: UseDat,
            DatConfigured: !string.IsNullOrWhiteSpace(DatRoot),
            ConvertEnabled: ConvertEnabled,
            ConvertOnly: ConvertOnly,
            ConvertedCount: LastRunResult?.ConvertedCount ?? 0,
            CanRollback: CanRollback);
    }

    // ═══ LOCALIZATION (GUI-047) ═════════════════════════════════════════
    /// <summary>XAML-bindable localization: {Binding Loc[Key]}.</summary>
    public ILocalizationService Loc => _loc;

    private bool _isRootDropTargetActive;
    public bool IsRootDropTargetActive
    {
        get => _isRootDropTargetActive;
        private set => SetProperty(ref _isRootDropTargetActive, value);
    }

    private string _rootDropAnnouncementText = string.Empty;
    public string RootDropAnnouncementText
    {
        get => _rootDropAnnouncementText;
        private set
        {
            if (SetProperty(ref _rootDropAnnouncementText, value))
                OnPropertyChanged(nameof(HasRootDropAnnouncement));
        }
    }

    public bool HasRootDropAnnouncement => !string.IsNullOrWhiteSpace(RootDropAnnouncementText);

    public bool IsConvertPresetActive => string.Equals(ActivePreset, "Convert", StringComparison.Ordinal);

    public IRelayCommand DashboardPrimaryCommand => IsConvertPresetActive ? ConvertOnlyCommand : RunCommand;

    public string DashboardPrimaryActionText => IsConvertPresetActive
        ? _loc["Start.ConvertOnlyButton"]
        : _loc["Start.CtaPreview"];

    public string DashboardPrimaryActionIcon => IsConvertPresetActive ? "\uE8AB" : "\uE768";

    public string DashboardPrimaryActionHintText => IsConvertPresetActive
        ? _loc["Start.ConvertOnlyTip"]
        : _loc["Start.CtaPreviewHint"];

    public string DashboardPrimaryActionAcceleratorKey => IsConvertPresetActive ? string.Empty : "F5";

    /// <summary>Add dropped folder paths (from drag-drop). Duplicates are skipped.</summary>
    public int AddDroppedFolders(IEnumerable<string> paths)
    {
        int added = 0;
        foreach (var path in paths)
        {
            if (System.IO.Directory.Exists(path) && !Roots.Contains(path))
            {
                Roots.Add(path);
                added++;
            }
        }
        return added;
    }

    internal int HandleDroppedFolders(IEnumerable<string> paths)
    {
        var added = AddDroppedFolders(paths);
        if (added > 0)
            RootDropAnnouncementText = _loc.Format("Start.DropAdded", added);

        return added;
    }

    internal void SetRootDropTargetActive(bool isActive)
    {
        IsRootDropTargetActive = isActive;
    }

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
    public IRelayCommand BrowseDatMappingFileCommand { get; }
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

    /// <summary>Notify XAML bindings that FeatureCommands dictionary has been populated.
    /// Must be called after FeatureCommandService.RegisterCommands() to refresh
    /// deferred Binding expressions like {Binding FeatureCommands[AutoFindTools]}.</summary>
    public void NotifyFeatureCommandsReady()
    {
        OnPropertyChanged(nameof(FeatureCommands));
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
        RefreshToolSurfaceState();
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
                System.Diagnostics.Debug.WriteLine($"[Romulus] Log discarded (no Dispatcher): [{level}] {text}");
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
        InvalidateWizardAnalysis();
        RefreshStatus();
        OnPropertyChanged(nameof(HasNoRoots));
        OnPropertyChanged(nameof(HasRootsConfigured));
        OnPropertyChanged(nameof(CanAdvanceWizard));
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
        {
            ClearError(propertyName);
            return;
        }

        var (normalized, reason) = ToolPathValidator.Validate(value);
        if (normalized is not null)
            ClearError(propertyName);
        else if (reason is not null)
            SetError(propertyName, reason, ValidationSeverity.Warning);
        else
            ClearError(propertyName);
    }

    private void ValidateDirectoryPath(string value, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(value))
            ClearError(propertyName);
        else if (RunProfileValidator.ValidateOptionalSafePath(value, propertyName) is { } safetyError)
            SetError(propertyName, safetyError, ValidationSeverity.Blocker);
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
