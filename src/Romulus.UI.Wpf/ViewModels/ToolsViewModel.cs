using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// GUI-026: Tool catalog ViewModel — extracted from MainViewModel.Filters.cs.
/// Manages ToolItems, categories, quick access, recent tools, search filtering,
/// maturity badging, and context recommendations.
/// </summary>
public sealed class ToolsViewModel : ObservableObject
{
    private const string SectionRecommended = "Empfohlen";
    private const string SectionQuickAccess = "Schnellzugriff";
    private const string SectionRecent = "Zuletzt verwendet";
    private const string SectionAll = "Alle Werkzeuge";

    private readonly ILocalizationService _loc;
    private readonly Dictionary<string, IRelayCommand> _toolLaunchCommands = new(StringComparer.Ordinal);
    private ToolContextSnapshot _lastContext = new(
        HasRoots: false,
        RootCount: 0,
        HasRunResult: false,
        CandidateCount: 0,
        DedupeGroupCount: 0,
        JunkCount: 0,
        UnverifiedCount: 0,
        UseDat: false,
        DatConfigured: false,
        ConvertEnabled: false,
        ConvertOnly: false,
        ConvertedCount: 0,
        CanRollback: false);

    private sealed record ToolCatalogEntry(
        string Key,
        string CategoryKey,
        string Icon,
        bool RequiresRunResult,
        bool IsEssential,
        ToolMaturity Maturity);

    // ═══ TOOL ITEMS ═══════════════════════════════════════════════════
    public ObservableCollection<ToolItem> ToolItems { get; } = [];
    public ICollectionView ToolItemsView { get; private set; } = null!;
    public ObservableCollection<ToolCategory> ToolCategories { get; } = [];
    public ObservableCollection<ToolItem> QuickAccessItems { get; } = [];
    public ObservableCollection<ToolItem> RecentToolItems { get; } = [];
    public ObservableCollection<ToolItem> RecommendedToolItems { get; } = [];

    public bool IsToolSearchActive => !string.IsNullOrWhiteSpace(_toolFilterText);
    public bool HasRecentTools => RecentToolItems.Count > 0;
    public bool HasRecommendedTools => RecommendedToolItems.Count > 0;
    public bool HasExperimentalTools => ExperimentalToolCount > 0;
    public int ProductionToolCount => ToolItems.Count(static item => item.Maturity == ToolMaturity.Production);
    public int GuidedToolCount => ToolItems.Count(static item => item.Maturity == ToolMaturity.Guided);
    public int ExperimentalToolCount => ToolItems.Count(static item => item.Maturity == ToolMaturity.Experimental);
    public int AvailableToolCount => ToolItems.Count(static item => !item.IsUnavailable && !item.IsLocked);
    public string ToolCountLabel => _loc.Format("Tools.CountFormat", ToolItems.Count);

    public IRelayCommand<string> ToggleToolPinCommand { get; }

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

    // ═══ SIDEBAR NAVIGATION ═══════════════════════════════════════════
    private string _selectedToolsSection = SectionRecommended;
    public string SelectedToolsSection
    {
        get => _selectedToolsSection;
        set => SetProperty(ref _selectedToolsSection, value);
    }

    private bool _isSimpleMode = true;
    public bool IsSimpleMode
    {
        get => _isSimpleMode;
        private set => SetProperty(ref _isSimpleMode, value);
    }

    // ═══ FEATURE COMMANDS ═════════════════════════════════════════════
    public Dictionary<string, ICommand> FeatureCommands { get; } = new(StringComparer.Ordinal);

    // Default pinned tool keys
    private static readonly HashSet<string> DefaultPinnedKeys =
    [
        FeatureCommandKeys.HealthScore,
        FeatureCommandKeys.DuplicateAnalysis,
        FeatureCommandKeys.RollbackQuick,
        FeatureCommandKeys.ExportCollection,
        FeatureCommandKeys.DatAutoUpdate,
        FeatureCommandKeys.IntegrityMonitor
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
        ToggleToolPinCommand = new RelayCommand<string>(toolKey =>
        {
            if (!string.IsNullOrWhiteSpace(toolKey))
                ToggleToolPin(toolKey);
        });
        InitToolItems();
    }

