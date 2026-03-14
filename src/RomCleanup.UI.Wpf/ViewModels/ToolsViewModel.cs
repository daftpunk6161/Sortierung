using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Models;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-026: Tool catalog ViewModel — extracted from MainViewModel.Filters.cs.
/// Manages ToolItems, categories, quick access, recent tools, and search filtering.
/// </summary>
public sealed class ToolsViewModel : ObservableObject
{
    // ═══ TOOL ITEMS ═══════════════════════════════════════════════════
    public ObservableCollection<ToolItem> ToolItems { get; } = [];
    public ICollectionView ToolItemsView { get; private set; } = null!;
    public ObservableCollection<ToolCategory> ToolCategories { get; } = [];
    public ObservableCollection<ToolItem> QuickAccessItems { get; } = [];
    public ObservableCollection<ToolItem> RecentToolItems { get; } = [];

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

    public bool HasRecentTools => RecentToolItems.Count > 0;

    // ═══ SIDEBAR NAVIGATION ═══════════════════════════════════════════
    private string _selectedToolsSection = "Schnellzugriff";
    public string SelectedToolsSection
    {
        get => _selectedToolsSection;
        set => SetProperty(ref _selectedToolsSection, value);
    }

    // ═══ FEATURE COMMANDS ═════════════════════════════════════════════
    public Dictionary<string, ICommand> FeatureCommands { get; } = new();

    // Default pinned tool keys
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

    public ToolsViewModel()
    {
        InitToolItems();
    }

    /// <summary>Assigns FeatureCommands to matching ToolItems. Call after FeatureCommandService.RegisterCommands().</summary>
    public void WireToolItemCommands()
    {
        foreach (var item in ToolItems)
        {
            if (FeatureCommands.TryGetValue(item.Key, out var cmd))
                item.Command = cmd;
        }
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

    /// <summary>Toggles pin state and rebuilds quick access.</summary>
    public void ToggleToolPin(string toolKey)
    {
        var item = ToolItems.FirstOrDefault(t => t.Key == toolKey);
        if (item is null) return;

        if (!item.IsPinned && QuickAccessItems.Count >= 6) return;

        item.IsPinned = !item.IsPinned;
        RebuildQuickAccess();
    }

    /// <summary>Updates IsLocked state on all RequiresRunResult tools based on whether a run result exists.</summary>
    public void RefreshToolLockState(bool hasRunResult)
    {
        HasRunResult = hasRunResult;
        foreach (var item in ToolItems.Where(t => t.RequiresRunResult))
            item.IsLocked = !hasRunResult;
    }

    private bool _hasRunResult;
    public bool HasRunResult { get => _hasRunResult; set => SetProperty(ref _hasRunResult, value); }

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
                IsLocked = needsResult,
                IsPlanned = isPlanned
            };
            ToolItems.Add(item);
        }

        ToolItemsView = CollectionViewSource.GetDefaultView(ToolItems);
        ToolItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ToolItem.Category)));
        ToolItemsView.Filter = ToolItemFilter;

        RebuildToolCategories();
        RebuildQuickAccess();
    }

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

    private void RebuildQuickAccess()
    {
        QuickAccessItems.Clear();
        foreach (var item in ToolItems.Where(t => t.IsPinned).Take(6))
            QuickAccessItems.Add(item);
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
