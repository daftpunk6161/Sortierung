using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that converts winner files after deduplication.
/// Uses shared ConversionPhaseHelper with set-member tracking so descriptor
/// conversions trash CUE/GDI/CCD sidecars atomically after verification.
/// </summary>
public sealed class WinnerConversionPipelinePhase : IPipelinePhase<WinnerConversionPhaseInput, WinnerConversionPhaseOutput>
{
    public string Name => "FormatConvert";

    public WinnerConversionPhaseOutput Execute(WinnerConversionPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke(RunProgressLocalization.Format("Convert.StartGroups", input.GameGroups.Count));

        var workItems = input.GameGroups
            .Select((group, index) =>
            {
                var winnerPath = group.Winner.MainPath;
                var skipBeforeConversion =
                    input.JunkRemovedPaths.Contains(winnerPath)
                    || !context.FileSystem.FileExists(winnerPath);

                return new ConversionPhaseHelper.ConversionWorkItem(
                    Index: index,
                    FilePath: winnerPath,
                    ConsoleKey: group.Winner.ConsoleKey ?? string.Empty,
                    TrackSetMembers: true,
                    SkipBeforeConversion: skipBeforeConversion);
            })
            .ToArray();

        var batch = ConversionPhaseHelper.ExecuteBatch(
            workItems,
            input.Converter,
            input.Options,
            context,
            progressUnitLabel: "Gruppen",
            cancellationToken);

        context.OnProgress?.Invoke(RunProgressLocalization.Format(
            "Convert.Completed",
            batch.Converted,
            batch.Skipped,
            batch.Blocked,
            batch.Errors));
        context.Metrics.CompletePhase(batch.Converted);

        return new WinnerConversionPhaseOutput(batch.Converted, batch.Errors, batch.Skipped, batch.Blocked, batch.Results);
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
