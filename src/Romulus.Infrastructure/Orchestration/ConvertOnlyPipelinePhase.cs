using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;

namespace Romulus.Infrastructure.Orchestration;

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

        var workItems = input.Candidates
            .Select((candidate, index) => new ConversionPhaseHelper.ConversionWorkItem(
                Index: index,
                FilePath: candidate.MainPath,
                ConsoleKey: candidate.ConsoleKey ?? string.Empty,
                TrackSetMembers: true,
                SkipBeforeConversion: !context.FileSystem.FileExists(candidate.MainPath)))
            .ToArray();

        var batch = ConversionPhaseHelper.ExecuteBatch(
            workItems,
            input.Converter,
            input.Options,
            context,
            progressUnitLabel: "Dateien",
            cancellationToken);

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {batch.Converted} konvertiert, {batch.Skipped} übersprungen, {batch.Blocked} blockiert, {batch.Errors} Fehler");
        context.Metrics.CompletePhase(batch.Converted);

        return new ConvertOnlyPhaseOutput(batch.Converted, batch.Errors, batch.Skipped, batch.Blocked, batch.Results);
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