    public void SetSimpleMode(bool simpleMode)
    {
        IsSimpleMode = simpleMode;
        if (simpleMode && !string.Equals(SelectedToolsSection, SectionRecommended, StringComparison.Ordinal))
            SelectedToolsSection = SectionRecommended;
    }

    /// <summary>Assigns FeatureCommands to matching ToolItems. Call after FeatureCommandService.RegisterCommands().</summary>
    public void WireToolItemCommands()
    {
        _toolLaunchCommands.Clear();

        foreach (var item in ToolItems)
        {
            var launchCommand = new RelayCommand(
                () => ExecuteTool(item.Key),
                () => CanExecuteTool(item.Key));
            _toolLaunchCommands[item.Key] = launchCommand;
            item.Command = launchCommand;
        }

        RefreshAvailabilityStates();
        RefreshContext(_lastContext);
        RefreshToolCommandStates();
    }

    /// <summary>Records usage of a tool and updates RecentToolItems.</summary>
    public void RecordToolUsage(string toolKey)
    {
        var item = ToolItems.FirstOrDefault(t => t.Key == toolKey);
        if (item is null)
            return;

        item.LastUsedAt = DateTime.Now;

        RecentToolItems.Clear();
        foreach (var recent in ToolItems
            .Where(static t => t.LastUsedAt.HasValue)
            .OrderByDescending(static t => t.LastUsedAt)
            .Take(6))
        {
            RecentToolItems.Add(recent);
        }

        OnPropertyChanged(nameof(HasRecentTools));
    }

    /// <summary>Toggles pin state and rebuilds quick access.</summary>
    public void ToggleToolPin(string toolKey)
    {
        var item = ToolItems.FirstOrDefault(t => t.Key == toolKey);
        if (item is null)
            return;

        if (!item.IsPinned && QuickAccessItems.Count >= 6)
            return;

        item.IsPinned = !item.IsPinned;
        RebuildQuickAccess();
    }

    /// <summary>Updates lock state, availability, and recommended surfaces.</summary>
    public void RefreshContext(ToolContextSnapshot snapshot)
    {
        _lastContext = snapshot;
        HasRunResult = snapshot.HasRunResult;

        foreach (var item in ToolItems)
        {
            item.IsLocked = item.RequiresRunResult && !snapshot.HasRunResult;
            item.IsUnavailable = !_toolLaunchCommands.ContainsKey(item.Key) || !FeatureCommands.ContainsKey(item.Key);
            item.IsRecommended = false;
            item.RecommendationReason = string.Empty;
        }

        RebuildRecommendedItems(snapshot);
        RefreshToolCommandStates();
        RefreshCatalogMetrics();
    }

    /// <summary>Updates IsLocked state on all RequiresRunResult tools based on whether a run result exists.</summary>
    public void RefreshToolLockState(bool hasRunResult)
        => RefreshContext(_lastContext with { HasRunResult = hasRunResult });

    private bool _hasRunResult;
    public bool HasRunResult
    {
        get => _hasRunResult;
        private set => SetProperty(ref _hasRunResult, value);
    }

