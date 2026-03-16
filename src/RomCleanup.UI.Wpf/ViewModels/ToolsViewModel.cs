using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// GUI-026: Tool catalog ViewModel — extracted from MainViewModel.Filters.cs.
/// Manages ToolItems, categories, quick access, recent tools, and search filtering.
/// </summary>
public sealed class ToolsViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

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

    public ToolsViewModel(ILocalizationService? loc = null)
    {
        _loc = loc ?? new LocalizationService();
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
        var items = new (string key, string catKey, string icon, bool needsResult)[]
        {
            // Analysis
            ("QuickPreview",       "Analysis",       "\xE8A7", false),
            ("HealthScore",        "Analysis",       "\xE8CB", true),
            ("CollectionDiff",     "Analysis",       "\xE8F1", false),
            ("DuplicateInspector", "Analysis",       "\xE71D", true),
            ("ConversionEstimate", "Analysis",       "\xE8EF", true),
            ("JunkReport",         "Analysis",       "\xE74D", true),
            ("RomFilter",          "Analysis",       "\xE721", true),
            ("DuplicateHeatmap",   "Analysis",       "\xEB05", true),
            ("MissingRom",         "Analysis",       "\xE783", true),
            ("CrossRootDupe",      "Analysis",       "\xE8B9", true),
            ("HeaderAnalysis",     "Analysis",       "\xE9D9", true),
            ("Completeness",       "Analysis",       "\xE73E", true),
            ("DryRunCompare",      "Analysis",       "\xE8F1", false),
            ("TrendAnalysis",      "Analysis",       "\xE9D2", false),
            ("EmulatorCompat",     "Analysis",       "\xE7FC", false),

            // Conversion
            ("ConversionPipeline", "Conversion",     "\xE8AB", false),
            ("NKitConvert",        "Conversion",     "\xE8AB", false),
            ("ConvertQueue",       "Conversion",     "\xE8CB", false),
            ("ConversionVerify",   "Conversion",     "\xE73E", false),
            ("FormatPriority",     "Conversion",     "\xE8CB", false),
            ("ParallelHashing",    "Conversion",     "\xE8CB", false),
            ("GpuHashing",         "Conversion",     "\xE8CB", false),

            // DatVerify
            ("DatAutoUpdate",      "DatVerify",      "\xE895", false),
            ("DatDiffViewer",      "DatVerify",      "\xE8F1", false),
            ("TosecDat",           "DatVerify",      "\xE8B5", false),
            ("CustomDatEditor",    "DatVerify",      "\xE70F", false),
            ("HashDatabaseExport", "DatVerify",      "\xE792", true),

            // Collection
            ("CollectionManager",  "Collection",     "\xE8F1", true),
            ("CloneListViewer",    "Collection",     "\xE8B9", true),
            ("CoverScraper",       "Collection",     "\xE8B9", true),
            ("GenreClassification","Collection",     "\xE8CB", true),
            ("PlaytimeTracker",    "Collection",     "\xE916", false),
            ("CollectionSharing",  "Collection",     "\xE72D", true),
            ("VirtualFolderPreview","Collection",    "\xE8B7", true),

            // Security
            ("IntegrityMonitor",   "Security",       "\xE72E", true),
            ("BackupManager",      "Security",       "\xE8F1", true),
            ("Quarantine",         "Security",       "\xE7BA", true),
            ("RuleEngine",         "Security",       "\xE713", false),
            ("PatchEngine",        "Security",       "\xE70F", false),
            ("HeaderRepair",       "Security",       "\xE90F", false),
            ("RollbackQuick",      "Security",       "\xE777", false),
            ("RollbackHistoryBack",    "Security",       "\xE7A7", false),
            ("RollbackHistoryForward", "Security",       "\xE7A6", false),

            // Workflow
            ("CommandPalette",     "Workflow",       "\xE721", false),
            ("SplitPanelPreview",  "Workflow",       "\xE8A0", true),
            ("FilterBuilder",      "Workflow",       "\xE71C", true),
            ("SortTemplates",      "Workflow",       "\xE8CB", false),
            ("PipelineEngine",     "Workflow",       "\xE8CB", false),
            ("SystemTray",         "Workflow",       "\xE8CB", false),
            ("SchedulerAdvanced",  "Workflow",       "\xE787", false),
            ("RulePackSharing",    "Workflow",       "\xE72D", false),
            ("ArcadeMergeSplit",   "Workflow",       "\xE8CB", false),
            ("AutoProfile",        "Workflow",       "\xE713", false),

            // Export
            ("PdfReport",          "Export",         "\xE8A5", true),
            ("LauncherIntegration","Export",         "\xE768", true),
            ("ToolImport",         "Export",         "\xE8B5", false),
            ("DuplicateExport",    "Export",         "\xE792", true),
            ("ExportCsv",          "Export",         "\xE792", true),
            ("ExportExcel",        "Export",         "\xE792", true),

            // Infrastructure
            ("StorageTiering",     "Infra",          "\xE8CB", true),
            ("NasOptimization",    "Infra",          "\xE8CB", false),
            ("FtpSource",          "Infra",          "\xE774", false),
            ("CloudSync",          "Infra",          "\xE753", false),
            ("PluginMarketplaceFeature","Infra",     "\xE71B", false),
            ("PluginManager",      "Infra",          "\xE71B", false),
            ("PortableMode",       "Infra",          "\xE8CB", false),
            ("DockerContainer",    "Infra",          "\xE8CB", false),
            ("MobileWebUI",        "Infra",          "\xE774", false),
            ("WindowsContextMenu", "Infra",          "\xE8CB", false),
            ("HardlinkMode",       "Infra",          "\xE8CB", true),
            ("MultiInstanceSync",  "Infra",          "\xE8CB", false),

            // UI
            ("Accessibility",      "UI",             "\xE7F8", false),
        };
        foreach (var (key, catKey, icon, needsResult) in items)
        {
            var isPlanned = key is "FtpSource" or "CloudSync" or "PluginMarketplaceFeature" or "PluginManager"
                or "ParallelHashing" or "GpuHashing" or "DockerContainer" or "MultiInstanceSync"
                or "TosecDat" or "PatchEngine" or "NKitConvert" or "WindowsContextMenu"
                or "EmulatorCompat" or "TrendAnalysis" or "GenreClassification" or "PlaytimeTracker"
                or "CoverScraper" or "CollectionSharing";
            var item = new ToolItem
            {
                Key = key, DisplayName = _loc[$"Tool.{key}"], Category = _loc[$"Tool.Cat.{catKey}"], Description = _loc[$"Tool.{key}.Desc"],
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
