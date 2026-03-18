using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase for ConvertOnly mode (no dedupe/move/sort).
/// </summary>
public sealed class ConvertOnlyPipelinePhase : IPipelinePhase<ConvertOnlyPhaseInput, ConvertOnlyPhaseOutput>
{
    public string Name => "FormatConvert";

    public ConvertOnlyPhaseOutput Execute(ConvertOnlyPhaseInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke($"[Convert] Nur-Konvertierung: {input.Candidates.Count} Dateien…");

        int converted = 0;
        int convertErrors = 0;
        int convertSkipped = 0;
        var totalCandidates = input.Candidates.Count;
        var processedCandidates = 0;

        foreach (var candidate in input.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            processedCandidates++;

            var path = candidate.MainPath;
            if (!File.Exists(path))
            {
                if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, err={convertErrors})");
                continue;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var consoleKey = candidate.ConsoleKey ?? "";
            var target = input.Converter.GetTargetFormat(consoleKey, ext);
            if (target is null)
            {
                convertSkipped++;
                if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, err={convertErrors})");
                continue;
            }

            if (string.Equals(ext, target.Extension, StringComparison.OrdinalIgnoreCase))
            {
                convertSkipped++;
                if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, err={convertErrors})");
                continue;
            }

            context.OnProgress?.Invoke($"[Convert] {Path.GetFileName(path)} → {target.Extension}");
            var convResult = input.Converter.Convert(path, target, cancellationToken);
            if (convResult.Outcome == ConversionOutcome.Success)
            {
                var verificationOk = convResult.TargetPath is not null && input.Converter.Verify(convResult.TargetPath, target);
                if (verificationOk)
                {
                    converted++;
                    PipelinePhaseHelpers.AppendConversionAudit(context, input.Options, path, convResult.TargetPath, target.ToolName);
                    PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, input.Options, path, convResult.TargetPath);
                }
                else
                {
                    convertErrors++;
                    if (convResult.TargetPath is not null)
                    {
                        context.OnProgress?.Invoke($"WARNING: Verification failed for {convResult.TargetPath}");
                        PipelinePhaseHelpers.AppendConversionFailedAudit(context, input.Options, path, convResult.TargetPath, target.ToolName);
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
                context.OnProgress?.Invoke($"WARNING: Conversion failed for {path}: {convResult.Reason}");
                PipelinePhaseHelpers.AppendConversionErrorAudit(context, input.Options, path, convResult.Reason);
            }

            if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, err={convertErrors})");
        }

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {converted} konvertiert, {convertSkipped} übersprungen, {convertErrors} Fehler");
        context.Metrics.CompletePhase(converted);

        return new ConvertOnlyPhaseOutput(converted, convertErrors, convertSkipped);
    }


}

public sealed record ConvertOnlyPhaseInput(
    IReadOnlyList<RomCandidate> Candidates,
    RunOptions Options,
    IFormatConverter Converter);

public sealed record ConvertOnlyPhaseOutput(
    int Converted,
    int ConvertErrors,
    int ConvertSkipped);