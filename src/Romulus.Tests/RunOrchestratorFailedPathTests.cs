using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Deep-dive audit (Orchestration / Run Lifecycle): regression tests for the
/// Failed-path parity gap.
///
/// Findings:
///   F1 – Failed catch path skipped report generation while Cancel path
///        called <c>TryGeneratePartialReport</c>. GUI/CLI/API therefore lost
///        forensic visibility for crashed runs even though scanned candidates
///        and partial KPI data were available.
///   F2 – Failed catch path did not set <c>IsPartial=true</c>. Cancel did.
///        Downstream projections that key off <c>IsPartial</c> for the
///        "vorläufig" markers therefore treated crashed runs as authoritative.
///   F3 – Failed catch path did not invoke <c>TryGeneratePartialDatAudit</c>
///        leaving the DAT audit empty for crashed runs even when scanned
///        candidates existed.
///   F5 – <c>PipelineState.FailedPhaseName</c>/<c>FailedPhaseStatus</c> were
///        never propagated into <c>RunResult</c>. GUI / CLI / API / Reports
///        therefore lost the fachliche Wahrheit "which phase aborted the run".
///   F6 – <c>WriteCompletedAuditSidecar</c> had an unreachable
///        <c>?? RunConstants.StatusCompleted</c> fallback that mixed two
///        status vocabularies. Removed; orchestrator vocabulary is now the
///        single source of truth in the sidecar metadata.
/// </summary>
public sealed class RunOrchestratorFailedPathTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _runRoot;

    public RunOrchestratorFailedPathTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RunOrchFailed_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _runRoot = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(_runRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void Execute_PipelineThrows_ResultIsMarkedPartial()
    {
        var orch = new RunOrchestrator(
            new ScanThrowingFileSystem(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_runRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeDryRun
        });

        Assert.Equal(RunConstants.StatusFailed, result.Status);
        Assert.True(result.IsPartial,
            "Failed runs must be marked IsPartial so projections show 'vorläufig' KPIs " +
            "(parity with cancelled runs which already set IsPartial).");
    }

    [Fact]
    public void Execute_PipelineThrows_WithReportPath_WritesPartialReport()
    {
        var reportDir = Path.Combine(_tempDir, "reports");
        Directory.CreateDirectory(reportDir);
        var reportPath = Path.Combine(reportDir, "report.html");

        var orch = new RunOrchestrator(
            new ScanThrowingFileSystem(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_runRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeDryRun,
            ReportPath = reportPath
        });

        Assert.Equal(RunConstants.StatusFailed, result.Status);
        Assert.False(string.IsNullOrEmpty(result.ReportPath),
            "Failed runs with a configured ReportPath must produce a partial report " +
            "so GUI/CLI/API surfaces stay aligned with the cancel path.");
        Assert.True(File.Exists(result.ReportPath!),
            $"Partial report file must exist at {result.ReportPath}.");
    }

    /// <summary>
    /// F4: When the completion audit-seal fails (IO/Unauthorized/InvalidOperation),
    /// the failure was logged to <c>_onProgress</c> but <i>not</i> surfaced on
    /// <c>RunResult.Warnings</c>. GUI/CLI/API/Reports therefore lost forensic
    /// visibility of broken sidecars whenever the run was already
    /// CompletedWithErrors (no status escalation, no warning, silent integrity gap).
    /// </summary>
    [Fact]
    public void Execute_AuditSealFails_AddsWarningToResult()
    {
        // Arrange a real (empty) audit CSV so WriteCompletedAuditSidecar enters
        // the seal call instead of early-returning.
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new SealFailingAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_runRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeDryRun,
            AuditPath = auditPath
        });

        Assert.Contains(result.Warnings,
            w => w.Contains("audit", StringComparison.OrdinalIgnoreCase)
              && (w.Contains("seal", StringComparison.OrdinalIgnoreCase)
               || w.Contains("sidecar", StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>
    /// F5: <c>PipelineState.FailedPhaseName</c> / <c>FailedPhaseStatus</c> were
    /// set by <c>PhasePlanExecutor</c> but never propagated into <c>RunResult</c>.
    /// GUI / CLI / API / Reports therefore had no fachliche Wahrheit about which
    /// phase aborted the run \u2014 only an opaque "failed" status. After the fix
    /// the scan-phase tags itself and <c>ApplyPartialPipelineState</c> copies
    /// the tag into the result.
    /// </summary>
    [Fact]
    public void Execute_PipelineThrows_ResultCarriesFailedPhaseName()
    {
        var orch = new RunOrchestrator(
            new ScanThrowingFileSystem(),
            new NoOpAuditStore());

        var result = orch.Execute(new RunOptions
        {
            Roots = [_runRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeDryRun
        });

        Assert.Equal(RunConstants.StatusFailed, result.Status);
        Assert.Equal("Scan", result.FailedPhaseName);
        Assert.Equal(RunConstants.StatusFailed, result.FailedPhaseStatus);
    }

    /// <summary>
    /// F6: <c>WriteCompletedAuditSidecar</c> previously had an
    /// <c>?? RunConstants.StatusCompleted</c> fallback that emitted the API
    /// lifecycle vocabulary ("completed") which the orchestrator never produces.
    /// After the fix the sidecar Status field always uses the orchestrator's
    /// vocabulary (ok / completed_with_errors / failed / cancelled / blocked).
    /// </summary>
    [Fact]
    public void Execute_OkRun_AuditSidecarUsesOrchestratorStatusVocabulary()
    {
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(auditPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");
        var sidecarPath = auditPath + ".meta.json";

        var orch = new RunOrchestrator(
            new Infrastructure.FileSystem.FileSystemAdapter(),
            new Infrastructure.Audit.AuditCsvStore(new Infrastructure.FileSystem.FileSystemAdapter()));

        var result = orch.Execute(new RunOptions
        {
            Roots = [_runRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeDryRun,
            AuditPath = auditPath
        });

        Assert.Equal(RunConstants.StatusOk, result.Status);
        Assert.True(File.Exists(sidecarPath), $"Sidecar must exist at {sidecarPath}.");
        var sidecarText = File.ReadAllText(sidecarPath);
        // Tolerate JSON indent / no-indent variants by stripping whitespace.
        var compact = System.Text.RegularExpressions.Regex.Replace(sidecarText, @"\s+", string.Empty);
        Assert.Contains("\"Status\":\"ok\"", compact);
        Assert.DoesNotContain("\"Status\":\"completed\"", compact);
    }

    /// <summary>
    /// IFileSystem fake: passes preflight checks but throws during scan so the
    /// orchestrator hits the non-OperationCanceledException catch branch.
    /// </summary>
    private sealed class ScanThrowingFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => throw new IOException("Simulated filesystem failure (deep-dive audit RED test)");
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }

    private sealed class SealFailingAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => throw new IOException("Simulated audit-seal failure (deep-dive audit RED test)");
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }
}
