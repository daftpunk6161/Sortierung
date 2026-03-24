using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;

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
        int convertBlocked = 0;
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
                if (processedGroups % 25 == 0 || processedGroups == totalGroups)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedGroups}/{totalGroups} Gruppen (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
                continue;
            }

            var ext = Path.GetExtension(winnerPath).ToLowerInvariant();
            var consoleKey = group.Winner.ConsoleKey ?? "";
            ConversionTarget? target = null;
            ConversionResult convResult;

            if (input.Converter is FormatConverterAdapter advancedConverter)
            {
                convResult = advancedConverter.ConvertForConsole(winnerPath, consoleKey, cancellationToken);
            }
            else
            {
                target = input.Converter.GetTargetFormat(consoleKey, ext);
                if (target is null)
                {
                    if (processedGroups % 25 == 0 || processedGroups == totalGroups)
                        context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedGroups}/{totalGroups} Gruppen (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
                    continue;
                }

                convResult = input.Converter.Convert(winnerPath, target, cancellationToken);
            }

            conversionResults.Add(convResult);

            if (convResult.Outcome == ConversionOutcome.Success)
            {
                var verificationOk = ConversionVerificationHelpers.IsVerificationSuccessful(convResult, input.Converter, target);
                if (verificationOk)
                {
                    var convertedPath = convResult.TargetPath;
                    if (convertedPath is null)
                    {
                        convertErrors++;
                        continue;
                    }

                    converted++;
                    PipelinePhaseHelpers.AppendConversionAudit(
                        context,
                        input.Options,
                        winnerPath,
                        convertedPath,
                        ConversionVerificationHelpers.ResolveToolName(convResult, target));
                    PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, input.Options, winnerPath, convertedPath);
                }
                else
                {
                    convertErrors++;
                    if (convResult.TargetPath is not null)
                    {
                        context.OnProgress?.Invoke($"WARNING: Verification failed for {convResult.TargetPath}");
                        PipelinePhaseHelpers.AppendConversionFailedAudit(
                            context,
                            input.Options,
                            winnerPath,
                            convResult.TargetPath,
                            ConversionVerificationHelpers.ResolveToolName(convResult, target));
                        // SEC-CONV-04: Clean up failed output to prevent orphaned corrupt files
                        try { if (File.Exists(convResult.TargetPath)) File.Delete(convResult.TargetPath); }
                        catch (IOException) { /* best-effort cleanup — file may be locked */ }
                    }
                }
            }
            else if (convResult.Outcome == ConversionOutcome.Skipped)
            {
                convertSkipped++;
            }
            else if (convResult.Outcome == ConversionOutcome.Blocked)
            {
                convertBlocked++;
            }
            else
            {
                convertErrors++;
                context.OnProgress?.Invoke($"WARNING: Conversion failed for {winnerPath}: {convResult.Reason}");
                PipelinePhaseHelpers.AppendConversionErrorAudit(context, input.Options, winnerPath, convResult.Reason);
                // SEC-CONV-05: Clean up any partial output left by a failed conversion tool
                if (convResult.TargetPath is not null)
                {
                    try { if (File.Exists(convResult.TargetPath)) File.Delete(convResult.TargetPath); }
                    catch (IOException) { /* best-effort cleanup — file may be locked */ }
                }
            }

            if (processedGroups % 25 == 0 || processedGroups == totalGroups)
                context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedGroups}/{totalGroups} Gruppen (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
        }

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {converted} konvertiert, {convertSkipped} übersprungen, {convertBlocked} blockiert, {convertErrors} Fehler");
        context.Metrics.CompletePhase(converted);

        return new WinnerConversionPhaseOutput(converted, convertErrors, convertSkipped, convertBlocked, conversionResults);
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
    int ConvertSkipped,
    int ConvertBlocked,
    IReadOnlyList<ConversionResult> ConversionResults);