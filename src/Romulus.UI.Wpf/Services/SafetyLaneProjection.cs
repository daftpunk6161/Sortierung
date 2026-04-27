using System.IO;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Models;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Wave-2 F-10: pure projection from <see cref="RomCandidate"/> to the three
/// safety-lane buckets used by the GUI's Library/Safety view. Centralised here so
/// the routing rules (SortDecision -> bucket, defensive UNKNOWN fallback) cannot
/// drift between the view-model and any future caller (Reports, CLI parity, tests).
/// </summary>
public static class SafetyLaneProjection
{
    public sealed record Result(
        IReadOnlyList<SafetyListItem> Blocked,
        IReadOnlyList<SafetyListItem> Review,
        IReadOnlyList<SafetyListItem> Unknown);

    public static Result Project(IReadOnlyList<RomCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var blocked = new List<SafetyListItem>();
        var review = new List<SafetyListItem>();
        var unknown = new List<SafetyListItem>();

        foreach (var candidate in candidates)
        {
            var reason = !string.IsNullOrWhiteSpace(candidate.MatchEvidence.Reasoning)
                ? candidate.MatchEvidence.Reasoning
                : candidate.ClassificationReasonCode;

            var item = new SafetyListItem(
                Path.GetFileName(candidate.MainPath),
                candidate.ConsoleKey,
                candidate.MatchEvidence.Level.ToString(),
                reason);

            var hasUnknownConsole = string.IsNullOrWhiteSpace(candidate.ConsoleKey)
                || candidate.ConsoleKey.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase);
            var routedToSafetyLane = false;

            switch (candidate.SortDecision)
            {
                case SortDecision.Blocked:
                    blocked.Add(item);
                    routedToSafetyLane = true;
                    break;
                case SortDecision.Review:
                    review.Add(item);
                    routedToSafetyLane = true;
                    break;
                case SortDecision.Unknown:
                    unknown.Add(item);
                    routedToSafetyLane = true;
                    break;
            }

            // Defensive fallback: a candidate without a usable console must remain
            // visible somewhere; "Unbekannt" is the safe default without double-counting.
            if (!routedToSafetyLane && hasUnknownConsole)
            {
                unknown.Add(item);
            }
        }

        return new Result(blocked, review, unknown);
    }
}
