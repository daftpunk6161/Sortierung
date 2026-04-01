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
        "HealthScore", "DuplicateAnalysis", "RollbackQuick", "ExportCollection", "DatAutoUpdate", "IntegrityMonitor"
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
            ("HealthScore",        "Analysis",       "\xE8CB", true),
            ("DuplicateAnalysis",  "Analysis",       "\xE71D", true),
            ("JunkReport",         "Analysis",       "\xE74D", true),
            ("RomFilter",          "Analysis",       "\xE721", true),
            ("MissingRom",         "Analysis",       "\xE783", true),
            ("HeaderAnalysis",     "Analysis",       "\xE9D9", true),
            ("Completeness",       "Analysis",       "\xE73E", true),
            ("DryRunCompare",      "Analysis",       "\xE8F1", false),

            // Conversion
            ("ConversionPipeline", "Conversion",     "\xE8AB", false),
            ("ConversionVerify",   "Conversion",     "\xE73E", false),
            ("FormatPriority",     "Conversion",     "\xE9D9", false),
            ("HeaderRepair",       "Conversion",     "\xE90F", false),

            // DatVerify
            ("DatAutoUpdate",      "DatVerify",      "\xE895", false),
            ("DatDiffViewer",      "DatVerify",      "\xE8F1", false),
            ("CustomDatEditor",    "DatVerify",      "\xE70F", false),
            ("HashDatabaseExport", "DatVerify",      "\xE792", true),

            // Collection
            ("CollectionManager",  "Collection",     "\xE8F1", true),
            ("CloneListViewer",    "Collection",     "\xE8B9", true),
            ("VirtualFolderPreview","Collection",    "\xE8B7", true),
            ("CollectionMerge",    "Collection",     "\xE71D", false),

            // Security
            ("IntegrityMonitor",   "Security",       "\xE72E", true),
            ("BackupManager",      "Security",       "\xE8F1", true),
            ("Quarantine",         "Security",       "\xE7BA", true),
            ("RuleEngine",         "Security",       "\xE713", false),
            ("RollbackQuick",      "Security",       "\xE777", false),

            // Workflow
            ("CommandPalette",     "Workflow",       "\xE721", false),
            ("FilterBuilder",      "Workflow",       "\xE71C", true),
            ("SortTemplates",      "Workflow",       "\xE762", false),
            ("PipelineEngine",     "Workflow",       "\xE9F5", false),
            ("RulePackSharing",    "Workflow",       "\xE72D", false),
            ("ArcadeMergeSplit",   "Workflow",       "\xE71D", false),
            ("AutoProfile",        "Workflow",       "\xE713", false),

            // Export
            ("HtmlReport",         "Export",         "\xE774", true),
            ("LauncherIntegration","Export",         "\xE768", true),
            ("DatImport",          "Export",         "\xE8B5", false),
            ("ExportCollection",   "Export",         "\xE792", true),

            // Infrastructure
            ("StorageTiering",     "Infra",          "\xE7F8", true),
            ("NasOptimization",    "Infra",          "\xE839", false),
            ("PortableMode",       "Infra",          "\xE8B7", false),
            ("ApiServer",          "Infra",          "\xE774", false),
            ("HardlinkMode",       "Infra",          "\xE71B", true),

            ("Accessibility",      "Infra",          "\xE7F8", false),
        };
        foreach (var (key, catKey, icon, needsResult) in items)
        {
            var isPlanned = false;
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

    // ═══ CONVERSION REGISTRY (moved from ToolsConversionView code-behind) ═══

    public ObservableCollection<ConversionCapabilityRow> ConversionCapabilities { get; } = [];

    private bool _hasConversionCapabilities;
    public bool HasConversionCapabilities { get => _hasConversionCapabilities; set => SetProperty(ref _hasConversionCapabilities, value); }

    public void LoadConversionRegistry()
    {
        ConversionCapabilities.Clear();
        var registryPath = FindDataFile("conversion-registry.json");
        var consolesPath = FindDataFile("consoles.json");

        if (registryPath is null || consolesPath is null)
        {
            HasConversionCapabilities = false;
            return;
        }

        try
        {
            var loader = new RomCleanup.Infrastructure.Conversion.ConversionRegistryLoader(registryPath, consolesPath);
            var capabilities = loader.GetCapabilities();
            foreach (var c in capabilities)
            {
                ConversionCapabilities.Add(new ConversionCapabilityRow(
                    c.SourceExtension,
                    c.TargetExtension,
                    c.Tool.ToolName,
                    c.Command,
                    c.Lossless ? "✓" : "✗",
                    c.Cost,
                    c.ApplicableConsoles is not null ? string.Join(", ", c.ApplicableConsoles) : _loc["Label.All"]
                ));
            }
            HasConversionCapabilities = ConversionCapabilities.Count > 0;
        }
        catch
        {
            HasConversionCapabilities = false;
        }
    }

    private static string? FindDataFile(string name)
    {
        var dir = AppDomain.CurrentDomain.BaseDirectory;
        for (var i = 0; i < 6; i++)
        {
            var candidate = System.IO.Path.Combine(dir, "data", name);
            if (System.IO.File.Exists(candidate)) return candidate;
            var parent = System.IO.Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir) break;
            dir = parent;
        }
        return null;
    }
}

public sealed record ConversionCapabilityRow(
    string Source,
    string Target,
    string Tool,
    string Command,
    string LosslessDisplay,
    int Cost,
    string Consoles);
