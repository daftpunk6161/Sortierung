using Romulus.Contracts.Models;
using Romulus.Core.Audit;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Read-only DAT audit pipeline phase that classifies candidates against a DAT index.
/// </summary>
public sealed class DatAuditPipelinePhase : IPipelinePhase<DatAuditInput, DatAuditResult>
{
    public string Name => "DatAudit";

    public DatAuditResult Execute(DatAuditInput input, PipelineContext context, CancellationToken cancellationToken)
    {
        context.Metrics.StartPhase(Name);

        var entries = new List<DatAuditEntry>(input.Candidates.Count);
        var have = 0;
        var haveWrongName = 0;
        var miss = 0;
        var unknown = 0;
        var ambiguous = 0;

        foreach (var candidate in input.Candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var hash = candidate.Hash;
            var headerlessHash = candidate.HeaderlessHash;
            var filePath = candidate.MainPath;
            var fileName = Path.GetFileName(filePath);
            var consoleKey = candidate.ConsoleKey;

            var result = DatAuditClassifier.ClassifyFull(hash, headerlessHash, fileName, consoleKey, input.DatIndex);

            entries.Add(new DatAuditEntry(
                FilePath: filePath,
                Hash: hash ?? string.Empty,
                Status: result.Status,
                DatGameName: result.DatGameName,
                DatRomFileName: result.DatRomFileName,
                ConsoleKey: result.ResolvedConsoleKey ?? consoleKey,
                Confidence: ToConfidence(result.Status)));

            switch (result.Status)
            {
                case DatAuditStatus.Have:
                    have++;
                    break;
                case DatAuditStatus.HaveWrongName:
                    haveWrongName++;
                    break;
                case DatAuditStatus.Miss:
                    miss++;
                    break;
                case DatAuditStatus.Unknown:
                    unknown++;
                    break;
                case DatAuditStatus.Ambiguous:
                    ambiguous++;
                    break;
            }
        }

        context.Metrics.CompletePhase(entries.Count);

        return new DatAuditResult(
            Entries: entries,
            HaveCount: have,
            HaveWrongNameCount: haveWrongName,
            MissCount: miss,
            UnknownCount: unknown,
            AmbiguousCount: ambiguous);
    }

    private static int ToConfidence(DatAuditStatus status)
        => status switch
        {
            DatAuditStatus.Have => 100,
            DatAuditStatus.HaveWrongName => 95,
            DatAuditStatus.Miss => 90,
            DatAuditStatus.Ambiguous => 70,
            _ => 60
        };
}

/// <summary>
/// Input for DAT audit phase execution.
/// </summary>
/// <param name="Candidates">Candidates to classify.</param>
/// <param name="DatIndex">Loaded DAT index for lookups.</param>
/// <param name="Options">Current run options.</param>
public sealed record DatAuditInput(
    IReadOnlyList<RomCandidate> Candidates,
    DatIndex DatIndex,
    RunOptions Options);
