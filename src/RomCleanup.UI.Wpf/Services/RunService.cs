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

    public RunService(IAppState? appState = null)
    {
        _appState = appState ?? new AppStateStore();
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
        var runOptions = new RunOptions
        {
            Roots = vm.Roots.ToList(),
            Mode = vm.DryRun ? "DryRun" : "Move",
            PreferRegions = vm.GetPreferredRegions(),
            Extensions = selectedExts.Length > 0 ? selectedExts : RunOptions.DefaultExtensions,
            RemoveJunk = vm.RemoveJunk,
            OnlyGames = vm.OnlyGames,
            KeepUnknownWhenOnlyGames = vm.KeepUnknownWhenOnlyGames,
            AggressiveJunk = vm.AggressiveJunk,
            SortConsole = vm.SortConsole,
            EnableDat = vm.UseDat,
            DatRoot = string.IsNullOrWhiteSpace(vm.DatRoot) ? null : vm.DatRoot,
            HashType = vm.DatHashType,
            ConvertFormat = (vm.ConvertEnabled || vm.ConvertOnly) ? "auto" : null,
            ConvertOnly = vm.ConvertOnly,
            TrashRoot = string.IsNullOrWhiteSpace(vm.TrashRoot) ? null : vm.TrashRoot,
            AuditPath = auditPath,
            ReportPath = reportPath,
            ConflictPolicy = vm.ConflictPolicy.ToString()
        };

        onProgress?.Invoke($"[Init] Konfiguration: Modus={runOptions.Mode}, {runOptions.Extensions.Count} Extension(s), {runOptions.Roots.Count} Root(s)");

        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? RunEnvironmentBuilder.ResolveDataDir();
        onProgress?.Invoke($"[Init] Datenverzeichnis: {dataDir}");

        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        settings.Dat.UseDat = vm.UseDat;
        settings.Dat.DatRoot = vm.DatRoot ?? settings.Dat.DatRoot;
        settings.Dat.HashType = vm.DatHashType;

        var env = RunEnvironmentBuilder.Build(runOptions, settings, dataDir, onWarning: onProgress);

        var orchestrator = new RunOrchestrator(
            env.FileSystem, env.Audit, env.ConsoleDetector, env.HashService, env.Converter, env.DatIndex, onProgress);

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
        var effectiveReportPath = ResolveReportPath(result.ReportPath, reportPath);

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

    private static string? ResolveReportPath(string? actualReportPath, string? plannedReportPath)
    {
        if (!string.IsNullOrWhiteSpace(actualReportPath) && File.Exists(actualReportPath))
            return Path.GetFullPath(actualReportPath);

        if (!string.IsNullOrWhiteSpace(plannedReportPath) && File.Exists(plannedReportPath))
            return Path.GetFullPath(plannedReportPath);

        var candidateDirs = new List<string>();
        if (!string.IsNullOrWhiteSpace(plannedReportPath))
        {
            var plannedDir = Path.GetDirectoryName(plannedReportPath);
            if (!string.IsNullOrWhiteSpace(plannedDir) && Directory.Exists(plannedDir))
                candidateDirs.Add(plannedDir);
        }

        var fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RomCleanupRegionDedupe",
            "reports");
        if (Directory.Exists(fallbackDir))
            candidateDirs.Add(fallbackDir);

        foreach (var dir in candidateDirs.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var latest = Directory.GetFiles(dir, "*.html", SearchOption.TopDirectoryOnly)
                    .OrderByDescending(File.GetLastWriteTimeUtc)
                    .FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(latest) && File.Exists(latest))
                    return Path.GetFullPath(latest);
            }
            catch
            {
                // best-effort lookup only
            }
        }

        return null;
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

}
