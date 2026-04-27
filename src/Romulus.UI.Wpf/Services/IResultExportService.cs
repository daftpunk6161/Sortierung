using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Wave-2 F-07: centralises HTML-report writing so the channel-divergence between
/// <see cref="RunReportWriter"/> (full RunResult available) and the candidate-only
/// <see cref="ReportGenerator"/> fallback no longer leaks into command-handler code.
/// FeatureCommandService just hands off the data and receives a uniform result.
/// </summary>
public interface IResultExportService
{
    HtmlReportWriteResult WriteHtmlReport(string targetPath, MainViewModel vm);
}

public readonly record struct HtmlReportWriteResult(bool Success, string Path, string ChannelUsed);

public sealed class ResultExportService : IResultExportService
{
    public HtmlReportWriteResult WriteHtmlReport(string targetPath, MainViewModel vm)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(vm);

        var mode = vm.CurrentRunState == Models.RunState.CompletedDryRun
            ? RunConstants.ModeDryRun
            : RunConstants.ModeMove;

        var dryRun = string.Equals(mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase);

        if (vm.LastRunResult is { } runResult)
        {
            // Preferred path: full RunResult yields the canonical report identical to CLI/API.
            RunReportWriter.WriteReport(targetPath, runResult, mode);
            return new HtmlReportWriteResult(true, targetPath, "RunReportWriter");
        }

        // Fallback: candidate/dedupe-only data (legacy parity, reached when only
        // a Preview was loaded without a fresh RunResult in memory).
        var (summary, entries) = FeatureService.BuildHtmlReportData(
            vm.LastCandidates.ToArray(),
            vm.LastDedupeGroups.ToArray(),
            runResult: null,
            dryRun: dryRun);
        var directory = System.IO.Path.GetDirectoryName(targetPath) ?? ".";
        ReportGenerator.WriteHtmlToFile(targetPath, directory, summary, entries);
        return new HtmlReportWriteResult(true, targetPath, "ReportGenerator");
    }
}
