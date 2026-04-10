using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Orchestration;

public sealed partial class RunOrchestrator
{
    private async Task<List<RomCandidate>> ApplyPersistedReviewApprovalsAsync(
        List<RomCandidate> candidates,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        if (_reviewDecisionService is null || candidates.Count == 0)
            return candidates;

        var effectiveCandidates = candidates;

        if (options.ApproveReviews)
        {
            var reviewCandidates = candidates
                .Where(static candidate => candidate.SortDecision == SortDecision.Review)
                .ToArray();

            if (reviewCandidates.Length > 0)
            {
                await _reviewDecisionService.PersistApprovalsAsync(
                    reviewCandidates,
                    source: "auto-run",
                    cancellationToken).ConfigureAwait(false);
            }
        }

        var approved = await _reviewDecisionService.ApplyApprovalsAsync(
            effectiveCandidates,
            cancellationToken).ConfigureAwait(false);

        return ReferenceEquals(approved, effectiveCandidates)
            ? effectiveCandidates
            : approved.ToList();
    }
}
