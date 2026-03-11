using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Infrastructure.Services;

/// <summary>
/// Application service facades for the ROM cleanup pipeline.
/// Port of ApplicationServices.ps1 — delegates to infrastructure services via ports.
/// </summary>
public sealed class ApplicationServiceFacade
{
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;
    private readonly IToolRunner _tools;
    private readonly IFormatConverter? _converter;
    private readonly RunOrchestrator _orchestrator;
    private readonly Action<string>? _log;

    public ApplicationServiceFacade(
        IFileSystem fs,
        IAuditStore audit,
        IToolRunner tools,
        RunOrchestrator orchestrator,
        IFormatConverter? converter = null,
        Action<string>? log = null)
    {
        _fs = fs;
        _audit = audit;
        _tools = tools;
        _orchestrator = orchestrator;
        _converter = converter;
        _log = log;
    }

    /// <summary>
    /// Run the full deduplication pipeline.
    /// Port of Invoke-RunDedupeService.
    /// </summary>
    public RunResult RunDedupe(RunOptions options, CancellationToken ct = default)
    {
        _log?.Invoke("ApplicationService: Starting dedupe run...");
        return _orchestrator.Execute(options, ct);
    }

    /// <summary>
    /// Perform audit rollback.
    /// Port of Invoke-RunRollbackService.
    /// </summary>
    public IReadOnlyList<string> RunRollback(
        string auditCsvPath,
        string[] allowedRestoreRoots,
        string[] allowedCurrentRoots,
        bool dryRun = true)
    {
        _log?.Invoke($"ApplicationService: Rollback from {auditCsvPath} (dryRun={dryRun})...");
        return _audit.Rollback(auditCsvPath, allowedRestoreRoots, allowedCurrentRoots, dryRun);
    }

    /// <summary>
    /// Run preflight validation.
    /// Port of Invoke-PreflightRun from Compatibility.ps1.
    /// </summary>
    public OperationResult RunPreflight(RunOptions options)
    {
        _log?.Invoke("ApplicationService: Running preflight...");
        return _orchestrator.Preflight(options);
    }

    /// <summary>
    /// Run standalone format conversion.
    /// Port of Invoke-RunConversionService with Operation='Standalone'.
    /// </summary>
    public StandaloneConversionPreview GetConversionPreview(
        IReadOnlyList<string> roots,
        IReadOnlyList<string>? allowedRoots = null,
        int previewLimit = 12)
    {
        return ExecutionHelpers.GetConversionPreview(_fs, roots, allowedRoots, previewLimit);
    }
}
