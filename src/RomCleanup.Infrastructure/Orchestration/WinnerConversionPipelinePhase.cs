using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that converts winner files after deduplication.
/// </summary>
public sealed class WinnerConversionPipelinePhase : IPipelinePhase<WinnerConversionPhaseInput, WinnerConversionPhaseOutput>
{
    public string Name => "FormatConvert";

    public WinnerConversionPhaseOutput Execute(WinnerConversionPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke($"[Convert] Starte Formatkonvertierung für {input.GameGroups.Count} Gruppen…");

        int converted = 0;
        int convertErrors = 0;
        int convertSkipped = 0;
        var totalGroups = input.GameGroups.Count;
        var processedGroups = 0;

        foreach (var group in input.GameGroups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedGroups++;

            var winnerPath = group.Winner.MainPath;
            if (input.JunkRemovedPaths.Contains(winnerPath) || !File.Exists(winnerPath))
            {
                if (processedGroups % 25 == 0 || processedGroups == totalGroups)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedGroups}/{totalGroups} Gruppen (ok={converted}, skip={convertSkipped}, err={convertErrors})");
                continue;
            }

            var ext = Path.GetExtension(winnerPath).ToLowerInvariant();
            var consoleKey = group.Winner.ConsoleKey ?? "";
            var target = input.Converter.GetTargetFormat(consoleKey, ext);
            if (target is null)
            {
                if (processedGroups % 25 == 0 || processedGroups == totalGroups)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedGroups}/{totalGroups} Gruppen (ok={converted}, skip={convertSkipped}, err={convertErrors})");
                continue;
            }

            var convResult = input.Converter.Convert(winnerPath, target, cancellationToken);
            if (convResult.Outcome == ConversionOutcome.Success)
            {
                var verificationOk = convResult.TargetPath is not null && input.Converter.Verify(convResult.TargetPath, target);
                if (verificationOk)
                {
                    converted++;
                    PipelinePhaseHelpers.AppendConversionAudit(context, input.Options, winnerPath, convResult.TargetPath, target.ToolName);
                    PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, input.Options, winnerPath, convResult.TargetPath);
                }
                else
                {
                    convertErrors++;
                    if (convResult.TargetPath is not null)
                    {
                        context.OnProgress?.Invoke($"WARNING: Verification failed for {convResult.TargetPath}");
                        PipelinePhaseHelpers.AppendConversionFailedAudit(context, input.Options, winnerPath, convResult.TargetPath, target.ToolName);
                        // SEC-CONV-04: Clean up failed output to prevent orphaned corrupt files
                        try { if (File.Exists(convResult.TargetPath)) File.Delete(convResult.TargetPath); }
                        catch { /* best-effort cleanup */ }
                    }
                }
            }
            else if (convResult.Outcome == ConversionOutcome.Skipped)
            {
                convertSkipped++;
            }
            else
            {
                convertErrors++;
                context.OnProgress?.Invoke($"WARNING: Conversion failed for {winnerPath}: {convResult.Reason}");
                PipelinePhaseHelpers.AppendConversionErrorAudit(context, input.Options, winnerPath, convResult.Reason);
            }

            if (processedGroups % 25 == 0 || processedGroups == totalGroups)
                context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedGroups}/{totalGroups} Gruppen (ok={converted}, skip={convertSkipped}, err={convertErrors})");
        }

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {converted} konvertiert, {convertSkipped} übersprungen, {convertErrors} Fehler");
        context.Metrics.CompletePhase(converted);

        return new WinnerConversionPhaseOutput(converted, convertErrors, convertSkipped);
    }


}

public sealed record WinnerConversionPhaseInput(
    IReadOnlyList<DedupeResult> GameGroups,
    RunOptions Options,
    IReadOnlySet<string> JunkRemovedPaths,
    IFormatConverter Converter);

public sealed record WinnerConversionPhaseOutput(
    int Converted,
    int ConvertErrors,
    int ConvertSkipped);