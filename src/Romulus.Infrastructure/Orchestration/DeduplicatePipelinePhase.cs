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
        context.OnProgress?.Invoke(RunProgressLocalization.Format("Dedupe.Start", input.Count));

        var dedupeSw = System.Diagnostics.Stopwatch.StartNew();
        var groups = DeduplicationEngine.Deduplicate(input);
        var gameGroups = GetGameGroups(groups);
        dedupeSw.Stop();

        var loserCount = gameGroups.Sum(g => g.Losers.Count);
        var junkGroupCount = groups.Count(g => g.Winner.Category == FileCategory.Junk);

        context.OnProgress?.Invoke(RunProgressLocalization.Format(
            "Dedupe.Completed",
            dedupeSw.ElapsedMilliseconds,
            gameGroups.Count,
            gameGroups.Count - loserCount,
            loserCount,
            junkGroupCount));
        context.Metrics.CompletePhase(gameGroups.Count);

        return new DedupePhaseOutput(groups, gameGroups, loserCount);
    }

    private static List<DedupeGroup> GetGameGroups(IReadOnlyList<DedupeGroup> groups)
    {
        return groups
            // Keep all meaningful domain groups (Game/NonGame/Unknown/Bios) and only
            // exclude pure junk groups so downstream parity metrics are not undercounted.
            .Where(g => g.Winner.Category != FileCategory.Junk || g.Losers.Any(l => l.Category != FileCategory.Junk))
            .ToList();
    }
}

public sealed record DedupePhaseOutput(
    IReadOnlyList<DedupeGroup> Groups,
    List<DedupeGroup> GameGroups,
    int LoserCount);
