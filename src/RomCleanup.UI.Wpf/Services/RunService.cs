using System.IO;
using System.Linq;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Paths;
using RomCleanup.Infrastructure.State;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Extracted from MainWindow.xaml.cs RunCoreAsync — builds infrastructure,
/// executes the RunOrchestrator pipeline, and generates the HTML report.
/// All methods run on a background thread (no Dispatcher calls).
/// RF-003 from gui-ux-deep-audit.md.
/// </summary>
public sealed class RunService : IRunService
{
    private readonly IAppState _appState;
    private readonly IRunOptionsFactory _runOptionsFactory;
    private readonly IRunEnvironmentFactory _runEnvironmentFactory;

    public RunService(
        IAppState? appState = null,
        IRunOptionsFactory? runOptionsFactory = null,
        IRunEnvironmentFactory? runEnvironmentFactory = null)
    {
        _appState = appState ?? new AppStateStore();
        _runOptionsFactory = runOptionsFactory ?? new RunOptionsFactory();
        _runEnvironmentFactory = runEnvironmentFactory ?? new RunEnvironmentFactory();
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
        _appState.SetValue("run.mode", vm.DryRun ? "DryRun" : "Move");

        onProgress?.Invoke("[Init] Initialisiere Infrastruktur…");

        string? auditPath = null;
        if ((!vm.DryRun || vm.ConvertOnly) && vm.Roots.Count > 0)
        {
            var auditDir = !string.IsNullOrWhiteSpace(vm.AuditRoot)
                ? vm.AuditRoot
                : ArtifactPathResolver.GetArtifactDirectory(vm.Roots, "audit-logs");
            auditDir = Path.GetFullPath(auditDir);
            auditPath = Path.Combine(auditDir, $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        }

        string? reportPath = null;
        if (vm.Roots.Count > 0)
        {
            var reportDir = ArtifactPathResolver.GetArtifactDirectory(vm.Roots, "reports");
            reportDir = Path.GetFullPath(reportDir);
            Directory.CreateDirectory(reportDir);
            reportPath = Path.Combine(reportDir, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html");
        }

        var selectedExts = vm.GetSelectedExtensions();
        var source = new ViewModelRunOptionsSource(vm, selectedExts);
        var runOptions = _runOptionsFactory.Create(source, auditPath, reportPath);

        onProgress?.Invoke($"[Init] Konfiguration: Modus={runOptions.Mode}, {runOptions.Extensions.Count} Extension(s), {runOptions.Roots.Count} Root(s)");

        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? RunEnvironmentBuilder.ResolveDataDir();
        onProgress?.Invoke($"[Init] Datenverzeichnis: {dataDir}");

        var env = _runEnvironmentFactory.Create(runOptions, onProgress);

        var orchestrator = new RunOrchestrator(
            env.FileSystem, env.AuditStore, env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex, onProgress,
            archiveHashService: env.ArchiveHashService);

        _appState.SetValue("run.build.completedUtc", DateTime.UtcNow);
        _appState.SetValue("run.auditPath", auditPath);
        _appState.SetValue("run.reportPath", reportPath);

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

        var result = orchestrator.Execute(options, ct);
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

    /// <summary>
    /// Get a directory at the same level as <paramref name="rootPath"/>.
    /// Falls back to a subdirectory within root for drive roots (C:\).
    /// </summary>
    public string GetSiblingDirectory(string rootPath, string siblingName)
    {
        var fullRoot = ArtifactPathResolver.NormalizeRoot(rootPath);
        return ArtifactPathResolver.GetSiblingDirectory(fullRoot, siblingName);
    }

    private sealed class ViewModelRunOptionsSource : IRunOptionsSource
    {
        public ViewModelRunOptionsSource(MainViewModel vm, IReadOnlyList<string> selectedExtensions)
        {
            _vm = vm;
            Roots = vm.Roots.ToList();
            Mode = vm.DryRun ? "DryRun" : "Move";
            PreferRegions = vm.GetPreferredRegions();
            Extensions = selectedExtensions.Count > 0 ? selectedExtensions : RunOptions.DefaultExtensions;
            RemoveJunk = vm.RemoveJunk;
            OnlyGames = vm.OnlyGames;
            KeepUnknownWhenOnlyGames = vm.KeepUnknownWhenOnlyGames;
            AggressiveJunk = vm.AggressiveJunk;
            SortConsole = vm.SortConsole;
            EnableDat = vm.UseDat;
            DatRoot = string.IsNullOrWhiteSpace(vm.DatRoot) ? null : vm.DatRoot;
            HashType = string.IsNullOrWhiteSpace(vm.DatHashType) ? "SHA1" : vm.DatHashType;
            ConvertFormat = (vm.ConvertEnabled || vm.ConvertOnly) ? "auto" : null;
            ConvertOnly = vm.ConvertOnly;
            TrashRoot = string.IsNullOrWhiteSpace(vm.TrashRoot) ? null : vm.TrashRoot;
            ConflictPolicy = vm.ConflictPolicy.ToString();
        }

        public IReadOnlyList<string> Roots { get; }
        public string Mode { get; }
        public string[] PreferRegions { get; }
        public IReadOnlyList<string> Extensions { get; }
        public bool RemoveJunk { get; }
        public bool OnlyGames { get; }
        public bool KeepUnknownWhenOnlyGames { get; }
        public bool AggressiveJunk { get; }
        public bool SortConsole { get; }
        public bool EnableDat { get; }
        public bool EnableDatAudit => EnableDat && _vm.EnableDatAudit;
        public bool EnableDatRename => EnableDat && _vm.EnableDatRename;
        public string? DatRoot { get; }
        public string HashType { get; }
        public string? ConvertFormat { get; }
        public bool ConvertOnly { get; }
        public string? TrashRoot { get; }
        public string ConflictPolicy { get; }

        private readonly MainViewModel _vm;
    }

}
