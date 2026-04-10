using System.IO;
using System.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Review;
using Romulus.Infrastructure.State;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Extracted from MainWindow.xaml.cs RunCoreAsync — builds infrastructure,
/// executes the RunOrchestrator pipeline, and generates the HTML report.
/// All methods run on a background thread (no Dispatcher calls).
/// RF-003 from gui-ux-deep-audit.md.
/// </summary>
public sealed class RunService : IRunService
{
    private readonly IAppState _appState;
    private readonly IRunEnvironmentFactory _runEnvironmentFactory;
    private readonly RunConfigurationMaterializer _runConfigurationMaterializer;
    private readonly IAuditStore _recoveryAuditStore;

    public RunService(
        IAppState? appState = null,
        IRunOptionsFactory? runOptionsFactory = null,
        IRunEnvironmentFactory? runEnvironmentFactory = null,
        RunConfigurationMaterializer? runConfigurationMaterializer = null,
        RunProfileService? runProfileService = null,
        IAuditStore? recoveryAuditStore = null)
    {
        _appState = appState ?? new AppStateStore();
        _runEnvironmentFactory = runEnvironmentFactory ?? new RunEnvironmentFactory();
        _recoveryAuditStore = recoveryAuditStore ?? new AuditCsvStore();
        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? RunEnvironmentBuilder.ResolveDataDir();
        var optionsFactory = runOptionsFactory ?? new RunOptionsFactory();
        var profileService = runProfileService ?? new RunProfileService(new JsonRunProfileStore(), dataDir);
        _runConfigurationMaterializer = runConfigurationMaterializer
            ?? new RunConfigurationMaterializer(new RunConfigurationResolver(profileService), optionsFactory);
    }

    /// <summary>Result of a single pipeline run.</summary>
    public sealed class RunServiceResult
    {
        public required RunResult Result { get; init; }
        public string? AuditPath { get; init; }
        public string? ReportPath { get; init; }
    }

