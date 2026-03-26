using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that converts winner files after deduplication.
/// Uses shared ConversionPhaseHelper — no set-member tracking needed
/// because MovePipelinePhase already handled set members for winners.
/// </summary>
public sealed class WinnerConversionPipelinePhase : IPipelinePhase<WinnerConversionPhaseInput, WinnerConversionPhaseOutput>
{
    public string Name => "FormatConvert";

    public WinnerConversionPhaseOutput Execute(WinnerConversionPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke($"[Convert] Starte Formatkonvertierung für {input.GameGroups.Count} Gruppen…");

        var counters = new ConversionPhaseHelper.ConversionCounters();
        var conversionResults = new List<ConversionResult>();
        var totalGroups = input.GameGroups.Count;
        var processedGroups = 0;

        foreach (var group in input.GameGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedGroups++;

            var winnerPath = group.Winner.MainPath;
            if (input.JunkRemovedPaths.Contains(winnerPath) || !File.Exists(winnerPath))
            {
                ReportProgress(context, processedGroups, totalGroups, counters);
                continue;
            }

            var convResult = ConversionPhaseHelper.ConvertSingleFile(
                winnerPath,
                group.Winner.ConsoleKey ?? "",
                input.Converter,
                input.Options,
                context,
                counters,
                trackSetMembers: false,
                cancellationToken);

            if (convResult is not null)
                conversionResults.Add(convResult);

            ReportProgress(context, processedGroups, totalGroups, counters);
        }

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {counters.Converted} konvertiert, {counters.Skipped} übersprungen, {counters.Blocked} blockiert, {counters.Errors} Fehler");
        context.Metrics.CompletePhase(counters.Converted);

        return new WinnerConversionPhaseOutput(counters.Converted, counters.Errors, counters.Skipped, counters.Blocked, conversionResults);
    }

    private static void ReportProgress(PipelineContext context, int processed, int total, ConversionPhaseHelper.ConversionCounters c)
    {
        if (processed % 25 == 0 || processed == total)
            context.OnProgress?.Invoke($"[Convert] Fortschritt: {processed}/{total} Gruppen (ok={c.Converted}, skip={c.Skipped}, blocked={c.Blocked}, err={c.Errors})");
    }
}

public sealed record WinnerConversionPhaseInput(
    IReadOnlyList<DedupeGroup> GameGroups,
    RunOptions Options,
    IReadOnlySet<string> JunkRemovedPaths,
    IFormatConverter Converter);

public sealed record WinnerConversionPhaseOutput(
    int Converted,
    int ConvertErrors,
    int ConvertSkipped,
    int ConvertBlocked,
    IReadOnlyList<ConversionResult> ConversionResults);