    private void InitToolItems()
    {
        foreach (var entry in CreateCatalogEntries())
        {
            var item = new ToolItem
            {
                Key = entry.Key,
                DisplayName = _loc[$"Tool.{entry.Key}"],
                Category = _loc[$"Tool.Cat.{entry.CategoryKey}"],
                Description = _loc[$"Tool.{entry.Key}.Desc"],
                Icon = entry.Icon,
                RequiresRunResult = entry.RequiresRunResult,
                IsEssential = entry.IsEssential,
                Maturity = entry.Maturity,
                MaturityBadgeText = entry.Maturity switch
                {
                    ToolMaturity.Production => _loc["Tools.Maturity.Production"],
                    ToolMaturity.Guided => _loc["Tools.Maturity.Guided"],
                    ToolMaturity.Experimental => _loc["Tools.Maturity.Experimental"],
                    _ => _loc["Tools.Maturity.Production"]
                },
                MaturityDescription = entry.Maturity switch
                {
                    ToolMaturity.Production => _loc["Tools.Maturity.Production.Desc"],
                    ToolMaturity.Guided => _loc["Tools.Maturity.Guided.Desc"],
                    ToolMaturity.Experimental => _loc["Tools.Maturity.Experimental.Desc"],
                    _ => _loc["Tools.Maturity.Production.Desc"]
                },
                UnlockRequirementText = entry.RequiresRunResult ? _loc["Tools.Requirement.RunResult"] : string.Empty,
                UnavailableText = _loc["Tools.Requirement.Host"],
                IsPinned = DefaultPinnedKeys.Contains(entry.Key),
                IsLocked = entry.RequiresRunResult,
                IsUnavailable = true,
                IsPlanned = false
            };
            ToolItems.Add(item);
        }

        ToolItemsView = CollectionViewSource.GetDefaultView(ToolItems);
        ToolItemsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ToolItem.Category)));
        ToolItemsView.Filter = ToolItemFilter;

        RebuildToolCategories();
        RebuildQuickAccess();
        RefreshCatalogMetrics();
    }

    private static IReadOnlyList<ToolCatalogEntry> CreateCatalogEntries()
    {
        return
        [
            new(FeatureCommandKeys.HealthScore, "Analysis", "\xE8CB", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.DuplicateAnalysis, "Analysis", "\xE71D", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.JunkReport, "Analysis", "\xE74D", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.RomFilter, "Analysis", "\xE721", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.MissingRom, "Analysis", "\xE783", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.HeaderAnalysis, "Analysis", "\xE9D9", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.Completeness, "Analysis", "\xE73E", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.DryRunCompare, "Analysis", "\xE8F1", false, false, ToolMaturity.Production),

            new(FeatureCommandKeys.ConversionPipeline, "Conversion", "\xE8AB", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.ConversionVerify, "Conversion", "\xE73E", false, true, ToolMaturity.Production),
            new(FeatureCommandKeys.FormatPriority, "Conversion", "\xE9D9", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.PatchPipeline, "Conversion", "\xE8A5", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.HeaderRepair, "Conversion", "\xE90F", false, true, ToolMaturity.Production),

            new(FeatureCommandKeys.DatAutoUpdate, "DatVerify", "\xE895", false, true, ToolMaturity.Production),
            new(FeatureCommandKeys.DatDiffViewer, "DatVerify", "\xE8F1", false, true, ToolMaturity.Production),
            new(FeatureCommandKeys.CustomDatEditor, "DatVerify", "\xE70F", false, false, ToolMaturity.Production),
            new(FeatureCommandKeys.HashDatabaseExport, "DatVerify", "\xE792", true, false, ToolMaturity.Production),

            new(FeatureCommandKeys.CollectionManager, "Collection", "\xE8F1", true, false, ToolMaturity.Experimental),
            new(FeatureCommandKeys.CloneListViewer, "Collection", "\xE8B9", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.VirtualFolderPreview, "Collection", "\xE8B7", true, false, ToolMaturity.Experimental),
            new(FeatureCommandKeys.CollectionMerge, "Collection", "\xE71D", false, true, ToolMaturity.Production),

            new(FeatureCommandKeys.IntegrityMonitor, "Security", "\xE72E", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.BackupManager, "Security", "\xE8F1", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.Quarantine, "Security", "\xE7BA", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.RuleEngine, "Security", "\xE713", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.RollbackQuick, "Security", "\xE777", false, true, ToolMaturity.Production),

            new(FeatureCommandKeys.CommandPalette, "Workflow", "\xE721", false, true, ToolMaturity.Production),
            new(FeatureCommandKeys.FilterBuilder, "Workflow", "\xE71C", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.SortTemplates, "Workflow", "\xE762", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.PipelineEngine, "Workflow", "\xE9F5", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.RulePackSharing, "Workflow", "\xE72D", false, false, ToolMaturity.Production),
            new(FeatureCommandKeys.ArcadeMergeSplit, "Workflow", "\xE71D", false, false, ToolMaturity.Production),
            new(FeatureCommandKeys.AutoProfile, "Workflow", "\xE713", false, false, ToolMaturity.Experimental),

            new(FeatureCommandKeys.HtmlReport, "Export", "\xE774", true, true, ToolMaturity.Production),
            new(FeatureCommandKeys.LauncherIntegration, "Export", "\xE768", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.DatImport, "Export", "\xE8B5", false, false, ToolMaturity.Production),
            new(FeatureCommandKeys.ExportCollection, "Export", "\xE792", true, true, ToolMaturity.Production),

            new(FeatureCommandKeys.StorageTiering, "Infra", "\xE7F8", true, false, ToolMaturity.Production),
            new(FeatureCommandKeys.NasOptimization, "Infra", "\xE839", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.PortableMode, "Infra", "\xE8B7", false, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.ApiServer, "Infra", "\xE774", false, false, ToolMaturity.Production),
            new(FeatureCommandKeys.HardlinkMode, "Infra", "\xE71B", true, false, ToolMaturity.Guided),
            new(FeatureCommandKeys.Accessibility, "Infra", "\xE7F8", false, false, ToolMaturity.Guided)
        ];
    }

    private void RebuildToolCategories()
    {
        ToolCategories.Clear();
        var isFirst = true;
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
        foreach (var item in ToolItems.Where(static t => t.IsPinned).Take(6))
            QuickAccessItems.Add(item);
    }

    private void RebuildRecommendedItems(ToolContextSnapshot snapshot)
    {
        RecommendedToolItems.Clear();

        var queuedKeys = new HashSet<string>(StringComparer.Ordinal);
        var recommendations = new List<(string Key, string Reason)>();

        void AddRecommendation(string key, string reason)
        {
            if (!queuedKeys.Add(key))
                return;

            var item = ToolItems.FirstOrDefault(t => t.Key == key);
            if (item is null || item.IsLocked || item.IsUnavailable || item.IsExperimental)
                return;

            recommendations.Add((key, reason));
        }

        if (!snapshot.HasRunResult)
        {
            if (snapshot.UseDat || snapshot.DatConfigured)
                AddRecommendation(FeatureCommandKeys.DatAutoUpdate, _loc["Tools.Recommend.SetupDat"]);

            if (snapshot.ConvertEnabled || snapshot.ConvertOnly)
            {
                AddRecommendation(FeatureCommandKeys.FormatPriority, _loc["Tools.Recommend.ConversionPlan"]);
                AddRecommendation(FeatureCommandKeys.ConversionVerify, _loc["Tools.Recommend.ConversionCheck"]);
                AddRecommendation(FeatureCommandKeys.PatchPipeline, _loc["Tools.Recommend.ConversionPlan"]);
            }

            if (snapshot.RootCount > 1)
                AddRecommendation(FeatureCommandKeys.CollectionMerge, _loc["Tools.Recommend.Collection"]);

            AddRecommendation(FeatureCommandKeys.CommandPalette, _loc["Tools.Recommend.Workflow"]);
            AddRecommendation(FeatureCommandKeys.RulePackSharing, _loc["Tools.Recommend.Setup"]);
        }
        else
        {
            AddRecommendation(FeatureCommandKeys.HealthScore, _loc["Tools.Recommend.RunInsight"]);

            if (snapshot.DedupeGroupCount > 0)
                AddRecommendation(FeatureCommandKeys.DuplicateAnalysis, _loc["Tools.Recommend.DedupeReview"]);

            if (snapshot.JunkCount > 0)
                AddRecommendation(FeatureCommandKeys.JunkReport, _loc["Tools.Recommend.JunkReview"]);

            if (snapshot.UseDat || snapshot.DatConfigured)
            {
                AddRecommendation(
                    snapshot.UnverifiedCount > 0 ? FeatureCommandKeys.MissingRom : FeatureCommandKeys.Completeness,
                    snapshot.UnverifiedCount > 0 ? _loc["Tools.Recommend.DatGaps"] : _loc["Tools.Recommend.DatCoverage"]);
            }

            if (snapshot.ConvertEnabled || snapshot.ConvertOnly || snapshot.ConvertedCount > 0)
                AddRecommendation(FeatureCommandKeys.ConversionVerify, _loc["Tools.Recommend.ConversionCheck"]);

            AddRecommendation(FeatureCommandKeys.ExportCollection, _loc["Tools.Recommend.RunInsight"]);

            if (snapshot.CanRollback)
            {
                AddRecommendation(FeatureCommandKeys.RollbackQuick, _loc["Tools.Recommend.Recovery"]);
                AddRecommendation(FeatureCommandKeys.IntegrityMonitor, _loc["Tools.Recommend.Recovery"]);
            }
        }

        if (recommendations.Count == 0)
        {
            foreach (var item in ToolItems.Where(static t => t.IsEssential && !t.IsLocked && !t.IsUnavailable && !t.IsExperimental).Take(6))
                recommendations.Add((item.Key, _loc["Tools.Recommend.Workflow"]));
        }

        foreach (var (key, reason) in recommendations.Take(6))
        {
            var item = ToolItems.First(t => t.Key == key);
            item.IsRecommended = true;
            item.RecommendationReason = reason;
            RecommendedToolItems.Add(item);
        }

        OnPropertyChanged(nameof(HasRecommendedTools));
    }

    private void RefreshAvailabilityStates()
    {
        foreach (var item in ToolItems)
            item.IsUnavailable = !_toolLaunchCommands.ContainsKey(item.Key) || !FeatureCommands.ContainsKey(item.Key);
    }

    private void RefreshToolCommandStates()
    {
        foreach (var command in _toolLaunchCommands.Values)
            command.NotifyCanExecuteChanged();
    }

    private void RefreshCatalogMetrics()
    {
        OnPropertyChanged(nameof(ProductionToolCount));
        OnPropertyChanged(nameof(GuidedToolCount));
        OnPropertyChanged(nameof(ExperimentalToolCount));
        OnPropertyChanged(nameof(AvailableToolCount));
        OnPropertyChanged(nameof(HasExperimentalTools));
        OnPropertyChanged(nameof(ToolCountLabel));
        OnPropertyChanged(nameof(HasRecentTools));
        OnPropertyChanged(nameof(HasRecommendedTools));
    }

    private bool CanExecuteTool(string toolKey)
    {
        var item = ToolItems.FirstOrDefault(t => t.Key == toolKey);
        if (item is null || item.IsLocked || item.IsUnavailable)
            return false;

        return FeatureCommands.TryGetValue(toolKey, out var command) && command.CanExecute(null);
    }

    private void ExecuteTool(string toolKey)
    {
        if (!FeatureCommands.TryGetValue(toolKey, out var command) || !command.CanExecute(null))
            return;

        command.Execute(null);
        RecordToolUsage(toolKey);
    }

    private bool ToolItemFilter(object obj)
    {
        if (obj is not ToolItem item)
            return false;

        if (string.IsNullOrWhiteSpace(_toolFilterText))
            return true;

        return item.DisplayName.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase)
            || item.Category.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase)
            || item.Description.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase)
            || item.MaturityBadgeText.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase)
            || item.RecommendationReason.Contains(_toolFilterText, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ CONVERSION REGISTRY (moved from ToolsConversionView code-behind) ═══

    public ObservableCollection<ConversionCapabilityRow> ConversionCapabilities { get; } = [];

    private bool _hasConversionCapabilities;
    public bool HasConversionCapabilities
    {
        get => _hasConversionCapabilities;
        set => SetProperty(ref _hasConversionCapabilities, value);
    }

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
            var loader = new Romulus.Infrastructure.Conversion.ConversionRegistryLoader(registryPath, consolesPath);
            var capabilities = loader.GetCapabilities();
            foreach (var capability in capabilities)
            {
                ConversionCapabilities.Add(new ConversionCapabilityRow(
                    capability.SourceExtension,
                    capability.TargetExtension,
                    capability.Tool.ToolName,
                    capability.Command,
                    capability.Lossless ? "✓" : "✗",
                    capability.Cost,
                    capability.ApplicableConsoles is not null ? string.Join(", ", capability.ApplicableConsoles) : _loc["Label.All"]));
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
            if (System.IO.File.Exists(candidate))
                return candidate;

            var parent = System.IO.Directory.GetParent(dir)?.FullName;
            if (parent is null || parent == dir)
                break;

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