    /// <summary>
    /// Build infrastructure and RunOptions from current ViewModel state.
    /// Must be called on a background thread — performs file I/O.
    /// </summary>
    public (RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)
        BuildOrchestrator(MainViewModel vm, Action<string>? onProgress = null)
    {
        _appState.SetValue("run.build.startedUtc", DateTime.UtcNow);
        _appState.SetValue("run.mode", vm.DryRun ? RunConstants.ModeDryRun : RunConstants.ModeMove);

        onProgress?.Invoke("[Init] Initialisiere Infrastruktur…");

        string? auditPath = null;
        if ((!vm.DryRun || vm.ConvertOnly) && vm.Roots.Count > 0)
        {
            var auditDir = !string.IsNullOrWhiteSpace(vm.AuditRoot)
                ? vm.AuditRoot
                : ArtifactPathResolver.GetArtifactDirectory(vm.Roots, AppIdentity.ArtifactDirectories.AuditLogs);
            auditDir = Path.GetFullPath(auditDir);
            auditPath = Path.Combine(auditDir, $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        }

        string? reportPath = null;
        if (vm.Roots.Count > 0)
        {
            var reportDir = ArtifactPathResolver.GetArtifactDirectory(vm.Roots, AppIdentity.ArtifactDirectories.Reports);
            reportDir = Path.GetFullPath(reportDir);
            Directory.CreateDirectory(reportDir);
            reportPath = Path.Combine(reportDir, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html");
        }

        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var materialized = _runConfigurationMaterializer.MaterializeAsync(
            vm.BuildCurrentRunConfigurationDraft(),
            vm.BuildCurrentRunConfigurationExplicitness(),
            settings,
            auditPath: auditPath,
            reportPath: reportPath).GetAwaiter().GetResult();
        var runOptions = materialized.Options;
        vm.RestoreRunConfigurationSelection(materialized.Workflow?.Id, materialized.EffectiveProfileId);

        onProgress?.Invoke($"[Init] Konfiguration: Modus={runOptions.Mode}, {runOptions.Extensions.Count} Extension(s), {runOptions.Roots.Count} Root(s)");
        if (!string.IsNullOrWhiteSpace(materialized.Workflow?.Name) || !string.IsNullOrWhiteSpace(materialized.Profile?.Name))
        {
            onProgress?.Invoke($"[Init] Workflow/Profil: {materialized.Workflow?.Name ?? "kein Workflow"} | {materialized.Profile?.Name ?? "kein Profil"}");
        }

        onProgress?.Invoke($"[Init] Datenverzeichnis: {dataDir}");

        var env = _runEnvironmentFactory.Create(runOptions, onProgress);

        var reviewDecisionService = ReviewDecisionServiceFactory.TryCreate(onProgress);

        var orchestrator = new RunOrchestrator(
            env.FileSystem, env.AuditStore, env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex, onProgress,
            archiveHashService: env.ArchiveHashService,
            knownBiosHashes: env.KnownBiosHashes,
            collectionIndex: env.CollectionIndex,
            enrichmentFingerprint: env.EnrichmentFingerprint,
            reviewDecisionService: reviewDecisionService);

        _appState.SetValue("run.build.completedUtc", DateTime.UtcNow);
        _appState.SetValue("run.auditPath", auditPath);
        _appState.SetValue("run.reportPath", reportPath);
        _appState.SetValue("run.workflowScenarioId", materialized.Workflow?.Id ?? string.Empty);
        _appState.SetValue("run.profileId", materialized.EffectiveProfileId ?? string.Empty);

        return (orchestrator, runOptions, auditPath, reportPath);
    }

    /// <summary>
    /// Execute the pipeline.
    /// Must be called on a background thread.
    /// </summary>
    public RunServiceResult ExecuteRun(
        RunOrchestrator orchestrator,
        RunOptions options,
        string? auditPath,
        string? reportPath,
        CancellationToken ct)
    {
        _appState.SetValue("run.execute.startedUtc", DateTime.UtcNow);
        _appState.SetValue("run.execute.cancelRequested", ct.IsCancellationRequested);
        var runStartedUtc = DateTime.UtcNow;

        try
        {
            var result = orchestrator.Execute(options, ct);
            var runCompletedUtc = DateTime.UtcNow;

            try
            {
                using var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath());
                CollectionRunSnapshotWriter.TryPersistAsync(
                    collectionIndex,
                    options,
                    result,
                    runStartedUtc,
                    runCompletedUtc).GetAwaiter().GetResult();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                _appState.SetValue("run.execute.indexPersistWarning", ex.Message);
            }

            var effectiveReportPath = ReportPathResolver.Resolve(result.ReportPath, reportPath);

            _appState.SetValue("run.execute.completedUtc", DateTime.UtcNow);
            _appState.SetValue("run.execute.status", result.Status);
            _appState.SetValue("run.execute.exitCode", result.ExitCode);
            _appState.SetValue("run.execute.durationMs", result.DurationMs);
            _appState.SetValue("run.execute.reportPath", effectiveReportPath);

            return new RunServiceResult
            {
                Result = result,
                AuditPath = auditPath,
                ReportPath = effectiveReportPath
            };
        }
        finally
        {
            orchestrator.Dispose();
        }
    }

    /// <summary>
    /// Get a directory at the same level as <paramref name="rootPath"/>.
    /// Falls back to a subdirectory within root for drive roots (C:\).
    /// </summary>
    public string GetSiblingDirectory(string rootPath, string siblingName)
    {
        var fullRoot = ArtifactPathResolver.NormalizeRoot(rootPath);
        return ArtifactPathResolver.GetSiblingDirectory(fullRoot, siblingName);
    }

    public bool HasVerifiedRollback(string? auditPath)
        => AuditRecoveryStateResolver.HasVerifiedRollback(_recoveryAuditStore, auditPath);

}
