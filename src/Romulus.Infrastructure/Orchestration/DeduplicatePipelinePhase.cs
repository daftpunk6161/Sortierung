using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that groups candidates by game key and selects deterministic winners.
/// </summary>
public sealed class DeduplicatePipelinePhase : IPipelinePhase<IReadOnlyList<RomCandidate>, DedupePhaseOutput>
{
    public string Name => "Deduplicate";

    public DedupePhaseOutput Execute(IReadOnlyList<RomCandidate> input, PipelineContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        context.Metrics.StartPhase(Name);
        context.OnProgress?.Invoke($"[Dedupe] Gruppiere {input.Count} Dateien nach GameKey…");

        var dedupeSw = System.Diagnostics.Stopwatch.StartNew();
        var groups = DeduplicationEngine.Deduplicate(input);
        var gameGroups = GetGameGroups(groups);
        dedupeSw.Stop();

        var loserCount = gameGroups.Sum(g => g.Losers.Count);
        var junkGroupCount = groups.Count(g => g.Winner.Category == FileCategory.Junk);

        context.OnProgress?.Invoke($"[Dedupe] Abgeschlossen in {dedupeSw.ElapsedMilliseconds}ms: {gameGroups.Count} Gruppen, Keep={gameGroups.Count - loserCount}, Move={loserCount}, Junk={junkGroupCount}");
        context.Metrics.CompletePhase(gameGroups.Count);

        return new DedupePhaseOutput(groups, gameGroups, loserCount);
    }

    private static List<DedupeGroup> GetGameGroups(IReadOnlyList<DedupeGroup> groups)
    {
        return groups
            .Where(g => g.Winner.Category == FileCategory.Game || g.Losers.Any(l => l.Category == FileCategory.Game))
            .ToList();
    }
}

public sealed record DedupePhaseOutput(
    IReadOnlyList<DedupeGroup> Groups,
    List<DedupeGroup> GameGroups,
    int LoserCount);
