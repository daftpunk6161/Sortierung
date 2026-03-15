using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using RomCleanup.UI.Wpf.Models;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // ═══ EXTENSION FILTERS (UX-004: VM-bound, replaces code-behind x:Name checkboxes) ═══
    public ObservableCollection<ExtensionFilterItem> ExtensionFilters { get; } = [];

    /// <summary>Grouped view for XAML binding with category headers.
    /// V2-WPF-L01: Initialized via InitExtensionFilters() called from constructor — null! is safe.</summary>
    public ICollectionView ExtensionFiltersView { get; private set; } = null!;

    /// <summary>Returns checked extensions, or empty array if none selected (= scan all).</summary>
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
        ConsoleFiltersView.Filter = FilterConsoleItem;
    }

    // GUI-087: Console filter search text
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

    // ═══ TOOL ITEMS (RD-004: Smart Werkzeuge layout with Quick Access, Recents, Expander categories) ═══
    public ObservableCollection<ToolItem> ToolItems { get; } = [];

    /// <summary>Grouped view for XAML binding with category headers (used for search results).</summary>
    public ICollectionView ToolItemsView { get; private set; } = null!;

    /// <summary>Expander-based category groups for the main tools view.</summary>
    public ObservableCollection<ToolCategory> ToolCategories { get; } = [];

    /// <summary>Quick Access pinned tools (max 6).</summary>
    public ObservableCollection<ToolItem> QuickAccessItems { get; } = [];

    /// <summary>Recently used tools (max 4, auto-tracked).</summary>
    public ObservableCollection<ToolItem> RecentToolItems { get; } = [];

    /// <summary>True when the search box has text – shows filtered list instead of categories.</summary>
    public bool IsToolSearchActive => !string.IsNullOrWhiteSpace(_toolFilterText);

    private string _toolFilterText = "";
    public string ToolFilterText
    {
        get => _toolFilterText;
        set
        {
            if (SetProperty(ref _toolFilterText, value))
            {
                ToolItemsView?.Refresh();
                OnPropertyChanged(nameof(IsToolSearchActive));
            }
        }
    }

    // Default pinned tool keys (sensible defaults for first-time users)
    private static readonly HashSet<string> DefaultPinnedKeys =
    [
        "QuickPreview", "HealthScore", "RollbackQuick", "ExportCsv", "DatAutoUpdate", "DuplicateInspector"
    ];

    // Category icon mapping
    private static readonly Dictionary<string, string> CategoryIcons = new()
    {
        ["Analyse & Berichte"] = "\xE9D9",
        ["Konvertierung & Hashing"] = "\xE8AB",
        ["DAT & Verifizierung"] = "\xE73E",
        ["Sammlungsverwaltung"] = "\xE8F1",
        ["Sicherheit & Integrität"] = "\xE72E",
        ["Workflow & Automatisierung"] = "\xE713",
        ["Export & Integration"] = "\xE792",
        ["Infrastruktur"] = "\xE8CB",
        ["UI & Erscheinungsbild"] = "\xE771",
    };

    private void InitToolItems()
    {
        var items = new (string key, string display, string cat, string desc, string icon, bool needsResult)[]
        {
            // Analyse & Berichte
            ("QuickPreview",       "Quick-Preview",              "Analyse & Berichte",        "Schnelle ROM-Vorschau (DryRun)",               "\xE8A7", false),
            ("HealthScore",        "Health-Score",               "Analyse & Berichte",        "Sammlungsqualität prüfen",                     "\xE8CB", true),
            ("CollectionDiff",     "Collection-Diff",            "Analyse & Berichte",        "Sammlungen vergleichen",                       "\xE8F1", false),
            ("DuplicateInspector", "Duplikat-Inspektor",         "Analyse & Berichte",        "Duplikate untersuchen",                        "\xE71D", true),
            ("ConversionEstimate", "Konvertierungs-Schätzung",   "Analyse & Berichte",        "Speicherersparnis berechnen",                  "\xE8EF", true),
            ("JunkReport",         "Junk-Bericht",               "Analyse & Berichte",        "Detaillierter Junk-Bericht",                   "\xE74D", true),
            ("RomFilter",          "ROM-Filter",                 "Analyse & Berichte",        "ROM-Sammlung durchsuchen",                     "\xE721", true),
            ("DuplicateHeatmap",   "Duplikat-Heatmap",           "Analyse & Berichte",        "Duplikatverteilung visualisieren",             "\xEB05", true),
            ("MissingRom",         "Fehlende ROMs",              "Analyse & Berichte",        "Fehlende ROMs ermitteln",                      "\xE783", true),
            ("CrossRootDupe",      "Cross-Root-Duplikate",       "Analyse & Berichte",        "Duplikate über mehrere Roots finden",          "\xE8B9", true),
            ("HeaderAnalysis",     "Header-Analyse",             "Analyse & Berichte",        "ROM-Header analysieren",                       "\xE9D9", true),
            ("Completeness",       "Vollständigkeit",            "Analyse & Berichte",        "Vollständigkeitsbericht",                      "\xE73E", true),
            ("DryRunCompare",      "DryRun-Vergleich",           "Analyse & Berichte",        "Zwei DryRun-Ergebnisse vergleichen",           "\xE8F1", false),
            ("TrendAnalysis",      "Trend-Analyse",              "Analyse & Berichte",        "Historische Trends",                           "\xE9D2", false),
            ("EmulatorCompat",     "Emulator-Kompatibilität",    "Analyse & Berichte",        "Kompatibilitätsmatrix",                        "\xE7FC", false),

            // Konvertierung & Hashing
            ("ConversionPipeline", "Konvertierungs-Pipeline",    "Konvertierung & Hashing",   "Konvertierungspipeline starten",               "\xE8AB", false),
            ("NKitConvert",        "NKit-Konvertierung",         "Konvertierung & Hashing",   "NKit-Images konvertieren",                     "\xE8AB", false),
            ("ConvertQueue",       "Konvert-Warteschlange",      "Konvertierung & Hashing",   "Warteschlange anzeigen",                       "\xE8CB", false),
            ("ConversionVerify",   "Konvertierung verifizieren", "Konvertierung & Hashing",   "Konvertierte Dateien prüfen",                  "\xE73E", false),
            ("FormatPriority",     "Format-Priorität",           "Konvertierung & Hashing",   "Format-Prioritätsliste anzeigen",              "\xE8CB", false),
            ("ParallelHashing",    "Parallel-Hashing",           "Konvertierung & Hashing",   "Hash-Threading konfigurieren (experimentell)", "\xE8CB", false),
            ("GpuHashing",         "GPU-Hashing",                "Konvertierung & Hashing",   "GPU-beschleunigtes Hashing (experimentell)",  "\xE8CB", false),

            // DAT & Verifizierung
            ("DatAutoUpdate",      "DAT Auto-Update",            "DAT & Verifizierung",       "Lokale DAT-Dateien prüfen",                    "\xE895", false),
            ("DatDiffViewer",      "DAT-Diff-Viewer",            "DAT & Verifizierung",       "DAT-Versionen vergleichen",                    "\xE8F1", false),
            ("TosecDat",           "TOSEC-DAT",                  "DAT & Verifizierung",       "TOSEC-DAT importieren",                        "\xE8B5", false),
            ("CustomDatEditor",    "Custom-DAT-Editor",          "DAT & Verifizierung",       "Eigene DAT-Einträge erstellen",                "\xE70F", false),
            ("HashDatabaseExport", "Hash-Datenbank",             "DAT & Verifizierung",       "Hash-Datenbank exportieren",                   "\xE792", true),

            // Sammlungsverwaltung
            ("CollectionManager",  "Smart Collection",           "Sammlungsverwaltung",       "Sammlung intelligent verwalten",               "\xE8F1", true),
            ("CloneListViewer",    "Clone-Liste",                "Sammlungsverwaltung",       "Clone-/Parent-Beziehungen",                    "\xE8B9", true),
            ("CoverScraper",       "Cover-Scraper",              "Sammlungsverwaltung",       "Cover-Bilder zuordnen",                        "\xE8B9", true),
            ("GenreClassification","Genre-Klassifikation",       "Sammlungsverwaltung",       "ROMs nach Genre einordnen",                    "\xE8CB", true),
            ("PlaytimeTracker",    "Spielzeit-Tracker",          "Sammlungsverwaltung",       "RetroArch-Spielzeiten auslesen",               "\xE916", false),
            ("CollectionSharing",  "Sammlung teilen",            "Sammlungsverwaltung",       "Sammlungsliste exportieren",                   "\xE72D", true),
            ("VirtualFolderPreview","Virtuelle Ordner",          "Sammlungsverwaltung",       "Virtuelle Ordnerstruktur planen",              "\xE8B7", true),

            // Sicherheit & Integrität
            ("IntegrityMonitor",   "Integritäts-Monitor",        "Sicherheit & Integrität",   "Baseline erstellen/prüfen",                    "\xE72E", true),
            ("BackupManager",      "Backup-Manager",             "Sicherheit & Integrität",   "Winner-Dateien sichern",                       "\xE8F1", true),
            ("Quarantine",         "Quarantäne",                 "Sicherheit & Integrität",   "Verdächtige Dateien isolieren",                "\xE7BA", true),
            ("RuleEngine",         "Regel-Engine",               "Sicherheit & Integrität",   "Aktive Regeln anzeigen",                       "\xE713", false),
            ("PatchEngine",        "Patch-Engine",               "Sicherheit & Integrität",   "ROM-Patches anwenden",                         "\xE70F", false),
            ("HeaderRepair",       "Header-Reparatur",           "Sicherheit & Integrität",   "ROM-Header reparieren",                        "\xE90F", false),
            ("RollbackQuick",      "Schnell-Rollback",           "Sicherheit & Integrität",   "Letzten Lauf rückgängig machen",               "\xE777", false),
            ("RollbackUndo",       "Rollback Undo",              "Sicherheit & Integrität",   "Rollback rückgängig machen",                   "\xE7A7", false),
            ("RollbackRedo",       "Rollback Redo",              "Sicherheit & Integrität",   "Rollback wiederherstellen",                    "\xE7A6", false),

            // Workflow & Automatisierung
            ("CommandPalette",     "Command-Palette",            "Workflow & Automatisierung", "Befehle suchen und ausführen",                 "\xE721", false),
            ("SplitPanelPreview",  "Split-Panel",                "Workflow & Automatisierung", "Winner/Loser-Vergleich",                       "\xE8A0", true),
            ("FilterBuilder",      "Filter-Builder",             "Workflow & Automatisierung", "Erweiterte Filter erstellen",                  "\xE71C", true),
            ("SortTemplates",      "Sort-Templates",             "Workflow & Automatisierung", "Sortierungs-Vorlagen",                         "\xE8CB", false),
            ("PipelineEngine",     "Pipeline-Engine",            "Workflow & Automatisierung", "Pipeline-Status anzeigen",                     "\xE8CB", false),
            ("SystemTray",         "System-Tray",                "Workflow & Automatisierung", "System-Tray ein-/ausschalten",                 "\xE8CB", false),
            ("SchedulerAdvanced",  "Cron-Tester",                "Workflow & Automatisierung", "Cron-Expressions testen",                     "\xE787", false),
            ("RulePackSharing",    "Regel-Pakete",               "Workflow & Automatisierung", "Regeln importieren/exportieren",               "\xE72D", false),
            ("ArcadeMergeSplit",   "Arcade Merge/Split",         "Workflow & Automatisierung", "Arcade-Sets analysieren",                     "\xE8CB", false),
            ("AutoProfile",        "Auto-Profil",                "Workflow & Automatisierung", "Profil automatisch erkennen",                  "\xE713", false),

            // Export & Integration
            ("PdfReport",          "PDF-Report",                 "Export & Integration",       "HTML-Report für PDF-Druck",                    "\xE8A5", true),
            ("LauncherIntegration","Launcher-Integration",       "Export & Integration",       "RetroArch-Playlist exportieren",               "\xE768", true),
            ("ToolImport",         "Tool-Import",                "Export & Integration",       "DAT-Dateien importieren",                      "\xE8B5", false),
            ("DuplicateExport",    "Duplikate exportieren",      "Export & Integration",       "Duplikatliste als CSV speichern",              "\xE792", true),
            ("ExportCsv",          "CSV Export",                 "Export & Integration",       "Sammlung als CSV exportieren",                 "\xE792", true),
            ("ExportExcel",        "Excel Export",               "Export & Integration",       "Sammlung als Excel-XML exportieren",           "\xE792", true),

            // Infrastruktur & Deployment
            ("StorageTiering",     "Storage-Tiering",            "Infrastruktur",              "Speicher-Analyse",                             "\xE8CB", true),
            ("NasOptimization",    "NAS-Optimierung",            "Infrastruktur",              "NAS-Pfad-Infos anzeigen",                     "\xE8CB", false),
            ("FtpSource",          "FTP-Quelle",                 "Infrastruktur",              "FTP/SFTP-Quelle konfigurieren",               "\xE774", false),
            ("CloudSync",          "Cloud-Sync",                 "Infrastruktur",              "Cloud-Status prüfen",                          "\xE753", false),
            ("PluginMarketplaceFeature","Plugin-Marktplatz",     "Infrastruktur",              "Plugin-System (geplant)",                      "\xE71B", false),
            ("PluginManager",      "Plugin-Manager",             "Infrastruktur",              "Installierte Plugins verwalten",               "\xE71B", false),
            ("PortableMode",       "Portable Modus",             "Infrastruktur",              "Portable-Modus Status",                        "\xE8CB", false),
            ("DockerContainer",    "Docker",                     "Infrastruktur",              "Docker-Dateien generieren",                    "\xE8CB", false),
            ("MobileWebUI",        "Mobile Web UI",              "Infrastruktur",              "REST API starten",                             "\xE774", false),
            ("WindowsContextMenu", "Kontextmenü",                "Infrastruktur",              "Windows-Kontextmenü registrieren",             "\xE8CB", false),
            ("HardlinkMode",       "Hardlink-Modus",             "Infrastruktur",              "Hardlink-Schätzung berechnen",                 "\xE8CB", true),
            ("MultiInstanceSync",  "Multi-Instanz",              "Infrastruktur",              "Lock-Dateien verwalten",                       "\xE8CB", false),

            // UI & Erscheinungsbild
            ("Accessibility",      "Barrierefreiheit",           "UI & Erscheinungsbild",      "Schriftgröße/Kontrast anpassen",               "\xE7F8", false),
            ("ThemeEngine",        "Theme-Engine",               "UI & Erscheinungsbild",      "Theme-Optionen",                               "\xE771", false),
        };
        foreach (var (key, display, cat, desc, icon, needsResult) in items)
        {
            var isPlanned = key is "FtpSource" or "CloudSync" or "PluginMarketplaceFeature" or "PluginManager"
                or "ParallelHashing" or "GpuHashing" or "DockerContainer" or "MultiInstanceSync"
                or "TosecDat" or "PatchEngine" or "NKitConvert" or "WindowsContextMenu"
                or "EmulatorCompat" or "TrendAnalysis" or "GenreClassification" or "PlaytimeTracker"
                or "CoverScraper" or "CollectionSharing";
            var item = new ToolItem
            {
                Key = key, DisplayName = display, Category = cat, Description = desc,
                Icon = icon, RequiresRunResult = needsResult,
                IsPinned = DefaultPinnedKeys.Contains(key),
                IsLocked = needsResult, // initially locked; unlocked after a run completes
                IsPlanned = isPlanned
            };
            ToolItems.Add(item);
        }

        // Build grouped view (still useful for search mode)
        ToolItemsView = CollectionViewSource.GetDefaultView(ToolItems);
        ToolItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ToolItem.Category)));
        ToolItemsView.Filter = ToolItemFilter;

        // Build category expanders
        RebuildToolCategories();

        // Build quick access
        RebuildQuickAccess();
    }

    /// <summary>Rebuilds the ToolCategories collection from ToolItems, grouped and ordered.</summary>
    private void RebuildToolCategories()
    {
        ToolCategories.Clear();
        bool isFirst = true;
        foreach (var group in ToolItems.GroupBy(t => t.Category))
        {
            var catIcon = CategoryIcons.GetValueOrDefault(group.Key, "\xE8CB");
            var cat = new ToolCategory { Name = group.Key, Icon = catIcon, IsExpanded = isFirst };
            foreach (var item in group)
                cat.Items.Add(item);
            ToolCategories.Add(cat);
            isFirst = false;
        }
    }

    /// <summary>Rebuilds the QuickAccessItems from pinned ToolItems.</summary>
    private void RebuildQuickAccess()
    {
        QuickAccessItems.Clear();
        foreach (var item in ToolItems.Where(t => t.IsPinned).Take(6))
            QuickAccessItems.Add(item);
    }

    /// <summary>Records usage of a tool and updates RecentToolItems.</summary>
    public void RecordToolUsage(string toolKey)
    {
        var item = ToolItems.FirstOrDefault(t => t.Key == toolKey);
        if (item is null) return;

        item.LastUsedAt = DateTime.Now;

        RecentToolItems.Clear();
        foreach (var recent in ToolItems
            .Where(t => t.LastUsedAt.HasValue)
            .OrderByDescending(t => t.LastUsedAt)
            .Take(4))
        {
            RecentToolItems.Add(recent);
        }
        OnPropertyChanged(nameof(HasRecentTools));
    }

    public bool HasRecentTools => RecentToolItems.Count > 0;

    /// <summary>Toggles pin state and rebuilds quick access.</summary>
    public void ToggleToolPin(string toolKey)
    {
        var item = ToolItems.FirstOrDefault(t => t.Key == toolKey);
        if (item is null) return;

        // Enforce max 6 pins
        if (!item.IsPinned && QuickAccessItems.Count >= 6) return;

        item.IsPinned = !item.IsPinned;
        RebuildQuickAccess();
    }

    /// <summary>Updates IsLocked state on all RequiresRunResult tools based on whether a run result exists.</summary>
    public void RefreshToolLockState()
    {
        bool hasResult = HasRunResult;
        foreach (var item in ToolItems.Where(t => t.RequiresRunResult))
            item.IsLocked = !hasResult;
    }

    private bool ToolItemFilter(object obj)
    {
        if (obj is not ToolItem item) return false;
        if (string.IsNullOrWhiteSpace(_toolFilterText)) return true;
        return item.DisplayName.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase);
    }
}
