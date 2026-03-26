using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase for ConvertOnly mode (no dedupe/move/sort).
/// Uses shared ConversionPhaseHelper for conversion logic with set-member tracking.
/// </summary>
public sealed class ConvertOnlyPipelinePhase : IPipelinePhase<ConvertOnlyPhaseInput, ConvertOnlyPhaseOutput>
{
    public string Name => "FormatConvert";

    public ConvertOnlyPhaseOutput Execute(ConvertOnlyPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke($"[Convert] Nur-Konvertierung: {input.Candidates.Count} Dateien…");

        var counters = new ConversionPhaseHelper.ConversionCounters();
        var conversionResults = new List<ConversionResult>();
        var totalCandidates = input.Candidates.Count;
        var processedCandidates = 0;

        foreach (var candidate in input.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedCandidates++;

            var path = candidate.MainPath;
            if (!File.Exists(path))
            {
                ReportProgress(context, processedCandidates, totalCandidates, counters);
                continue;
            }

            var convResult = ConversionPhaseHelper.ConvertSingleFile(
                path,
                candidate.ConsoleKey ?? "",
                input.Converter,
                input.Options,
                context,
                counters,
                trackSetMembers: true,
                cancellationToken);

            if (convResult is not null)
                conversionResults.Add(convResult);

            ReportProgress(context, processedCandidates, totalCandidates, counters);
        }

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {counters.Converted} konvertiert, {counters.Skipped} übersprungen, {counters.Blocked} blockiert, {counters.Errors} Fehler");
        context.Metrics.CompletePhase(counters.Converted);

        return new ConvertOnlyPhaseOutput(counters.Converted, counters.Errors, counters.Skipped, counters.Blocked, conversionResults);
    }

    private static void ReportProgress(PipelineContext context, int processed, int total, ConversionPhaseHelper.ConversionCounters c)
    {
        if (processed % 25 == 0 || processed == total)
            context.OnProgress?.Invoke($"[Convert] Fortschritt: {processed}/{total} Dateien (ok={c.Converted}, skip={c.Skipped}, blocked={c.Blocked}, err={c.Errors})");
    }
}

public sealed record ConvertOnlyPhaseInput(
    IReadOnlyList<RomCandidate> Candidates,
    RunOptions Options,
    IFormatConverter Converter);

public sealed record ConvertOnlyPhaseOutput(
    int Converted,
    int ConvertErrors,
    int ConvertSkipped,
    int ConvertBlocked,
    IReadOnlyList<ConversionResult> ConversionResults);