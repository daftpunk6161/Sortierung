using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-046: Abstracts RunService for DI injection and testability.</summary>
public interface IRunService
{
    (RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)
        BuildOrchestrator(MainViewModel vm, Action<string>? onProgress = null);

    RunService.RunServiceResult ExecuteRun(
        RunOrchestrator orchestrator,
        RunOptions options,
        string? auditPath,
        string? reportPath,
        CancellationToken ct);

    string GetSiblingDirectory(string rootPath, string siblingName);

    bool HasVerifiedRollback(string? auditPath) => false;
}
