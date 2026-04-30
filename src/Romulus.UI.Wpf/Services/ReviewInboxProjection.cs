using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Models;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// T-W4-REVIEW-INBOX (wave 5): pure projection from <see cref="RomCandidate"/>
/// to the three Inbox lanes (Safe / Review / Blocked) shown in the
/// Review-Inbox workflow. This is the Single Source of Truth for the lane
/// routing used by <c>ReviewInboxViewModel</c>; it must not duplicate
/// scoring, DAT or winner-selection logic. Routing rules:
///   * Safe    = SortDecision.Sort, SortDecision.DatVerified
///   * Review  = SortDecision.Review, SortDecision.Unknown
///   * Blocked = SortDecision.Blocked
/// </summary>
public static class ReviewInboxProjection
{
    public sealed record Result(
        IReadOnlyList<ReviewInboxRow> Safe,
        IReadOnlyList<ReviewInboxRow> Review,
        IReadOnlyList<ReviewInboxRow> Blocked);

    public static Result Project(IReadOnlyList<RomCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var safe = new List<ReviewInboxRow>();
        var review = new List<ReviewInboxRow>();
        var blocked = new List<ReviewInboxRow>();

        foreach (var c in candidates)
        {
            var row = new ReviewInboxRow(
                System.IO.Path.GetFileName(c.MainPath),
                c.ConsoleKey,
                c.GameKey,
                c.SortDecision.ToString(),
                c.MatchEvidence.Level.ToString(),
                !string.IsNullOrWhiteSpace(c.MatchEvidence.Reasoning)
                    ? c.MatchEvidence.Reasoning
                    : c.ClassificationReasonCode);

            switch (c.SortDecision)
            {
                case SortDecision.Sort:
                case SortDecision.DatVerified:
                    safe.Add(row);
                    break;
                case SortDecision.Review:
                case SortDecision.Unknown:
                    review.Add(row);
                    break;
                case SortDecision.Blocked:
                    blocked.Add(row);
                    break;
            }
        }

        return new Result(safe, review, blocked);
    }
}
