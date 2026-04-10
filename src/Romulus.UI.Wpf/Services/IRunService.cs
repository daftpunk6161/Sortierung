using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Services;

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
