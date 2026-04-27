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
        context.OnProgress?.Invoke(RunProgressLocalization.Format("Convert.OnlyStart", input.Candidates.Count));

        var workItems = input.Candidates
            .Select((candidate, index) => new ConversionPhaseHelper.ConversionWorkItem(
                Index: index,
                FilePath: candidate.MainPath,
                ConsoleKey: candidate.ConsoleKey ?? string.Empty,
                // R4-009: ConvertOnly mode must NOT track set members to avoid orphaned
                // .bin siblings when only the .cue is converted. Set tracking is bound
                // to dedupe/move flow which is absent in ConvertOnly.
                TrackSetMembers: false,
                SkipBeforeConversion: !context.FileSystem.FileExists(candidate.MainPath)))
            .ToArray();

        var batch = ConversionPhaseHelper.ExecuteBatch(
            workItems,
            input.Converter,
            input.Options,
            context,
            progressUnitLabel: "Dateien",
            cancellationToken);

        context.OnProgress?.Invoke(RunProgressLocalization.Format(
            "Convert.Completed",
            batch.Converted,
            batch.Skipped,
            batch.Blocked,
            batch.Errors));
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
