using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using RomCleanup.Core.Classification;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Analysis;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Profiles;
using RomCleanup.Infrastructure.Workflow;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private RunProfileService _runProfileService = null!;
    private RunConfigurationMaterializer _runConfigurationMaterializer = null!;
    private bool _suppressRunConfigurationSelectionApply;
    private bool _applyingRunConfigurationSelection;
    private bool _wizardAnalysisDirty = true;
    private CancellationTokenSource? _wizardAnalysisCts;

    private bool _wizardAnalysisInProgress;
    public bool WizardAnalysisInProgress
    {
        get => _wizardAnalysisInProgress;
        private set => SetProperty(ref _wizardAnalysisInProgress, value);
    }

    private string _wizardAnalysisSummary = string.Empty;
    public string WizardAnalysisSummary
    {
        get => _wizardAnalysisSummary;
        private set => SetProperty(ref _wizardAnalysisSummary, value);
    }

    private string _wizardRecommendationSummary = string.Empty;
    public string WizardRecommendationSummary
    {
        get => _wizardRecommendationSummary;
        private set => SetProperty(ref _wizardRecommendationSummary, value);
    }

    public bool WizardHasAnalysis => !string.IsNullOrWhiteSpace(WizardAnalysisSummary);

    public ObservableCollection<RunProfileSummary> AvailableRunProfiles { get; } = [];
    public ObservableCollection<WorkflowScenarioDefinition> AvailableWorkflows { get; } = [];

    private string? _selectedWorkflowScenarioId;
    public string? SelectedWorkflowScenarioId
    {
        get => _selectedWorkflowScenarioId;
        set
        {
            var normalized = NormalizeSelection(value);
            if (!SetProperty(ref _selectedWorkflowScenarioId, normalized))
                return;

            OnRunConfigurationSelectionChanged();
        }
    }

    private string? _selectedRunProfileId;
    public string? SelectedRunProfileId
    {
        get => _selectedRunProfileId;
        set
        {
            var normalized = NormalizeSelection(value);
            if (!SetProperty(ref _selectedRunProfileId, normalized))
                return;

            OnRunConfigurationSelectionChanged();
        }
    }

    public bool HasSelectedWorkflow => TryGetSelectedWorkflow() is not null;
    public bool HasSelectedRunProfile => TryGetSelectedProfileSummary() is not null;

    public string SelectedWorkflowName => TryGetSelectedWorkflow()?.Name ?? "Kein Workflow";
    public string SelectedWorkflowDescription => TryGetSelectedWorkflow()?.Description ?? "Kein gefuehrtes Szenario ausgewaehlt.";
    public string SelectedWorkflowStepsSummary => TryGetSelectedWorkflow() is { Steps.Length: > 0 } workflow
        ? string.Join(" -> ", workflow.Steps)
        : "Keine Workflow-Schritte aktiv.";

    public string SelectedRunProfileName => TryGetSelectedProfileSummary()?.Name ?? "Kein Profil";
    public string SelectedRunProfileDescription => TryGetSelectedProfileSummary()?.Description ?? "Kein Profil ausgewaehlt.";

    public string RunConfigurationSelectionSummary
        => $"{SelectedWorkflowName} | {SelectedRunProfileName}";

    public bool CanAdvanceWizard => Shell.WizardStep switch
    {
        0 => Roots.Count > 0,
        1 => GetPreferredRegions().Length > 0,
        _ => true
    };

    internal RunProfileService RunProfileService => _runProfileService;
    internal RunConfigurationMaterializer RunConfigurationMaterializer => _runConfigurationMaterializer;

    private void InitializeRunConfigurationServices(
        RunProfileService? runProfileService,
        RunConfigurationMaterializer? runConfigurationMaterializer)
    {
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? RunEnvironmentBuilder.ResolveDataDir();

        _runProfileService = runProfileService
            ?? new RunProfileService(new JsonRunProfileStore(), dataDir);
        _runConfigurationMaterializer = runConfigurationMaterializer
            ?? new RunConfigurationMaterializer(
                new RunConfigurationResolver(_runProfileService),
                new RunOptionsFactory());

        RefreshRunConfigurationCatalogs();
    }

    internal void RefreshRunConfigurationCatalogs()
    {
        try
        {
            var profiles = _runProfileService.ListAsync().GetAwaiter().GetResult();
            AvailableRunProfiles.Clear();
            foreach (var profile in profiles)
                AvailableRunProfiles.Add(profile);

            AvailableWorkflows.Clear();
            foreach (var workflow in WorkflowScenarioCatalog.List()
                         .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase))
            {
                AvailableWorkflows.Add(workflow);
            }

            OnRunConfigurationSelectionMetadataChanged();
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or JsonException)
        {
            AddLog($"[Profiles] Katalog konnte nicht geladen werden: {ex.Message}", "WARN");
        }
    }

    internal void RestoreRunConfigurationSelection(string? workflowScenarioId, string? profileId)
    {
        _suppressRunConfigurationSelectionApply = true;
        try
        {
            SelectedWorkflowScenarioId = workflowScenarioId;
            SelectedRunProfileId = profileId;
        }
        finally
        {
            _suppressRunConfigurationSelectionApply = false;
        }

        OnRunConfigurationSelectionMetadataChanged();
    }

    internal RunConfigurationDraft BuildCurrentRunConfigurationDraft(bool includeSelections = true)
    {
        return new RunConfigurationDraft
        {
            Roots = Roots.ToArray(),
            Mode = DryRun ? RunConstants.ModeDryRun : RunConstants.ModeMove,
            WorkflowScenarioId = includeSelections ? NormalizeSelection(SelectedWorkflowScenarioId) : null,
            ProfileId = includeSelections ? NormalizeSelection(SelectedRunProfileId) : null,
            PreferRegions = GetPreferredRegions(),
            Extensions = BuildSelectedExtensionsForRunConfiguration(),
            RemoveJunk = RemoveJunk,
            OnlyGames = OnlyGames,
            KeepUnknownWhenOnlyGames = KeepUnknownWhenOnlyGames,
            AggressiveJunk = AggressiveJunk,
            SortConsole = SortConsole,
            EnableDat = UseDat,
            EnableDatAudit = UseDat && EnableDatAudit,
            EnableDatRename = UseDat && EnableDatRename,
            DatRoot = string.IsNullOrWhiteSpace(DatRoot) ? null : DatRoot,
            HashType = string.IsNullOrWhiteSpace(DatHashType) ? RunConstants.DefaultHashType : DatHashType,
            ConvertFormat = (ConvertEnabled || ConvertOnly) ? "auto" : null,
            ConvertOnly = ConvertOnly,
            ApproveReviews = ApproveReviews,
            ConflictPolicy = ConflictPolicy.ToString(),
            TrashRoot = string.IsNullOrWhiteSpace(TrashRoot) ? null : TrashRoot
        };
    }

    internal RunConfigurationExplicitness BuildCurrentRunConfigurationExplicitness()
    {
        return new RunConfigurationExplicitness
        {
            Mode = true,
            PreferRegions = true,
            Extensions = true,
            RemoveJunk = true,
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = true,
            AggressiveJunk = true,
            SortConsole = true,
            EnableDat = true,
            EnableDatAudit = true,
            EnableDatRename = true,
            DatRoot = true,
            HashType = true,
            ConvertFormat = true,
            ConvertOnly = true,
            ApproveReviews = true,
            ConflictPolicy = true,
            TrashRoot = true
        };
    }

    internal RunProfileDocument BuildCurrentRunProfileDocument(string id, string name, string? description)
    {
        var draft = BuildCurrentRunConfigurationDraft(includeSelections: false);
        var settings = new RunProfileSettings
        {
            PreferRegions = draft.PreferRegions,
            Extensions = draft.Extensions,
            RemoveJunk = draft.RemoveJunk,
            OnlyGames = draft.OnlyGames,
            KeepUnknownWhenOnlyGames = draft.KeepUnknownWhenOnlyGames,
            AggressiveJunk = draft.AggressiveJunk,
            SortConsole = draft.SortConsole,
            EnableDat = draft.EnableDat,
            EnableDatAudit = draft.EnableDatAudit,
            EnableDatRename = draft.EnableDatRename,
            DatRoot = draft.DatRoot,
            HashType = draft.HashType,
            ConvertFormat = draft.ConvertFormat,
            ConvertOnly = draft.ConvertOnly,
            ApproveReviews = draft.ApproveReviews,
            ConflictPolicy = draft.ConflictPolicy,
            TrashRoot = draft.TrashRoot,
            Mode = draft.Mode
        };

        return new RunProfileDocument
        {
            Version = 1,
            Id = id.Trim(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            BuiltIn = false,
            Tags = BuildProfileTags(),
            WorkflowScenarioId = NormalizeSelection(SelectedWorkflowScenarioId),
            Settings = settings
        };
    }

    internal IReadOnlyDictionary<string, string> GetCurrentRunConfigurationMap()
        => BuildRunConfigurationMap(BuildCurrentRunConfigurationDraft());

    internal static IReadOnlyDictionary<string, string> BuildRunConfigurationMap(RunConfigurationDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);

        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["workflowScenarioId"] = draft.WorkflowScenarioId ?? string.Empty,
            ["profileId"] = draft.ProfileId ?? string.Empty,
            ["mode"] = draft.Mode ?? string.Empty,
            ["preferRegions"] = string.Join(",", draft.PreferRegions ?? Array.Empty<string>()),
            ["extensions"] = string.Join(",", draft.Extensions ?? Array.Empty<string>()),
            ["removeJunk"] = draft.RemoveJunk?.ToString() ?? string.Empty,
            ["onlyGames"] = draft.OnlyGames?.ToString() ?? string.Empty,
            ["keepUnknownWhenOnlyGames"] = draft.KeepUnknownWhenOnlyGames?.ToString() ?? string.Empty,
            ["aggressiveJunk"] = draft.AggressiveJunk?.ToString() ?? string.Empty,
            ["sortConsole"] = draft.SortConsole?.ToString() ?? string.Empty,
            ["enableDat"] = draft.EnableDat?.ToString() ?? string.Empty,
            ["enableDatAudit"] = draft.EnableDatAudit?.ToString() ?? string.Empty,
            ["enableDatRename"] = draft.EnableDatRename?.ToString() ?? string.Empty,
            ["datRoot"] = draft.DatRoot ?? string.Empty,
            ["hashType"] = draft.HashType ?? string.Empty,
            ["convertFormat"] = draft.ConvertFormat ?? string.Empty,
            ["convertOnly"] = draft.ConvertOnly?.ToString() ?? string.Empty,
            ["approveReviews"] = draft.ApproveReviews?.ToString() ?? string.Empty,
            ["conflictPolicy"] = draft.ConflictPolicy ?? string.Empty,
            ["trashRoot"] = draft.TrashRoot ?? string.Empty
        };
    }

    internal void ApplySelectedRunConfiguration()
    {
        if (_suppressRunConfigurationSelectionApply || _applyingRunConfigurationSelection)
            return;

        var workflowId = NormalizeSelection(SelectedWorkflowScenarioId);
        var profileId = NormalizeSelection(SelectedRunProfileId);
        if (workflowId is null && profileId is null)
        {
            OnRunConfigurationSelectionMetadataChanged();
            return;
        }

        _applyingRunConfigurationSelection = true;
        try
        {
            var dataDir = FeatureService.ResolveDataDirectory()
                          ?? RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
            var baselineDraft = BuildCurrentRunConfigurationDraft(includeSelections: false);
            var selectionDraft = new RunConfigurationDraft
            {
                Roots = baselineDraft.Roots,
                WorkflowScenarioId = workflowId,
                ProfileId = profileId
            };

            var materialized = _runConfigurationMaterializer.MaterializeAsync(
                selectionDraft,
                new RunConfigurationExplicitness(),
                settings,
                baselineDraft: baselineDraft).GetAwaiter().GetResult();

            ApplyMaterializedRunConfiguration(materialized);
        }
        catch (InvalidOperationException ex)
        {
            AddLog($"[Profiles] Auswahl konnte nicht angewendet werden: {ex.Message}", "WARN");
            OnRunConfigurationSelectionMetadataChanged();
        }
        finally
        {
            _applyingRunConfigurationSelection = false;
        }
    }

    internal void ApplyMaterializedRunConfiguration(MaterializedRunConfiguration materialized)
    {
        ArgumentNullException.ThrowIfNull(materialized);

        var draft = materialized.EffectiveDraft;
        _suppressRunConfigurationSelectionApply = true;
        try
        {
            DryRun = !string.Equals(draft.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase);
            RemoveJunk = draft.RemoveJunk ?? RemoveJunk;
            OnlyGames = draft.OnlyGames ?? OnlyGames;
            KeepUnknownWhenOnlyGames = draft.KeepUnknownWhenOnlyGames ?? KeepUnknownWhenOnlyGames;
            AggressiveJunk = draft.AggressiveJunk ?? AggressiveJunk;
            SortConsole = draft.SortConsole ?? SortConsole;
            UseDat = draft.EnableDat ?? UseDat;
            EnableDatAudit = (draft.EnableDat ?? UseDat) && (draft.EnableDatAudit ?? false);
            EnableDatRename = (draft.EnableDat ?? UseDat) && (draft.EnableDatRename ?? false);
            DatRoot = draft.DatRoot ?? string.Empty;
            DatHashType = draft.HashType ?? RunConstants.DefaultHashType;
            ConvertEnabled = !string.IsNullOrWhiteSpace(draft.ConvertFormat);
            ConvertOnly = draft.ConvertOnly ?? false;
            ApproveReviews = draft.ApproveReviews ?? false;
            TrashRoot = draft.TrashRoot ?? string.Empty;
            ApplyConflictPolicyFromDraft(draft.ConflictPolicy);
            ApplyPreferredRegions(draft.PreferRegions);
            ApplySelectedExtensions(draft.Extensions);
            SetRunConfigurationSelectionInternal(materialized.Workflow?.Id, materialized.EffectiveProfileId);
        }
        finally
        {
            _suppressRunConfigurationSelectionApply = false;
        }

        RefreshStatus();
        UpdateWizardRegionSummary();
        OnRunConfigurationSelectionMetadataChanged();
    }

    private void OnRunConfigurationSelectionChanged()
    {
        OnRunConfigurationSelectionMetadataChanged();

        if (!_suppressRunConfigurationSelectionApply)
            ApplySelectedRunConfiguration();
    }

    private void OnRunConfigurationSelectionMetadataChanged()
    {
        OnPropertyChanged(nameof(HasSelectedWorkflow));
        OnPropertyChanged(nameof(HasSelectedRunProfile));
        OnPropertyChanged(nameof(SelectedWorkflowName));
        OnPropertyChanged(nameof(SelectedWorkflowDescription));
        OnPropertyChanged(nameof(SelectedWorkflowStepsSummary));
        OnPropertyChanged(nameof(SelectedRunProfileName));
        OnPropertyChanged(nameof(SelectedRunProfileDescription));
        OnPropertyChanged(nameof(RunConfigurationSelectionSummary));
        OnPropertyChanged(nameof(CanAdvanceWizard));
        OnPropertyChanged(nameof(WizardHasAnalysis));
    }

    private void OnShellStatePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.WizardStep))
        {
            OnPropertyChanged(nameof(CanAdvanceWizard));
            if (Shell.ShowFirstRunWizard && Shell.WizardStep >= 1)
                EnsureWizardAnalysisStarted();
        }

        if (e.PropertyName is nameof(ShellViewModel.SelectedNavTag) or nameof(ShellViewModel.SelectedSubTab))
            OnPropertyChanged(nameof(ShowSmartActionBar));
    }

    private void InvalidateWizardAnalysis()
    {
        _wizardAnalysisDirty = true;
        _wizardAnalysisCts?.Cancel();
        _wizardAnalysisCts = null;
        WizardAnalysisInProgress = false;
        WizardAnalysisSummary = string.Empty;
        WizardRecommendationSummary = string.Empty;
        OnPropertyChanged(nameof(WizardHasAnalysis));
    }

    private void EnsureWizardAnalysisStarted()
    {
        if (!_wizardAnalysisDirty || WizardAnalysisInProgress || Roots.Count == 0)
            return;

        _wizardAnalysisCts?.Cancel();
        _wizardAnalysisCts = new CancellationTokenSource();
        _ = AnalyzeWizardSetupAsync(_wizardAnalysisCts.Token);
    }

    internal async Task AnalyzeWizardSetupAsync(CancellationToken cancellationToken = default)
    {
        if (Roots.Count == 0)
        {
            InvalidateWizardAnalysis();
            return;
        }

        WizardAnalysisInProgress = true;
        WizardAnalysisSummary = "Analyse laeuft...";
        WizardRecommendationSummary = string.Empty;
        OnPropertyChanged(nameof(WizardHasAnalysis));

        try
        {
            var roots = Roots
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Select(static root => root.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var selectedExtensions = BuildSelectedExtensionsForRunConfiguration();
            var extensionSet = new HashSet<string>(selectedExtensions
                .Where(static ext => !string.IsNullOrWhiteSpace(ext))
                .Select(static ext => ext.StartsWith('.') ? ext : "." + ext), StringComparer.OrdinalIgnoreCase);

            var analysis = await Task.Run(
                () => BuildWizardScanData(roots, extensionSet, AggressiveJunk, cancellationToken),
                cancellationToken).ConfigureAwait(true);

            if (cancellationToken.IsCancellationRequested)
                return;

            var advisor = FeatureService.GetConversionAdvisor(analysis.Candidates);
            var topConsoles = analysis.ConsoleCounts
                .OrderByDescending(static kv => kv.Value)
                .ThenBy(static kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                .Take(5)
                .Select(static kv => $"{kv.Key} ({kv.Value})")
                .ToArray();

            var recommendedWorkflowId = ResolveRecommendedWizardWorkflow(
                analysis.HasDiscLikeFormats,
                analysis.HasCartridgeFormats,
                advisor.SavedBytes);

            var workflow = WorkflowScenarioCatalog.TryGet(recommendedWorkflowId);
            if (string.IsNullOrWhiteSpace(SelectedWorkflowScenarioId))
                SelectedWorkflowScenarioId = recommendedWorkflowId;
            if (string.IsNullOrWhiteSpace(SelectedRunProfileId) && workflow is not null)
                SelectedRunProfileId = workflow.RecommendedProfileId;

            var convertibleFiles = advisor.Consoles.Sum(static item => item.FileCount);
            WizardAnalysisSummary =
                $"Erkannt: {analysis.TotalFiles} Datei(en), Junk-Schaetzung: {analysis.JunkFiles}, konvertierbar: {convertibleFiles}, Einsparpotenzial: {FeatureService.FormatSize(advisor.SavedBytes)}.";

            WizardRecommendationSummary =
                $"Empfohlen: {(workflow?.Name ?? recommendedWorkflowId)} | Top-Systeme: {(topConsoles.Length > 0 ? string.Join(", ", topConsoles) : "keine")}";

            _wizardAnalysisDirty = false;
            OnPropertyChanged(nameof(WizardHasAnalysis));
            OnPropertyChanged(nameof(CanAdvanceWizard));
        }
        catch (OperationCanceledException)
        {
            // Ignore canceled wizard analyses.
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            WizardAnalysisSummary = "Analyse fehlgeschlagen.";
            WizardRecommendationSummary = ex.Message;
            OnPropertyChanged(nameof(WizardHasAnalysis));
        }
        finally
        {
            WizardAnalysisInProgress = false;
        }
    }

    internal static string ResolveRecommendedWizardWorkflow(
        bool hasDiscLikeFormats,
        bool hasCartridgeFormats,
        long estimatedSavingsBytes)
    {
        if (hasDiscLikeFormats && !hasCartridgeFormats)
            return WorkflowScenarioIds.FormatOptimization;
        if (hasDiscLikeFormats && hasCartridgeFormats)
            return WorkflowScenarioIds.NewCollectionSetup;
        if (!hasDiscLikeFormats && hasCartridgeFormats)
            return estimatedSavingsBytes > 0 ? WorkflowScenarioIds.QuickClean : WorkflowScenarioIds.FullAudit;

        return WorkflowScenarioIds.FullAudit;
    }

    private static WizardScanData BuildWizardScanData(
        IReadOnlyList<string> roots,
        HashSet<string> extensionSet,
        bool aggressiveJunk,
        CancellationToken cancellationToken)
    {
        var detector = TryLoadWizardConsoleDetector();
        var consoleCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<RomCandidate>();
        var totalFiles = 0;
        var junkFiles = 0;
        var hasDiscLikeFormats = false;
        var hasCartridgeFormats = false;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var extension = Path.GetExtension(file);
                if (string.IsNullOrWhiteSpace(extension) || !extensionSet.Contains(extension))
                    continue;

                long sizeBytes;
                try
                {
                    sizeBytes = new FileInfo(file).Length;
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                totalFiles++;
                var baseName = Path.GetFileNameWithoutExtension(file);
                if (CollectionExportService.GetJunkReason(baseName, aggressiveJunk) is not null)
                    junkFiles++;

                var normalizedExtension = extension.ToLowerInvariant();
                if (normalizedExtension is ".iso" or ".bin" or ".cue" or ".chd" or ".gdi" or ".cso" or ".pbp" or ".rvz" or ".wbfs")
                    hasDiscLikeFormats = true;
                if (normalizedExtension is ".nes" or ".sfc" or ".smc" or ".gba" or ".gb" or ".gbc" or ".nds" or ".z64" or ".n64" or ".v64")
                    hasCartridgeFormats = true;

                var consoleKey = detector?.DetectByExtension(extension)
                                 ?? detector?.DetectByFolder(file, root)
                                 ?? "unknown";
                consoleCounts[consoleKey] = consoleCounts.TryGetValue(consoleKey, out var count) ? count + 1 : 1;

                candidates.Add(new RomCandidate
                {
                    MainPath = file,
                    Extension = extension,
                    SizeBytes = sizeBytes,
                    ConsoleKey = consoleKey,
                    Category = FileCategory.Game
                });

                if (candidates.Count >= 25000)
                    return new WizardScanData(totalFiles, junkFiles, hasDiscLikeFormats, hasCartridgeFormats, consoleCounts, candidates);
            }
        }

        return new WizardScanData(totalFiles, junkFiles, hasDiscLikeFormats, hasCartridgeFormats, consoleCounts, candidates);
    }

    private static ConsoleDetector? TryLoadWizardConsoleDetector()
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var consolesPath = Path.Combine(dataDir, "consoles.json");
        if (!File.Exists(consolesPath))
            return null;

        try
        {
            var json = File.ReadAllText(consolesPath);
            return ConsoleDetector.LoadFromJson(json);
        }
        catch (IOException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private sealed record WizardScanData(
        int TotalFiles,
        int JunkFiles,
        bool HasDiscLikeFormats,
        bool HasCartridgeFormats,
        IReadOnlyDictionary<string, int> ConsoleCounts,
        IReadOnlyList<RomCandidate> Candidates);

    private void SetRunConfigurationSelectionInternal(string? workflowScenarioId, string? profileId)
    {
        SelectedWorkflowScenarioId = workflowScenarioId;
        SelectedRunProfileId = profileId;
    }

    private WorkflowScenarioDefinition? TryGetSelectedWorkflow()
        => AvailableWorkflows.FirstOrDefault(item => string.Equals(item.Id, SelectedWorkflowScenarioId, StringComparison.OrdinalIgnoreCase));

    private RunProfileSummary? TryGetSelectedProfileSummary()
        => AvailableRunProfiles.FirstOrDefault(item => string.Equals(item.Id, SelectedRunProfileId, StringComparison.OrdinalIgnoreCase));

    private void ApplyConflictPolicyFromDraft(string? conflictPolicy)
    {
        if (string.IsNullOrWhiteSpace(conflictPolicy))
            return;

        if (Enum.TryParse<Models.ConflictPolicy>(conflictPolicy, ignoreCase: true, out var parsed))
            ConflictPolicy = parsed;
    }

    private void ApplyPreferredRegions(string[]? preferredRegions)
    {
        var orderedRegions = (preferredRegions ?? RunConstants.DefaultPreferRegions)
            .Where(static region => !string.IsNullOrWhiteSpace(region))
            .Select(static region => region.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        ApplyRegionPreset(orderedRegions);
    }

    private void ApplySelectedExtensions(string[]? extensions)
    {
        var normalized = new HashSet<string>(
            (extensions is { Length: > 0 } ? extensions : RunOptions.DefaultExtensions)
            .Select(static extension => extension.StartsWith('.') ? extension : "." + extension),
            StringComparer.OrdinalIgnoreCase);

        foreach (var filter in ExtensionFilters)
            filter.IsChecked = normalized.Contains(filter.Extension);

        OnPropertyChanged(nameof(SelectedExtensionCount));
        OnPropertyChanged(nameof(ExtensionCountDisplay));
    }

    private string[] BuildSelectedExtensionsForRunConfiguration()
    {
        var selected = GetSelectedExtensions();
        return selected.Length == 0 ? RunOptions.DefaultExtensions : selected;
    }

    private string[] BuildProfileTags()
    {
        var tags = new List<string>();
        if (UseDat)
            tags.Add("dat");
        if (SortConsole)
            tags.Add("sorting");
        if (ConvertEnabled || ConvertOnly)
            tags.Add("conversion");
        if (DryRun)
            tags.Add("preview");

        return tags
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static tag => tag, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? NormalizeSelection(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
