using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;

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
        int convertBlocked = 0;
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
                if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                    context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
                continue;
            }

            var ext = Path.GetExtension(path).ToLowerInvariant();
            var consoleKey = candidate.ConsoleKey ?? "";
            ConversionTarget? target = null;
            ConversionResult convResult;

            if (input.Converter is FormatConverterAdapter advancedConverter)
            {
                convResult = advancedConverter.ConvertForConsole(path, consoleKey, cancellationToken);
            }
            else
            {
                target = input.Converter.GetTargetFormat(consoleKey, ext);
                if (target is null)
                {
                    convertSkipped++;
                    if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                        context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
                    continue;
                }

                if (string.Equals(ext, target.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    convertSkipped++;
                    if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                        context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
                    continue;
                }

                context.OnProgress?.Invoke($"[Convert] {Path.GetFileName(path)} → {target.Extension}");
                convResult = input.Converter.Convert(path, target, cancellationToken);
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
                        path,
                        convertedPath,
                        ConversionVerificationHelpers.ResolveToolName(convResult, target));
                    PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, input.Options, path, convertedPath);
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
                            path,
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
                context.OnProgress?.Invoke($"WARNING: Conversion failed for {path}: {convResult.Reason}");
                PipelinePhaseHelpers.AppendConversionErrorAudit(context, input.Options, path, convResult.Reason);
            }

            if (processedCandidates % 25 == 0 || processedCandidates == totalCandidates)
                context.OnProgress?.Invoke($"[Convert] Fortschritt: {processedCandidates}/{totalCandidates} Dateien (ok={converted}, skip={convertSkipped}, blocked={convertBlocked}, err={convertErrors})");
        }

        context.OnProgress?.Invoke($"[Convert] Abgeschlossen: {converted} konvertiert, {convertSkipped} übersprungen, {convertBlocked} blockiert, {convertErrors} Fehler");
        context.Metrics.CompletePhase(converted);

        return new ConvertOnlyPhaseOutput(converted, convertErrors, convertSkipped, convertBlocked, conversionResults);
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